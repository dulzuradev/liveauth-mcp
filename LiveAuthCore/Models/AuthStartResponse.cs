namespace LiveAuthCore.Models;

public class AuthStartResponse
{
    public Guid SessionId { get; set; }
    public string Invoice { get; set; } = string.Empty;
    public string PaymentHash { get; set; } = string.Empty;
    public long AmountSats { get; set; }
    public long ExpiresAtUnix { get; set; }
}