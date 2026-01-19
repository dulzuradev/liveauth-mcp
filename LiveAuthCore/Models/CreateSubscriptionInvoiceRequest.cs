namespace LiveAuthCore.Models;

public class CreateSubscriptionInvoiceRequest
{
    public Guid ProjectId { get; set; }
    public string Plan { get; set; } = "pro";
}

