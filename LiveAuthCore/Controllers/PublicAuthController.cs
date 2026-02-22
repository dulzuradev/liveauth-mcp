using System.Security.Claims;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Middleware;
using LiveAuthCore.Models;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/public/auth")]
[AllowAnonymous] // API-key middleware secures this
public class PublicAuthController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _lightning;
    private readonly IConfiguration _configuration;

    public PublicAuthController(
        LiveAuthDbContext db,
        LightningService lightning,
        IConfiguration configuration)
    {
        _db = db;
        _lightning = lightning;
        _configuration = configuration;
    }

    private Project? GetCurrentProject()
    {
        if (HttpContext.Items.TryGetValue("LW_Project", out var p) && p is Project proj1)
            return proj1;

        if (HttpContext.Items.TryGetValue(HttpContextKeys.Project, out var q) && q is Project proj2)
            return proj2;

        if (HttpContext.Items.TryGetValue("Project", out var r) && r is Project proj3)
            return proj3;

        // Fallback: look up project from X-LW-Public header directly
        if (Request.Headers.TryGetValue("X-LW-Public", out var pubKeyValues) && 
            !string.IsNullOrWhiteSpace(pubKeyValues.FirstOrDefault()))
        {
            var pubKey = pubKeyValues.FirstOrDefault();
            return _db.Projects.FirstOrDefault(p => p.PublicKey == pubKey && p.IsActive);
        }

        // 👇 Optional but very helpful
        HttpContext.RequestServices
            .GetService<ILogger<PublicAuthController>>()?
            .LogWarning("LiveAuth: Project missing from HttpContext.Items");

        return null;
    }
    
    
    
    private void EnsureMonthlyWindow(Project project)
    {
        if (project.MonthlyAuthPeriodStart == default ||
            project.MonthlyAuthPeriodStart.AddMonths(1) <= DateTime.UtcNow)
        {
            project.MonthlyAuthPeriodStart = DateTime.UtcNow;
            project.MonthlyAuthCount = 0;
        }
    }
    
    private string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();

    // POST /api/public/auth/start
    [HttpPost("start")]
    public async Task<ActionResult<PublicStartAuthResponse>> Start(
        [FromBody] PublicStartAuthRequest request,
        CancellationToken ct)
    {
        var project = GetCurrentProject();
        if (project == null)
            return Unauthorized("Missing or invalid API key.");

        if (!project.IsActive)
            return Forbid("Project is inactive.");
        
        EnsureMonthlyWindow(project);

        // TEST mode is unlimited
        var env = (project.Environment ?? "TEST").Trim().ToUpperInvariant();
        if (env != "TEST")
        {
            // Free tier enforcement
            var isFreeTier = project.Plan == "free";
            if (isFreeTier)
            {
                if (project.MonthlyAuthCount >= PlanLimits.FreeMonthlyAuthLimit)
                {
                    return StatusCode(StatusCodes.Status402PaymentRequired, new
                    {
                        error = "upgrade_required",
                        message = "Monthly free tier limit exceeded",
                        limit = PlanLimits.FreeMonthlyAuthLimit
                    });
                }

                if (project.SatsPerLogin > PlanLimits.FreeMaxSatsPerAuth)
                {
                    return StatusCode(StatusCodes.Status402PaymentRequired, new
                    {
                        error = "upgrade_required",
                        message = "Free tier max sats per verification exceeded",
                        maxSats = PlanLimits.FreeMaxSatsPerAuth
                    });
                }
            }
        }
        
        var clientIp = GetClientIp();

        var maxPerIpPerHour = project.MaxAuthsPerIpPerHour > 0
            ? project.MaxAuthsPerIpPerHour
            : 30;

        if (!string.IsNullOrWhiteSpace(clientIp) && maxPerIpPerHour > 0)
        {
            var cutoff = DateTime.UtcNow.AddHours(-1);

            var recentCount = await _db.AuthSessions
                .Where(s =>
                    s.ProjectId == project.Id &&
                    s.ClientIp == clientIp &&
                    s.CreatedAt >= cutoff)
                .CountAsync(ct);

            if (recentCount >= maxPerIpPerHour)
            {
                _db.AuthEvents.Add(new AuthEvent
                {
                    Id = Guid.NewGuid(),
                    ProjectId = project.Id,
                    ApiKeyId = null,
                    EventType = AuthEventType.RateLimitHit,
                    CreatedAt = DateTime.UtcNow,
                    ClientIp = clientIp,
                    Success = false,
                    SatsPaid = 0,
                    Reason = "RATE_LIMIT"
                });

                await _db.SaveChangesAsync(ct);

                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    error = "Too many auth attempts from this IP. Try again later."
                });
            }
        }

        long satsPerLogin = env == "TEST"
            ? 21
            : (project.SatsPerLogin > 0 ? project.SatsPerLogin : 21L);

        var expiryMinutes = 10;
        string? bolt11 = null;
        string? rHashHex = null;

        if ((env == "LIVE" && satsPerLogin > 0) || (request.UserHint != null && request.UserHint == "demo-user"))
        {
            var memo = $"LightningWall login – project {project.Name}";
            // CENTRALIZED: Use new method that returns hex payment hash
            var invoiceResult = await _lightning.CreateInvoiceWithHashAsync(
                project.Id.ToString(),
                satsPerLogin,
                memo);

            bolt11 = invoiceResult.Bolt11;
            rHashHex = invoiceResult.PaymentHash;
        }

        var session = new AuthSession
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Environment = env,
            UserHint = request.UserHint?.Trim(),
            AmountSats = satsPerLogin,
            InvoiceRHash = rHashHex,
            InvoiceBolt11 = bolt11,
            ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes),
            IsPaid = false,
            ClientIp = clientIp,
            CreatedAt = DateTime.UtcNow
        };

        _db.AuthSessions.Add(session);

        _db.AuthEvents.Add(new AuthEvent
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ApiKeyId = null,
            EventType = AuthEventType.LoginRequested,
            CreatedAt = DateTime.UtcNow,
            ClientIp = clientIp,
            Success = false,
            SatsPaid = null,
            Reason = "PUBLIC_AUTH_START"
        });
        
        project.MonthlyAuthCount += 1;
        _db.Projects.Update(project);

        await _db.SaveChangesAsync(ct);

        return Ok(new PublicStartAuthResponse
        {
            SessionId = session.Id,
            Invoice = bolt11, // null in TEST
            AmountSats = satsPerLogin,
            ExpiresAtUnix = new DateTimeOffset(session.ExpiresAt).ToUnixTimeSeconds(),
            Mode = env
        });
    }

    // POST /api/public/auth/confirm
    [HttpPost("confirm")]
    public async Task<ActionResult<PublicConfirmAuthResponse>> Confirm(
        [FromBody] PublicConfirmAuthRequest request,
        CancellationToken ct)
    {
        var project = GetCurrentProject();
        if (project == null)
            return Unauthorized("Missing or invalid API key.");

        var session = await _db.AuthSessions
            .Include(s => s.Project)
            .SingleOrDefaultAsync(s => s.Id == request.SessionId, ct);

        if (session == null || session.ProjectId != project.Id)
        {
            return Ok(new PublicConfirmAuthResponse { Verified = false, Token = null });
        }

        if (DateTime.UtcNow > session.ExpiresAt)
        {
            _db.AuthEvents.Add(new AuthEvent
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                ApiKeyId = null,
                EventType = AuthEventType.LoginFailed,
                CreatedAt = DateTime.UtcNow,
                ClientIp = session.ClientIp,
                Success = false,
                SatsPaid = null,
                Reason = "SESSION_EXPIRED"
            });
            await _db.SaveChangesAsync(ct);

            return Ok(new PublicConfirmAuthResponse { Verified = false, Token = null });
        }

        var currentIp = GetClientIp();

        if (!string.IsNullOrWhiteSpace(session.ClientIp) &&
            !string.IsNullOrWhiteSpace(currentIp) &&
            !string.Equals(session.ClientIp, currentIp, StringComparison.OrdinalIgnoreCase))
        {
            _db.AuthEvents.Add(new AuthEvent
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                ApiKeyId = null,
                EventType = AuthEventType.LoginFailed,
                CreatedAt = DateTime.UtcNow,
                ClientIp = currentIp,
                Success = false,
                SatsPaid = null,
                Reason = "IP_MISMATCH"
            });
            await _db.SaveChangesAsync(ct);

            return Ok(new PublicConfirmAuthResponse { Verified = false, Token = null });
        }

        var env = (session.Environment ?? project.Environment ?? "TEST")
            .Trim()
            .ToUpperInvariant();
        
        var allowDemo =
            env == "TEST" &&
            project.AllowDemoAuth &&
            request.Simulate;

        if (allowDemo)
        {
            if (!session.IsPaid)
            {
                session.IsPaid = true;
                session.PaidAt = DateTime.UtcNow;

                _db.AuthEvents.Add(new AuthEvent
                {
                    Id = Guid.NewGuid(),
                    ProjectId = project.Id,
                    ApiKeyId = null,
                    EventType = AuthEventType.LoginSucceeded,
                    CreatedAt = DateTime.UtcNow,
                    ClientIp = session.ClientIp,
                    Success = true,
                    SatsPaid = 0,
                    Reason = "DEMO_SIMULATED_SUCCESS"
                });

                await _db.SaveChangesAsync(ct);
            }

            var demoToken = GenerateEndUserJwt(session, project);

            return Ok(new PublicConfirmAuthResponse
            {
                Verified = true,
                Token = demoToken
            });
        }

        // TEST mode: auto-verify
        if (env == "TEST" || session.AmountSats <= 0 || string.IsNullOrWhiteSpace(session.InvoiceRHash))
        {
            if (!session.IsPaid)
            {
                session.IsPaid = true;
                session.PaidAt = DateTime.UtcNow;

                _db.AuthEvents.Add(new AuthEvent
                {
                    Id = Guid.NewGuid(),
                    ProjectId = project.Id,
                    ApiKeyId = null,
                    EventType = AuthEventType.LoginSucceeded,
                    CreatedAt = DateTime.UtcNow,
                    ClientIp = session.ClientIp,
                    Success = true,
                    SatsPaid = 0,
                    Reason = "PUBLIC_AUTH_TEST"
                });

                await _db.SaveChangesAsync(ct);
            }

            return Ok(new PublicConfirmAuthResponse
            {
                Verified = true,
                Token = GenerateEndUserJwt(session, project)
            });
        }

        // LIVE mode: check invoice
        if (!session.IsPaid)
        {
            var status = await _lightning.GetInvoiceStatusAsync(session.InvoiceRHash!);
            if (status.IsPaid)
            {
                session.IsPaid = true;
                session.PaidAt = DateTime.UtcNow;
                session.PayerLightningAuthKey = status.PayerLightningAuthKey;

                _db.AuthEvents.Add(new AuthEvent
                {
                    Id = Guid.NewGuid(),
                    ProjectId = project.Id,
                    ApiKeyId = null,
                    EventType = AuthEventType.LoginSucceeded,
                    CreatedAt = DateTime.UtcNow,
                    ClientIp = session.ClientIp,
                    Success = true,
                    SatsPaid = session.AmountSats,
                    Reason = "PUBLIC_AUTH_PAID"
                });

                await _db.SaveChangesAsync(ct);
            }
        }

        if (!session.IsPaid)
        {
            return Ok(new PublicConfirmAuthResponse { Verified = false, Token = null });
        }

        return Ok(new PublicConfirmAuthResponse
        {
            Verified = true,
            Token = GenerateEndUserJwt(session, project)
        });
    }

    private string GenerateEndUserJwt(AuthSession session, Project project)
    {
        var subjectUserId = $"lw:{project.Id}:{session.Id}";

        var extraClaims = new[]
        {
            new Claim("projectId", project.Id.ToString()),
            new Claim("projectPublicKey", project.PublicKey ?? string.Empty),
            new Claim("lwEnv", session.Environment ?? project.Environment ?? "TEST"),
            new Claim("authSessionId", session.Id.ToString())
        };

        return _lightning.GenerateJwtToken(
            userId: subjectUserId,
            role: "User",
            extraClaims: extraClaims,
            expiresUtc: DateTime.UtcNow.AddHours(1)
        );
    }
}
