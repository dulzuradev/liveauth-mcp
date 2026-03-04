using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class McpProxyController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly L402Service _l402;
    private readonly IConfiguration _config;

    public McpProxyController(LiveAuthDbContext db, L402Service l402, IConfiguration config)
    {
        _db = db;
        _l402 = l402;
        _config = config;
    }

    /// <summary>
    /// List all MCP proxies for the authenticated project
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var projectId = GetProjectId();
        if (projectId == null)
            return Unauthorized();

        var proxies = await _db.McpProxies
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        return Ok(proxies.Select(p => new
        {
            p.Id,
            p.Name,
            p.UpstreamUrl,
            p.SatsPerRequest,
            p.IsActive,
            p.CustomPath,
            p.TotalRequests,
            p.TotalSatsEarned,
            CreatedAt = p.CreatedAt.ToString("O"),
            ProxyUrl = $"/mcp/{p.CustomPath ?? p.Id.ToString("N")[..8]}"
        }));
    }

    /// <summary>
    /// Register a new MCP proxy
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMcpProxyRequest req, CancellationToken ct)
    {
        var projectId = GetProjectId();
        if (projectId == null)
            return Unauthorized();

        // Check if custom path is taken
        if (!string.IsNullOrEmpty(req.CustomPath))
        {
            var existing = await _db.McpProxies
                .AnyAsync(p => p.CustomPath == req.CustomPath && p.ProjectId == projectId, ct);
            if (existing)
                return BadRequest("Custom path already in use");
        }

        var proxy = new McpProxy
        {
            ProjectId = projectId.Value,
            Name = req.Name,
            UpstreamUrl = req.UpstreamUrl.TrimEnd('/'),
            SatsPerRequest = req.SatsPerRequest > 0 ? req.SatsPerRequest : 1,
            CustomPath = req.CustomPath,
            IsActive = true
        };

        _db.McpProxies.Add(proxy);
        await _db.SaveChangesAsync(ct);

        return Created($"/api/mcpproxy/{proxy.Id}", new
        {
            proxy.Id,
            proxy.Name,
            proxy.UpstreamUrl,
            proxy.SatsPerRequest,
            proxy.CustomPath,
            ProxyUrl = $"/mcp/{proxy.CustomPath ?? proxy.Id.ToString("N")[..8]}"
        });
    }

    /// <summary>
    /// Update an MCP proxy
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMcpProxyRequest req, CancellationToken ct)
    {
        var projectId = GetProjectId();
        if (projectId == null)
            return Unauthorized();

        var proxy = await _db.McpProxies
            .FirstOrDefaultAsync(p => p.Id == id && p.ProjectId == projectId, ct);

        if (proxy == null)
            return NotFound();

        if (!string.IsNullOrEmpty(req.Name))
            proxy.Name = req.Name;
        if (req.SatsPerRequest > 0)
            proxy.SatsPerRequest = req.SatsPerRequest;
        if (req.IsActive.HasValue)
            proxy.IsActive = req.IsActive.Value;
        if (!string.IsNullOrEmpty(req.UpstreamUrl))
            proxy.UpstreamUrl = req.UpstreamUrl.TrimEnd('/');

        proxy.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { proxy.Id, proxy.Name, proxy.UpstreamUrl, proxy.SatsPerRequest, proxy.IsActive });
    }

    /// <summary>
    /// Delete an MCP proxy
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var projectId = GetProjectId();
        if (projectId == null)
            return Unauthorized();

        var proxy = await _db.McpProxies
            .FirstOrDefaultAsync(p => p.Id == id && p.ProjectId == projectId, ct);

        if (proxy == null)
            return NotFound();

        _db.McpProxies.Remove(proxy);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// Get proxy usage stats
    /// </summary>
    [HttpGet("{id:guid}/stats")]
    public async Task<IActionResult> GetStats(Guid id, CancellationToken ct)
    {
        var projectId = GetProjectId();
        if (projectId == null)
            return Unauthorized();

        var proxy = await _db.McpProxies
            .FirstOrDefaultAsync(p => p.Id == id && p.ProjectId == projectId, ct);

        if (proxy == null)
            return NotFound();

        return Ok(new
        {
            proxy.TotalRequests,
            proxy.TotalSatsEarned,
            proxy.SatsPerRequest,
            AverageSatsPerDay = proxy.TotalSatsEarned // Could add daily aggregation
        });
    }

    private Guid? GetProjectId()
    {
        var claim = User.FindFirst("project_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

public class CreateMcpProxyRequest
{
    public string Name { get; set; } = "";
    public string UpstreamUrl { get; set; } = "";
    public int SatsPerRequest { get; set; } = 1;
    public string? CustomPath { get; set; }
}

public class UpdateMcpProxyRequest
{
    public string? Name { get; set; }
    public string? UpstreamUrl { get; set; }
    public int SatsPerRequest { get; set; }
    public bool? IsActive { get; set; }
}
