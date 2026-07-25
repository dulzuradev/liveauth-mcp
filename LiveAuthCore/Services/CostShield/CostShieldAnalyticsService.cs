using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models.CostShield;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services.CostShield;

public interface ICostShieldAnalyticsService
{
    Task<CostShieldAnalyticsResult<CostShieldOverviewResponse>> GetOverviewAsync(
        Guid projectId,
        Guid developerId,
        bool isAdmin,
        int windowHours,
        CancellationToken ct);

    Task<CostShieldAnalyticsResult<CostShieldEventListResponse>> GetEventsAsync(
        Guid projectId,
        Guid developerId,
        bool isAdmin,
        int limit,
        int offset,
        CancellationToken ct);
}

public sealed class CostShieldAnalyticsService : ICostShieldAnalyticsService
{
    private static readonly AuthEventType[] DeniedEventTypes =
    {
        AuthEventType.CostShieldChallengeFailed,
        AuthEventType.CostShieldReplayBlocked,
        AuthEventType.CostShieldRateLimited,
        AuthEventType.CostShieldInvalidOrigin
    };

    private static readonly AuthEventType[] InvalidEventTypes =
    {
        AuthEventType.CostShieldChallengeFailed,
        AuthEventType.CostShieldInvalidOrigin
    };

    private readonly LiveAuthDbContext _db;

    public CostShieldAnalyticsService(LiveAuthDbContext db)
    {
        _db = db;
    }

