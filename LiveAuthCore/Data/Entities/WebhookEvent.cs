namespace LiveAuthCore.Data.Entities;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public enum WebhookEventStatus
{
    Pending   = 0,
    Delivering = 1,
    Delivered = 2,
    Dead      = 3
}

public class WebhookEvent
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid ProjectId { get; set; }

    [ForeignKey(nameof(ProjectId))]
    public Project Project { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string EventType { get; set; } = default!;

    [Required]
    public string PayloadJson { get; set; } = default!;

    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }

    public int AttemptCount { get; set; }

    public WebhookEventStatus Status { get; set; }

    public DateTime? LastAttemptAt { get; set; }
    public int? LastStatusCode { get; set; }

    public string? LastError { get; set; }
}
