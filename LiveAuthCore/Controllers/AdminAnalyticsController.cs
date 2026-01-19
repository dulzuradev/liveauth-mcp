using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/admin/analytics/projects")]
[Authorize(Roles = "Admin")]
public class AdminProjectAnalyticsController : ControllerBase
{
    private readonly LiveAuthDbContext _db;

    public AdminProjectAnalyticsController(LiveAuthDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdminProjectUsageDto>>> GetProjectUsage(
        [FromQuery] int windowHours = 24,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (windowHours <= 0 || windowHours > 720)
            windowHours = 24;

        if (limit <= 0 || limit > 200)
            limit = 50;

        var from = DateTime.UtcNow.AddHours(-windowHours);

        // Aggregate auth events by project
        var stats = await _db.AuthEvents
            .Where(e => e.CreatedAt >= from)
            .GroupBy(e => e.ProjectId)
            .Select(g => new
            {
                ProjectId = g.Key,
                Auths = g.Count(),
                Successes = g.Count(x => x.Success),
                Failures = g.Count(x => !x.Success),
                RateLimits = g.Count(x => x.EventType == AuthEventType.RateLimitHit),
                SatsPaid = g.Sum(x => x.SatsPaid ?? 0)
            })
            .ToListAsync(ct);

        if (stats.Count == 0)
            return Ok(new List<AdminProjectUsageDto>());

        var projectIds = stats.Select(s => s.ProjectId).Distinct().ToList();

        // Load projects in one query
        var projects = await _db.Projects
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Plan
            })
            .ToDictionaryAsync(p => p.Id, ct);

        var results = stats
            .Select(s =>
            {
                projects.TryGetValue(s.ProjectId, out var p);

                return new AdminProjectUsageDto
                {
                    ProjectId = s.ProjectId,
                    Name = p?.Name ?? "Unknown / Deleted",
                    Plan = (p?.Plan ?? "free").ToLowerInvariant() == "pro"
                        ? "pro"
                        : "free",

                    Auths = s.Auths,
                    Successes = s.Successes,
                    Failures = s.Failures,
                    RateLimitHits = s.RateLimits,
                    SatsPaid = s.SatsPaid
                };
            })
            .OrderByDescending(x => x.Auths)
            .Take(limit)
            .ToList();

        return Ok(results);
    }
}
