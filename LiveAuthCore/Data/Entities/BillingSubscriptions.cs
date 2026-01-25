namespace LiveAuthCore.Data.Entities;

public class BillingSubscription
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Plan { get; set; } = "pro";

    public long AmountSats { get; set; }

    public string InvoiceBolt11 { get; set; } = null!;
    public string InvoiceRHash { get; set; } = null!;

    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

}