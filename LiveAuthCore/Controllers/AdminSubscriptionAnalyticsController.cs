using LiveAuthCore.Data;
using LiveAuthCore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/admin/analytics/subscriptions")]
[Authorize(Roles = "Admin")]
public class AdminSubscriptionAnalyticsController : ControllerBase
{
    private readonly LiveAuthDbContext _db;

    public AdminSubscriptionAnalyticsController(LiveAuthDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdminSubscriptionDto>>> GetSubscriptions(
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var query = _db.BillingSubscriptions
            .Include(x => x.Project)
            .AsQueryable();

        query = status switch
        {
            "active" => query.Where(x => x.IsPaid && x.ExpiresAt > now),
            "expired" => query.Where(x => x.IsPaid && x.ExpiresAt <= now),
            "pending" => query.Where(x => !x.IsPaid && x.ExpiresAt > now),
            _ => query
        };

        var results = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AdminSubscriptionDto
            {
                SubscriptionId = x.Id,
                ProjectId = x.ProjectId,
                ProjectName = x.Project!.Name,
                Plan = x.Plan,
                IsPaid = x.IsPaid,
                AmountSats = x.AmountSats,
                CreatedAt = x.CreatedAt,
                PaidAt = x.PaidAt,
                ExpiresAt = x.ExpiresAt
            })
            .ToListAsync(ct);

        return Ok(results);
    }
}