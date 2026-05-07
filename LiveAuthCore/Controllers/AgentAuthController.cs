using System.Security.Cryptography;
using System.Text;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

/// <summary>
/// Agent authentication for AI agents (OpenClaw, etc.)
/// </summary>
[ApiController]
[Route("api/agent/auth")]
[AllowAnonymous]
public class AgentAuthController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _lightning;
    private readonly IConfiguration _config;
    private readonly ILogger<AgentAuthController> _logger;

    public AgentAuthController(
        LiveAuthDbContext db,
        LightningService lightning,
        IConfiguration config,
        ILogger<AgentAuthController> logger)
    {
        _db = db;
        _lightning = lightning;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Start agent authentication - returns a PoW challenge
    /// </summary>
    [HttpPost("start")]
    public async Task<ActionResult<AgentAuthStartResponse>> StartAgentAuth(
        [FromBody] AgentAuthStartRequest request,
        CancellationToken ct)
    {
        // Validate request
        if (string.IsNullOrWhiteSpace(request.AgentId))
            return BadRequest(new { error = "AgentId is required" });

        if (string.IsNullOrWhiteSpace(request.PublicKey))
            return BadRequest(new { error = "PublicKey is required" });

        // Get project by API key or public key
        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.SecretKeyHash == request.PublicKey, ct);

        if (project == null)
        {
            // Try by public key field
            project = await _db.Projects
                .FirstOrDefaultAsync(p => p.PublicKey == request.PublicKey, ct);
        }

        if (project == null)
            return Unauthorized(new { error = "Invalid agent credentials" });

        if (!project.IsActive)
            return Forbid("Project is inactive.");

        // Generate PoW challenge
        var challenge = GenerateChallenge(request.AgentId, project.Id.ToString());
        var difficulty = _config.GetValue<int>("LiveAuth:AgentPowDifficulty", 16);

        var session = new AgentAuthSession
        {
            Id = Guid.NewGuid(),
            AgentId = request.AgentId,
            ProjectId = project.Id,
            Challenge = challenge,
            DifficultyBits = difficulty,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            IsVerified = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.AgentAuthSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Agent auth started for {AgentId} on project {ProjectId}",
            request.AgentId, project.Id);

        return Ok(new AgentAuthStartResponse
        {
            SessionId = session.Id,
            Challenge = challenge,
            DifficultyBits = difficulty,
            ExpiresAtUnix = new DateTimeOffset(session.ExpiresAt).ToUnixTimeSeconds()
        });
    }

    /// <summary>
    /// Verify PoW solution and get auth token
    /// </summary>
    [HttpPost("verify")]
    public async Task<ActionResult<AgentAuthVerifyResponse>> VerifyAgentAuth(
        [FromBody] AgentAuthVerifyRequest request,
        CancellationToken ct)
    {
        if (!request.SessionId.HasValue)
            return BadRequest(new { error = "SessionId is required" });

        if (string.IsNullOrWhiteSpace(request.Solution))
            return BadRequest(new { error = "Solution is required" });

        var session = await _db.AgentAuthSessions
            .Include(s => s.Project)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, ct);

        if (session == null)
            return BadRequest(new { error = "Invalid session" });

        if (DateTime.UtcNow > session.ExpiresAt)
            return BadRequest(new { error = "Session expired" });

        if (session.IsVerified)
            return Ok(new AgentAuthVerifyResponse
            {
                Verified = true,
                Token = session.AuthToken,
                ExpiresAtUnix = new DateTimeOffset(session.ExpiresAt).ToUnixTimeSeconds()
            });

        // Verify PoW solution
        var isValid = VerifySolution(
            session.Challenge,
            request.Solution,
            session.DifficultyBits);

        if (!isValid)
        {
            _logger.LogWarning("Invalid PoW solution for agent session {SessionId}", session.Id);
            return Ok(new AgentAuthVerifyResponse
            {
                Verified = false,
                Error = "Invalid solution"
            });
        }

        // Generate auth token
        var token = GenerateToken(session.AgentId, session.ProjectId.ToString());
        session.IsVerified = true;
        session.AuthToken = token;
        session.SolvedAt = DateTime.UtcNow;
        session.ExpiresAt = DateTime.UtcNow.AddHours(24); // Token valid for 24h

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Agent {AgentId} authenticated successfully", session.AgentId);

        return Ok(new AgentAuthVerifyResponse
        {
            Verified = true,
            Token = token,
            ExpiresAtUnix = new DateTimeOffset(session.ExpiresAt).ToUnixTimeSeconds()
        });
    }

    /// <summary>
    /// Validate an agent auth token
    /// </summary>
    [HttpPost("validate")]
    public async Task<ActionResult<AgentAuthValidateResponse>> ValidateToken(
        [FromBody] AgentAuthValidateRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "Token is required" });

        var session = await _db.AgentAuthSessions
            .Include(s => s.Project)
            .FirstOrDefaultAsync(s => s.AuthToken == request.Token, ct);

        if (session == null)
            return Ok(new AgentAuthValidateResponse { Valid = false });

        if (DateTime.UtcNow > session.ExpiresAt)
            return Ok(new AgentAuthValidateResponse { Valid = false });

        return Ok(new AgentAuthValidateResponse
        {
            Valid = true,
            AgentId = session.AgentId,
            ProjectId = session.ProjectId,
            ProjectName = session.Project?.Name,
            ExpiresAtUnix = new DateTimeOffset(session.ExpiresAt).ToUnixTimeSeconds()
        });
    }

    private static string GenerateChallenge(string agentId, string projectId)
    {
        var data = $"{agentId}:{projectId}:{DateTime.UtcNow.Ticks}:{Guid.NewGuid()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(bytes);
    }

    private static bool VerifySolution(string challenge, string solution, int difficultyBits)
    {
        // Solution should be: challenge + ":" + nonce
        var parts = solution.Split(':');
        if (parts.Length != 2) return false;

        var nonce = parts[1];
        var dataToHash = challenge + ":" + nonce;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(dataToHash));

        // Build target: difficulty bits must be leading zeros
        // e.g., 17 bits → first byte has top 7 bits zero (bits 4-0 of byte 0)
        // Formula: target[i] = 0xFF >> (8 - (difficulty % 8)) for partial byte
        var target = new byte[32];
        int fullBytes = difficultyBits / 8;
        int remBits = difficultyBits % 8;

        for (int i = 0; i < fullBytes; i++)
            target[i] = 0x00;

        if (fullBytes < 32)
            target[fullBytes] = (byte)(0xFF >> remBits);
        for (int i = fullBytes + 1; i < 32; i++)
            target[i] = 0xFF;

        // Constant-time comparison: check if hash < target
        return IsLessThan(hash, target);
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

    private static string GenerateToken(string agentId, string projectId)
    {
        var data = $"{agentId}:{projectId}:{DateTime.UtcNow.Ticks}:{Guid.NewGuid()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(bytes);
    }
}

// Request/Response DTOs
public class AgentAuthStartRequest
{
    public string AgentId { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
}

public class AgentAuthStartResponse
{
    public Guid SessionId { get; set; }
    public string Challenge { get; set; } = string.Empty;
    public int DifficultyBits { get; set; }
    public long ExpiresAtUnix { get; set; }
}

public class AgentAuthVerifyRequest
{
    public Guid? SessionId { get; set; }
    public string Solution { get; set; } = string.Empty;
}

public class AgentAuthVerifyResponse
{
    public bool Verified { get; set; }
    public string? Token { get; set; }
    public long ExpiresAtUnix { get; set; }
    public string? Error { get; set; }
}

public class AgentAuthValidateRequest
{
    public string Token { get; set; } = string.Empty;
}

public class AgentAuthValidateResponse
{
    public bool Valid { get; set; }
    public string? AgentId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public long ExpiresAtUnix { get; set; }
}
