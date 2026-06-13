using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services;

public class DeveloperVerificationService
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _ln;
    private readonly IConfiguration _cfg;
    private readonly LightningFeeSettingsService _feeSettings;

    public DeveloperVerificationService(
        LiveAuthDbContext db,
        LightningService ln,
        IConfiguration cfg,
        LightningFeeSettingsService feeSettings)
    {
        _db = db;
        _ln = ln;
        _cfg = cfg;
        _feeSettings = feeSettings;
    }

    public async Task<VerificationSession> StartSessionAsync(Project project, string userRef, long amountSats, string memo)
    {
        // Reset monthly count if we're in a new month
        var now = DateTime.UtcNow;
        if (project.MonthlyAuthPeriodStart.Month != now.Month || project.MonthlyAuthPeriodStart.Year != now.Year)
        {
            project.MonthlyAuthCount = 0;
            project.MonthlyAuthPeriodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        // Check quota
        var limit = PlanLimits.GetMonthlyAuthLimit(project.Plan ?? "free", project.ProPaidUntil);
        if (project.MonthlyAuthCount >= limit)
        {
            var plan = project.Plan?.ToLowerInvariant() ?? "free";
            throw new ApplicationException(
                $"Monthly {limit:N0} verification limit exceeded. Upgrade to {(plan == "free" ? "Pro" : "Enterprise")} for more.");
        }

        var settings = await _feeSettings.GetCurrentAsync();
        var invoiceFeeSats = BasisPointFeeMath.CalculateFeeSats(
            amountSats,
            settings.InvoiceFeeBasisPoints,
            settings.InvoiceMinimumFeeSats);
        var totalChargedSats = amountSats + invoiceFeeSats;

        var inv = await _ln.CreateInvoice(userRef, totalChargedSats, memo, project);

        var session = new VerificationSession
        {
            ProjectId = project.Id,
            UserRef = userRef,
            AmountSats = totalChargedSats,
            BaseAmountSats = amountSats,
            InvoiceFeeBasisPoints = settings.InvoiceFeeBasisPoints,
            InvoiceFeeMinimumSats = settings.InvoiceMinimumFeeSats,
            InvoiceFeeSats = invoiceFeeSats,
            TotalChargedSats = totalChargedSats,
            CreditAmountSats = amountSats,
            PaymentHashB64 = inv.RHash,
            Invoice = inv.PaymentRequest,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60)
        };

        _db.VerificationSessions.Add(session);
        _db.UsageEvents.Add(new UsageEvent
        {
            ProjectId = project.Id,
            Type = "invoice_created",
            SatsCharged = 0
        });

        // Increment monthly auth count (counts attempts, not successful verifications)
        project.MonthlyAuthCount += 1;

        // Detach project from EF change tracker so SaveChangesOnly updates the count
        // without triggering a full project entity update. This avoids conflicts when
        // the caller still holds a reference to the same project instance.
        _db.Entry(project).State = EntityState.Modified;
        _db.Entry(project).Property(p => p.MonthlyAuthCount).IsModified = true;
        _db.Entry(project).Property(p => p.MonthlyAuthPeriodStart).IsModified = true;

        await _db.SaveChangesAsync();
        return session;
    }

    public async Task<(bool Verified, string? Token)> ConfirmSessionAsync(Project project, Guid sessionId)
    {
        var session = await _db.VerificationSessions
            .SingleOrDefaultAsync(s => s.Id == sessionId && s.ProjectId == project.Id);

        if (session == null)
            throw new KeyNotFoundException("Session not found.");

        if (session.Status == VerificationStatus.Paid)
        {
            var tokenCached = _ln.GenerateJwtToken(
                userId: session.UserRef, 
                role: "User");
            return (true, tokenCached);
        }

        if (DateTime.UtcNow > session.ExpiresAt)
        {
            session.Status = VerificationStatus.Expired;
            await _db.SaveChangesAsync();
            return (false, null);
        }

        var paid = await _ln.CheckPaymentStatus(session.PaymentHashB64);

        if (!paid)
            return (false, null);

        session.Status = VerificationStatus.Paid;
        session.PaidAt = DateTime.UtcNow;

        // Mint LiveAuth token tied to project + session
        var token = GenerateLiveAuthToken(project.Id.ToString(), session.UserRef, "lightning", session.Id.ToString());

        _db.UsageEvents.Add(new UsageEvent
        {
            ProjectId = project.Id,
            Type = "verified",
            SatsCharged = session.AmountSats
        });

        await _db.SaveChangesAsync();

        return (true, token);
    }

    public bool VerifyLiveAuthToken(string token, out IDictionary<string, string> claims)
    {
        claims = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(token))
            return false;

        // Validate signature AND expiry (prevents accepting arbitrary JWT-shaped strings)
        try
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(_cfg["Jwt:SigningKey"] ?? _cfg["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured")));
            var parameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _cfg["Jwt:Issuer"],
                ValidAudience = _cfg["Jwt:Audience"]
            };

            handler.ValidateToken(token, parameters, out var validatedToken);
            var jwt = (System.IdentityModel.Tokens.Jwt.JwtSecurityToken)validatedToken;

            foreach (var c in jwt.Claims)
                claims[c.Type] = c.Value;

            return true;
        }
        catch (Microsoft.IdentityModel.Tokens.SecurityTokenExpiredException)
        {
            // Token is valid but expired — expected, not a security issue
            return false;
        }
        catch (Microsoft.IdentityModel.Tokens.SecurityTokenInvalidSignatureException)
        {
            // Signature mismatch — potential tampering
            return false;
        }
        catch (Microsoft.IdentityModel.Tokens.SecurityTokenValidationException)
        {
            // Malformed token, wrong issuer/audience, etc.
            return false;
        }
        catch (InvalidOperationException)
        {
            // Missing JWT configuration — not a token problem
            return false;
        }
        catch (Exception)
        {
            // Fail secure: any unexpected error means reject the token
            // Could log here for security monitoring (never return true for a bad token)
            return false;
        }
    }

    private string GenerateLiveAuthToken(string projectId, string userRef, string method, string sessionId)
    {
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(_cfg["Jwt:SigningKey"] ?? _cfg["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured")));
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new System.Security.Claims.Claim("projectId", projectId),
            new System.Security.Claims.Claim("userRef", userRef),
            new System.Security.Claims.Claim("method", method),
            new System.Security.Claims.Claim("sessionId", sessionId)
        };

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: _cfg["Jwt:Issuer"],
            audience: _cfg["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}
