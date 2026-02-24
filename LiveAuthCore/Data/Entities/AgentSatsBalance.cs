using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LiveAuthCore.Data.Entities;

[Table("AgentSatsBalances")]
public class AgentSatsBalance
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string AgentId { get; set; } = string.Empty;

    public long Balance { get; set; }

    public long TotalEarned { get; set; }

    public long TotalSpent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

[Table("SatsInvoices")]
public class SatsInvoice
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string AgentId { get; set; } = string.Empty;

    public long AmountSats { get; set; }

    [MaxLength(2000)]
    public string PaymentRequest { get; set; } = string.Empty;

    [MaxLength(100)]
    public string PaymentHash { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Status { get; set; } = "pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? PaidAt { get; set; }
}
