using System.Text.Json.Serialization;
using LiveAuthCore.Models.Mcp;

namespace LiveAuthCore.Bitcoin.Models;

public sealed record BitcoinFeeEstimate(int TargetBlocks, decimal? SatPerVbyte, string? UnavailableReason = null);

public sealed record BitcoinFeeEstimatesResponse(
    IReadOnlyList<BitcoinFeeEstimate> Estimates,
    DateTime ObservedAt,
    string Source,
    bool Cached,
    bool Stale = false)
{
    public McpSignedReceipt? Receipt { get; init; }
    [JsonIgnore] public long NodeLatencyMilliseconds { get; init; }
}

public sealed record BitcoinMempoolSummary(
    long TransactionCount,
    long VirtualSize,
    long MemoryUsageBytes,
    long? TotalFeesSats,
    decimal MempoolMinFeeSatVb,
    decimal? IncrementalRelayFeeSatVb,
    DateTime ObservedAt,
    string Source,
    bool Cached,
    bool Stale = false)
{
    public McpSignedReceipt? Receipt { get; init; }
    [JsonIgnore] public long NodeLatencyMilliseconds { get; init; }
}

public sealed class BitcoinRawTransactionRequest
{
    [JsonPropertyName("rawTransaction")]
    public string RawTransaction { get; set; } = string.Empty;
}

public sealed record BitcoinTransactionFees(long? BaseSats, decimal? EffectiveSatPerVbyte);

public sealed record BitcoinPreflightResult(
    bool Accepted,
    string? Txid,
    string? Wtxid,
    int? Vsize,
    BitcoinTransactionFees? Fees,
    string? RejectCode,
    string? RejectReason,
    DateTime ObservedAt,
    string Source)
{
    public McpSignedReceipt? Receipt { get; init; }
    [JsonIgnore] public long NodeLatencyMilliseconds { get; init; }
}

public sealed record BitcoinBroadcastResult(
    bool Accepted,
    bool Broadcasted,
    bool AlreadyKnown,
    bool Recovered,
    string Txid,
    string? Wtxid,
    int? Vsize,
    BitcoinTransactionFees? Fees,
    DateTime? BroadcastAt,
    DateTime ObservedAt,
    string Source,
    string? RejectCode = null,
    string? RejectReason = null)
{
    public McpSignedReceipt? Receipt { get; init; }
    [JsonIgnore] public long NodeLatencyMilliseconds { get; init; }
}

public sealed record BitcoinMempoolTransactionDetails(
    long? FeeSats,
    int? Vsize,
    decimal? EffectiveSatPerVbyte,
    int? AncestorCount,
    int? DescendantCount);

public sealed record BitcoinTransactionStatus(
    string Txid,
    string Status,
    int Confirmations,
    int? BlockHeight,
    string? BlockHash,
    BitcoinMempoolTransactionDetails? Mempool,
    DateTime ObservedAt,
    string Source)
{
    public McpSignedReceipt? Receipt { get; init; }
    [JsonIgnore] public long NodeLatencyMilliseconds { get; init; }
}

public sealed record BitcoinErrorEnvelope(BitcoinError Error);

public sealed record BitcoinError(
    string Code,
    string Message,
    bool Retryable,
    string? RequestId = null,
    int? RetryAfterSeconds = null);

public sealed record BitcoinPaidResult<T>(
    T Value,
    int PriceSats,
    Guid? RevenueEventId,
    bool Duplicate);
