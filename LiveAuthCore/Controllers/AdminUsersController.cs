using LiveAuthCore.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public class AdminUsersController : ControllerBase
{
    private readonly LiveAuthDbContext _db;

    public AdminUsersController(LiveAuthDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// List all developers with project counts. Supports search by email or GitHub username.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AdminUsersListResponse>> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var query = _db.Developers
            .Include(d => d.Projects)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var sl = search.ToLower();
            query = query.Where(d =>
                EF.Functions.Like(d.Email.ToLower(), $"%{sl}%") ||
                (d.GitHubUsername != null && EF.Functions.Like(d.GitHubUsername.ToLower(), $"%{sl}%")));
        }

        var total = await query.CountAsync(ct);

        var devs = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        var projectIds = devs.SelectMany(d => d.Projects.Where(p => p.DeletedAt == null).Select(p => p.Id)).Distinct().ToList();
        var authCounts = await _db.AuthSessions
            .Where(s => s.IsPaid && projectIds.Contains(s.ProjectId))
            .GroupBy(s => s.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, ct);

        var users = devs.Select(d =>
        {
            var activeProjectIds = d.Projects.Where(p => p.DeletedAt == null).Select(p => p.Id).ToHashSet();
            return new AdminUserDto
            {
                Id = d.Id,
                Email = d.Email,
                GitHubUsername = d.GitHubUsername,
                CreatedAt = d.CreatedAt,
                EmailVerified = d.EmailVerified,
                ProjectCount = d.Projects.Count(p => p.DeletedAt == null),
                ProProjectCount = d.Projects.Count(p => p.DeletedAt == null && p.Plan == "pro"),
                TotalAuths = activeProjectIds.Sum(id => authCounts.GetValueOrDefault(id)),
                HasLightningKey = d.LightningAuthKey != null
            };
        }).ToList();

        return Ok(new AdminUsersListResponse
        {
            Users = users,
            Total = total
        });
    }

    /// <summary>
    /// Get a single developer's full profile with all their projects.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminUserDetailResponse>> GetUser(
        Guid id,
        CancellationToken ct = default)
    {
        var dev = await _db.Developers
            .Include(d => d.Projects.Where(p => p.DeletedAt == null))
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (dev == null)
            return NotFound(new { error = "User not found" });

        var projectIds = dev.Projects.Select(p => p.Id).ToList();
        var authStats = await _db.AuthSessions
            .Where(s => s.IsPaid && projectIds.Contains(s.ProjectId))
            .GroupBy(s => s.ProjectId)
            .Select(g => new { ProjectId = g.Key, TotalAuths = g.Count(), TotalSats = g.Sum(s => s.AmountSats), LastAuthAt = g.Max(s => (DateTime?)s.PaidAt) })
            .ToListAsync(ct);
        var authDict = authStats.ToDictionary(x => x.ProjectId);

        var projects = dev.Projects.Select(p =>
        {
            var stats = authDict.GetValueOrDefault(p.Id);
            return new AdminUserProjectDto
            {
                Id = p.Id,
                Name = p.Name,
                PublicKey = p.PublicKey,
                Plan = p.Plan,
                CreatedAt = p.CreatedAt,
                IsActive = p.IsActive,
                ProPaidUntil = p.ProPaidUntil,
                TotalAuths = stats?.TotalAuths ?? 0,
                TotalSats = stats?.TotalSats ?? 0,
                LastAuthAt = stats?.LastAuthAt
            };
        }).ToList();

        return Ok(new AdminUserDetailResponse
        {
            Id = dev.Id,
            Email = dev.Email,
            GitHubUsername = dev.GitHubUsername,
            CreatedAt = dev.CreatedAt,
            EmailVerified = dev.EmailVerified,
            HasLightningKey = dev.LightningAuthKey != null,
            Projects = projects
        });
    }
}

// ── Response Models ──────────────────────────────────────

public class AdminUsersListResponse
{
    public List<AdminUserDto> Users { get; set; } = new();
    public int Total { get; set; }
}

public class AdminUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string? GitHubUsername { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool EmailVerified { get; set; }
    public int ProjectCount { get; set; }
    public int ProProjectCount { get; set; }
    public int TotalAuths { get; set; }
    public bool HasLightningKey { get; set; }
}

public class AdminUserDetailResponse : AdminUserDto
{
    public new List<AdminUserProjectDto> Projects { get; set; } = new();
}

public class AdminUserProjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string Plan { get; set; } = "free";
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime? ProPaidUntil { get; set; }
    public int TotalAuths { get; set; }
    public long TotalSats { get; set; }
    public DateTime? LastAuthAt { get; set; }
}
