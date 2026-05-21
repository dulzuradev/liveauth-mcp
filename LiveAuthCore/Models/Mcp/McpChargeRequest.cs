namespace LiveAuthCore.Models.Mcp;

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

public record McpChargeRequest
{
    /// <summary>
    /// Cost in sats for this MCP call.
    /// </summary>
    [Required(ErrorMessage = "CallCostSats is required")]
    [Range(1, int.MaxValue, ErrorMessage = "CallCostSats must be positive")]
    public int CallCostSats { get; init; }

    [MaxLength(200)]
    public string? ToolMethodName { get; init; }

    [MaxLength(200)]
    public string? IdempotencyKey { get; init; }

    public string? AgentId { get; init; }

    public JsonElement? Metadata { get; init; }
}
