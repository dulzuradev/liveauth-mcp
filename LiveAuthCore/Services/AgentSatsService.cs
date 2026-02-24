using System.Security.Cryptography;
using System.Text;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services;

/// <summary>
/// LND-based sats printer for agent payments
/// Human pays Lightning invoice → agent receives sats balance
/// </summary>
public class AgentSatsService
{
    private readonly LiveAuthDbContext _db;
    private readonly LightningService _lightning;
    private readonly ILogger<AgentSatsService> _logger;

    public AgentSatsService(
        LiveAuthDbContext db,
        LightningService lightning,
        ILogger<AgentSatsService> logger)
    {
        _db = db;
        _lightning = lightning;
        _logger = logger;
    }

    /// <summary>
    /// Create a Lightning invoice for adding sats to agent's balance
    /// </summary>
    public async Task<SatsInvoice> CreateInvoiceAsync(string agentId, long amountSats, CancellationToken ct = default)
    {
        if (amountSats <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amountSats));

        // Ensure tables exist
        await EnsureTablesExistAsync(ct);

        _logger.LogInformation("Creating sats invoice for agent {AgentId}, amount: {Amount} sats", 
            agentId, amountSats);

        // Create invoice via LND
        var memo = $"LiveAuth Agent Sats - {agentId}";
        var invoiceResult = await _lightning.CreateInvoiceWithHashAsync(
            agentId, 
            amountSats, 
            memo);

        var invoice = new SatsInvoice
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            AmountSats = amountSats,
            PaymentRequest = invoiceResult.Bolt11,
            PaymentHash = invoiceResult.PaymentHash,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };

        _db.SatsInvoices.Add(invoice);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created invoice {PaymentHash} for {Amount} sats", 
            invoice.PaymentHash, amountSats);

        return invoice;
    }

    private async Task EnsureTablesExistAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS AgentSatsBalances (
                    Id TEXT PRIMARY KEY,
                    AgentId TEXT NOT NULL,
                    Balance INTEGER NOT NULL DEFAULT 0,
                    TotalEarned INTEGER NOT NULL DEFAULT 0,
                    TotalSpent INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    LastUpdated TEXT NOT NULL
                );
                
                CREATE TABLE IF NOT EXISTS SatsInvoices (
                    Id TEXT PRIMARY KEY,
                    AgentId TEXT NOT NULL,
                    AmountSats INTEGER NOT NULL,
                    PaymentRequest TEXT NOT NULL,
                    PaymentHash TEXT NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'pending',
                    CreatedAt TEXT NOT NULL,
                    ExpiresAt TEXT NOT NULL,
                    PaidAt TEXT
                );
            ", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error ensuring tables exist");
        }
    }

    /// <summary>
    /// Check if invoice is paid and credit agent's balance
    /// </summary>
    public async Task<bool> CheckAndCreditInvoiceAsync(string paymentHash, CancellationToken ct = default)
    {
        var invoice = await _db.SatsInvoices
            .FirstOrDefaultAsync(i => i.PaymentHash == paymentHash, ct);

        if (invoice == null)
        {
            _logger.LogWarning("Invoice not found for payment hash: {PaymentHash}", paymentHash);
            return false;
        }

        if (invoice.Status == "paid")
        {
            _logger.LogInformation("Invoice already paid: {PaymentHash}", paymentHash);
            return true;
        }

        // Convert hex to base64 for LND API
        var paymentHashBytes = Convert.FromHexString(paymentHash);
        var paymentHashB64 = Convert.ToBase64String(paymentHashBytes);

        // Check with LND if paid
        var isPaid = await _lightning.CheckPaymentStatus(paymentHashB64);
        
        if (isPaid)
        {
            invoice.Status = "paid";
            invoice.PaidAt = DateTime.UtcNow;
            
            // Credit agent's balance
            var balance = await _db.AgentSatsBalances
                .FirstOrDefaultAsync(b => b.AgentId == invoice.AgentId, ct);

            if (balance == null)
            {
                balance = new AgentSatsBalance
                {
                    Id = Guid.NewGuid(),
                    AgentId = invoice.AgentId,
                    Balance = invoice.AmountSats,
                    TotalEarned = invoice.AmountSats,
                    CreatedAt = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                };
                _db.AgentSatsBalances.Add(balance);
            }
            else
            {
                balance.Balance += invoice.AmountSats;
                balance.TotalEarned += invoice.AmountSats;
                balance.LastUpdated = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
            
            _logger.LogInformation("Credited {Amount} sats to agent {AgentId}", 
                invoice.AmountSats, invoice.AgentId);
        }

        return isPaid;
    }

    /// <summary>
    /// Get agent's sats balance
    /// </summary>
    public async Task<AgentSatsBalance> GetBalanceAsync(string agentId, CancellationToken ct = default)
    {
        var balance = await _db.AgentSatsBalances
            .FirstOrDefaultAsync(b => b.AgentId == agentId, ct);

        return balance ?? new AgentSatsBalance
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            Balance = 0,
            TotalEarned = 0,
            TotalSpent = 0
        };
    }

    /// <summary>
    /// Deduct sats from agent's balance (for API calls)
    /// </summary>
    public async Task<(bool success, long remaining)> DeductAsync(string agentId, long amountSats, CancellationToken ct = default)
    {
        var balance = await _db.AgentSatsBalances
            .FirstOrDefaultAsync(b => b.AgentId == agentId, ct);

        if (balance == null || balance.Balance < amountSats)
        {
            _logger.LogWarning("Insufficient balance for agent {AgentId}: has {Balance}, needs {Amount}",
                agentId, balance?.Balance ?? 0, amountSats);
            return (false, balance?.Balance ?? 0);
        }

        balance.Balance -= amountSats;
        balance.TotalSpent += amountSats;
        balance.LastUpdated = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deducted {Amount} sats from agent {AgentId}, remaining: {Remaining}",
            amountSats, agentId, balance.Balance);

        return (true, balance.Balance);
    }

    /// <summary>
    /// Get all invoices for an agent
    /// </summary>
    public async Task<List<SatsInvoice>> GetInvoicesAsync(string agentId, CancellationToken ct = default)
    {
        return await _db.SatsInvoices
            .Where(i => i.AgentId == agentId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
    }
}
