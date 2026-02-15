namespace LiveAuthCore.Models.Mcp;

public record McpStartResponse(
    string QuoteId,
    object? PowChallenge,
    string? Invoice
);
