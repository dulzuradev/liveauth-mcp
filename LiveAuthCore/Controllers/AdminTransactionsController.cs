using System.ComponentModel.DataAnnotations;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/admin/analytics/transactions")]
[Authorize(Roles = "Admin")]
public class AdminTransactionsController : ControllerBase
{
    private readonly LiveAuthDbContext _db;

    public AdminTransactionsController(LiveAuthDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<AdminTransactionsResponse>> GetTransactions(
        [FromQuery] string? search = null,
        [FromQuery] string? type = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var query = _db.AuthSessions
            .Where(s => s.IsPaid)
            .AsQueryable();

        // Search by payment hash, invoice, ID, or IP
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(s => 
                (s.InvoiceRHash != null && s.InvoiceRHash.ToLower().Contains(searchLower)) ||
                (s.InvoiceBolt11 != null && s.InvoiceBolt11.ToLower().Contains(searchLower)) ||
                s.Id.ToString().ToLower().Contains(searchLower) ||
                (s.ClientIp != null && s.ClientIp.ToLower().Contains(searchLower)));
        }

        // Filter by type (AUTH = auth sessions)
        var transactions = await query
            .OrderByDescending(s => s.PaidAt ?? s.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        var total = await query.CountAsync(ct);
        var totalSats = await query.SumAsync(s => s.AmountSats, ct);

        // Get project info
        var projectIds = transactions.Select(t => t.ProjectId).Distinct().ToList();
        var projects = await _db.Projects
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.PublicKey })
            .ToListAsync(ct);

        var projectDict = projects.ToDictionary(p => p.Id);

        var result = transactions.Select(t => new TransactionDto
        {
            Id = t.Id.ToString(),
            Type = "AUTH",
            ProjectId = t.ProjectId,
            ProjectName = projectDict.GetValueOrDefault(t.ProjectId)?.Name,
            ProjectPublicKey = projectDict.GetValueOrDefault(t.ProjectId)?.PublicKey,
            AmountSats = (int)t.AmountSats,
            PaymentHash = t.InvoiceRHash ?? "",
            Invoice = t.InvoiceBolt11 ?? "",
            Status = t.IsPaid ? "PAID" : "PENDING",
            CreatedAt = t.CreatedAt,
            PaidAt = t.PaidAt,
            ClientIp = t.ClientIp,
            Environment = t.Environment ?? ""
        }).ToList();

        return Ok(new AdminTransactionsResponse
        {
            Transactions = result,
            Total = total,
            TotalSats = totalSats
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TransactionDetailDto>> GetTransaction(
        string id,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guidId))
        {
            return BadRequest(new { error = "Invalid transaction ID" });
        }

        var session = await _db.AuthSessions
            .Include(s => s.Project)
            .FirstOrDefaultAsync(s => s.Id == guidId, ct);

        if (session == null)
        {
            return NotFound(new { error = "Transaction not found" });
        }

        return Ok(new TransactionDetailDto
        {
            Id = session.Id.ToString(),
            Type = "AUTH",
            ProjectId = session.ProjectId,
            ProjectName = session.Project?.Name ?? "",
            ProjectPublicKey = session.Project?.PublicKey ?? "",
            AmountSats = (int)session.AmountSats,
            PaymentHash = session.InvoiceRHash ?? "",
            Invoice = session.InvoiceBolt11 ?? "",
            Status = session.IsPaid ? "PAID" : "PENDING",
            CreatedAt = session.CreatedAt,
            PaidAt = session.PaidAt,
            ClientIp = session.ClientIp,
            UserHint = session.UserHint,
            Environment = session.Environment ?? "",
            PayerLightningKey = session.PayerLightningAuthKey
        });
    }
}

public class AdminTransactionsResponse
{
    public List<TransactionDto> Transactions { get; set; } = new();
    public int Total { get; set; }
    public long TotalSats { get; set; }
}

public class TransactionDto
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public Guid ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? ProjectPublicKey { get; set; }
    public int AmountSats { get; set; }
    public string? PaymentHash { get; set; }
    public string? Invoice { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? ClientIp { get; set; }
    public string? Environment { get; set; }
}

public class TransactionDetailDto : TransactionDto
{
    public string? UserHint { get; set; }
    public string? PayerLightningKey { get; set; }
}
