using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities;

/// <summary>
/// Durable idempotency/audit record for Bitcoin broadcasts. Raw transaction hex is
/// intentionally never persisted; RequestHash commits to the caller input instead.
/// </summary>
public sealed class BitcoinGatewayOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? McpGateTokenId { get; set; }
    [MaxLength(80)] public string Operation { get; set; } = string.Empty;
    [MaxLength(200)] public string IdempotencyKey { get; set; } = string.Empty;
    [MaxLength(64)] public string RequestHash { get; set; } = string.Empty;
    [MaxLength(128)] public string RequestId { get; set; } = string.Empty;
    [MaxLength(64)] public string? Txid { get; set; }
    [MaxLength(32)] public string Status { get; set; } = "Processing";
    [MaxLength(128)] public string? ErrorCode { get; set; }
    public string? ResultJson { get; set; }
    public Guid? RevenueEventId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
