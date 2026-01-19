namespace LiveAuthCore.Models;

public sealed class AdminSubscriptionDto
{
    public Guid SubscriptionId { get; set; }
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = "";

    public string Plan { get; set; } = "";
    public bool IsPaid { get; set; }
    public long AmountSats { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}