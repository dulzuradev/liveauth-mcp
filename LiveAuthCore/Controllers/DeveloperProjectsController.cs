using System.Security.Claims;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JwtRegisteredClaimNames = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames;

namespace LiveAuthCore.Controllers;

[ApiController]
[Route("api/dev/projects")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DeveloperProjectsController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly ApiKeyService _keys;
    private readonly WebhookService _webhooks;
    private readonly BillingService _billingService;

    public DeveloperProjectsController(
        LiveAuthDbContext db,
        ApiKeyService keys,
        WebhookService webhooks,
        BillingService billingService)
    {
        _db = db;
        _keys = keys;
        _webhooks = webhooks;
        _billingService = billingService;
    }

    private bool IsAdmin() => User.IsInRole("Admin");

    // 🔒 SINGLE source of truth
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

    private async Task<Developer> GetOrCreateDeveloperAsync(Guid devId, CancellationToken ct)
    {
        var dev = await _db.Developers.SingleOrDefaultAsync(d => d.Id == devId, ct);
        if (dev != null) return dev;

        dev = new Developer
        {
            Id = devId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Developers.Add(dev);
        await _db.SaveChangesAsync(ct);

        return dev;
    }

    // ─────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<CreateProjectResponse>> CreateProject(
        [FromBody] CreateProjectRequest req,
        CancellationToken ct)
    {
        try
        {
            var devId = GetDeveloperId();
            var dev = await GetOrCreateDeveloperAsync(devId, ct);

            var (pub, sec, hash) = _keys.GenerateKeys();

            var project = new Project
            {
                DeveloperId = dev.Id,
                Name = req.Name,
                PublicKey = pub,
                SecretKeyHash = hash
            };

            _db.Projects.Add(project);
            await _db.SaveChangesAsync(ct);

            return Ok(new CreateProjectResponse
            {
                ProjectId = project.Id,
                PublicKey = pub,
                SecretKey = sec
            });
        }
        catch
        {
            return Unauthorized(new { error = "Unauthorized or invalid token" });
        }
    }

    [HttpGet]
    public async Task<ActionResult<ListProjectsResponse>> ListProjects(CancellationToken ct)
    {
        try
        {
            var devId = GetDeveloperId();
            await GetOrCreateDeveloperAsync(devId, ct);

            var projects = await _db.Projects
                .Where(p => IsAdmin() || p.DeveloperId == devId)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new ProjectDto
                {
                    ProjectId = p.Id,
                    Name = p.Name,
                    PublicKey = p.PublicKey,

                    // ✅ REQUIRED
                    Plan = p.Plan ?? "free",
                    CreatedAt = p.CreatedAt,

                    // Optional / defaults
                    Environment = p.Environment,
                    Active = p.IsActive,
                    MonthlyQuota = p.MonthlyQuota,
                    MonthlyUsed = p.MonthlyUsed,
                    SatsPerLogin = p.SatsPerLogin,
                    ProPaidUntil = p.ProPaidUntil,
                    MonthlyAuthPeriodStart = p.MonthlyAuthPeriodStart
                })
                .ToListAsync(ct);

            return Ok(new ListProjectsResponse { Projects = projects });
        }
        catch
        {
            return Unauthorized(new { error = "Unauthorized or invalid token" });
        }
    }
}
