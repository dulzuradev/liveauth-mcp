namespace LiveAuthCore.Models.Mcp;

public record McpToolListResponse(
    IReadOnlyList<McpToolDto> Tools
);

public record McpToolDto(
    Guid Id,
    Guid? DeveloperId,
    Guid? ProjectId,
    string Name,
    string Slug,
    string Description,
    string? Category,
    string Status,
    string Visibility,
    int DefaultCostSats,
    int MinCostSats,
    int MaxCostSats,
    string? WebsiteUrl,
    string? DocsUrl,
    string? WebhookUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CreateMcpToolRequest(
    Guid? ProjectId,
    string Name,
    string? Slug,
    string? Description,
    string? Category,
    string? Visibility,
    string? Status,
    int DefaultCostSats,
    int MinCostSats,
    int MaxCostSats,
    string? WebsiteUrl,
    string? DocsUrl,
    string? WebhookUrl
);

public record UpdateMcpToolRequest(
    Guid? ProjectId,
    bool? ClearProject,
    string? Name,
    string? Slug,
    string? Description,
    string? Category,
    string? Visibility,
    string? Status,
    int? DefaultCostSats,
    int? MinCostSats,
    int? MaxCostSats,
    string? WebsiteUrl,
    string? DocsUrl,
    string? WebhookUrl
);

public record McpToolRevenueSummaryResponse(
    Guid ToolId,
    string ToolName,
    string ToolStatus,
    int WindowHours,
    long Calls,
    long GrossSats,
    long PlatformFeeSats,
    long NetSats,
    double AverageGrossSatsPerCall
);

public record McpToolRevenueEventsResponse(
    Guid ToolId,
    int Limit,
    IReadOnlyList<McpToolRevenueEventDto> Events
);

public record McpToolRevenueEventDto(
    Guid Id,
    Guid McpToolId,
    Guid? McpGateTokenId,
    Guid? McpGateSessionId,
    Guid? PayingProjectId,
    string? AgentId,
    string ToolMethodName,
    int GrossSats,
    int PlatformFeeSats,
    int NetSats,
    int FeeBasisPoints,
    string Status,
    string? IdempotencyKey,
    string? RequestId,
    string? MetadataJson,
    DateTime CreatedAt,
    Guid? ReversalOfEventId
);
