namespace LiveAuthCore.Models.Mcp;

public record McpRefreshRequest(string RefreshToken);

public record McpRefreshResponse(
    string Jwt,
    int ExpiresIn,
    long RemainingBudgetSats
);
