using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/public/pow")]
[AllowAnonymous] // Secured by PublicKeyAuthMiddleware
public class PublicPowController : ControllerBase
{
    private readonly LightningService _jwt;
    private readonly PowChallengeSigner _signer;
    private readonly PowReplayService _replay;
    private readonly PowDifficultyService _difficulty;
    private readonly PowAttemptLogger _attempts;
    private readonly PowRateLimitService _rateLimit;
    private readonly ILogger<PublicPowController> _logger;

    private readonly IConfiguration _configuration;
    private readonly LiveAuthDbContext _db;

    public PublicPowController(
        IConfiguration configuration,
        LiveAuthDbContext db,
        LightningService jwt,
        PowChallengeSigner signer,
        PowReplayService replay,
        PowDifficultyService difficulty,
        PowAttemptLogger attempts,
        PowRateLimitService rateLimit,
        ILogger<PublicPowController> logger)
    {
        _configuration = configuration;
        _db = db;
        _jwt = jwt;
        _signer = signer;
        _replay = replay;
        _difficulty = difficulty;
        _attempts = attempts;
        _rateLimit = rateLimit;
        _logger = logger;
    }

    /* ============================================================
     * Helpers
     * ============================================================ */

    private Project? GetProject()
    {
        // First try to get from HttpContext (set by middleware)
        if (HttpContext.Items.TryGetValue("LW_Project", out var value) && value is Project proj)
            return proj;

        // Fallback to demo project if no API key provided
        try
        {
            return GetDemoProjectAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get demo project for PoW challenge");
            return null;
        }
    }

    private async Task<Project?> GetDemoProjectAsync(CancellationToken ct)
    {
        // Get demo project ID from config
        var demoProjectIdStr = _configuration["LiveAuth:DemoProjectId"] ?? "00000000-0000-0000-0000-000000000002";
        
        if (!Guid.TryParse(demoProjectIdStr, out var projectId))
        {
            _logger.LogError("Invalid DemoProjectId config: {DemoProjectId}", demoProjectIdStr);
            return null;
        }

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.Id == projectId &&
                p.IsActive,
            ct);

        if (project == null)
        {
            _logger.LogWarning("Demo project not found in database. Looking for ID: {ProjectId}", projectId);
        }

