namespace LiveAuthCore.Bitcoin.Rpc;

public interface IBitcoinNodeClient
{
    Task<BitcoinNodeFeeEstimate> EstimateSmartFeeAsync(int targetBlocks, CancellationToken ct);
    Task<BitcoinNodeMempoolInfo> GetMempoolInfoAsync(CancellationToken ct);
    Task<BitcoinNodePreflightResult> TestMempoolAcceptAsync(string rawTransaction, CancellationToken ct);
    Task<string> SendRawTransactionAsync(string rawTransaction, CancellationToken ct);
    Task<BitcoinNodeMempoolEntry?> GetMempoolEntryAsync(string txid, CancellationToken ct);
    Task<BitcoinNodeRawTransaction?> GetRawTransactionAsync(string txid, CancellationToken ct);
    Task<BitcoinNodeBlockHeader?> GetBlockHeaderAsync(string blockHash, CancellationToken ct);
}

public sealed record BitcoinNodeFeeEstimate(decimal? FeeRateBtcPerKvB, int? Blocks, IReadOnlyList<string> Errors);

public sealed record BitcoinNodeMempoolInfo(
    long Size,
    long? Vsize,
    long Bytes,
    long Usage,
    decimal? TotalFeeBtc,
    decimal MempoolMinFeeBtcPerKvB,
    decimal? IncrementalRelayFeeBtcPerKvB);

public sealed record BitcoinNodePreflightResult(
    bool Allowed,
    string? Txid,
    string? Wtxid,
    int? Vsize,
    decimal? BaseFeeBtc,
    decimal? EffectiveFeeRateBtcPerKvB,
    string? RejectReason,
    string? PackageError);

public sealed record BitcoinNodeMempoolEntry(
    int? Vsize,
    decimal? BaseFeeBtc,
    decimal? EffectiveFeeRateBtcPerKvB,
    int? AncestorCount,
    int? DescendantCount);

public sealed record BitcoinNodeRawTransaction(string Txid, string? BlockHash, int? Confirmations);
public sealed record BitcoinNodeBlockHeader(string Hash, int? Height, int? Confirmations);
