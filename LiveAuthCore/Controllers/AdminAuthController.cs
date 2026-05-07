using System.Security.Claims;
using System.Text;
using System.Security.Cryptography;
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
    private const long AdminPaymentSats = 10_000;

    public AdminAuthController(LiveAuthDbContext db, LightningService lightning, IConfiguration config)
    {
        _db = db;
        _lightning = lightning;
        _config = config;
    }

    [HttpGet("status")]
    public async Task<ActionResult<AdminStatusResponse>> GetStatus(CancellationToken ct)
    {
        var token = Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "");
        
        if (!string.IsNullOrEmpty(token))
        {
            // Check if valid session
            var session = await _db.AdminSessions
                .FirstOrDefaultAsync(s => s.Token == token && s.ExpiresAt > DateTime.UtcNow, ct);
            
            if (session != null)
            {
                return Ok(new AdminStatusResponse
                {
                    IsAuthenticated = true,
                    Username = session.Username,
                    IsOwner = session.IsOwner
                });
            }
        }

        return Ok(new AdminStatusResponse { IsAuthenticated = false });
    }

    [HttpPost("payment")]
    public async Task<ActionResult<AdminPaymentResponse>> CreatePayment(CancellationToken ct)
    {
        // Check if admin already exists
        var adminExists = await _db.AdminSessions.AnyAsync(ct);
        
        // First payment: 10k sats to set up admin
        // After that: 100 sats per login
        var amountSats = adminExists ? 100 : AdminPaymentSats;
        var memo = adminExists ? "LiveAuth Admin Login" : "LiveAuth Admin Setup (10,000 sats)";
        
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(15);

        // CENTRALIZED: Use new method that returns hex payment hash
        var invoiceResult = await _lightning.CreateInvoiceWithHashAsync(
            userId: "admin-setup",
            amountSats: amountSats,
            memo: memo,
            expiryMinutes: 15
        );

        var paymentSession = new AdminPaymentSession
        {
            Id = Guid.NewGuid(),
            AmountSats = amountSats,
            InvoiceBolt11 = invoiceResult.Bolt11,
            InvoiceRHash = invoiceResult.PaymentHash,  // Now stores hex!
            IsPaid = false,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        _db.AdminPaymentSessions.Add(paymentSession);
        await _db.SaveChangesAsync(ct);

        return Ok(new AdminPaymentResponse
        {
            SessionId = paymentSession.Id,
            Invoice = invoiceResult.Bolt11,
            AmountSats = amountSats,
            IsSetup = !adminExists,
            ExpiresAtUnix = new DateTimeOffset(expiresAt).ToUnixTimeSeconds()
        });
    }

    [HttpPost("verify")]
    public async Task<ActionResult<AdminVerifyResponse>> VerifyPayment([FromBody] AdminVerifyRequest request, CancellationToken ct)
    {
        var session = await _db.AdminPaymentSessions
            .SingleOrDefaultAsync(x => x.Id == request.SessionId, ct);

        if (session == null)
            return BadRequest(new { error = "Invalid session" });

        if (session.IsPaid)
        {
            return Ok(new AdminVerifyResponse
            {
                Paid = true,
                CanSetPassword = !await _db.AdminSessions.AnyAsync(ct)
            });
        }

        if (DateTime.UtcNow > session.ExpiresAt)
            return Ok(new AdminVerifyResponse { Paid = false, Error = "Payment expired" });

        var status = await _lightning.GetInvoiceStatusAsync(session.InvoiceRHash);

        if (!status.IsPaid)
            return Ok(new AdminVerifyResponse { Paid = false });

        session.IsPaid = true;
        session.PaidAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var canSetPassword = !await _db.AdminSessions.AnyAsync(ct);

        return Ok(new AdminVerifyResponse
        {
            Paid = true,
            CanSetPassword = canSetPassword
        });
    }

    [HttpPost("setup")]
    public async Task<ActionResult<AdminSetupResponse>> SetupAdmin([FromBody] AdminSetupRequest request, CancellationToken ct)
    {
        // Skip payment check for local development or if explicitly bypassed
        var skipPayment = _config["Admin:SkipPayment"] == "true";
        
        if (!skipPayment)
        {
            // Verify payment first
            var paymentSession = await _db.AdminPaymentSessions
                .Where(s => s.IsPaid && s.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(s => s.PaidAt)
                .FirstOrDefaultAsync(ct);

            if (paymentSession == null)
                return StatusCode(403, new { error = "Payment required" });
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Username and password required" });

        if (request.Password.Length < 8)
            return BadRequest(new { error = "Password must be at least 8 characters" });

        // Check if admin already exists
        var adminExists = await _db.AdminSessions.AnyAsync(ct);
        if (adminExists)
            return BadRequest(new { error = "Admin already exists" });

        // Hash password
        var salt = GenerateSalt();
        var hash = HashPassword(request.Password, salt);

        var session = new AdminSession
        {
            Id = Guid.NewGuid(),
            Username = request.Username.Trim().ToLowerInvariant(),
            PasswordHash = hash,
            PasswordSalt = salt,
            IsOwner = true,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            Token = Guid.NewGuid().ToString("N")
        };

        _db.AdminSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        // Generate JWT token
        var jwtToken = _lightning.GenerateAdminJwtToken(session.Id.ToString());

        return Ok(new AdminSetupResponse
        {
            Success = true,
            Token = jwtToken,
            Username = session.Username
        });
    }

    [HttpPost("login")]
    public async Task<ActionResult<AdminLoginResponse>> Login([FromBody] AdminLoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Username and password required" });

        var session = await _db.AdminSessions
            .FirstOrDefaultAsync(s => s.Username == request.Username.Trim().ToLowerInvariant(), ct);

        if (session == null)
            return Unauthorized(new { error = "Invalid credentials" });

        var computedHash = HashPassword(request.Password, session.PasswordSalt);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHash),
                Encoding.UTF8.GetBytes(session.PasswordHash)))
            return Unauthorized(new { error = "Invalid credentials" });

        // Generate JWT token
        var jwtToken = _lightning.GenerateAdminJwtToken(session.Id.ToString());
        session.Token = jwtToken;
        session.ExpiresAt = DateTime.UtcNow.AddDays(30);
        await _db.SaveChangesAsync(ct);

        return Ok(new AdminLoginResponse
        {
            Success = true,
            Token = jwtToken,
            Username = session.Username
        });
    }

    [HttpPost("logout")]
    public async Task<ActionResult> Logout(CancellationToken ct)
    {
        var token = Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "");
        if (!string.IsNullOrEmpty(token))
        {
            var session = await _db.AdminSessions
                .FirstOrDefaultAsync(s => s.Token == token, ct);
            if (session != null)
            {
                session.Token = null;
                session.ExpiresAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }
        return Ok(new { success = true });
    }

    private static string GenerateSalt()
    {
        var salt = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        return Convert.ToBase64String(salt);
    }

    private static string HashPassword(string password, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, 100_000, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(pbkdf2.GetBytes(32));
    }
}

// Request/Response DTOs
public class AdminStatusResponse
{
    public bool IsAuthenticated { get; set; }
    public string? Username { get; set; }
    public bool? IsOwner { get; set; }
}

public class AdminPaymentResponse
{
    public Guid SessionId { get; set; }
    public string Invoice { get; set; } = string.Empty;
    public long AmountSats { get; set; }
    public bool IsSetup { get; set; }
    public long ExpiresAtUnix { get; set; }
}

public class AdminVerifyRequest
{
    public Guid SessionId { get; set; }
}

public class AdminVerifyResponse
{
    public bool Paid { get; set; }
    public bool? CanSetPassword { get; set; }
    public string? Error { get; set; }
}

public class AdminSetupRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AdminSetupResponse
{
    public bool Success { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

public class AdminLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AdminLoginResponse
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? Username { get; set; }
    public string? Error { get; set; }
}
