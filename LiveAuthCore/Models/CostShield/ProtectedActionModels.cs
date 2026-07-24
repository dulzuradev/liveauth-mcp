namespace LiveAuthCore.Models.CostShield;

public sealed class UpsertProtectedActionRequest
{
    public string Environment { get; set; } = "TEST";
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int BaseDifficulty { get; set; } = 17;
    public int SuspiciousDifficulty { get; set; } = 20;
    public int MaximumDifficulty { get; set; } = 24;
    public int AnonymousRequestLimit { get; set; } = 5;
    public int AnonymousLimitWindowSeconds { get; set; } = 3600;
    public int? AuthenticatedRequestLimit { get; set; }
    public int? AuthenticatedLimitWindowSeconds { get; set; }
    public bool RequireSingleUseToken { get; set; } = true;
    public int TokenLifetimeSeconds { get; set; } = 120;
    public List<string> AllowedOrigins { get; set; } = new();
    public string FailureBehavior { get; set; } = "Deny";
    public bool AllowLightningFallback { get; set; }
    public int LightningPriceSats { get; set; } = 25;
    public string LightningFallbackMode { get; set; } = "RateLimitOnly";
    public bool LightningBypassesProofOfWork { get; set; } = true;
    public decimal EstimatedCostPerExecution { get; set; }
}

public sealed record ProtectedActionDto(
    Guid Id,
    Guid ProjectId,
    string Environment,
    string Name,
    string DisplayName,
    string Description,
    bool IsEnabled,
    int BaseDifficulty,
    int SuspiciousDifficulty,
    int MaximumDifficulty,
    int AnonymousRequestLimit,
    int AnonymousLimitWindowSeconds,
    int? AuthenticatedRequestLimit,
    int? AuthenticatedLimitWindowSeconds,
    bool RequireSingleUseToken,
    int TokenLifetimeSeconds,
    IReadOnlyList<string> AllowedOrigins,
    string FailureBehavior,
    bool AllowLightningFallback,
    int LightningPriceSats,
    string LightningFallbackMode,
    bool LightningBypassesProofOfWork,
    decimal EstimatedCostPerExecution,
    int ConfigurationVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ProtectedActionListResponse(
    IReadOnlyList<ProtectedActionDto> Actions);
