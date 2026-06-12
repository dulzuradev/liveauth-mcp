using LiveAuthCore.Data;
using LiveAuthCore.Models.Mcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/admin/analytics/mcp")]
[Authorize(Roles = "Admin")]
public class AdminMcpAnalyticsController : ControllerBase
{
    private readonly LiveAuthDbContext _db;

    public AdminMcpAnalyticsController(LiveAuthDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<McpToolRevenueOverviewResponse>> GetMcpRevenue(
        [FromQuery] Guid? projectId = null,
        [FromQuery] int windowHours = 24,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        windowHours = Math.Clamp(windowHours, 1, 720);
        limit = Math.Clamp(limit, 1, 50);
        var since = DateTime.UtcNow.AddHours(-windowHours);

        var toolsQuery = _db.McpTools
            .AsNoTracking()
            .Where(t => t.RemovedAt == null);

        if (projectId.HasValue)
            toolsQuery = toolsQuery.Where(t => t.ProjectId == projectId.Value);

        var tools = await toolsQuery
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Slug,
                t.Status
            })
            .ToListAsync(ct);

        if (tools.Count == 0)
        {
            return Ok(new McpToolRevenueOverviewResponse(
                WindowHours: windowHours,
                PaidCalls: 0,
                GrossSats: 0,
                PlatformFeeSats: 0,
                NetSats: 0,
                DeniedCharges: 0,
                TopTools: Array.Empty<McpToolRevenueTopToolDto>()));
        }

        var toolIds = tools.Select(t => t.Id).ToList();
        var events = _db.McpToolRevenueEvents
            .Where(e => toolIds.Contains(e.McpToolId) && e.CreatedAt >= since);

        var paidCalls = await events.LongCountAsync(e => e.Status == "Charged", ct);
        var grossSats = await events
            .Where(e => e.Status == "Charged")
            .SumAsync(e => (long?)e.GrossSats, ct) ?? 0L;
        var platformFeeSats = await events
            .Where(e => e.Status == "Charged")
            .SumAsync(e => (long?)e.PlatformFeeSats, ct) ?? 0L;
        var netSats = await events
            .Where(e => e.Status == "Charged")
            .SumAsync(e => (long?)e.NetSats, ct) ?? 0L;
        var deniedCharges = await events.LongCountAsync(e => e.Status == "Denied", ct);

        var topRaw = await events
            .GroupBy(e => e.McpToolId)
            .Select(g => new
            {
                ToolId = g.Key,
                Calls = g.LongCount(e => e.Status == "Charged"),
                GrossSats = g.Where(e => e.Status == "Charged").Sum(e => (long?)e.GrossSats) ?? 0L,
                PlatformFeeSats = g.Where(e => e.Status == "Charged").Sum(e => (long?)e.PlatformFeeSats) ?? 0L,
                NetSats = g.Where(e => e.Status == "Charged").Sum(e => (long?)e.NetSats) ?? 0L,
                DeniedCharges = g.LongCount(e => e.Status == "Denied")
            })
            .OrderByDescending(t => t.GrossSats)
            .ThenByDescending(t => t.Calls)
            .Take(limit)
            .ToListAsync(ct);

        var toolMap = tools.ToDictionary(t => t.Id);
        var topTools = topRaw
            .Where(t => toolMap.ContainsKey(t.ToolId))
            .Select(t =>
            {
                var tool = toolMap[t.ToolId];
                return new McpToolRevenueTopToolDto(
                    ToolId: t.ToolId,
                    ToolName: tool.Name,
                    ToolSlug: tool.Slug,
                    ToolStatus: tool.Status,
                    Calls: t.Calls,
                    GrossSats: t.GrossSats,
                    PlatformFeeSats: t.PlatformFeeSats,
                    NetSats: t.NetSats,
                    DeniedCharges: t.DeniedCharges,
                    AverageGrossSatsPerCall: t.Calls > 0 ? Math.Round((double)t.GrossSats / t.Calls, 2) : 0
                );
            })
            .ToList();

        return Ok(new McpToolRevenueOverviewResponse(
            WindowHours: windowHours,
            PaidCalls: paidCalls,
            GrossSats: grossSats,
            PlatformFeeSats: platformFeeSats,
            NetSats: netSats,
            DeniedCharges: deniedCharges,
            TopTools: topTools));
    }
}
