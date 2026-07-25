using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LiveAuthCore.Data.Entities;

public enum AuthEventType
{
    LoginRequested = 0,
    LoginSucceeded = 1,
    LoginFailed = 2,
    CaptchaRequested = 3,
    CaptchaPassed = 4,
    CaptchaFailed = 5,
    RateLimitHit = 6,
    PowChallengeIssued = 7,
    PowFailed = 8,
    PowSolved = 9,
    CostShieldChallengeIssued = 10,
    CostShieldChallengeCompleted = 11,
    CostShieldChallengeFailed = 12,
    CostShieldAuthorizationIssued = 13,
    CostShieldAuthorizationVerified = 14,
    CostShieldAuthorizationConsumed = 15,
    CostShieldReplayBlocked = 16,
    CostShieldRateLimited = 17,
    CostShieldInvalidOrigin = 18
}

public class AuthEvent
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid ProjectId { get; set; }

    [ForeignKey(nameof(ProjectId))]
    public Project Project { get; set; } = null!;

    public Guid? ApiKeyId { get; set; }

    [ForeignKey(nameof(ApiKeyId))]
    public ProjectApiKey? ApiKey { get; set; }

    [Required]
    public AuthEventType EventType { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    [MaxLength(64)]
    public string? ClientIp { get; set; }

    public bool Success { get; set; }

    public long? SatsPaid { get; set; }

    [MaxLength(256)]
    public string? Reason { get; set; }

    public Guid? ProtectedActionId { get; set; }

    [ForeignKey(nameof(ProtectedActionId))]
    public ProtectedAction? ProtectedAction { get; set; }

    [MaxLength(16)]
    public string? Environment { get; set; }

    [MaxLength(64)]
    public string? IpAddressHash { get; set; }

    [MaxLength(64)]
    public string? ClientContextHash { get; set; }

    [MaxLength(64)]
    public string? SubjectHash { get; set; }

    [MaxLength(32)]
    public string? VerificationMethod { get; set; }

    public int? DurationMilliseconds { get; set; }

    public decimal? EstimatedCostProtected { get; set; }

    public string? MetadataJson { get; set; }
}

public interface IAuthEventLogger
{
    Task LogAsync(AuthEvent evt);
}
