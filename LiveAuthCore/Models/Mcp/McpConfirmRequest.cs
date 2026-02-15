namespace LiveAuthCore.Models.Mcp;

public record McpConfirmRequest(
    string QuoteId,
    // For PoW flow
    string? ChallengeHex = null,
    long? Nonce = null,
    string? HashHex = null,
    int? DifficultyBits = null,
    long? ExpiresAtUnix = null,
    string? Sig = null,
    // For Lightning flow
    string? PaymentHash = null
);