    public async Task<CostShieldAnalyticsResult<CostShieldOverviewResponse>> GetOverviewAsync(
        Guid projectId,
        Guid developerId,
        bool isAdmin,
        int windowHours,
        CancellationToken ct)
    {
        if (windowHours is < 1 or > 24 * 30)
            return CostShieldAnalyticsResult<CostShieldOverviewResponse>.Invalid();

        if (!await CanAccessProjectAsync(projectId, developerId, isAdmin, ct))
            return CostShieldAnalyticsResult<CostShieldOverviewResponse>.NotFound();

        var windowEnd = DateTime.UtcNow;
        var windowStart = windowEnd.AddHours(-windowHours);
        var actions = await _db.ProtectedActions
            .AsNoTracking()
            .Where(action => action.ProjectId == projectId)
            .Select(action => new
            {
                action.Id,
                action.Name,
                action.DisplayName,
                action.IsEnabled,
                action.EstimatedCostPerExecution
            })
            .ToListAsync(ct);

        var eventCounts = await _db.AuthEvents
            .AsNoTracking()
            .Where(evt =>
                evt.ProjectId == projectId &&
                evt.ProtectedActionId != null &&
                evt.CreatedAt >= windowStart &&
                evt.CreatedAt <= windowEnd)
            .GroupBy(evt => evt.EventType)
            .Select(group => new
            {
                EventType = group.Key,
                Count = group.Count()
            })
            .ToDictionaryAsync(item => item.EventType, item => item.Count, ct);

        var actionEventCounts = await _db.AuthEvents
            .AsNoTracking()
            .Where(evt =>
                evt.ProjectId == projectId &&
                evt.ProtectedActionId != null &&
                evt.CreatedAt >= windowStart &&
                evt.CreatedAt <= windowEnd)
            .GroupBy(evt => new
            {
                ProtectedActionId = evt.ProtectedActionId!.Value,
                evt.EventType
            })
            .Select(group => new
            {
                group.Key.ProtectedActionId,
                group.Key.EventType,
                Count = group.Count()
            })
            .ToListAsync(ct);

        var protectedCostEvents = await _db.AuthEvents
            .AsNoTracking()
            .Where(evt =>
                evt.ProjectId == projectId &&
                evt.ProtectedActionId != null &&
                evt.CreatedAt >= windowStart &&
                evt.CreatedAt <= windowEnd &&
                evt.EstimatedCostProtected != null)
            .Select(evt => new
            {
                ProtectedActionId = evt.ProtectedActionId!.Value,
                Cost = evt.EstimatedCostProtected!.Value
            })
            .ToListAsync(ct);
        var protectedCostsByAction = protectedCostEvents
            .GroupBy(evt => evt.ProtectedActionId)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Count = group.Count(),
                    Cost = group.Sum(evt => evt.Cost)
                });

        var countsByAction = actionEventCounts
            .GroupBy(item => item.ProtectedActionId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(item => item.EventType, item => item.Count));

        var topActions = actions
            .Select(action =>
            {
                countsByAction.TryGetValue(action.Id, out var counts);
                counts ??= new Dictionary<AuthEventType, int>();
                var denied = Sum(counts, DeniedEventTypes);
                protectedCostsByAction.TryGetValue(
                    action.Id,
                    out var protectedCost);
                return new CostShieldActionUsageDto(
                    action.Id,
                    action.Name,
                    action.DisplayName,
                    Get(counts, AuthEventType.CostShieldChallengeIssued),
                    Get(counts, AuthEventType.CostShieldAuthorizationIssued),
                    protectedCost?.Count ?? 0,
                    denied,
                    denied * action.EstimatedCostPerExecution);
            })
            .OrderByDescending(action => action.ChallengesIssued)
            .ThenByDescending(action => action.RequestsDenied)
            .Take(5)
            .ToList();

        var challengesIssued = Get(
            eventCounts,
            AuthEventType.CostShieldChallengeIssued);
        var challengesCompleted = Get(
            eventCounts,
            AuthEventType.CostShieldChallengeCompleted);
        var protectedRequests = protectedCostEvents.Count;
        var requestsDenied = Sum(eventCounts, DeniedEventTypes);
        var estimatedCostAvoided = actions.Sum(action =>
        {
            if (!countsByAction.TryGetValue(action.Id, out var counts))
                return 0m;
            return Sum(counts, DeniedEventTypes) * action.EstimatedCostPerExecution;
        });

        var averageDuration = await _db.AuthEvents
            .AsNoTracking()
            .Where(evt =>
                evt.ProjectId == projectId &&
                evt.ProtectedActionId != null &&
                evt.CreatedAt >= windowStart &&
                evt.CreatedAt <= windowEnd &&
                evt.DurationMilliseconds != null &&
                evt.EventType == AuthEventType.CostShieldChallengeCompleted)
            .Select(evt => (double?)evt.DurationMilliseconds)
            .AverageAsync(ct);

        return CostShieldAnalyticsResult<CostShieldOverviewResponse>.Found(
            new CostShieldOverviewResponse(
                windowHours,
                windowStart,
                windowEnd,
                actions.Count,
                actions.Count(action => action.IsEnabled),
                challengesIssued,
                challengesCompleted,
                Get(eventCounts, AuthEventType.CostShieldAuthorizationIssued),
                protectedRequests,
                requestsDenied,
                Get(eventCounts, AuthEventType.CostShieldRateLimited),
                Sum(eventCounts, InvalidEventTypes),
                Get(eventCounts, AuthEventType.CostShieldReplayBlocked),
                protectedCostEvents.Sum(evt => evt.Cost),
                estimatedCostAvoided,
                challengesIssued == 0
                    ? 0
                    : Math.Round(
                        challengesCompleted * 100d / challengesIssued,
                        1),
                averageDuration,
                EstimatedValues: true,
                topActions));
    }

    public async Task<CostShieldAnalyticsResult<CostShieldEventListResponse>> GetEventsAsync(
        Guid projectId,
        Guid developerId,
        bool isAdmin,
        int limit,
        int offset,
        CancellationToken ct)
    {
        if (limit is < 1 or > 100 || offset is < 0 or > 100_000)
            return CostShieldAnalyticsResult<CostShieldEventListResponse>.Invalid();

        if (!await CanAccessProjectAsync(projectId, developerId, isAdmin, ct))
            return CostShieldAnalyticsResult<CostShieldEventListResponse>.NotFound();

        var query = _db.AuthEvents
            .AsNoTracking()
            .Where(evt =>
                evt.ProjectId == projectId &&
                evt.ProtectedActionId != null);
        var total = await query.CountAsync(ct);
        var events = await query
            .OrderByDescending(evt => evt.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(evt => new CostShieldEventDto(
                evt.Id,
                evt.ProtectedActionId,
                evt.ProtectedAction != null ? evt.ProtectedAction.Name : null,
                evt.ProtectedAction != null ? evt.ProtectedAction.DisplayName : null,
                evt.EventType.ToString(),
                evt.Environment,
                evt.VerificationMethod,
                evt.Success,
                evt.Reason,
                MaskSource(evt.IpAddressHash),
                evt.DurationMilliseconds,
                evt.EstimatedCostProtected,
                evt.CreatedAt))
            .ToListAsync(ct);

        return CostShieldAnalyticsResult<CostShieldEventListResponse>.Found(
            new CostShieldEventListResponse(total, limit, offset, events));
    }

    private Task<bool> CanAccessProjectAsync(
        Guid projectId,
        Guid developerId,
        bool isAdmin,
        CancellationToken ct)
    {
        return _db.Projects.AnyAsync(project =>
            project.Id == projectId &&
            !project.IsDeleted &&
            (isAdmin || project.DeveloperId == developerId), ct);
    }

    private static int Get(
        IReadOnlyDictionary<AuthEventType, int> counts,
        AuthEventType type)
        => counts.TryGetValue(type, out var count) ? count : 0;

    private static int Sum(
        IReadOnlyDictionary<AuthEventType, int> counts,
        IEnumerable<AuthEventType> types)
        => types.Sum(type => Get(counts, type));

    private static string? MaskSource(string? hash)
        => string.IsNullOrWhiteSpace(hash)
            ? null
            : $"source_{hash[..Math.Min(10, hash.Length)]}";
}

public enum CostShieldAnalyticsStatus
{
    Found,
    NotFound,
    Invalid
}

public sealed record CostShieldAnalyticsResult<T>(
    CostShieldAnalyticsStatus Status,
    T? Value)
{
    public static CostShieldAnalyticsResult<T> Found(T value)
        => new(CostShieldAnalyticsStatus.Found, value);

    public static CostShieldAnalyticsResult<T> NotFound()
        => new(CostShieldAnalyticsStatus.NotFound, default);

    public static CostShieldAnalyticsResult<T> Invalid()
        => new(CostShieldAnalyticsStatus.Invalid, default);
}
