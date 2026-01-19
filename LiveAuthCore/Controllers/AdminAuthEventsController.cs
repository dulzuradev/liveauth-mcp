using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/admin/analytics/events")]
[Authorize(Roles = "Admin")]
public class AdminAuthEventsController : ControllerBase
{
    private readonly LiveAuthDbContext _db;

    public AdminAuthEventsController(LiveAuthDbContext db)
    {
        _db = db;
    }
    
    [HttpGet]
    public async Task<ActionResult<List<AdminAuthEventDto>>> GetEvents(
        [FromQuery] int windowHours = 24,
        [FromQuery] Guid? projectId = null,
        [FromQuery] AuthEventType? eventType = null,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var from = DateTime.UtcNow.AddHours(-windowHours);

        var query = _db.AuthEvents
            .Include(e => e.Project)
            .Where(e => e.CreatedAt >= from);

        if (projectId.HasValue)
            query = query.Where(e => e.ProjectId == projectId.Value);

        if (eventType.HasValue)
            query = query.Where(e => e.EventType == eventType.Value);

        var rawResults = await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .Select(e => new
            {
                e.CreatedAt,
                e.ProjectId,
                ProjectName = e.Project!.Name,
                e.EventType,
                e.Success,
                e.SatsPaid,
                e.Reason,
                e.ClientIp
            })
            .ToListAsync(ct);

        var results = rawResults
            .Select(e => new AdminAuthEventDto
            {
                Timestamp = e.CreatedAt,
                ProjectId = e.ProjectId,
                ProjectName = e.ProjectName,
                EventType = e.EventType.ToString(),
                Success = e.Success,
                SatsPaid = e.SatsPaid,
                Reason = e.Reason,
                ClientIpMasked = MaskIp(e.ClientIp)
            })
            .ToList();

        return Ok(results);
    }

    private static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "";
        var parts = ip.Split('.');
        return parts.Length == 4 ? $"{parts[0]}.{parts[1]}.x.x" : ip;
    }
}
