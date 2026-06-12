namespace LiveAuthCore.Models.Mcp;

public record McpChargeResponse(
    string Status,
    long CallsUsed,
    long SatsUsed,
    int? GrossSats = null,
    int? PlatformFeeSats = null,
    int? NetSats = null,
    int? FeeBasisPoints = null,
    Guid? RevenueEventId = null,
    string? Reason = null,
    McpSignedReceipt? Receipt = null,
    Guid? ToolId = null,
    string? ToolName = null,
    string? ToolSlug = null
);
