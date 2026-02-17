namespace LiveAuthCore.Models.Mcp;

public record McpStartResponse(
    string QuoteId,
    object? PowChallenge,
    McpInvoice? Invoice
);

public record McpInvoice(
    string? Bolt11,
    long AmountSats,
    long ExpiresAtUnix,
    string? PaymentHash
);
