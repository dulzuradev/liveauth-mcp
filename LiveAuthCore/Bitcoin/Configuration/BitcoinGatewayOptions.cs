namespace LiveAuthCore.Bitcoin.Configuration;

public sealed class BitcoinGatewayOptions
{
    public const string SectionName = "BitcoinGateway";

    public bool Enabled { get; set; }
    public string RpcUrl { get; set; } = "http://127.0.0.1:8332";
    public string? RpcUser { get; set; }
    public string? RpcPassword { get; set; }
    public string? RpcCookieFile { get; set; }
    public string Network { get; set; } = "mainnet";
    public int MaxRawTransactionBytes { get; set; } = 400_000;
    public decimal MaxFeeRateSatPerVbyte { get; set; } = 1_000m;
    public long MaxAbsoluteFeeSats { get; set; } = 10_000_000;
    public int RpcTimeoutMs { get; set; } = 10_000;
    public int CircuitBreakerThresholdMs { get; set; } = 5_000;
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    public int CircuitBreakerBreakSeconds { get; set; } = 30;
    public int FeeEstimateCacheSeconds { get; set; } = 30;
    public int MempoolSummaryCacheSeconds { get; set; } = 15;
    public int StaleCacheSeconds { get; set; } = 300;
    public int ReadRateLimitPerMinute { get; set; } = 60;
    public int BroadcastRateLimitPerMinute { get; set; } = 5;
    public int IdempotencyLeaseSeconds { get; set; } = 30;
    public int OperationRetentionDays { get; set; } = 90;
    public int CleanupIntervalHours { get; set; } = 24;
    public BitcoinGatewayToolOptions Tools { get; set; } = new();
}

public sealed class BitcoinGatewayToolOptions
{
    public BitcoinToolPriceOptions FeeEstimates { get; set; } = new() { PriceSats = 3 };
    public BitcoinToolPriceOptions MempoolSummary { get; set; } = new() { PriceSats = 3 };
    public BitcoinToolPriceOptions PreflightTransaction { get; set; } = new() { PriceSats = 5 };
    public BitcoinToolPriceOptions BroadcastTransaction { get; set; } = new() { PriceSats = 25 };
    public BitcoinToolPriceOptions TransactionStatus { get; set; } = new() { PriceSats = 3 };
}

public sealed class BitcoinToolPriceOptions
{
    public int PriceSats { get; set; } = 1;
}

public static class BitcoinGatewayTools
{
    public const string FeeEstimates = "bitcoin_get_fee_estimates";
    public const string MempoolSummary = "bitcoin_get_mempool_summary";
    public const string PreflightTransaction = "bitcoin_preflight_transaction";
    public const string BroadcastTransaction = "bitcoin_broadcast_transaction";
    public const string TransactionStatus = "bitcoin_get_transaction_status";
}
