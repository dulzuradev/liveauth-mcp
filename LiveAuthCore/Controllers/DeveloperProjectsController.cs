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
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Developer,Admin")]
public class DeveloperProjectsController : ControllerBase
{
    private readonly LiveAuthDbContext _db;
    private readonly ApiKeyService _keys;
    private readonly WebhookService _webhooks;
    private readonly AuthEventService _authEvents;
    private readonly BillingService _billingService;

    public DeveloperProjectsController(
        LiveAuthDbContext db,
        ApiKeyService keys,
        WebhookService webhooks,
        AuthEventService authEvents,
        BillingService billingService)
    {
        _db = db;
        _keys = keys;
        _webhooks = webhooks;
        _authEvents = authEvents;
        _billingService = billingService;
    }

    private bool IsAdmin() => User.IsInRole("Admin");

    /// <summary>
    /// Developer tokens use GUID userId. Admin tokens may use "admin" or non-GUID.
    /// For Admin we don't enforce ownership, so we allow non-GUID.
    /// </summary>
    protected Guid GetDeveloperIdOrThrow()
    {
        // Prefer explicit developer id, fall back to common JWT subject/user id claims.
        var claim =
            User.FindFirst("developer_id") ??
            User.FindFirst("userId") ??
            User.FindFirst(ClaimTypes.NameIdentifier) ??
            User.FindFirst(JwtRegisteredClaimNames.Sub);

        if (claim == null || !Guid.TryParse(claim.Value, out var devId))
            throw new UnauthorizedAccessException("Developer ID missing from token");

        return devId;
    }

    /// <summary>
    /// Ensures the Developer row exists for the current JWT.
    /// - For Admins: returns null (no ownership enforcement).
    /// - For Developers: creates row if missing (first live run).
    /// </summary>
    private async Task<Developer?> GetOrCreateDeveloperAsync(Guid devId, CancellationToken ct = default)
    {
        // Admins are allowed through the controller but may not have a GUID identity.
        // If they do have a GUID, they can behave like a developer; otherwise skip.
        // Here we assume devId is a GUID already (caller uses GetDeveloperIdOrThrow()).
        var dev = await _db.Developers.SingleOrDefaultAsync(d => d.Id == devId, ct);
        if (dev != null) return dev;

        // Auto-provision developer on first successful JWT usage
        dev = new Developer
        {
            Id = devId,
            CreatedAt = DateTime.UtcNow
        };

        // Optional: populate Email if your Developer entity has it.
        // This compiles even if Email doesn't exist if you comment it in/out.
        // var email = User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
        //            ?? User.FindFirst(ClaimTypes.Email)?.Value;
        // if (!string.IsNullOrWhiteSpace(email))
        //     dev.Email = email;

        _db.Developers.Add(dev);
        await _db.SaveChangesAsync(ct);

        return dev;
    }

