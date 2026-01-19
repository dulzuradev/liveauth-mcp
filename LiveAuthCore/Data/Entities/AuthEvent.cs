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
    PowSolved = 9
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
}

public interface IAuthEventLogger
{
    Task LogAsync(AuthEvent evt);
}
