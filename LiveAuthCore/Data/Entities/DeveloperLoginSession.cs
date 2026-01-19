namespace LiveAuthCore.Data.Entities;

public enum DevLoginStatus
{
    Pending = 0,
    Paid = 1,
    Expired = 2
}

public class DeveloperLoginSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? DeveloperId { get; set; }
    public Developer? Developer { get; set; }

    public string DeveloperEmail { get; set; } = string.Empty;

    public long AmountSats { get; set; } = 1;
    public string PaymentHashB64 { get; set; } = string.Empty;
    public string Invoice { get; set; } = string.Empty;

    public DevLoginStatus Status { get; set; } = DevLoginStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(10);
    public DateTime? PaidAt { get; set; }
}