namespace LiveAuthCore.Data.Entities;

public enum VerificationStatus
{
    Pending = 0,
    Paid = 1,
    Expired = 2
}

public class VerificationSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string UserRef { get; set; } = string.Empty;

    public long AmountSats { get; set; }
    public string PaymentHashB64 { get; set; } = string.Empty;
    public string Invoice { get; set; } = string.Empty;

    public VerificationStatus Status { get; set; } = VerificationStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
}