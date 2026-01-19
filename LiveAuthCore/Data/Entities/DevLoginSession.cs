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
    public DateTime ExpiresAt { get; set; }

    public bool IsPaid { get; set; } = false;
    public DateTime? PaidAt { get; set; }

    // LNURL-auth / Lightning identity of whoever paid
    public string? PayerLightningAuthKey { get; set; }
}
