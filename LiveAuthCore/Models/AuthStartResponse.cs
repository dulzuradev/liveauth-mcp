namespace LiveAuthCore.Models;

public class AuthStartResponse
{
    public Guid SessionId { get; set; }
    public string Invoice { get; set; } = string.Empty;
    public string PaymentHash { get; set; } = string.Empty;
    public long AmountSats { get; set; }
    public long BaseAmountSats { get; set; }
    public int InvoiceFeeBasisPoints { get; set; }
    public long InvoiceFeeMinimumSats { get; set; }
    public long InvoiceFeeSats { get; set; }
    public long TotalChargedSats { get; set; }
    public long CreditAmountSats { get; set; }
    public long ExpiresAtUnix { get; set; }
}
