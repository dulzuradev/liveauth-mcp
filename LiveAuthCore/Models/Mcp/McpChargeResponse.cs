namespace LiveAuthCore.Models.Mcp;

public record McpChargeResponse(
    string Status,
    long CallsUsed,
    long SatsUsed
);
