using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

public class MintRequest
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string MintUrl { get; set; } = string.Empty;

    public long Amount { get; set; }

    public string? PaymentHash { get; set; }

    public string? Invoice { get; set; }

    public MintRequestStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public enum MintRequestStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
