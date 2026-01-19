namespace LiveAuthCore.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

[ApiController]
[Route("api/dev/auth")]
public class DevAuthController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _ln;
    private readonly IConfiguration _config;
    private readonly AuthEventService _authEvents;

    public DevAuthController(
        LiveAuthDbContext db,
        LightningService ln,
        IConfiguration config,
        AuthEventService authEvents)
    {
        _db = db;
        _ln = ln;
        _config = config;
        _authEvents = authEvents;
    }

    // POST /api/dev/auth/start
    [HttpPost("start")]
    public async Task<ActionResult<DevStartLoginResponse>> StartLogin(
        [FromBody] DevStartLoginRequest request,
        CancellationToken ct)
    {
        var email = (request.DeveloperEmail ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest("Developer email is required.");

        // ─────────────────────────────────────────────
        // Amount sats & expiry from config (defaults if missing)
        // ─────────────────────────────────────────────
        var amountSats =
            _config.GetValue<long?>("DevLogin:AmountSats")
            ?? 21L;

        if (amountSats < 0)
            amountSats = 0;

        var expiryMinutes =
            _config.GetValue<int?>("DevLogin:ExpiryMinutes")
            ?? 10;

        if (expiryMinutes <= 0)
            expiryMinutes = 10;

        // ─────────────────────────────────────────────
        // Create Lightning invoice for the login request
        // (uses real LND, or mock if enabled)
        // ─────────────────────────────────────────────
        var invoiceResult =
            await _ln.CreateLoginInvoiceAsync(email, amountSats, expiryMinutes);

        var session = new DevLoginSession
        {
            Id = Guid.NewGuid(),
            Email = email,

            // r_hash (base64)
            InvoiceId     = invoiceResult.InvoiceId,
            InvoiceBolt11 = invoiceResult.Bolt11,

            AmountSats = amountSats,
            ExpiresAt  = DateTime.UtcNow.AddMinutes(expiryMinutes),
            IsPaid     = false
        };

        _db.DevLoginSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return Ok(new DevStartLoginResponse
        {
            SessionId     = session.Id,
            Invoice       = session.InvoiceBolt11,
            AmountSats    = amountSats,
            ExpiresAtUnix = new DateTimeOffset(session.ExpiresAt).ToUnixTimeSeconds()
        });
    }


    // POST /api/dev/auth/confirm
    [HttpPost("confirm")]
    public async Task<ActionResult<DevConfirmLoginResponse>> ConfirmLogin(
        [FromBody] DevConfirmLoginRequest request)
    {
        var session = await _db.DevLoginSessions
            .SingleOrDefaultAsync(s => s.Id == request.SessionId);

        if (session == null)
        {
            return Ok(new DevConfirmLoginResponse
            {
                Verified = false,
                Token = null
            });
        }

        if (DateTime.UtcNow > session.ExpiresAt)
        {
            return Ok(new DevConfirmLoginResponse
            {
                Verified = false,
                Token = null
            });
        }

        // Query Lightning node: has this invoice been paid?
        var status = await _ln.GetInvoiceStatusAsync(session.InvoiceId);

        if (!status.IsPaid)
        {
            return Ok(new DevConfirmLoginResponse
            {
                Verified = false,
                Token = null
            });
        }

        // Mark session as paid (idempotent)
        if (!session.IsPaid)
        {
            session.IsPaid = true;
            session.PaidAt = DateTime.UtcNow;
            session.PayerLightningAuthKey = status.PayerLightningAuthKey;
            await _db.SaveChangesAsync();
        }

        var email = session.Email.Trim();
        var payerKey = status.PayerLightningAuthKey;

        Developer dev;

        // --------------------------------------------------------------------
        // FUTURE-PROOF PATH: when payerKey is available (LNURL-auth, etc.)
        // --------------------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(payerKey))
        {
            var devByKey = await _db.Developers
                .SingleOrDefaultAsync(d => d.LightningAuthKey == payerKey);

            var devByEmail = await _db.Developers
                .SingleOrDefaultAsync(d => d.Email == email);

            // CASE 1: no devs yet; first-time login
            if (devByKey == null && devByEmail == null)
            {
                dev = new Developer
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    LightningAuthKey = payerKey
                };
                _db.Developers.Add(dev);
                await _db.SaveChangesAsync();
            }
            // CASE 2: we know this Lightning key, but email is new or changed
            else if (devByKey != null)
            {
                dev = devByKey;

                if (!string.Equals(dev.Email, email, StringComparison.OrdinalIgnoreCase))
                {
                    // optionally allow updating email to latest
                    dev.Email = email;
                    await _db.SaveChangesAsync();
                }
            }
            // CASE 3: email exists but belongs to a different Lightning identity
            else // devByKey == null && devByEmail != null
            {
                if (string.IsNullOrWhiteSpace(devByEmail!.LightningAuthKey))
                {
                    // old account with no bound Lightning key: bind now
                    devByEmail.LightningAuthKey = payerKey;
                    await _db.SaveChangesAsync();
                    dev = devByEmail;
                }
                else if (devByEmail.LightningAuthKey == payerKey)
                {
                    dev = devByEmail;
                }
                else
                {
                    // 🚫 EMAIL HIJACK ATTEMPT:
                    // Someone is trying to login with an email already claimed
                    // by a different Lightning identity.
                    return Ok(new DevConfirmLoginResponse
                    {
                        Verified = false,
                        Token = null
                    });
                }
            }
        }
        // --------------------------------------------------------------------
        // CURRENT PATH: no Lightning identity – fallback to email-based dev
        // NOTE: This does NOT protect against email hijack; it matches your
        // current behavior until LNURL-auth is wired.
        // --------------------------------------------------------------------
        else
        {
            var devByEmail = await _db.Developers
                .SingleOrDefaultAsync(d => d.Email == email);

            if (devByEmail == null)
            {
                dev = new Developer
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    LightningAuthKey = null
                };
                _db.Developers.Add(dev);
                await _db.SaveChangesAsync();
            }
            else
            {
                dev = devByEmail;
            }
        }

        // Issue JWT with userId claim + Developer role
        var token = GenerateJwtForDeveloper(dev);

        return Ok(new DevConfirmLoginResponse
        {
            Verified = true,
            Token = token
        });
    }

    private string GenerateJwtForDeveloper(Developer dev)
    {
        // Prefer Jwt:SigningKey if present, else fall back to Jwt:Key
        var signingKey = _config["Jwt:SigningKey"] ?? _config["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException(
                "JWT signing key not configured. Set Jwt:SigningKey or Jwt:Key in configuration.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("userId", dev.Id.ToString()),
            new Claim(ClaimTypes.Role, "Developer")
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

}
