using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

/// <summary>
/// Tracks a Lightning invoice purchase for L402 balance top-up.
/// </summary>
public class L402Purchase
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Project whose L402BalanceSats will be credited on payment.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Developer who initiated this purchase.
    /// </summary>
    public Guid DeveloperId { get; set; }

    /// <summary>
    /// Amount added to L402BalanceSats on settlement (in sats).
    /// </summary>
    public long AmountSats { get; set; }

    public long BaseAmountSats { get; set; }
    public int InvoiceFeeBasisPoints { get; set; }
    public long InvoiceFeeMinimumSats { get; set; }
    public long InvoiceFeeSats { get; set; }
    public long TotalChargedSats { get; set; }
    public long CreditAmountSats { get; set; }

    /// <summary>
    /// LND r_hash (hex) — used to look up invoice status.
    /// </summary>
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>
    /// Bolt11 invoice string shown to the user.
    /// </summary>
    public string Bolt11 { get; set; } = string.Empty;

    /// <summary>
    /// Unix timestamp when the invoice expires.
    /// </summary>
    public long ExpiresAtUnix { get; set; }

    /// <summary>
    /// pending → settling (paid, crediting) → settled → expired
    /// </summary>
    public string Status { get; set; } = "pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SettledAt { get; set; }

    // Navigation
    public Project? Project { get; set; }
}
