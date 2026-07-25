using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LiveAuthCore.Data.Entities;

public sealed class ProtectedAction
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    [ForeignKey(nameof(ProjectId))]
    public Project Project { get; set; } = null!;

    [MaxLength(8)]
    public string Environment { get; set; } = "TEST";

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(500)]
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

    [MaxLength(32)]
    public string FailureBehavior { get; set; } = ProtectedActionFailureBehaviors.Deny;

    public bool AllowLightningFallback { get; set; }

    public int LightningPriceSats { get; set; } = 25;

    [MaxLength(32)]
    public string LightningFallbackMode { get; set; } = ProtectedActionLightningModes.RateLimitOnly;

    public bool LightningBypassesProofOfWork { get; set; } = true;

    [Column(TypeName = "decimal(18,6)")]
    public decimal EstimatedCostPerExecution { get; set; }

    public int ConfigurationVersion { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CostShieldAuthorization> Authorizations { get; set; } =
        new List<CostShieldAuthorization>();
}

public static class ProtectedActionFailureBehaviors
{
    public const string Deny = "Deny";
    public const string LightningFallback = "LightningFallback";
}

public static class ProtectedActionLightningModes
{
    public const string RateLimitOnly = "RateLimitOnly";
    public const string Always = "Always";
}
