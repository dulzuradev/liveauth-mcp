using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Data.Entities.Mcp;
using LiveAuthCore.Models.Mcp;
using LiveAuthCore.Services;
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
    private readonly McpReceiptService _receiptService;
    private readonly WebhookService _webhooks;

    public DeveloperMcpToolsController(
        LiveAuthDbContext db,
        McpReceiptService receiptService,
        WebhookService webhooks)
    {
        _db = db;
        _receiptService = receiptService;
        _webhooks = webhooks;
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

    [HttpGet("revenue")]
    public async Task<ActionResult<McpToolRevenueOverviewResponse>> GetRevenueOverview(
        [FromQuery] Guid? projectId = null,
        [FromQuery] int windowHours = 24,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        if (projectId.HasValue && !await CanUseProjectAsync(projectId.Value, ct))
            return NotFound("Project not found.");

        windowHours = Math.Clamp(windowHours, 1, 24 * 90);
        limit = Math.Clamp(limit, 1, 50);
        var since = DateTime.UtcNow.AddHours(-windowHours);

        var tools = await AccessibleTools()
            .Where(t => !projectId.HasValue || t.ProjectId == projectId.Value)
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

        if (!TryNormalizeWebhookUrl(req.WebhookUrl, out var webhookUrl))
            return BadRequest("Webhook URL must be a valid http or https URL.");

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
            WebhookUrl = webhookUrl,
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

        string? webhookUrl = null;
        if (req.WebhookUrl != null && !TryNormalizeWebhookUrl(req.WebhookUrl, out webhookUrl))
            return BadRequest("Webhook URL must be a valid http or https URL.");

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
        tool.WebhookUrl = req.WebhookUrl == null ? tool.WebhookUrl : webhookUrl;
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

    [HttpPost("{id:guid}/test-charge")]
    public async Task<ActionResult<TestMcpToolChargeResponse>> TestCharge(
        Guid id,
        [FromBody] TestMcpToolChargeRequest req,
        CancellationToken ct = default)
    {
        var tool = await FindMutableToolAsync(id, ct);
        if (tool == null)
            return NotFound();

        var projectResult = await ResolveProjectForTestChargeAsync(tool, req.ProjectId, ct);
        if (projectResult.Error != null)
            return projectResult.Error;

        var project = projectResult.Project!;
        var callCostSats = req.CallCostSats ?? tool.DefaultCostSats;

        if (callCostSats < tool.MinCostSats)
            return BadRequest($"callCostSats must be at least {tool.MinCostSats} for this tool");

        if (tool.MaxCostSats > 0 && callCostSats > tool.MaxCostSats)
            return BadRequest($"callCostSats must be no more than {tool.MaxCostSats} for this tool");

        var fee = CalculatePlatformFee(callCostSats);
        var methodName = string.IsNullOrWhiteSpace(req.ToolMethodName)
            ? tool.Slug
            : req.ToolMethodName.Trim();

        var revenueEvent = new McpToolRevenueEvent
        {
            Id = Guid.NewGuid(),
            McpToolId = tool.Id,
            PayingProjectId = project.Id,
            AgentId = TrimOrNull(req.AgentId),
            ToolMethodName = methodName,
            GrossSats = callCostSats,
            PlatformFeeSats = fee.PlatformFeeSats,
            NetSats = fee.NetSats,
            FeeBasisPoints = fee.FeeBasisPoints,
            Status = "Test",
            RequestId = HttpContext.TraceIdentifier,
            MetadataJson = req.Metadata.HasValue ? JsonSerializer.Serialize(req.Metadata.Value) : null,
            CreatedAt = DateTime.UtcNow
        };

        var receipt = _receiptService.CreateReceipt(revenueEvent, tool);
        var webhookEventType = "liveauth.mcp.tool.paid_call.test";
        var webhookDestinationUrl = (string.IsNullOrWhiteSpace(tool.WebhookUrl)
            ? project.WebhookUrl
            : tool.WebhookUrl)?.Trim();

        Guid? webhookEventId = null;
        if (!string.IsNullOrWhiteSpace(webhookDestinationUrl))
        {
            var payload = new
            {
                type = webhookEventType,
                testMode = true,
                createdAt = revenueEvent.CreatedAt,
                projectId = project.Id,
                payingProjectId = revenueEvent.PayingProjectId,
                mcpToolId = tool.Id,
                toolName = tool.Name,
                toolSlug = tool.Slug,
                toolMethodName = revenueEvent.ToolMethodName,
                revenueEventId = revenueEvent.Id,
                mcpGateTokenId = revenueEvent.McpGateTokenId,
                mcpGateSessionId = revenueEvent.McpGateSessionId,
                agentId = revenueEvent.AgentId,
                grossSats = revenueEvent.GrossSats,
                platformFeeSats = revenueEvent.PlatformFeeSats,
                netSats = revenueEvent.NetSats,
                feeBasisPoints = revenueEvent.FeeBasisPoints,
                status = revenueEvent.Status,
                idempotencyKey = revenueEvent.IdempotencyKey,
                requestId = revenueEvent.RequestId,
                metadata = DeserializeMetadataJson(revenueEvent.MetadataJson),
                receipt
            };

            webhookEventId = await _webhooks.EnqueueAsync(
                project,
                webhookEventType,
                payload,
                tool.WebhookUrl,
                ct);
        }

        var charge = new McpChargeResponse(
            "ok",
            CallsUsed: 0,
            SatsUsed: 0,
            GrossSats: revenueEvent.GrossSats,
            PlatformFeeSats: revenueEvent.PlatformFeeSats,
            NetSats: revenueEvent.NetSats,
            FeeBasisPoints: revenueEvent.FeeBasisPoints,
            RevenueEventId: revenueEvent.Id,
            Receipt: receipt,
            ToolId: tool.Id,
            ToolName: tool.Name,
            ToolSlug: tool.Slug);

        var message = webhookEventId.HasValue
            ? "Test paid-call receipt generated and webhook queued. No revenue was recorded."
            : "Test paid-call receipt generated. Configure a tool webhook URL or project webhook URL to queue delivery.";

        return Ok(new TestMcpToolChargeResponse(
            Charge: charge,
            WebhookQueued: webhookEventId.HasValue,
            WebhookEventId: webhookEventId,
            WebhookEventType: webhookEventId.HasValue ? webhookEventType : null,
            WebhookDestinationUrl: webhookDestinationUrl,
            WebhookStatus: webhookEventId.HasValue ? WebhookEventStatus.Pending.ToString() : null,
            Message: message));
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

    private async Task<(Project? Project, ActionResult? Error)> ResolveProjectForTestChargeAsync(
        McpTool tool,
        Guid? requestedProjectId,
        CancellationToken ct)
    {
        var projectId = requestedProjectId ?? tool.ProjectId;

        if (projectId.HasValue)
        {
            if (!await CanUseProjectAsync(projectId.Value, ct))
                return (null, NotFound("Project not found."));

            var project = await _db.Projects
                .FirstOrDefaultAsync(p => p.Id == projectId.Value && !p.IsDeleted, ct);

            return project == null
                ? (null, NotFound("Project not found."))
                : (project, null);
        }

        var projects = _db.Projects.Where(p => !p.IsDeleted);
        if (!IsAdmin())
        {
            var devId = GetDeveloperId();
            projects = projects.Where(p => p.DeveloperId == devId);
        }

        var fallbackProject = await projects
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return fallbackProject == null
            ? (null, BadRequest("Create a project before testing a paid MCP tool."))
            : (fallbackProject, null);
    }

    private static (int PlatformFeeSats, int NetSats, int FeeBasisPoints) CalculatePlatformFee(int grossSats)
    {
        const int feeBasisPoints = LightningFeeSettingsService.McpPaidToolFeeBasisPoints;
        var platformFeeSats = (int)BasisPointFeeMath.CalculateFeeSats(
            grossSats,
            feeBasisPoints,
            LightningFeeSettingsService.McpPaidToolMinimumFeeSats);

        return (platformFeeSats, grossSats - platformFeeSats, feeBasisPoints);
    }

    private static object? DeserializeMetadataJson(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(metadataJson);
        }
        catch (JsonException)
        {
            return metadataJson;
        }
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

    private static bool TryNormalizeWebhookUrl(string? webhookUrl, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return true;

        if (!Uri.TryCreate(webhookUrl.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return false;

        normalized = uri.ToString();
        return true;
    }
}
