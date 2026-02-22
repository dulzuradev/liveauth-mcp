namespace LiveAuthCore.Models.Mcp;

using System.ComponentModel.DataAnnotations;

public record McpChargeRequest
{
    /// <summary>
    /// Cost in sats for this MCP call.
    /// </summary>
    [Required(ErrorMessage = "CallCostSats is required")]
    [Range(1, 1000, ErrorMessage = "CallCostSats must be between 1 and 1000")]
    public int CallCostSats { get; init; }
}
