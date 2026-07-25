namespace LiveAuthCore.Models.CostShield;

public sealed record CostShieldOverviewResponse(
    int WindowHours,
    DateTime WindowStart,
    DateTime WindowEnd,
    int ProtectedActionCount,
    int EnabledActionCount,
    int ChallengesIssued,
    int ChallengesCompleted,
    int AuthorizationsIssued,
    int ProtectedRequests,
    int RequestsDenied,
    int RateLimitedRequests,
    int InvalidAttempts,
    int ReplayAttemptsBlocked,
    decimal EstimatedProviderCostAuthorized,
    decimal EstimatedCostAvoided,
    double ChallengeSuccessRate,
    double? AverageChallengeTimeMilliseconds,
    bool EstimatedValues,
    IReadOnlyList<CostShieldActionUsageDto> TopActions);

public sealed record CostShieldActionUsageDto(
    Guid ProtectedActionId,
    string Action,
    string DisplayName,
    int ChallengesIssued,
    int AuthorizationsIssued,
    int ProtectedRequests,
    int RequestsDenied,
    decimal EstimatedCostAvoided);

public sealed record CostShieldEventListResponse(
    int Total,
    int Limit,
    int Offset,
    IReadOnlyList<CostShieldEventDto> Events);

public sealed record CostShieldEventDto(
    Guid Id,
    Guid? ProtectedActionId,
    string? Action,
    string? DisplayName,
    string EventType,
    string? Environment,
    string? VerificationMethod,
    bool Success,
    string? Reason,
    string? Source,
    int? DurationMilliseconds,
    decimal? EstimatedCostProtected,
    DateTime CreatedAt);
