using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities.Mcp;

public class McpGateSession
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    // One of these is used depending on auth path
    public string? PowChallengeHex { get; set; }
    public int? PowDifficultyBits { get; set; }
    public long? PowExpiresAtUnix { get; set; }
    public string? PowSignature { get; set; }

    public string? LightningInvoice { get; set; }
    public string? LightningPaymentHash { get; set; }

    // Budgeting / charging
    public int SatsPerCallAtStart { get; set; }

    public string Status { get; set; } = "pending"; // pending|confirmed|expired|canceled

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(10);
}
