using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

/// <summary>
/// Admin user session with username/password authentication
/// </summary>
public sealed class AdminSession
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    public string PasswordSalt { get; set; } = string.Empty;

    public bool IsOwner { get; set; }

    public string? Token { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Lightning payment session for admin access
/// </summary>
public sealed class AdminPaymentSession
{
    [Key]
    public Guid Id { get; set; }

    public long AmountSats { get; set; }

    public string InvoiceBolt11 { get; set; } = string.Empty;

    public string InvoiceRHash { get; set; } = string.Empty;

    public bool IsPaid { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }
}