    // ✅ Create project (owner inferred from JWT)
    [HttpPost]
    public async Task<ActionResult<CreateProjectResponse>> CreateProject([FromBody] CreateProjectRequest req, CancellationToken ct)
    {
        var devId = GetDeveloperIdOrThrow();
        var dev = await GetOrCreateDeveloperAsync(devId, ct);

        // If somehow null (shouldn't happen with GUID devId), treat as unauthorized
        if (dev == null) return Unauthorized("Developer not found.");

        var (pub, sec, secHash) = _keys.GenerateKeys();

        var project = new Project
        {
            DeveloperId = dev.Id,
            Name = req.Name,
            PublicKey = pub,
            SecretKeyHash = secHash
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

    // ✅ List projects for current developer
    [HttpGet]
    public async Task<ActionResult<ListProjectsResponse>> ListProjects(CancellationToken ct)
    {
        var devId = GetDeveloperIdOrThrow();

        // Auto-provision developer on first request so LIST doesn't 401 on a brand new account
        if (!IsAdmin())
        {
            await GetOrCreateDeveloperAsync(devId, ct);
        }

        var query = _db.Projects
            .Where(p => IsAdmin() || p.DeveloperId == devId)
            .OrderByDescending(p => p.CreatedAt);

        var projects = await query
            .Select(p => new ProjectDto
            {
                ProjectId = p.Id,
                Name = p.Name,
                PublicKey = p.PublicKey,
                Plan = p.Plan,
                MonthlyQuota = p.MonthlyQuota,
                MonthlyUsed = p.MonthlyUsed,
                CreatedAt = p.CreatedAt,
                Environment = p.Environment,
                Active = p.IsActive,
                SatsPerLogin = p.SatsPerLogin,
                ProPaidUntil = p.ProPaidUntil,
                MonthlyAuthPeriodStart = p.MonthlyAuthPeriodStart
            })
            .ToListAsync(ct);

        return Ok(new ListProjectsResponse { Projects = projects });
    }

    // ✅ Rotate secret (owner-only unless admin)
    [HttpPost("{projectId:guid}/rotate-secret")]
    public async Task<ActionResult<RotateSecretResponse>> RotateSecret(Guid projectId, CancellationToken ct)
    {
        var devId = GetDeveloperIdOrThrow();

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectId && p.IsActive, ct);
        if (project == null) return NotFound("Project not found.");

        if (!IsAdmin() && project.DeveloperId != devId)
            return Forbid("Not your project.");

        var (newSecret, newHash) = _keys.GenerateNewSecret();
        project.SecretKeyHash = newHash;

        await _db.SaveChangesAsync(ct);

        return Ok(new RotateSecretResponse
        {
            ProjectId = project.Id,
            PublicKey = project.PublicKey,
            SecretKey = newSecret,
            RotatedAt = DateTime.UtcNow
        });
    }

    // ✅ Update project status (Active / Paused)
    [HttpPatch("{projectId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid projectId, [FromBody] UpdateProjectStatusRequest request, CancellationToken ct)
    {
        var devId = GetDeveloperIdOrThrow();

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound("Project not found.");

        var now = DateTime.UtcNow;

        // NOTE: Your original logic had redundant checks; preserved behavior but simplified the intent:
        // LIVE mode restrictions handled elsewhere; here we only gate based on grace period if needed.
        if (_billingService.IsInGracePeriod(project, now))
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, new
            {
                error = "pro_grace_restricted",
                message = "Pro subscription is in grace period. Renew to toggle LIVE mode."
            });
        }

        if (!IsAdmin() && project.DeveloperId != devId)
            return Forbid("Not your project.");

        project.IsActive = request.Active;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ✅ Get project settings
    [HttpGet("{projectId}/settings")]
    public async Task<ActionResult<ProjectSettingsResponse>> GetSettings(string projectId, CancellationToken ct)
    {
        var devId = GetDeveloperIdOrThrow();

        if (!Guid.TryParse(projectId, out var projectGuid))
            return BadRequest("Invalid project id.");

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectGuid, ct);
        if (project == null) return NotFound("Project not found.");

        if (!IsAdmin() && project.DeveloperId != devId)
            return Forbid("Not your project.");

        return Ok(new ProjectSettingsResponse
        {
            AllowedDomains = project.AllowedDomains ?? new List<string>(),
            WebhookUrl = project.WebhookUrl,
            SatsPerLogin = project.SatsPerLogin,
            MaxAuthsPerIpPerHour = project.MaxAuthsPerIpPerHour,
            AllowDemoAuth = project.AllowDemoAuth
        });
    }

    // ✅ Update project settings
    [HttpPut("{projectId}/settings")]
    public async Task<IActionResult> UpdateSettings(string projectId, [FromBody] UpdateProjectSettingsRequest request, CancellationToken ct)
    {
        var devId = GetDeveloperIdOrThrow();

        if (!Guid.TryParse(projectId, out var projectGuid))
            return BadRequest("Invalid project id.");

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectGuid, ct);
        if (project == null) return NotFound("Project not found.");

        if (!IsAdmin() && project.DeveloperId != devId)
            return Forbid("Not your project.");

        project.WebhookUrl = request.WebhookUrl;
        project.SatsPerLogin = request.SatsPerLogin <= 0 ? 0 : request.SatsPerLogin;
        project.MaxAuthsPerIpPerHour = request.MaxAuthsPerIpPerHour <= 0 ? 0 : request.MaxAuthsPerIpPerHour;

        var cleanedDomains = (request.AllowedDomains ?? new List<string>())
            .Select(d => d.Trim())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        project.AllowedDomains = cleanedDomains;
        project.AllowDemoAuth = request.AllowDemoAuth;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ✅ Get analytics
    [HttpGet("{projectId:guid}/analytics")]
    public async Task<ActionResult<AnalyticsSummary>> GetAnalytics(Guid projectId, [FromQuery] int windowHours = 24, CancellationToken ct = default)
    {
        var devId = GetDeveloperIdOrThrow();

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound("Project not found.");
        if (!IsAdmin() && project.DeveloperId != devId)
            return Forbid("Not your project.");

        windowHours = Math.Clamp(windowHours, 1, 24 * 7);
        var cutoff = DateTime.UtcNow.AddHours(-windowHours);

        var eventsQuery = _db.AuthEvents
            .Where(e => e.ProjectId == projectId && e.CreatedAt >= cutoff);

        var totalAuths = await eventsQuery.CountAsync(e =>
            e.EventType == AuthEventType.LoginRequested ||
            e.EventType == AuthEventType.CaptchaRequested, ct);

        var successAuths = await eventsQuery.CountAsync(e =>
            e.EventType == AuthEventType.LoginSucceeded ||
            e.EventType == AuthEventType.CaptchaPassed, ct);

        var failedAuths = await eventsQuery.CountAsync(e =>
            e.EventType == AuthEventType.LoginFailed ||
            e.EventType == AuthEventType.CaptchaFailed, ct);

        var satsPaid = await eventsQuery
            .Where(e => e.SatsPaid.HasValue)
            .SumAsync(e => (long?)e.SatsPaid, ct) ?? 0L;

        var rateLimitHits = await eventsQuery.CountAsync(e => e.EventType == AuthEventType.RateLimitHit, ct);

        return Ok(new AnalyticsSummary
        {
            TotalAuths24h = totalAuths,
            Success24h = successAuths,
            Failed24h = failedAuths,
            SatsPaid24h = satsPaid,
            RateLimitHits24h = rateLimitHits
        });
    }

    // ✅ Get logs
    [HttpGet("{projectId:guid}/logs")]
    public async Task<ActionResult<IReadOnlyList<LogEntry>>> GetLogs(Guid projectId, [FromQuery] int limit = 50, [FromQuery] int windowHours = 24, CancellationToken ct = default)
    {
        var devId = GetDeveloperIdOrThrow();

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound("Project not found.");
        if (!IsAdmin() && project.DeveloperId != devId)
            return Forbid("Not your project.");

        limit = Math.Clamp(limit, 1, 500);
        windowHours = Math.Clamp(windowHours, 1, 24 * 7);
        var cutoff = DateTime.UtcNow.AddHours(-windowHours);

        var events = await _db.AuthEvents
            .Where(e => e.ProjectId == projectId && e.CreatedAt >= cutoff)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        var logs = events.Select(e =>
        {
            var status = e.Success ? "SUCCESS" : "FAILED";
            if (e.EventType == AuthEventType.RateLimitHit)
                status = "RATE_LIMIT";

            var reason = e.Reason;
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = e.EventType switch
                {
                    AuthEventType.LoginRequested => "LOGIN_REQUESTED",
                    AuthEventType.LoginSucceeded => "LOGIN_SUCCEEDED",
                    AuthEventType.LoginFailed => "LOGIN_FAILED",
                    AuthEventType.CaptchaRequested => "CAPTCHA_REQUESTED",
                    AuthEventType.CaptchaPassed => "CAPTCHA_PASSED",
                    AuthEventType.CaptchaFailed => "CAPTCHA_FAILED",
                    AuthEventType.RateLimitHit => "RATE_LIMIT",
                    _ => "UNKNOWN"
                };
            }

            return new LogEntry
            {
                Timestamp = e.CreatedAt,
                IpMasked = e.ClientIp ?? "unknown",
                Sats = e.SatsPaid ?? 0L,
                Status = status,
                Reason = reason
            };
        }).ToList();

        return Ok(logs);
    }

