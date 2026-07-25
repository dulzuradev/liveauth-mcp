using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models.CostShield;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Services.CostShield;

public interface IProtectedActionService
{
    Task<ProtectedActionReadResult> ListAsync(
        Guid projectId,
        Guid developerId,
        bool isAdmin,
        string? environment,
        CancellationToken ct);

    Task<ProtectedActionReadResult> GetAsync(
        Guid projectId,
        Guid actionId,
        Guid developerId,
        bool isAdmin,
        CancellationToken ct);

    Task<ProtectedActionWriteResult> CreateAsync(
        Guid projectId,
        Guid developerId,
        bool isAdmin,
        UpsertProtectedActionRequest request,
        CancellationToken ct);

    Task<ProtectedActionWriteResult> UpdateAsync(
        Guid projectId,
        Guid actionId,
        Guid developerId,
        bool isAdmin,
        UpsertProtectedActionRequest request,
        CancellationToken ct);

    Task<ProtectedActionWriteResult> DeleteAsync(
        Guid projectId,
        Guid actionId,
        Guid developerId,
        bool isAdmin,
        CancellationToken ct);
}

public sealed class ProtectedActionService : IProtectedActionService
{
    private readonly LiveAuthDbContext _db;

    public ProtectedActionService(LiveAuthDbContext db)
    {
        _db = db;
    }

    public async Task<ProtectedActionReadResult> ListAsync(
        Guid projectId,
        Guid developerId,
        bool isAdmin,
        string? environment,
        CancellationToken ct)
    {
        if (!await CanAccessProjectAsync(projectId, developerId, isAdmin, ct))
            return ProtectedActionReadResult.NotFound();

        var query = _db.ProtectedActions
            .AsNoTracking()
            .Where(action => action.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(environment))
        {
            var normalizedEnvironment = environment.Trim().ToUpperInvariant();
            if (normalizedEnvironment is not ("TEST" or "LIVE"))
            {
                return ProtectedActionReadResult.Invalid(new Dictionary<string, string[]>
                {
                    ["environment"] = new[] { "Environment must be TEST or LIVE." }
                });
            }

            query = query.Where(action => action.Environment == normalizedEnvironment);
        }

        var actions = await query
            .OrderBy(action => action.Environment)
            .ThenBy(action => action.Name)
            .ToListAsync(ct);

        return ProtectedActionReadResult.Found(actions);
    }

    public async Task<ProtectedActionReadResult> GetAsync(
        Guid projectId,
        Guid actionId,
        Guid developerId,
        bool isAdmin,
        CancellationToken ct)
    {
        var action = await AccessibleActions(projectId, developerId, isAdmin)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == actionId, ct);