        return project;
    }

    private static string RandomHex(int bytes)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes))
            .ToLowerInvariant();

    private static byte[] Sha256Bytes(string input)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(input));
    }

    private static bool IsLessThan(byte[] hash, byte[] target)
    {
        for (int i = 0; i < 32; i++)
        {
            if (hash[i] < target[i]) return true;
            if (hash[i] > target[i]) return false;
        }

        return true;
    }

    private static byte[] TargetFromBits(int bits)
    {
        var target = new byte[32];

        int fullBytes = bits / 8;
        int remBits = bits % 8;

        for (int i = 0; i < fullBytes; i++)
            target[i] = 0x00;

        if (fullBytes < 32)
        {
            target[fullBytes] = (byte)(0xFF >> remBits);
            for (int i = fullBytes + 1; i < 32; i++)
                target[i] = 0xFF;
        }

        return target;
    }

    /* ============================================================
     * GET /api/public/pow/challenge
     * ============================================================ */

    /// <summary>
    /// Creates a new challenge.
    /// </summary>
    /// <returns></returns>
    [HttpGet("challenge")]
    public async Task<IActionResult> CreateChallenge(
        CancellationToken ct // <-- injected automatically
    )
    {
        var project = GetProject();
        
        if (project == null)
        {
            _logger.LogWarning("PoW challenge request: project not found in HttpContext.");
            return Unauthorized();
        }

        // Rate limiting: Prevent hash grinding and DoS
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_rateLimit.TryAcquire(ipAddress, project.Id))
        {
            return StatusCode(429, new
            {
                error = "rate_limit_exceeded",
                error_description = "Too many challenge requests. Please try again later.",
                retry_after_seconds = 60
            });
        }

        // Log SDK version for tracking
        var sdkVersion = Request.Headers.TryGetValue("X-LW-SDK-Version", out var sdkVer) 
            ? sdkVer.ToString() 
            : "unknown";

        _logger.LogDebug("PoW challenge: project={ProjectId}, env={Env}, sdkVersion={SdkVersion}",
            project.Id, project.Environment, sdkVersion);

        if (!project.IsActive)
        {
            _logger.LogWarning("PoW challenge request: project {ProjectId} is inactive.", project.Id);
            return Forbid();
        }

        // Use request-scoped cancellation
        int difficultyBits =
            await _difficulty.GetDifficultyAsync(project, ct);

        var challengeHex = RandomHex(16);
        var target = TargetFromBits(difficultyBits);
        var expiresAtUnix =
            DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();

        var payload =
            BuildPayload(project.Id, challengeHex, difficultyBits, expiresAtUnix);

        var sig = _signer.Sign(payload);

        _logger.LogDebug("PoW challenge generated: project={ProjectId}, difficulty={Difficulty}, expires={Expires}", 
            project.Id, difficultyBits, expiresAtUnix);

        return Ok(new PowChallengeResponse(
            ProjectPublicKey: project.PublicKey,
            ChallengeHex: challengeHex,
            TargetHex: Convert.ToHexString(target).ToLowerInvariant(),
            DifficultyBits: difficultyBits,
            ExpiresAtUnix: expiresAtUnix,
            Signature: sig
        ));
    }

    private static string BuildPayload(Guid projectId, string challengeHex, int difficultyBits, long expiresAtUnix)
        => $"{projectId}:{challengeHex}:{difficultyBits}:{expiresAtUnix}";

    /* ============================================================
     * POST /api/public/pow/verify
     * ============================================================ */

    [HttpPost("verify")]
