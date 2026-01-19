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
[Route("api/admin/auth")]
[AllowAnonymous]
public class AdminAuthController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _lightning;
    private readonly IConfiguration _config;

    public AdminAuthController(LiveAuthDbContext db, LightningService lightning, IConfiguration config)
    {
        _db = db;
        _lightning = lightning;
        _config = config;
    }

    private bool IsAllowedAdminEmail(string email)
    {
        var list = _config.GetSection("LiveAuthAdmin:AllowedEmails").Get<string[]>() ?? Array.Empty<string>();
        return list.Any(x => string.Equals(x.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    [HttpPost("start")]
    public async Task<ActionResult<AdminStartLoginResponse>> Start([FromBody] AdminStartLoginRequest request, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "invalid_email", message = "Email is required." });

        if (!IsAllowedAdminEmail(email))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "not_allowed", message = "This email is not allowed for admin access." });

        // Idempotency-ish: if there is an active unpaid session for this email, reuse it (prevents invoice spam)
        var now = DateTime.UtcNow;
        var existing = await _db.AdminLoginSessions
            .Where(s => s.Email == email && !s.IsPaid && s.ExpiresAt > now)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            return Ok(new AdminStartLoginResponse
            {
                SessionId = existing.Id,
                Invoice = existing.InvoiceBolt11,
                AmountSats = existing.AmountSats,
                ExpiresAtUnix = new DateTimeOffset(existing.ExpiresAt).ToUnixTimeSeconds()
            });
        }

        // v1: fixed admin login amount (cheap but nonzero)
        var amountSats = 21L;
        var expiresAt = now.AddMinutes(10);

        var memo = $"LiveAuth Admin Login – {email}";
        var invoice = await _lightning.CreateInvoice(
            userId: $"admin:{email}",
            amountSats: amountSats,
            memo: memo
        );

        var session = new AdminLoginSession
        {
            Id = Guid.NewGuid(),
            Email = email,
            AmountSats = amountSats,
            InvoiceBolt11 = invoice.PaymentRequest,
            InvoiceRHash = invoice.RHash,
            IsPaid = false,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        _db.AdminLoginSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return Ok(new AdminStartLoginResponse
        {
            SessionId = session.Id,
            Invoice = session.InvoiceBolt11,
            AmountSats = session.AmountSats,
            ExpiresAtUnix = new DateTimeOffset(session.ExpiresAt).ToUnixTimeSeconds()
        });
    }

    [HttpPost("confirm")]
    public async Task<ActionResult<AdminConfirmLoginResponse>> Confirm([FromBody] AdminConfirmLoginRequest request, CancellationToken ct)
    {
        var session = await _db.AdminLoginSessions
            .SingleOrDefaultAsync(x => x.Id == request.SessionId, ct);

        if (session == null)
            return Ok(new AdminConfirmLoginResponse { Verified = false });

        if (session.IsPaid)
        {
            return Ok(new AdminConfirmLoginResponse
            {
                Verified = true,
                Token = GenerateAdminJwt(session),
                ExpiresAtUnix = new DateTimeOffset(DateTime.UtcNow.AddHours(8)).ToUnixTimeSeconds()
            });
        }

        if (DateTime.UtcNow > session.ExpiresAt)
            return Ok(new AdminConfirmLoginResponse { Verified = false });

        var status = await _lightning.GetInvoiceStatusAsync(session.InvoiceRHash);

        if (!status.IsPaid)
            return Ok(new AdminConfirmLoginResponse { Verified = false });

        // idempotent-ish: mark paid only once
        session.IsPaid = true;
        session.PaidAt = DateTime.UtcNow;
        session.PayerLightningAuthKey = status.PayerLightningAuthKey;

        await _db.SaveChangesAsync(ct);

        return Ok(new AdminConfirmLoginResponse
        {
            Verified = true,
            Token = GenerateAdminJwt(session),
            ExpiresAtUnix = new DateTimeOffset(DateTime.UtcNow.AddHours(8)).ToUnixTimeSeconds()
        });
    }

    private string GenerateAdminJwt(AdminLoginSession session)
    {
        // IMPORTANT: use a distinct "aud" if your JWT validator supports it.
        // If your LightningService GenerateJwtToken hardcodes aud, that’s ok for v1,
        // but ideally add an overload to set audience = "LiveAuthAdmin".

        var claims = new[]
        {
            new Claim("email", session.Email),
            new Claim("scope", "admin"),
            new Claim("adminSessionId", session.Id.ToString())
        };

        return _lightning.GenerateJwtToken(
            userId: $"admin:{session.Email}",
            role: "Admin",
            extraClaims: claims,
            expiresUtc: DateTime.UtcNow.AddHours(8)
        );
    }
}
