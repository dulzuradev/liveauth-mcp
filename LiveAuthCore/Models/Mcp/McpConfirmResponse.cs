namespace LiveAuthCore.Models.Mcp;

public record McpConfirmResponse(
    string? Jwt,
    int ExpiresIn,
    long RemainingBudgetSats,
    string? PaymentStatus = null,
    string? RefreshToken = null
);
