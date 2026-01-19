using System.Security.Claims;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/public/demo")]
[AllowAnonymous] // demo-only, API key middleware still applies
public class PublicDemoAuthController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _lightning;
    private readonly IConfiguration _configuration;

    public PublicDemoAuthController(
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

        if (HttpContext.Items.TryGetValue("Project", out var p2) && p2 is Project proj2)
            return proj2;

        return null;
    }

    private string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();

    // ───────────────────────────────────────────────────────────────
    // POST /api/public/demo/start
    // Creates a REAL Lightning invoice (tiny sats)
    // ───────────────────────────────────────────────────────────────
    [HttpPost("start")]
    public async Task<ActionResult<PublicStartAuthResponse>> StartDemo(
        CancellationToken ct)
    {
        var project = await GetDemoProjectAsync(ct);
        if (project == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                "Demo project is not configured.");
        }

        if (!project.IsActive)
            return Forbid("Project is inactive.");

        const long demoSats = 3;
        const int expiryMinutes = 15;

        var memo = $"LiveAuth Demo – Lightning Verification";

        var invoice = await _lightning.CreateInvoice(
            project.Id.ToString(),
            demoSats,
            memo);

        var session = new AuthSession
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Environment = "DEMO",
            AmountSats = demoSats,
            InvoiceRHash = invoice.RHash,
            InvoiceBolt11 = invoice.PaymentRequest,
            ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes),
            IsPaid = false,
            ClientIp = GetClientIp(),
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
            ClientIp = session.ClientIp,
            Success = false,
            SatsPaid = null,
            Reason = "DEMO_LIGHTNING_START"
        });

        await _db.SaveChangesAsync(ct);

        return Ok(new PublicStartAuthResponse
        {
            SessionId = session.Id,
            Invoice = invoice.PaymentRequest,
            AmountSats = demoSats,
            ExpiresAtUnix = new DateTimeOffset(session.ExpiresAt).ToUnixTimeSeconds(),
            Mode = "DEMO"
        });
    }

    // ───────────────────────────────────────────────────────────────
    // POST /api/public/demo/confirm
    // Polls Lightning until invoice is ACTUALLY paid
    // ───────────────────────────────────────────────────────────────
    [HttpPost("confirm")]
    public async Task<ActionResult<PublicConfirmAuthResponse>> ConfirmDemo(
        [FromBody] PublicConfirmAuthRequest request,
        CancellationToken ct)
    {
        var project = GetCurrentProject();
        if (project == null)
            return Unauthorized("Missing or invalid API key.");

        var session = await _db.AuthSessions
            .SingleOrDefaultAsync(s => s.Id == request.SessionId, ct);

        if (session == null || session.ProjectId != project.Id)
        {
            return Ok(new PublicConfirmAuthResponse
            {
                Verified = false,
                Token = null
            });
        }

        if (DateTime.UtcNow > session.ExpiresAt)
        {
            return Ok(new PublicConfirmAuthResponse
            {
                Verified = false,
                Token = null
            });
        }

        if (session.IsPaid)
        {
            return Ok(new PublicConfirmAuthResponse
            {
                Verified = true,
                Token = GenerateDemoJwt(session, project)
            });
        }

        var status = await _lightning.GetInvoiceStatusAsync(session.InvoiceRHash!);
        if (!status.IsPaid)
        {
            return Ok(new PublicConfirmAuthResponse
            {
                Verified = false,
                Token = null
            });
        }

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
            Reason = "DEMO_LIGHTNING_PAID"
        });

        await _db.SaveChangesAsync(ct);

        return Ok(new PublicConfirmAuthResponse
        {
            Verified = true,
            Token = GenerateDemoJwt(session, project)
        });
    }

    // ───────────────────────────────────────────────────────────────
    // Demo JWT (explicitly marked)
    // ───────────────────────────────────────────────────────────────
    private string GenerateDemoJwt(AuthSession session, Project project)
    {
        var subjectUserId = $"lw-demo:{project.Id}:{session.Id}";

        var claims = new[]
        {
            new Claim("projectId", project.Id.ToString()),
            new Claim("authSessionId", session.Id.ToString()),
            new Claim("lwEnv", "DEMO"),
            new Claim("lwDemo", "true")
        };

        return _lightning.GenerateJwtToken(
            userId: subjectUserId,
            role: "DemoUser",
            extraClaims: claims,
            expiresUtc: DateTime.UtcNow.AddMinutes(30)
        );
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
    
}
