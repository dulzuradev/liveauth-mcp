namespace LiveAuthCore.Models.Mcp;

public record McpStartResponse(
    string QuoteId,
    object? PowChallenge,
    McpInvoice? Invoice,
    string? AuthHint  // Set to "l402_bundle" when ForceL402=true to hint macaroon auth
);

public record McpInvoice(
    string? Bolt11,
    long AmountSats,
    long ExpiresAtUnix,
    string? PaymentHash
);
