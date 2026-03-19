using System.Security.Claims;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

/// <summary>
/// Nostr-based agent authentication
/// </summary>
[ApiController]
[Route("api/public/nostr")]
[AllowAnonymous]
public class NostrAuthController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _lightning;
    private readonly NostrService _nostr;
    private readonly ILogger<NostrAuthController> _logger;

    private const int ChallengeExpiryMinutes = 10;

    public NostrAuthController(
        LiveAuthDbContext db,
        LightningService lightning,
        NostrService nostr,
        ILogger<NostrAuthController> logger)
    {
        _db = db;
        _lightning = lightning;
        _nostr = nostr;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/public/nostr/register
    /// Register a Nostr npub and get a verification challenge
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<NostrRegisterResponse>> Register(
        [FromBody] NostrRegisterRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Npub))
            return BadRequest(new { error = "npub is required" });

        string npubHex;
        try
        {
            npubHex = _nostr.NpubToHex(request.Npub);
        }
        catch
        {
            return BadRequest(new { error = "Invalid npub format" });
        }

        // Create a session for this Nostr agent
        var session = new NostrAgentSession
        {
            Id = Guid.NewGuid(),
            NpubHex = npubHex,
            Lud16 = request.Lud16?.Trim().ToLowerInvariant(),
            Challenge = _nostr.GenerateChallenge(Guid.NewGuid().ToString("N")),
            ExpiresAt = DateTime.UtcNow.AddMinutes(ChallengeExpiryMinutes),
            CreatedAt = DateTime.UtcNow
        };

        _db.NostrAgentSessions.Add(session);

        // Note: Nostr auth isn't tied to a specific project, so we skip AuthEvent logging
        // or you could use a system/agent project ID

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Nostr agent registered: {Npub}...", npubHex[..8]);

        return Ok(new NostrRegisterResponse(session.Id, session.Challenge));
    }

    /// <summary>
    /// POST /api/public/nostr/verify
    /// Verify the agent owns the private key by checking the Schnorr signature
    /// </summary>
    [HttpPost("verify")]
    public async Task<ActionResult<NostrVerifyResponse>> Verify(
        [FromBody] NostrVerifyRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) || !Guid.TryParse(request.SessionId, out var sessionId))
            return BadRequest(new { error = "Invalid session ID" });

        if (string.IsNullOrWhiteSpace(request.Sig))
            return BadRequest(new { error = "Signature is required" });

        var session = await _db.NostrAgentSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null)
            return NotFound(new { error = "Session not found" });

        if (DateTime.UtcNow > session.ExpiresAt)
            return BadRequest(new { error = "Challenge expired" });

        if (session.VerifiedAt != null)
            return BadRequest(new { error = "Session already verified" });

        // Verify the signature
        var isValid = _nostr.VerifySignature(request.Sig, session.Challenge, session.NpubHex);

        if (!isValid)
        {
            // Signature verification failed - don't log to AuthEvent since no project
            return Unauthorized(new { error = "Invalid signature" });
        }

        // Mark as verified
        session.VerifiedAt = DateTime.UtcNow;
        
        // Generate JWT token
        var token = GenerateNostrJwt(session);

        // Note: AuthEvent logging skipped for Nostr-only auth (no project)

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Nostr agent verified: {Npub}...", session.NpubHex[..8]);

        return Ok(new NostrVerifyResponse(token, _nostr.HexToNpub(session.NpubHex), session.Lud16));
    }

    private string GenerateNostrJwt(NostrAgentSession session)
    {
        var claims = new List<Claim>
        {
            new("sub", session.NpubHex),
            new("type", "nostr"),
            new("npub", _nostr.HexToNpub(session.NpubHex)),
            new("sessionId", session.Id.ToString())
        };

        if (!string.IsNullOrWhiteSpace(session.Lud16))
        {
            claims.Add(new Claim("lud16", session.Lud16));
        }

        return _lightning.GenerateJwtToken(
            userId: $"nostr:{session.NpubHex}",
            role: "NostrAgent",
            extraClaims: claims.ToArray(),
            expiresUtc: DateTime.UtcNow.AddHours(1)
        );
    }
}

// Request/Response DTOs
public record NostrRegisterRequest(string Npub, string? Lud16);

public class NostrRegisterResponse
{
    public Guid SessionId { get; init; }
    public string Challenge { get; init; }

    public NostrRegisterResponse(Guid sessionId, string challenge)
    {
        SessionId = sessionId;
        Challenge = challenge;
    }
}

public record NostrVerifyRequest(string SessionId, string Sig);

public class NostrVerifyResponse
{
    public string Token { get; init; }
    public string Npub { get; init; }
    public string? Lud16 { get; init; }

    public NostrVerifyResponse(string token, string npub, string? lud16)
    {
        Token = token;
        Npub = npub;
        Lud16 = lud16;
    }
}
