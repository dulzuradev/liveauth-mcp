namespace LiveAuthCore.Models.Mcp;

public record BillingUsageResponse(
    long L402BalanceSats,
    long CallsUsedToday,
    long SatsUsedToday,
    long FreeDailyLimitSats,
    long FreeDailyLimitCalls
);