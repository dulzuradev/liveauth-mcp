namespace LiveAuthCore.Models;

public class CreateSubscriptionInvoiceResponse
{
    public Guid SessionId { get; set; }
    public string Invoice { get; set; } = null!;
    public long AmountSats { get; set; }
    public long ExpiresAtUnix { get; set; }
}

