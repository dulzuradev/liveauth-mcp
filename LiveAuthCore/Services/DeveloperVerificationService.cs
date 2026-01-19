using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services;

public class DeveloperVerificationService
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _ln;
    private readonly IConfiguration _cfg;

    public DeveloperVerificationService(LiveAuthDbContext db, LightningService ln, IConfiguration cfg)
    {
        _db = db;
        _ln = ln;
        _cfg = cfg;
    }

    public async Task<VerificationSession> StartSessionAsync(Project project, string userRef, long amountSats, string memo)
    {
        // quota checks
        if (project.MonthlyUsed >= project.MonthlyQuota && project.Plan == "free")
            throw new ApplicationException("Monthly quota exceeded. Upgrade required.");

        var inv = await _ln.CreateInvoice(userRef, amountSats, memo);

        var session = new VerificationSession
        {
            ProjectId = project.Id,
            UserRef = userRef,
            AmountSats = amountSats,
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

        project.MonthlyUsed += 1;

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
                role: "User"); // replace with LiveAuth token below if you want
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
        try
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            foreach (var c in jwt.Claims)
                claims[c.Type] = c.Value;

            // If you want full signature validation, add TokenValidationParameters.
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string GenerateLiveAuthToken(string projectId, string userRef, string method, string sessionId)
    {
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]));
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