        return action == null
            ? ProtectedActionReadResult.NotFound()
            : ProtectedActionReadResult.Found(new[] { action });
    }

    public async Task<ProtectedActionWriteResult> CreateAsync(
        Guid projectId,
        Guid developerId,
        bool isAdmin,
        UpsertProtectedActionRequest request,
        CancellationToken ct)
    {
        var project = await FindAccessibleProjectAsync(projectId, developerId, isAdmin, ct);
        if (project == null)
            return ProtectedActionWriteResult.NotFound();

        var policy = ProtectedActionPolicy.Evaluate(request);
        if (!policy.IsValid)
            return ProtectedActionWriteResult.Invalid(policy.Errors);

        var actionLimit = PlanLimits.GetProtectedActionLimit(project.Plan ?? "free", project.ProPaidUntil);
        var actionCount = await _db.ProtectedActions.CountAsync(action => action.ProjectId == projectId, ct);
        if (actionCount >= actionLimit)
            return ProtectedActionWriteResult.PlanLimit(actionLimit);

        if (await NameExistsAsync(
                projectId,
                policy.Normalized.Environment,
                policy.Normalized.Name,
                excludingActionId: null,
                ct))
        {
            return ProtectedActionWriteResult.Conflict();
        }

        var now = DateTime.UtcNow;
        var action = new ProtectedAction
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            ConfigurationVersion = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        Apply(action, policy.Normalized);
        _db.ProtectedActions.Add(action);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return ProtectedActionWriteResult.Conflict();
        }

        return ProtectedActionWriteResult.Created(action);
    }

    public async Task<ProtectedActionWriteResult> UpdateAsync(
        Guid projectId,
        Guid actionId,
        Guid developerId,
        bool isAdmin,
        UpsertProtectedActionRequest request,
        CancellationToken ct)
    {
        var action = await AccessibleActions(projectId, developerId, isAdmin)
            .FirstOrDefaultAsync(item => item.Id == actionId, ct);
        if (action == null)
            return ProtectedActionWriteResult.NotFound();

        var policy = ProtectedActionPolicy.Evaluate(request);
        if (!policy.IsValid)
            return ProtectedActionWriteResult.Invalid(policy.Errors);

        if (await NameExistsAsync(
                projectId,
                policy.Normalized.Environment,
                policy.Normalized.Name,
                actionId,
                ct))
        {
            return ProtectedActionWriteResult.Conflict();
        }

        Apply(action, policy.Normalized);
        action.ConfigurationVersion = checked(action.ConfigurationVersion + 1);
        action.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return ProtectedActionWriteResult.Conflict();
        }

        return ProtectedActionWriteResult.Updated(action);
    }

    public async Task<ProtectedActionWriteResult> DeleteAsync(
        Guid projectId,
        Guid actionId,
        Guid developerId,
        bool isAdmin,
        CancellationToken ct)
    {
        var action = await AccessibleActions(projectId, developerId, isAdmin)
            .FirstOrDefaultAsync(item => item.Id == actionId, ct);
        if (action == null)
            return ProtectedActionWriteResult.NotFound();

        if (await _db.CostShieldAuthorizations.AnyAsync(
                authorization => authorization.ProtectedActionId == actionId,
                ct))
        {
            return ProtectedActionWriteResult.InUse();
        }

        _db.ProtectedActions.Remove(action);
        await _db.SaveChangesAsync(ct);
        return ProtectedActionWriteResult.Deleted();
    }

    private IQueryable<ProtectedAction> AccessibleActions(
        Guid projectId,
        Guid developerId,
        bool isAdmin)
    {
        var query = _db.ProtectedActions.Where(action =>
            action.ProjectId == projectId &&
            !action.Project.IsDeleted);

        return isAdmin
            ? query
            : query.Where(action => action.Project.DeveloperId == developerId);
    }

    private async Task<Project?> FindAccessibleProjectAsync(
        Guid projectId,
        Guid developerId,
        bool isAdmin,
        CancellationToken ct)
    {
        return await _db.Projects.FirstOrDefaultAsync(project =>
            project.Id == projectId &&
            !project.IsDeleted &&
            (isAdmin || project.DeveloperId == developerId), ct);
    }

    private async Task<bool> CanAccessProjectAsync(
        Guid projectId,
        Guid developerId,
        bool isAdmin,
        CancellationToken ct)
    {
        return await _db.Projects.AnyAsync(project =>
            project.Id == projectId &&
            !project.IsDeleted &&
            (isAdmin || project.DeveloperId == developerId), ct);
    }

    private Task<bool> NameExistsAsync(
        Guid projectId,
        string environment,
        string name,
        Guid? excludingActionId,
        CancellationToken ct)
    {
        return _db.ProtectedActions.AnyAsync(action =>
            action.ProjectId == projectId &&
            action.Environment == environment &&
            action.Name == name &&
            (!excludingActionId.HasValue || action.Id != excludingActionId.Value), ct);
    }

    private static void Apply(ProtectedAction action, UpsertProtectedActionRequest request)
    {
        action.Environment = request.Environment;
        action.Name = request.Name;
        action.DisplayName = request.DisplayName;
        action.Description = request.Description;
        action.IsEnabled = request.IsEnabled;
        action.BaseDifficulty = request.BaseDifficulty;
        action.SuspiciousDifficulty = request.SuspiciousDifficulty;
        action.MaximumDifficulty = request.MaximumDifficulty;
        action.AnonymousRequestLimit = request.AnonymousRequestLimit;
        action.AnonymousLimitWindowSeconds = request.AnonymousLimitWindowSeconds;
        action.AuthenticatedRequestLimit = request.AuthenticatedRequestLimit;
        action.AuthenticatedLimitWindowSeconds = request.AuthenticatedLimitWindowSeconds;
        action.RequireSingleUseToken = request.RequireSingleUseToken;
        action.TokenLifetimeSeconds = request.TokenLifetimeSeconds;
        action.AllowedOrigins = request.AllowedOrigins.ToList();
        action.FailureBehavior = request.FailureBehavior;
        action.AllowLightningFallback = request.AllowLightningFallback;
        action.LightningPriceSats = request.LightningPriceSats;
        action.LightningFallbackMode = request.LightningFallbackMode;
        action.LightningBypassesProofOfWork = request.LightningBypassesProofOfWork;
        action.EstimatedCostPerExecution = request.EstimatedCostPerExecution;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}

public enum ProtectedActionResultStatus
{
    Found,
    Created,
    Updated,
    Deleted,
    NotFound,
    Invalid,
    Conflict,
    InUse,
    PlanLimitReached
}

public sealed record ProtectedActionReadResult(
    ProtectedActionResultStatus Status,
    IReadOnlyList<ProtectedAction> Actions,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static ProtectedActionReadResult Found(IReadOnlyList<ProtectedAction> actions)
        => new(ProtectedActionResultStatus.Found, actions);

    public static ProtectedActionReadResult NotFound()
        => new(ProtectedActionResultStatus.NotFound, Array.Empty<ProtectedAction>());

    public static ProtectedActionReadResult Invalid(IReadOnlyDictionary<string, string[]> errors)
        => new(ProtectedActionResultStatus.Invalid, Array.Empty<ProtectedAction>(), errors);
}

public sealed record ProtectedActionWriteResult(
    ProtectedActionResultStatus Status,
    ProtectedAction? Action = null,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    int? Limit = null)
{
    public static ProtectedActionWriteResult Created(ProtectedAction action)
        => new(ProtectedActionResultStatus.Created, action);

    public static ProtectedActionWriteResult Updated(ProtectedAction action)
        => new(ProtectedActionResultStatus.Updated, action);

    public static ProtectedActionWriteResult Deleted()
        => new(ProtectedActionResultStatus.Deleted);

    public static ProtectedActionWriteResult NotFound()
        => new(ProtectedActionResultStatus.NotFound);

    public static ProtectedActionWriteResult Invalid(IReadOnlyDictionary<string, string[]> errors)
        => new(ProtectedActionResultStatus.Invalid, Errors: errors);

    public static ProtectedActionWriteResult Conflict()
        => new(ProtectedActionResultStatus.Conflict);

    public static ProtectedActionWriteResult InUse()
        => new(ProtectedActionResultStatus.InUse);

    public static ProtectedActionWriteResult PlanLimit(int limit)
        => new(ProtectedActionResultStatus.PlanLimitReached, Limit: limit);
}
