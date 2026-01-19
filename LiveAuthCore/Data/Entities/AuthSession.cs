namespace LiveAuthCore.Data.Entities;

public class AuthSession
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string? Environment { get; set; }  // "TEST" | "LIVE"
    public string? UserHint { get; set; }

    public long AmountSats { get; set; }

    public string? InvoiceRHash { get; set; }    // base64 r_hash from LND
    public string? InvoiceBolt11 { get; set; }   // payment_request

    public DateTime ExpiresAt { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }

    public string? PayerLightningAuthKey { get; set; }
    
    public string? ClientIp { get; set; } 

    public DateTime CreatedAt { get; set; }
    
    public enum AuthIntent
    {
        Standard,   // real customers
        DemoPow,
        DemoLightning
    }

    
}