public async Task<IActionResult> Verify(
    [FromBody] PowVerifyRequest req,
    CancellationToken ct)
{
    var project = GetProject();
    if (project == null)
    {
        _logger.LogWarning("PoW verify request: project not found in HttpContext.");
        return Unauthorized();
    }
    
    // Log SDK version for tracking
    var sdkVersion = Request.Headers.TryGetValue("X-LW-SDK-Version", out var sdkVer) 
        ? sdkVer.ToString() 
        : "unknown";
    
    if (!project.IsActive)
    {
        _logger.LogWarning("PoW verify request: project {ProjectId} is inactive.", project.Id);
        return Forbid();
    }

    if (string.IsNullOrWhiteSpace(req.ChallengeHex) ||
        string.IsNullOrWhiteSpace(req.HashHex) ||
        string.IsNullOrWhiteSpace(req.Nonce.ToString()) ||
        string.IsNullOrWhiteSpace(req.Sig))
    {
        _logger.LogWarning("PoW verify request: project {ProjectId} sent missing fields.", project.Id);
        return BadRequest("Missing fields.");
    }

    var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    if (req.ExpiresAtUnix <= nowUnix)
    {
        _logger.LogInformation("PoW verify failed: challenge expired for project {ProjectId}.", project.Id);
        return Ok(new PowVerifyResponse(false, null, "lightning"));
    }

    // Approximate solve duration (fine for v1)
    var solveMs =
        (nowUnix - (req.ExpiresAtUnix - 300)) * 1000;

    // Use difficulty from the signed challenge payload, not current adaptive difficulty.
    // This fixes the race condition where difficulty changes between challenge and verify.
    int difficultyBits = req.DifficultyBits;

    // Sanity check: difficulty must be in valid range (prevents tampered requests)
    // Allow 8-24 bits (demo can use lower difficulty)
    if (difficultyBits < 8 || difficultyBits > 24)
    {
        _logger.LogWarning("PoW verify failed: difficultyBits {Bits} out of range for project {ProjectId}.", difficultyBits, project.Id);
        return BadRequest("Invalid difficulty.");
    }

    // ------------------------------------------------
    // 1) Signature verification (stateless integrity)
    // ------------------------------------------------
    var payload = BuildPayload(
        project.Id,
        req.ChallengeHex,
        difficultyBits,
        req.ExpiresAtUnix
    );

    if (!_signer.Verify(payload, req.Sig))
    {
        _logger.LogWarning("PoW verify failed: invalid signature for project {ProjectId}.", project.Id);
        await _difficulty.RecordResultAsync(
            project.Id,
            solveMs,
            success: false,
            ct
        );
        await _attempts.RecordAsync(
            project.Id,
            solveMs,
            success: false,
            ct
        );


        return Ok(new PowVerifyResponse(false, null, "lightning"));
    }

    // ------------------------------------------------
    // 2) Hash correctness
    // ------------------------------------------------
    var input =
        $"{project.PublicKey}:{req.ChallengeHex}:{req.Nonce}";

    var computed = Sha256Bytes(input);
    var computedHex = Convert.ToHexString(computed).ToLowerInvariant();

    if (!computedHex.Equals(req.HashHex, StringComparison.OrdinalIgnoreCase))
    {
        _logger.LogWarning("PoW verify failed: hash mismatch for project {ProjectId}.", project.Id);
        await _difficulty.RecordResultAsync(
            project.Id,
            solveMs,
            success: false,
            ct
        );
        await _attempts.RecordAsync(
            project.Id,
            solveMs,
            success: false,
            ct
        );

        return Ok(new PowVerifyResponse(false, null, null));
    }

    // ------------------------------------------------
    // 3) Difficulty target check
    // ------------------------------------------------
    var target = TargetFromBits(difficultyBits);
    if (!IsLessThan(computed, target))
    {
        _logger.LogWarning("PoW verify failed: difficulty target not met for project {ProjectId}. Bits={Bits}", project.Id, difficultyBits);
        await _difficulty.RecordResultAsync(
            project.Id,
            solveMs,
            success: false,
            ct
        );
        await _attempts.RecordAsync(
            project.Id,
            solveMs,
            success: false,
            ct
        );

        return Ok(new PowVerifyResponse(false, null, "lightning"));
    }

    // ------------------------------------------------
    // 4) Replay protection (nonce-level)
    // ------------------------------------------------
    var firstUse = await _replay.TryMarkNonceUsedAsync(
        project.Id,
        req.ChallengeHex,
        req.Nonce.ToString(),
        req.ExpiresAtUnix,
        ct
    );

    if (!firstUse)
    {
        _logger.LogWarning("PoW verify failed: replay detected for project {ProjectId}, nonce {Nonce}.", project.Id, req.Nonce);
        await _difficulty.RecordResultAsync(
            project.Id,
            solveMs,
            success: false,
            ct
        );
        await _attempts.RecordAsync(
            project.Id,
            solveMs,
            success: false,
            ct
        );

        return Ok(new PowVerifyResponse(false, null, "lightning"));
    }

    // ------------------------------------------------
    // 5) Success → issue short-lived JWT
    // ------------------------------------------------
    _logger.LogInformation("PoW verify success: project={ProjectId}, solveMs={SolveMs}, difficulty={Difficulty}, sdkVersion={SdkVersion}", 
        project.Id, solveMs, difficultyBits, sdkVersion);
    await _difficulty.RecordResultAsync(
        project.Id,
        solveMs,
        success: true,
        ct
    );
    await _attempts.RecordAsync(
        project.Id,
        solveMs,
        success: true,
        ct
    );

    var subjectUserId =
        $"pow:{project.Id}:{req.ChallengeHex}";

    var extraClaims = new[]
    {
        new Claim("projectId", project.Id.ToString()),
        new Claim("projectPublicKey", project.PublicKey),
        new Claim("authType", "pow")
    };

    var token = _jwt.GenerateJwtToken(
        userId: subjectUserId,
        role: "User",
        extraClaims: extraClaims,
        expiresUtc: DateTime.UtcNow.AddMinutes(10)
    );

    return Ok(new PowVerifyResponse(true, token, null));
}
}