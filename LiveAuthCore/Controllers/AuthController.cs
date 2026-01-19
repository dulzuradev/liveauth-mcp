using LiveAuthCore.Data;
using LiveAuthCore.Models;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize] // <-- API key auth scheme protects these endpoints
public class AuthController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly DeveloperVerificationService _devAuth;

    public AuthController(LiveAuthDbContext db, DeveloperVerificationService devAuth)
    {
        _db = db;
        _devAuth = devAuth;
    }

    private Guid GetProjectId()
        => Guid.Parse(User.Claims.Single(c => c.Type == "projectId").Value);

    /// <summary>
    /// Dev backend calls this to start a Lightning verification session.
    /// Requires: Authorization: Bearer la_sk_...
    /// </summary>
    [HttpPost("start")]
    public async Task<ActionResult<AuthStartResponse>> Start([FromBody] AuthStartRequest req)
    {
        var projectId = GetProjectId();
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return Unauthorized();

        var session = await _devAuth.StartSessionAsync(
            project,
            req.UserRef,
            req.AmountSats,
            req.Memo
        );

        return Ok(new AuthStartResponse
        {
            SessionId = session.Id,
            Invoice = session.Invoice,
            PaymentHash = session.PaymentHashB64,
            AmountSats = session.AmountSats,
            ExpiresAtUnix = new DateTimeOffset(session.ExpiresAt).ToUnixTimeSeconds()
        });
    }

    /// <summary>
    /// Dev backend polls this to confirm settlement + receive LiveAuth token.
    /// </summary>
    [HttpPost("confirm")]
    public async Task<ActionResult<AuthConfirmResponse>> Confirm([FromBody] AuthConfirmRequest req)
    {
        var projectId = GetProjectId();
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return Unauthorized();

        var (verified, token) = await _devAuth.ConfirmSessionAsync(project, req.SessionId);

        return Ok(new AuthConfirmResponse
        {
            Verified = verified,
            Token = token,
            Method = "lightning",
            ExpiresIn = 300
        });
    }

    /// <summary>
    /// Anyone can verify a LiveAuth token (no key required).
    /// Useful for clients or edge services.
    /// </summary>
    [HttpPost("verify-token")]
    [AllowAnonymous]
    public ActionResult<VerifyTokenResponse> VerifyToken([FromBody] VerifyTokenRequest req)
    {
        var ok = _devAuth.VerifyLiveAuthToken(req.Token, out var claims);

        return Ok(new VerifyTokenResponse
        {
            Valid = ok,
            Claims = ok ? claims.ToDictionary(k => k.Key, v => v.Value) : null
        });
    }
}
