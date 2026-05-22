using System.Security.Claims;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities.Mcp;
using LiveAuthCore.Models.Mcp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JwtRegisteredClaimNames = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/dev/mcp-tools")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DeveloperMcpToolsController : ControllerBase
{
    private readonly LiveAuthDbContext _db;

    public DeveloperMcpToolsController(LiveAuthDbContext db)
    {
        _db = db;
    }

    private bool IsAdmin() => User.IsInRole("Admin");

    private Guid GetDeveloperId()
    {
        var raw =
            User.FindFirst("userId")?.Value ??
            User.FindFirst("developer_id")?.Value ??
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(raw, out var id))
            throw new UnauthorizedAccessException("Invalid developer identity");

        return id;
    }

    [HttpGet]
    public async Task<ActionResult<McpToolListResponse>> ListTools(CancellationToken ct = default)
    {
        var tools = await AccessibleTools()
            .OrderBy(t => t.Name)
            .Select(t => new McpToolDto(
                t.Id,
                t.DeveloperId,
                t.ProjectId,
                t.Name,
                t.Slug,
                t.Description,
                t.Category,
                t.Status,
                t.Visibility,
                t.DefaultCostSats,
                t.MinCostSats,
                t.MaxCostSats,
                t.CreatedAt,
                t.UpdatedAt
            ))
            .ToListAsync(ct);

        return Ok(new McpToolListResponse(tools));
    }

    [HttpGet("{id:guid}/revenue")]
    public async Task<ActionResult<McpToolRevenueSummaryResponse>> GetRevenueSummary(
        Guid id,
        [FromQuery] int windowHours = 24,
        CancellationToken ct = default)
    {
        var tool = await FindAccessibleToolAsync(id, ct);
        if (tool == null)
            return NotFound();

        windowHours = Math.Clamp(windowHours, 1, 24 * 90);
        var since = DateTime.UtcNow.AddHours(-windowHours);

        var events = _db.McpToolRevenueEvents
            .Where(e => e.McpToolId == id && e.CreatedAt >= since && e.Status == "Charged");

        var calls = await events.LongCountAsync(ct);
        var grossSats = await events.SumAsync(e => (long?)e.GrossSats, ct) ?? 0L;
        var platformFeeSats = await events.SumAsync(e => (long?)e.PlatformFeeSats, ct) ?? 0L;
        var netSats = await events.SumAsync(e => (long?)e.NetSats, ct) ?? 0L;

        return Ok(new McpToolRevenueSummaryResponse(
            ToolId: tool.Id,
            ToolName: tool.Name,
            ToolStatus: tool.Status,
            WindowHours: windowHours,
            Calls: calls,
            GrossSats: grossSats,
            PlatformFeeSats: platformFeeSats,
            NetSats: netSats,
            AverageGrossSatsPerCall: calls > 0 ? Math.Round((double)grossSats / calls, 2) : 0
        ));
    }

    [HttpGet("{id:guid}/revenue/events")]
    public async Task<ActionResult<McpToolRevenueEventsResponse>> GetRevenueEvents(
        Guid id,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var tool = await FindAccessibleToolAsync(id, ct);
        if (tool == null)
            return NotFound();

        limit = Math.Clamp(limit, 1, 200);

        var events = await _db.McpToolRevenueEvents
            .Where(e => e.McpToolId == id)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .Select(e => new McpToolRevenueEventDto(
                e.Id,
                e.McpToolId,
                e.McpGateTokenId,
                e.McpGateSessionId,
                e.PayingProjectId,
                e.AgentId,
                e.ToolMethodName,
                e.GrossSats,
                e.PlatformFeeSats,
                e.NetSats,
                e.FeeBasisPoints,
                e.Status,
                e.IdempotencyKey,
                e.RequestId,
                e.MetadataJson,
                e.CreatedAt,
                e.ReversalOfEventId
            ))
            .ToListAsync(ct);

        return Ok(new McpToolRevenueEventsResponse(
            ToolId: tool.Id,
            Limit: limit,
            Events: events
        ));
    }

    private async Task<McpTool?> FindAccessibleToolAsync(Guid toolId, CancellationToken ct)
    {
        return await AccessibleTools()
            .FirstOrDefaultAsync(t => t.Id == toolId, ct);
    }

    private IQueryable<McpTool> AccessibleTools()
    {
        var query = _db.McpTools
            .AsNoTracking()
            .Where(t => t.RemovedAt == null);

        if (IsAdmin())
            return query;

        var devId = GetDeveloperId();
        var projectIds = _db.Projects
            .Where(p => p.DeveloperId == devId && !p.IsDeleted)
            .Select(p => p.Id);

        return query.Where(t =>
            t.DeveloperId == devId ||
            (t.ProjectId.HasValue && projectIds.Contains(t.ProjectId.Value)));
    }
}
