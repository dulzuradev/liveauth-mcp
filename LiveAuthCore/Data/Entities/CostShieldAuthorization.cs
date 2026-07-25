using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LiveAuthCore.Data.Entities;

public sealed class CostShieldAuthorization
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    [ForeignKey(nameof(ProjectId))]
    public Project Project { get; set; } = null!;

    public Guid ProtectedActionId { get; set; }

    [ForeignKey(nameof(ProtectedActionId))]
    public ProtectedAction ProtectedAction { get; set; } = null!;

    [MaxLength(64)]
    public string ChallengeId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string TokenId { get; set; } = string.Empty;

    [MaxLength(16)]
    public string Environment { get; set; } = "TEST";

    [MaxLength(32)]
    public string VerificationMethod { get; set; } = "pow";

    public int Difficulty { get; set; }

    [MaxLength(512)]
    public string? Origin { get; set; }

    [MaxLength(64)]
    public string ClientContextHash { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? SubjectHash { get; set; }

    public bool RequireSingleUse { get; set; } = true;

    public int ConfigurationVersion { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = CostShieldAuthorizationStatuses.Active;

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    [MaxLength(64)]
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}

public static class CostShieldAuthorizationStatuses
{
    public const string Active = "Active";
    public const string Consumed = "Consumed";
    public const string Revoked = "Revoked";
}
