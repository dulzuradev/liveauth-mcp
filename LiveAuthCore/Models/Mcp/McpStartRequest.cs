namespace LiveAuthCore.Models.Mcp;

public record McpStartRequest(
    bool? ForceLightning = null,
    bool? ForceL402 = null
);
