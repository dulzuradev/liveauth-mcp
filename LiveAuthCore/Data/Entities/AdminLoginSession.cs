using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

public sealed class AdminLoginSession
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string Email { get; set; } = string.Empty;

    public long AmountSats { get; set; }

    public string InvoiceBolt11 { get; set; } = string.Empty;

    // Base64 rHash / payment hash string returned by LightningService.CreateInvoice
    public string InvoiceRHash { get; set; } = string.Empty;

    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    // Optional: for debugging / linking payer identity if you already capture it
    public string? PayerLightningAuthKey { get; set; }
}