    [HttpPost("{projectId:guid}/test-webhook")]
    public async Task<IActionResult> TestWebhook(Guid projectId, CancellationToken ct)
    {
        var devId = GetDeveloperIdOrThrow();

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound("Project not found.");

        if (!IsAdmin() && project.DeveloperId != devId)
            return Forbid("Not your project.");

        if (string.IsNullOrWhiteSpace(project.WebhookUrl))
            return BadRequest("No webhook URL configured for this project.");

        var payload = new
        {
            type = "liveauth.webhook.test",
            projectId = project.Id,
            createdAt = DateTime.UtcNow,
            message = "This is a test webhook from LiveAuth."
        };

        await _webhooks.EnqueueAsync(project, "liveauth.webhook.test", payload, ct);

        return Accepted();
    }

    [HttpGet("{projectId:guid}/keys")]
    public async Task<ActionResult<ListProjectApiKeysResponse>> ListApiKeys(Guid projectId, CancellationToken ct)
    {
        var devId = GetDeveloperIdOrThrow();

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound("Project not found.");

        if (!IsAdmin() && project.DeveloperId != devId)
            return Forbid("Not your project.");

        var keys = await _db.ProjectApiKeys
            .Where(k => k.ProjectId == projectId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ProjectApiKeyDto
            {
                Id = k.Id,
                Label = k.Label,
                PublicKey = k.PublicKey,
                CreatedAt = k.CreatedAt,
                LastUsedAt = k.LastUsedAt,
                IsActive = k.IsActive
            })
            .ToListAsync(ct);

        return Ok(new ListProjectApiKeysResponse { Keys = keys });
    }

