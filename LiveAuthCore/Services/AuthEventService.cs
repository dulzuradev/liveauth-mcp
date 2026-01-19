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
}