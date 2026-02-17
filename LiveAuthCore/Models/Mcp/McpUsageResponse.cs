namespace LiveAuthCore.Models.Mcp;

public record McpUsageResponse(
    string Status,
    long CallsUsed,
    long SatsUsed,
    long MaxSatsPerDay,
    long RemainingBudgetSats,
    long MaxCallsPerMinute,
    DateTime ExpiresAt,
    DateTime? DayWindowStart
);
