using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Data.Entities.Mcp;
using LiveAuthCore.Models.Mcp;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers.Mcp;

[ApiController]
[Route("api/mcp")]
[AllowAnonymous] // Secured by PublicKeyAuthMiddleware (X-LW-Public)
public class McpGateController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _lightning;
    private readonly LightningService _jwt;
    private readonly PowChallengeSigner _signer;
    private readonly PowDifficultyService _difficulty;
    private readonly ApiKeyService _apiKeyService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<McpGateController> _logger;

    public McpGateController(
        LiveAuthDbContext db,
        LightningService lightning,
        LightningService jwt,
        PowChallengeSigner signer,
        PowDifficultyService difficulty,
        ApiKeyService apiKeyService,
        IConfiguration configuration,
        ILogger<McpGateController> logger)
    {
        _db = db;
        _lightning = lightning;
        _jwt = jwt;
        _signer = signer;
        _difficulty = difficulty;
        _apiKeyService = apiKeyService;
        _configuration = configuration;
        _logger = logger;
    }

    private Project? GetProject()
    {
        // First try to get from middleware-set item (regular API key auth)
        if (HttpContext.Items.TryGetValue("LW_Project", out var value) && value is Project proj)
            return proj;

        // Fallback to demo project if no API key provided or auth failed
        return GetDemoProjectAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task<Project?> GetDemoProjectAsync(CancellationToken ct)
    {
        var demoProjectId = _configuration["LiveAuth:DemoProjectId"];
        if (!Guid.TryParse(demoProjectId, out var projectId))
            return null;

        return await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p =>
                    p.Id == projectId &&
                    p.IsActive,
                ct);
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

    private static string BuildPowPayload(Guid projectId, string challengeHex, int difficultyBits, long expiresAtUnix)
        => $"{projectId}:{challengeHex}:{difficultyBits}:{expiresAtUnix}";

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] McpStartRequest req, CancellationToken ct)
    {
        var project = GetProject();
        if (project == null) return Unauthorized();
        if (!project.IsActive) return Forbid();

        // v1 config: reuse existing per-project sats/login as sats/call until we add explicit fields.
        var satsPerCall = Math.Clamp(project.SatsPerLogin, 1, 50);

        // Default to PoW unless ForceLightning
        var forceLightning = req.ForceLightning == true;

        McpGateSession session;
        object? powChallenge = null;
        McpInvoice? invoice = null;

        if (forceLightning)
        {
            // Generate Lightning invoice
            var satsAmount = satsPerCall * 10; // Default: 10x sats per call as buffer
            
            var invoiceResult = await _lightning.CreateLoginInvoiceAsync(
                $"mcp:{project.Id}",
                satsAmount,
                10,
                project
            );

            session = new McpGateSession
            {
                ProjectId = project.Id,
                LightningInvoice = invoiceResult.Bolt11,
                LightningPaymentHash = invoiceResult.InvoiceId,
                SatsPerCallAtStart = satsPerCall,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };

            invoice = new McpInvoice(
                Bolt11: invoiceResult.Bolt11,
                AmountSats: invoiceResult.AmountSats,
                ExpiresAtUnix: invoiceResult.ExpiresAtUnix,
                PaymentHash: invoiceResult.InvoiceId
            );
        }
        else
        {
            var difficultyBits = await _difficulty.GetDifficultyAsync(project, ct);
            var challengeHex = RandomHex(16);
            var expiresAtUnix = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
            var payload = BuildPowPayload(project.Id, challengeHex, difficultyBits, expiresAtUnix);
            var sig = _signer.Sign(payload);

            session = new McpGateSession
            {
                ProjectId = project.Id,
                PowChallengeHex = challengeHex,
                PowDifficultyBits = difficultyBits,
                PowExpiresAtUnix = expiresAtUnix,
                PowSignature = sig,
                SatsPerCallAtStart = satsPerCall,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };

            var target = TargetFromBits(difficultyBits);
            powChallenge = new
            {
                projectId = project.Id,
                projectPublicKey = project.PublicKey,
                challengeHex,
                targetHex = Convert.ToHexString(target).ToLowerInvariant(),
                difficultyBits,
                expiresAtUnix,
                signature = sig
            };
        }

        _db.McpGateSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return Ok(new McpStartResponse(
            QuoteId: session.Id.ToString(),
            PowChallenge: powChallenge,
            Invoice: invoice
        ));
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] McpConfirmRequest req, CancellationToken ct)
    {
        var project = GetProject();
        if (project == null) return Unauthorized();
        if (!project.IsActive) return Forbid();

        if (!Guid.TryParse(req.QuoteId, out var sessionId))
            return BadRequest("Invalid quoteId");

        var session = await _db.McpGateSessions
            .Where(s => s.Id == sessionId && s.ProjectId == project.Id)
            .FirstOrDefaultAsync(ct);

        if (session == null) return NotFound();
        if (session.Status != "pending") return BadRequest("Session not pending");
        if (session.ExpiresAt < DateTime.UtcNow) return BadRequest("Session expired");

        // PoW confirm
        if (!string.IsNullOrWhiteSpace(req.ChallengeHex))
        {
            if (req.ChallengeHex != session.PowChallengeHex)
                return BadRequest("Challenge mismatch");

            if (req.DifficultyBits == null || req.ExpiresAtUnix == null || string.IsNullOrWhiteSpace(req.Sig))
                return BadRequest("Missing PoW signed fields");

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (req.ExpiresAtUnix <= nowUnix)
                return BadRequest("Challenge expired");

            var payload = BuildPowPayload(project.Id, req.ChallengeHex, req.DifficultyBits.Value, req.ExpiresAtUnix.Value);
            if (!_signer.Verify(payload, req.Sig))
                return Unauthorized("Invalid signature");

            if (req.Nonce == null || string.IsNullOrWhiteSpace(req.HashHex))
                return BadRequest("Missing PoW solution");

            // Hash check
            var input = $"{project.PublicKey}:{req.ChallengeHex}:{req.Nonce.Value}";
            var computed = Sha256Bytes(input);
            var computedHex = Convert.ToHexString(computed).ToLowerInvariant();
            if (!computedHex.Equals(req.HashHex, StringComparison.OrdinalIgnoreCase))
                return Unauthorized("Hash mismatch");

            // Difficulty check
            var target = TargetFromBits(req.DifficultyBits.Value);
            if (!IsLessThan(computed, target))
                return Unauthorized("Difficulty not met");

            // Success → mint gate JWT (short-lived)
            session.Status = "confirmed";

            var jti = Guid.NewGuid().ToString("N");
            var expiresUtc = DateTime.UtcNow.AddMinutes(10);

            var extraClaims = new[]
            {
                new Claim("projectId", project.Id.ToString()),
                new Claim("authType", "mcp_pow"),
                new Claim("mcpQuoteId", session.Id.ToString()),
                new Claim("jti", jti)
            };

            var tokenJwt = _jwt.GenerateJwtToken(
                userId: $"mcp:{project.Id}:{session.Id}",
                role: "McpClient",
                extraClaims: extraClaims,
                expiresUtc: expiresUtc
            );

            var gateToken = new McpGateToken
            {
                ProjectId = project.Id,
                SessionId = session.Id,
                JwtId = jti,
                RefreshToken = Guid.NewGuid().ToString("N"),
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = expiresUtc,
                CallsUsed = 0,
                SatsUsed = 0,
                // v1: placeholder limits; will move to Project config
                MaxCallsPerMinute = 60,
                MaxSatsPerDay = 10_000,
                DayWindowStart = DateTime.UtcNow.Date,
                Status = "active"
            };

            _db.McpGateTokens.Add(gateToken);
            await _db.SaveChangesAsync(ct);

            var remaining = gateToken.MaxSatsPerDay - gateToken.SatsUsed;

            return Ok(new McpConfirmResponse(
                Jwt: tokenJwt,
                ExpiresIn: (int)TimeSpan.FromMinutes(10).TotalSeconds,
                RemainingBudgetSats: remaining,
                RefreshToken: gateToken.RefreshToken
            ));
        }

        // Lightning confirm - check if invoice is paid
        if (!string.IsNullOrWhiteSpace(session.LightningPaymentHash))
        {
            var status = await _lightning.GetInvoiceStatusAsync(session.LightningPaymentHash, project);

            if (!status.IsPaid)
            {
                return Ok(new McpConfirmResponse(
                    Jwt: null,
                    ExpiresIn: 0,
                    RemainingBudgetSats: 0,
                    PaymentStatus: "pending"
                ));
            }

            // Payment confirmed - mint JWT
            session.Status = "confirmed";

            var jti = Guid.NewGuid().ToString("N");
            var expiresUtc = DateTime.UtcNow.AddMinutes(10);

            var extraClaims = new[]
            {
                new Claim("projectId", project.Id.ToString()),
                new Claim("authType", "mcp_lightning"),
                new Claim("mcpQuoteId", session.Id.ToString()),
                new Claim("jti", jti)
            };

            var tokenJwt = _jwt.GenerateJwtToken(
                userId: $"mcp:{project.Id}:{session.Id}",
                role: "McpClient",
                extraClaims: extraClaims,
                expiresUtc: expiresUtc
            );

            var gateToken = new McpGateToken
            {
                ProjectId = project.Id,
                SessionId = session.Id,
                JwtId = jti,
                RefreshToken = Guid.NewGuid().ToString("N"),
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = expiresUtc,
                CallsUsed = 0,
                SatsUsed = 0,
                MaxCallsPerMinute = 60,
                MaxSatsPerDay = session.SatsPerCallAtStart * 100, // Prepaid buffer
                DayWindowStart = DateTime.UtcNow.Date,
                Status = "active"
            };

            _db.McpGateTokens.Add(gateToken);
            await _db.SaveChangesAsync(ct);

            return Ok(new McpConfirmResponse(
                Jwt: tokenJwt,
                ExpiresIn: (int)TimeSpan.FromMinutes(10).TotalSeconds,
                RemainingBudgetSats: gateToken.MaxSatsPerDay,
                PaymentStatus: "paid",
                RefreshToken: gateToken.RefreshToken
            ));
        }

        return BadRequest("No valid confirmation method provided");
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] McpRefreshRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return BadRequest("Refresh token required");

        var gateToken = await _db.McpGateTokens
            .Where(t => t.RefreshToken == req.RefreshToken && t.Status == "active")
            .FirstOrDefaultAsync(ct);

        if (gateToken == null)
            return Unauthorized("Invalid refresh token");

        // Get project for JWT generation
        var project = await _db.Projects
            .Where(p => p.Id == gateToken.ProjectId && p.IsActive)
            .FirstOrDefaultAsync(ct);

        if (project == null)
            return Forbid();

        // Generate new JWT
        var newJti = Guid.NewGuid().ToString("N");
        var expiresUtc = DateTime.UtcNow.AddMinutes(10);

        var extraClaims = new[]
        {
            new Claim("projectId", project.Id.ToString()),
            new Claim("authType", "mcp_refresh"),
            new Claim("mcpQuoteId", gateToken.SessionId.ToString()),
            new Claim("jti", newJti)
        };

        var tokenJwt = _jwt.GenerateJwtToken(
            userId: $"mcp:{project.Id}:{gateToken.SessionId}",
            role: "McpClient",
            extraClaims: extraClaims,
            expiresUtc: expiresUtc
        );

        // Update token record
        gateToken.JwtId = newJti;
        gateToken.ExpiresAt = expiresUtc;
        await _db.SaveChangesAsync(ct);

        var remaining = gateToken.MaxSatsPerDay - gateToken.SatsUsed;

        return Ok(new McpRefreshResponse(
            Jwt: tokenJwt,
            ExpiresIn: (int)TimeSpan.FromMinutes(10).TotalSeconds,
            RemainingBudgetSats: remaining
        ));
    }

    /// <summary>
    /// LNURL-compatible endpoint for lnget. Returns the Lightning invoice for polling.
    /// GET /api/mcp/lnurl/{quoteId}
    /// </summary>
    [HttpGet("lnurl/{quoteId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLnurl(string quoteId, CancellationToken ct)
    {
        var project = GetProject();
        if (project == null) return Unauthorized();
        if (!project.IsActive) return Forbid();

        if (!Guid.TryParse(quoteId, out var sessionId))
            return BadRequest("Invalid quoteId");

        var session = await _db.McpGateSessions
            .Where(s => s.Id == sessionId && s.ProjectId == project.Id)
            .FirstOrDefaultAsync(ct);

        if (session == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(session.LightningInvoice))
            return BadRequest("No Lightning invoice for this session");

        // Return lnget-compatible format
        return Ok(new
        {
            pr = session.LightningInvoice,
            routes = Array.Empty<string>()
        });
    }

    [HttpGet("status/{quoteId}")]
    public async Task<IActionResult> GetStatus(string quoteId, CancellationToken ct)
    {
        var project = GetProject();
        if (project == null) return Unauthorized();
        if (!project.IsActive) return Forbid();

        if (!Guid.TryParse(quoteId, out var sessionId))
            return BadRequest("Invalid quoteId");

        var session = await _db.McpGateSessions
            .Where(s => s.Id == sessionId && s.ProjectId == project.Id)
            .FirstOrDefaultAsync(ct);

        if (session == null)
            return NotFound();

        // If Lightning, check payment status
        string? paymentStatus = null;
        if (!string.IsNullOrWhiteSpace(session.LightningPaymentHash))
        {
            var status = await _lightning.GetInvoiceStatusAsync(session.LightningPaymentHash, project);
            paymentStatus = status.IsPaid ? "paid" : "pending";
        }

        return Ok(new
        {
            quoteId = session.Id.ToString(),
            status = session.Status,
            paymentStatus,
            expiresAt = session.ExpiresAt
        });
    }

    [HttpPost("charge")]
    [Authorize] // requires JWT
    public async Task<IActionResult> Charge([FromBody] McpChargeRequest req, CancellationToken ct)
    {
        if (req.CallCostSats <= 0) return BadRequest("callCostSats must be positive");

        var projectIdStr = User.FindFirst("projectId")?.Value;
        var jti = User.FindFirst("jti")?.Value;

        if (string.IsNullOrWhiteSpace(projectIdStr) || string.IsNullOrWhiteSpace(jti))
            return Unauthorized("Missing projectId/jti");

        if (!Guid.TryParse(projectIdStr, out var projectId))
            return Unauthorized("Invalid projectId");

        var gateToken = await _db.McpGateTokens
            .Where(t => t.ProjectId == projectId && t.JwtId == jti && t.Status == "active")
            .FirstOrDefaultAsync(ct);

        if (gateToken == null) return Unauthorized("Unknown token");
        if (gateToken.ExpiresAt < DateTime.UtcNow) return Unauthorized("Token expired");

        // Get project for L402 balance check
        var project = await _db.Projects
            .Where(p => p.Id == gateToken.ProjectId && p.IsActive)
            .FirstOrDefaultAsync(ct);

        if (project == null) return Forbid("Project not active");

        // Reset day window if needed
        if (gateToken.DayWindowStart.Date != DateTime.UtcNow.Date)
        {
            gateToken.DayWindowStart = DateTime.UtcNow.Date;
            gateToken.SatsUsed = 0;
            gateToken.CallsUsed = 0;
        }

        // Check L402 balance first - charge from balance if available
        if (project.L402BalanceSats >= req.CallCostSats)
        {
            project.L402BalanceSats -= req.CallCostSats;
            gateToken.CallsUsed += 1;
            gateToken.SatsUsed += req.CallCostSats;
            await _db.SaveChangesAsync(ct);
            return Ok(new McpChargeResponse("ok", gateToken.CallsUsed, gateToken.SatsUsed));
        }

        // Fall back to session budget
        if (gateToken.SatsUsed + req.CallCostSats > gateToken.MaxSatsPerDay)
        {
            return Ok(new McpChargeResponse("deny", gateToken.CallsUsed, gateToken.SatsUsed));
        }

        gateToken.CallsUsed += 1;
        gateToken.SatsUsed += req.CallCostSats;

        await _db.SaveChangesAsync(ct);

        return Ok(new McpChargeResponse("ok", gateToken.CallsUsed, gateToken.SatsUsed));
    }

    [HttpGet("usage")]
    [Authorize]
    public async Task<IActionResult> GetUsage(CancellationToken ct)
    {
        var projectIdStr = User.FindFirst("projectId")?.Value;
        var jti = User.FindFirst("jti")?.Value;

        if (string.IsNullOrWhiteSpace(projectIdStr) || string.IsNullOrWhiteSpace(jti))
            return Unauthorized("Missing projectId/jti");

        if (!Guid.TryParse(projectIdStr, out var projectId))
            return Unauthorized("Invalid projectId");

        var gateToken = await _db.McpGateTokens
            .Where(t => t.ProjectId == projectId && t.JwtId == jti && t.Status == "active")
            .FirstOrDefaultAsync(ct);

        if (gateToken == null)
            return NotFound("No active session found");

        // Reset day window if needed
        if (gateToken.DayWindowStart.Date != DateTime.UtcNow.Date)
        {
            gateToken.DayWindowStart = DateTime.UtcNow.Date;
            gateToken.SatsUsed = 0;
            gateToken.CallsUsed = 0;
            await _db.SaveChangesAsync(ct);
        }

        var remaining = gateToken.MaxSatsPerDay - gateToken.SatsUsed;

        return Ok(new McpUsageResponse(
            Status: gateToken.Status,
            CallsUsed: gateToken.CallsUsed,
            SatsUsed: gateToken.SatsUsed,
            MaxSatsPerDay: gateToken.MaxSatsPerDay,
            RemainingBudgetSats: remaining,
            MaxCallsPerMinute: gateToken.MaxCallsPerMinute,
            ExpiresAt: gateToken.ExpiresAt,
            DayWindowStart: gateToken.DayWindowStart
        ));
    }
}
