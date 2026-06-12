namespace LiveAuthCore.Models.Mcp;

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

public record McpChargeRequest
{
    /// <summary>
    /// Optional cost in sats for this MCP call. When omitted, LiveAuth resolves the
    /// price from a registered tool or the project's global MCP price.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "CallCostSats must be positive")]
    public int? CallCostSats { get; init; }

    /// <summary>
    /// Optional registered MCP tool ID to charge through the generic charge endpoint.
    /// </summary>
    public Guid? ToolId { get; init; }

    /// <summary>
    /// Optional registered MCP tool slug or name to charge through the generic endpoint.
    /// Slugs are preferred because they are globally unique.
    /// </summary>
    [MaxLength(200)]
    public string? ToolName { get; init; }

    [MaxLength(200)]
    public string? ToolMethodName { get; init; }

    [MaxLength(200)]
    public string? IdempotencyKey { get; init; }

    public string? AgentId { get; init; }

    public JsonElement? Metadata { get; init; }
}
