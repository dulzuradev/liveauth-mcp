using System.Security.Claims;
using System.Text.RegularExpressions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
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
    private static readonly Regex SlugRegex = new("^[a-z0-9][a-z0-9-]{1,98}[a-z0-9]$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Draft",
        "Active",
        "Paused"
    };

    private static readonly HashSet<string> AllowedVisibilities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Private",
        "Unlisted",
        "Public"
    };

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
                t.WebsiteUrl,
                t.DocsUrl,
                t.WebhookUrl,
                t.CreatedAt,
                t.UpdatedAt
            ))
            .ToListAsync(ct);

        return Ok(new McpToolListResponse(tools));
    }

    [HttpPost]
    public async Task<ActionResult<McpToolDto>> CreateTool(
        [FromBody] CreateMcpToolRequest req,
        CancellationToken ct = default)
    {
        var devId = GetDeveloperId();
        await GetOrCreateDeveloperAsync(devId, ct);

        var validation = await ValidateToolInputAsync(
            req.ProjectId,
            req.Name,
            req.Slug,
            req.Visibility,
            req.Status,
            req.DefaultCostSats,
            req.MinCostSats,
            req.MaxCostSats,
            existingToolId: null,
            ct);

        if (validation.Result != null)
            return validation.Result;

        var now = DateTime.UtcNow;
        var tool = new McpTool
        {
            DeveloperId = devId,
            ProjectId = req.ProjectId,
            Name = req.Name.Trim(),
            Slug = validation.Slug,
            Description = (req.Description ?? string.Empty).Trim(),
            Category = TrimOrNull(req.Category),
            Visibility = NormalizeVisibility(req.Visibility),
            Status = NormalizeStatus(req.Status),
            DefaultCostSats = req.DefaultCostSats,
            MinCostSats = req.MinCostSats,
            MaxCostSats = req.MaxCostSats,
            WebsiteUrl = TrimOrNull(req.WebsiteUrl),
            DocsUrl = TrimOrNull(req.DocsUrl),
            WebhookUrl = TrimOrNull(req.WebhookUrl),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.McpTools.Add(tool);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetTool), new { id = tool.Id }, ToDto(tool));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<McpToolDto>> GetTool(Guid id, CancellationToken ct = default)
    {
        var tool = await FindAccessibleToolAsync(id, ct);
        if (tool == null)
            return NotFound();

        return Ok(ToDto(tool));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<McpToolDto>> UpdateTool(
        Guid id,
        [FromBody] UpdateMcpToolRequest req,
        CancellationToken ct = default)
    {
        var tool = await FindMutableToolAsync(id, ct);
        if (tool == null)
            return NotFound();

        var projectId = req.ClearProject == true ? null : req.ProjectId ?? tool.ProjectId;
        var name = req.Name ?? tool.Name;
        var slug = req.Slug ?? tool.Slug;
        var visibility = req.Visibility ?? tool.Visibility;
        var status = req.Status ?? tool.Status;
        var defaultCostSats = req.DefaultCostSats ?? tool.DefaultCostSats;
        var minCostSats = req.MinCostSats ?? tool.MinCostSats;
        var maxCostSats = req.MaxCostSats ?? tool.MaxCostSats;

        var validation = await ValidateToolInputAsync(
            projectId,
            name,
            slug,
            visibility,
            status,
            defaultCostSats,
            minCostSats,
            maxCostSats,
            id,
            ct);

        if (validation.Result != null)
            return validation.Result;

        tool.ProjectId = projectId;
        tool.Name = name.Trim();
        tool.Slug = validation.Slug;
        tool.Description = req.Description == null ? tool.Description : req.Description.Trim();
        tool.Category = req.Category == null ? tool.Category : TrimOrNull(req.Category);
        tool.Visibility = NormalizeVisibility(visibility);
        tool.Status = NormalizeStatus(status);
        tool.DefaultCostSats = defaultCostSats;
        tool.MinCostSats = minCostSats;
        tool.MaxCostSats = maxCostSats;
        tool.WebsiteUrl = req.WebsiteUrl == null ? tool.WebsiteUrl : TrimOrNull(req.WebsiteUrl);
        tool.DocsUrl = req.DocsUrl == null ? tool.DocsUrl : TrimOrNull(req.DocsUrl);
        tool.WebhookUrl = req.WebhookUrl == null ? tool.WebhookUrl : TrimOrNull(req.WebhookUrl);
        tool.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(tool));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTool(Guid id, CancellationToken ct = default)
    {
        var tool = await FindMutableToolAsync(id, ct);
        if (tool == null)
            return NotFound();

        tool.Status = "Removed";
        tool.RemovedAt = DateTime.UtcNow;
        tool.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return NoContent();
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

    private async Task<McpTool?> FindMutableToolAsync(Guid toolId, CancellationToken ct)
    {
        var query = _db.McpTools
            .Where(t => t.Id == toolId && t.RemovedAt == null);

        if (IsAdmin())
            return await query.FirstOrDefaultAsync(ct);

        var devId = GetDeveloperId();
        return await query.FirstOrDefaultAsync(t => t.DeveloperId == devId, ct);
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

    private async Task<Developer> GetOrCreateDeveloperAsync(Guid devId, CancellationToken ct)
    {
        var dev = await _db.Developers.SingleOrDefaultAsync(d => d.Id == devId, ct);
        if (dev != null)
            return dev;

        dev = new Developer
        {
            Id = devId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Developers.Add(dev);
        await _db.SaveChangesAsync(ct);

        return dev;
    }

    private async Task<(ActionResult<McpToolDto>? Result, string Slug)> ValidateToolInputAsync(
        Guid? projectId,
        string? name,
        string? slug,
        string? visibility,
        string? status,
        int defaultCostSats,
        int minCostSats,
        int maxCostSats,
        Guid? existingToolId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            return (BadRequest("Tool name is required and must be 100 characters or less."), string.Empty);

        var normalizedSlug = NormalizeSlug(slug, name);
        if (!SlugRegex.IsMatch(normalizedSlug))
            return (BadRequest("Tool slug must be 3-100 lowercase letters, numbers, or hyphens."), normalizedSlug);

        if (!AllowedVisibilities.Contains(visibility ?? "Private"))
            return (BadRequest("Visibility must be Private, Unlisted, or Public."), normalizedSlug);

        if (!AllowedStatuses.Contains(status ?? "Draft"))
            return (BadRequest("Status must be Draft, Active, or Paused."), normalizedSlug);

        if (minCostSats < 1 || defaultCostSats < minCostSats || maxCostSats < defaultCostSats)
            return (BadRequest("Cost bounds must satisfy 1 <= min <= default <= max."), normalizedSlug);

        var slugExists = await _db.McpTools.AnyAsync(t =>
            t.Slug == normalizedSlug &&
            t.RemovedAt == null &&
            (!existingToolId.HasValue || t.Id != existingToolId.Value), ct);

        if (slugExists)
            return (Conflict("An MCP tool with this slug already exists."), normalizedSlug);

        if (projectId.HasValue && !await CanUseProjectAsync(projectId.Value, ct))
            return (NotFound("Project not found."), normalizedSlug);

        return (null, normalizedSlug);
    }

    private async Task<bool> CanUseProjectAsync(Guid projectId, CancellationToken ct)
    {
        if (IsAdmin())
            return await _db.Projects.AnyAsync(p => p.Id == projectId && !p.IsDeleted, ct);

        var devId = GetDeveloperId();
        return await _db.Projects.AnyAsync(p => p.Id == projectId && p.DeveloperId == devId && !p.IsDeleted, ct);
    }

    private static McpToolDto ToDto(McpTool tool) => new(
        tool.Id,
        tool.DeveloperId,
        tool.ProjectId,
        tool.Name,
        tool.Slug,
        tool.Description,
        tool.Category,
        tool.Status,
        tool.Visibility,
        tool.DefaultCostSats,
        tool.MinCostSats,
        tool.MaxCostSats,
        tool.WebsiteUrl,
        tool.DocsUrl,
        tool.WebhookUrl,
        tool.CreatedAt,
        tool.UpdatedAt
    );

    private static string NormalizeSlug(string? slug, string? name)
    {
        var raw = string.IsNullOrWhiteSpace(slug) ? name ?? string.Empty : slug;
        return Regex.Replace(raw.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    }

    private static string NormalizeStatus(string? status)
    {
        var value = string.IsNullOrWhiteSpace(status) ? "Draft" : status.Trim();
        return AllowedStatuses.First(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeVisibility(string? visibility)
    {
        var value = string.IsNullOrWhiteSpace(visibility) ? "Private" : visibility.Trim();
        return AllowedVisibilities.First(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
