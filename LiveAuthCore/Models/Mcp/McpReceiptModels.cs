namespace LiveAuthCore.Models.Mcp;

public record McpSignedReceipt(
    string Version,
    string Payload,
    string Signature,
    string SignatureAlgorithm,
    string KeyId,
    McpCallReceipt Body
);

public record McpCallReceipt(
    string ReceiptId,
    Guid RevenueEventId,
    Guid McpToolId,
    string ToolName,
    string ToolSlug,
    string ToolMethodName,
    Guid? McpGateTokenId,
    Guid? McpGateSessionId,
    Guid? PayingProjectId,
    string? AgentId,
    int GrossSats,
    int PlatformFeeSats,
    int NetSats,
    int FeeBasisPoints,
    string Status,
    string? IdempotencyKey,
    string? RequestId,
    DateTime CreatedAt
);
