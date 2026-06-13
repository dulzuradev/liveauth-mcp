namespace LiveAuthCore.Data.Entities;

using System;

public class DevLoginSession
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    // Lightning invoice fields
    public string InvoiceId { get; set; } = string.Empty;
    public string InvoiceBolt11 { get; set; } = string.Empty;
    public long AmountSats { get; set; }
    public long BaseAmountSats { get; set; }
    public int InvoiceFeeBasisPoints { get; set; }
    public long InvoiceFeeMinimumSats { get; set; }
    public long InvoiceFeeSats { get; set; }
    public long TotalChargedSats { get; set; }
    public long CreditAmountSats { get; set; }
    public DateTime ExpiresAt { get; set; }

    public bool IsPaid { get; set; } = false;
    public DateTime? PaidAt { get; set; }

    // LNURL-auth / Lightning identity of whoever paid
    public string? PayerLightningAuthKey { get; set; }
}
