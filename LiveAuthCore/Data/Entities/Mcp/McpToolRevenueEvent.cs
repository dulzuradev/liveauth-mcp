using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Data.Entities.Mcp;

public class McpToolRevenueEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid McpToolId { get; set; }

    public Guid? McpGateTokenId { get; set; }

    public Guid? McpGateSessionId { get; set; }

    public Guid? PayingProjectId { get; set; }

    public string? AgentId { get; set; }

    [MaxLength(200)]
    public string ToolMethodName { get; set; } = string.Empty;

    public int GrossSats { get; set; }

    public int PlatformFeeSats { get; set; }

    public int NetSats { get; set; }

    public int FeeBasisPoints { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "Charged";

    [MaxLength(200)]
    public string? IdempotencyKey { get; set; }

    public string? RequestId { get; set; }

    public string? MetadataJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid? ReversalOfEventId { get; set; }
}