    [HttpPost("{projectId:guid}/keys")]
    public async Task<ActionResult<CreateApiKeyResponse>> CreateApiKey(Guid projectId, [FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        var devId = GetDeveloperIdOrThrow();

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound("Project not found.");
        if (!IsAdmin() && project.DeveloperId != devId)
            return Forbid("Not your project.");

        var (apiKey, secret) = await _keys.CreateApiKeyForProjectAsync(project, request.Label);

        return Ok(new CreateApiKeyResponse
        {
            Id = apiKey.Id,
            Label = apiKey.Label,
            PublicKey = apiKey.PublicKey,
            SecretKey = secret
        });
    }

    [HttpPost("{projectId:guid}/keys/{keyId:guid}/revoke")]
    public async Task<IActionResult> RevokeApiKey(Guid projectId, Guid keyId, CancellationToken ct)
    {
        var devId = GetDeveloperIdOrThrow();

        var key = await _db.ProjectApiKeys
            .Include(k => k.Project)
            .SingleOrDefaultAsync(k => k.Id == keyId && k.ProjectId == projectId, ct);

        if (key == null) return NotFound("API key not found.");

        if (!IsAdmin() && key.Project.DeveloperId != devId)
            return Forbid("Not your project.");

        key.IsActive = false;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPatch("{projectId:guid}/keys/{keyId:guid}")]
    public async Task<IActionResult> UpdateApiKeyLabel(Guid projectId, Guid keyId, [FromBody] UpdateApiKeyLabelRequest request, CancellationToken ct)
    {
        var devId = GetDeveloperIdOrThrow();

        var key = await _db.ProjectApiKeys
            .Include(k => k.Project)
            .SingleOrDefaultAsync(k => k.Id == keyId && k.ProjectId == projectId, ct);

        if (key == null) return NotFound("API key not found.");

        if (!IsAdmin() && key.Project.DeveloperId != devId)
            return Forbid("Not your project.");

        var newLabel = request.Label?.Trim();
        if (string.IsNullOrWhiteSpace(newLabel))
            return BadRequest("Label cannot be empty.");

        key.Label = newLabel;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpGet("{projectId:guid}/webhooks")]
    public async Task<ActionResult<ListWebhookEventsResponse>> GetWebhookEvents(Guid projectId, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var devId = GetDeveloperIdOrThrow();

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound("Project not found.");

        if (!IsAdmin() && project.DeveloperId != devId)
            return Forbid("Not your project.");

        limit = Math.Clamp(limit, 1, 200);

        var events = await _db.WebhookEvents
            .Where(e => e.ProjectId == projectId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .Select(e => new WebhookEventDto
            {
                Id = e.Id,
                EventType = e.EventType,
                CreatedAt = e.CreatedAt,
                LastAttemptAt = e.LastAttemptAt,
                AttemptCount = e.AttemptCount,
                Status = e.Status,
                LastStatusCode = e.LastStatusCode,
                LastError = e.LastError
            })
            .ToListAsync(ct);

        return Ok(new ListWebhookEventsResponse { Events = events });
    }

    [HttpPost("{projectId:guid}/webhooks/{eventId:guid}/replay")]
    public async Task<IActionResult> ReplayWebhook(Guid projectId, Guid eventId, CancellationToken ct)
    {
        var devId = GetDeveloperIdOrThrow();

        var evt = await _db.WebhookEvents
            .Include(e => e.Project)
            .SingleOrDefaultAsync(e => e.Id == eventId && e.ProjectId == projectId, ct);

        if (evt == null) return NotFound("Webhook event not found.");

        if (!IsAdmin() && evt.Project.DeveloperId != devId)
            return Forbid("Not your project.");

        evt.Status = WebhookEventStatus.Pending;
        evt.NextAttemptAt = DateTime.UtcNow;
        evt.LastError = null;
        evt.LastStatusCode = null;

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPatch("{projectId:guid}/environment")]
    public async Task<IActionResult> UpdateEnvironment(Guid projectId, [FromBody] UpdateProjectEnvironmentRequest request, CancellationToken ct)
    {
        var devId = GetDeveloperIdOrThrow();

        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null)
            return NotFound("Project not found.");

        if (!IsAdmin() && project.DeveloperId != devId)
            return Forbid("Not your project.");

        var env = (request.Environment ?? string.Empty).Trim().ToUpperInvariant();
        if (env != "TEST" && env != "LIVE")
            return BadRequest("Environment must be TEST or LIVE.");

        if (env == "LIVE")
        {
            var now = DateTime.UtcNow;

            var hasActivePro =
                project.Plan == "pro" &&
                project.ProPaidUntil.HasValue &&
                project.ProPaidUntil.Value > now;

            if (_billingService.IsInGracePeriod(project, now))
            {
                return StatusCode(StatusCodes.Status402PaymentRequired, new
                {
                    error = "pro_grace_restricted",
                    message = "Your Pro subscription is in a grace period. Renew to enable LIVE mode."
                });
            }

            if (!hasActivePro)
            {
                return StatusCode(StatusCodes.Status402PaymentRequired, new
                {
                    error = "pro_required",
                    message = "An active Pro subscription is required to enable LIVE mode."
                });
            }
        }

        project.Environment = env;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}
