using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;

namespace LiveAuthCore.Services;

public class AuthEventService
{
    private readonly LiveAuthDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthEventService(LiveAuthDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    private string? GetClientIp()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx == null) return null;

        // Basic extraction; you can later honor X-Forwarded-For, etc.
        return ctx.Connection.RemoteIpAddress?.ToString();
    }

    public async Task LogAsync(
        Project project,
        AuthEventType type,
        bool success,
        string? reason = null,
        long? satsPaid = null,
        ProjectApiKey? apiKey = null,
        CancellationToken ct = default)
    {
        var evt = new AuthEvent
        {
            Id        = Guid.NewGuid(),
            ProjectId = project.Id,
            ApiKeyId  = apiKey?.Id,
            EventType = type,
            CreatedAt = DateTime.UtcNow,
            ClientIp  = GetClientIp(),
            Success   = success,
            SatsPaid  = satsPaid,
            Reason    = reason
        };

        _db.AuthEvents.Add(evt);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Developer-scoped event log (no project context). Uses a placeholder project ID.
    /// </summary>
    public async Task LogAsync(
        Guid? developerId,
        string eventType,
        bool success,
        string? reason = null,
        long? satsPaid = null,
        CancellationToken ct = default)
    {
        // Use a fixed placeholder for dev-scoped events (not project-linked)
        var projectId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var evt = new AuthEvent
        {
            Id        = Guid.NewGuid(),
            ProjectId = projectId,
            ApiKeyId  = null,
            EventType = Enum.TryParse<AuthEventType>(eventType, true, out var t)
                ? t
                : AuthEventType.LoginRequested,
            CreatedAt = DateTime.UtcNow,
            ClientIp  = GetClientIp(),
            Success   = success,
            SatsPaid  = satsPaid,
            Reason    = reason
        };

        _db.AuthEvents.Add(evt);
        await _db.SaveChangesAsync(ct);
    }

    public void Log(Guid? developerId, string eventType, bool success, string? reason = null)
        => LogAsync(developerId, eventType, success, reason).GetAwaiter().GetResult();
}