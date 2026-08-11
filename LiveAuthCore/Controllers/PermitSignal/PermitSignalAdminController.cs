using LiveAuthCore.Data;
using LiveAuthCore.Services.PermitSignal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers.PermitSignal;

[ApiController]
[Route("api/admin/permitsignal")]
[Authorize(Roles = "Admin")]
public sealed class PermitSignalAdminController : ControllerBase
{
    private static readonly string[] ToolSlugs =
    [
        "permitsignal-search-projects", "permitsignal-find-opportunities",
        "permitsignal-analyze-project", "permitsignal-property-history"
    ];

    private readonly LiveAuthDbContext _db;
    private readonly IPermitSynchronizationService _synchronization;

    public PermitSignalAdminController(LiveAuthDbContext db, IPermitSynchronizationService synchronization)
    {
        _db = db;
        _synchronization = synchronization;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var tools = await _db.McpTools.AsNoTracking().Where(tool => ToolSlugs.Contains(tool.Slug))
            .Select(tool => new { tool.Id, tool.Name, tool.Slug }).ToListAsync(ct);
        var toolIds = tools.Select(tool => tool.Id).ToArray();
        var revenue = await _db.McpToolRevenueEvents.AsNoTracking()
            .Where(item => toolIds.Contains(item.McpToolId) && item.Status == "Charged")
            .GroupBy(item => item.McpToolId)
            .Select(group => new { ToolId = group.Key, Calls = group.LongCount(), Sats = group.Sum(item => (long)item.GrossSats) })
            .ToListAsync(ct);
        var sourceStatus = await _db.PermitSources.AsNoTracking().OrderBy(source => source.SourceIdentifier)
            .Select(source => new
            {
                source.SourceIdentifier, source.Municipality, source.State, source.HealthStatus,
                source.LastSuccessfulSync, source.LastError, source.OfficialDatasetUrl
            }).ToListAsync(ct);
        var topMunicipalities = await _db.PermitProjects.AsNoTracking()
            .GroupBy(project => new { project.Municipality, project.State })
            .Select(group => new { group.Key.Municipality, group.Key.State, Records = group.LongCount() })
            .OrderByDescending(item => item.Records).Take(10).ToListAsync(ct);

        return Ok(new
        {
            permitRecordsStored = await _db.PermitProjects.LongCountAsync(ct),
            permitsAddedLast24Hours = await _db.PermitProjects.LongCountAsync(project => project.CreatedAt >= since, ct),
            sources = sourceStatus,
            tools = tools.Select(tool =>
            {
                var stats = revenue.SingleOrDefault(item => item.ToolId == tool.Id);
                return new { tool.Name, tool.Slug, Calls = stats?.Calls ?? 0, SatsGenerated = stats?.Sats ?? 0 };
            }),
            topMunicipalities
        });
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Synchronize([FromQuery] string? source = null, CancellationToken ct = default)
        => Ok(await _synchronization.SynchronizeAsync(source, ct));
}
