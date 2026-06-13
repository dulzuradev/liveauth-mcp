using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace LiveAuthCore.Services;

public class DeveloperAuthService
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _ln;
    private readonly IConfiguration _cfg;
    private readonly LightningFeeSettingsService _feeSettings;

    public DeveloperAuthService(
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

    public async Task<DeveloperLoginSession> StartLoginAsync(string email, long amountSats = 1)
    {
        email = email.Trim().ToLowerInvariant();

        // ensure developer exists
        var dev = await _db.Developers.SingleOrDefaultAsync(d => d.Email == email);
        if (dev == null)
        {
            dev = new Developer { Email = email };
            _db.Developers.Add(dev);
            await _db.SaveChangesAsync();
        }

        // create a nonce for memo replay safety (optional but nice)
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var memo = $"LiveAuth Developer Login - {email} - nonce:{nonce}";

        var settings = await _feeSettings.GetCurrentAsync();
        var invoiceFeeSats = BasisPointFeeMath.CalculateFeeSats(
            amountSats,
            settings.InvoiceFeeBasisPoints,
            settings.InvoiceMinimumFeeSats);
        var totalChargedSats = amountSats + invoiceFeeSats;

        var inv = await _ln.CreateInvoice(
            userId: dev.Id.ToString(),
            amountSats: totalChargedSats,
            memo: memo
        );

        var session = new DeveloperLoginSession
        {
            DeveloperId = dev.Id,
            DeveloperEmail = email,
            AmountSats = totalChargedSats,
            BaseAmountSats = amountSats,
            InvoiceFeeBasisPoints = settings.InvoiceFeeBasisPoints,
            InvoiceFeeMinimumSats = settings.InvoiceMinimumFeeSats,
            InvoiceFeeSats = invoiceFeeSats,
            TotalChargedSats = totalChargedSats,
            CreditAmountSats = amountSats,
            PaymentHashB64 = inv.RHash,
            Invoice = inv.PaymentRequest,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };

        _db.DeveloperLoginSessions.Add(session);
        await _db.SaveChangesAsync();

        return session;
    }

    public async Task<(bool Verified, string? Jwt)> ConfirmLoginAsync(Guid sessionId)
    {
        var session = await _db.DeveloperLoginSessions
            .Include(s => s.Developer)
            .SingleOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
            throw new KeyNotFoundException("Login session not found.");

        if (session.Status == DevLoginStatus.Paid)
            return (true, GenerateDeveloperJwt(session.Developer!));

        if (DateTime.UtcNow > session.ExpiresAt)
        {
            session.Status = DevLoginStatus.Expired;
            await _db.SaveChangesAsync();
            return (false, null);
        }

        var paid = await _ln.CheckPaymentStatus(session.PaymentHashB64);
        if (!paid)
        {
            session.Status = DevLoginStatus.Pending;
            await _db.SaveChangesAsync();
            return (false, null);
        }


        session.Status = DevLoginStatus.Paid;
        session.PaidAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (true, GenerateDeveloperJwt(session.Developer!));
    }

    private string GenerateDeveloperJwt(Developer dev)
    {
        // You already have JWT settings. We’ll include developerId + role=Developer.
        return _ln.GenerateJwtToken(dev.Id.ToString(), "Developer");
    }
}
