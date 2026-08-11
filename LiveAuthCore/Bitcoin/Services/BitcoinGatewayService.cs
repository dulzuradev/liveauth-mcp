using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using LiveAuthCore.Bitcoin.Configuration;
using LiveAuthCore.Bitcoin.Models;
using LiveAuthCore.Bitcoin.Rpc;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace LiveAuthCore.Bitcoin.Services;

public sealed record BitcoinTransactionIdentity(string Txid, string Wtxid, int RawBytes);

public interface IBitcoinGatewayService
{
    BitcoinTransactionIdentity ValidateRawTransaction(string rawTransaction);
    Task<BitcoinFeeEstimatesResponse> GetFeeEstimatesAsync(CancellationToken ct);
    Task<BitcoinMempoolSummary> GetMempoolSummaryAsync(CancellationToken ct);
    Task<BitcoinPreflightResult> PreflightAsync(string rawTransaction, CancellationToken ct);
    Task<BitcoinPreflightResult> PrepareBroadcastAsync(string rawTransaction, CancellationToken ct);
    Task<BitcoinBroadcastResult> SubmitAsync(string rawTransaction, BitcoinPreflightResult preflight, CancellationToken ct);
    Task<BitcoinTransactionStatus> GetTransactionStatusAsync(string txid, CancellationToken ct);
}

public sealed class BitcoinGatewayService : IBitcoinGatewayService
{
    private const string Source = "liveauth-bitcoin-node";
    private static readonly int[] FeeTargets = [1, 3, 6, 25, 144];
    private readonly IBitcoinNodeClient _node;
    private readonly IOptionsMonitor<BitcoinGatewayOptions> _options;
    private readonly SemaphoreSlim _feeLock = new(1, 1);
    private readonly SemaphoreSlim _mempoolLock = new(1, 1);
    private readonly ConcurrentDictionary<string, BroadcastPermit> _broadcastPermits = new(StringComparer.OrdinalIgnoreCase);
    private CacheEntry<BitcoinFeeEstimatesResponse>? _fees;
    private CacheEntry<BitcoinMempoolSummary>? _mempool;

    public BitcoinGatewayService(IBitcoinNodeClient node, IOptionsMonitor<BitcoinGatewayOptions> options)
    {
        _node = node;
        _options = options;
    }

    public BitcoinTransactionIdentity ValidateRawTransaction(string rawTransaction)
    {
        if (string.IsNullOrWhiteSpace(rawTransaction))
            throw InvalidTransaction("rawTransaction is required.");
        var normalized = rawTransaction.Trim();
        if ((normalized.Length & 1) != 0 || normalized.Any(ch => !Uri.IsHexDigit(ch)))
            throw InvalidTransaction("rawTransaction must be an even-length hexadecimal string.");
        var bytes = normalized.Length / 2;
        if (bytes > Math.Clamp(_options.CurrentValue.MaxRawTransactionBytes, 100, 4_000_000))
            throw InvalidTransaction("rawTransaction exceeds the configured LiveAuth transaction size limit.");

        try
        {
            var transaction = Transaction.Parse(normalized, ConfiguredNetwork());
            return new BitcoinTransactionIdentity(transaction.GetHash().ToString(),
                transaction.GetWitHash().ToString(), bytes);
        }
        catch (Exception ex) when (ex is FormatException or EndOfStreamException or ArgumentException)
        {
            throw InvalidTransaction("rawTransaction is not a well-formed Bitcoin transaction.", ex);
        }
    }

    public async Task<BitcoinFeeEstimatesResponse> GetFeeEstimatesAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var freshSeconds = Math.Clamp(_options.CurrentValue.FeeEstimateCacheSeconds, 1, 300);
        if (_fees is { } cached && cached.FreshUntil > now)
            return cached.Value with { Cached = true, NodeLatencyMilliseconds = 0 };

        await _feeLock.WaitAsync(ct);
        try
        {
            now = DateTime.UtcNow;
            if (_fees is { } lockedCached && lockedCached.FreshUntil > now)
                return lockedCached.Value with { Cached = true, NodeLatencyMilliseconds = 0 };
            try
            {
                var rpc = Stopwatch.StartNew();
                var observations = await Task.WhenAll(FeeTargets.Select(target =>
                    _node.EstimateSmartFeeAsync(target, ct)));
                var estimates = observations.Select((estimate, index) => new BitcoinFeeEstimate(
                    FeeTargets[index],
                    estimate.FeeRateBtcPerKvB.HasValue ? BtcPerKvBToSatPerVbyte(estimate.FeeRateBtcPerKvB.Value) : null,
                    estimate.FeeRateBtcPerKvB.HasValue ? null : string.Join("; ", estimate.Errors))).ToArray();
                var value = new BitcoinFeeEstimatesResponse(estimates, now, Source, false)
                {
                    NodeLatencyMilliseconds = rpc.ElapsedMilliseconds
                };
                _fees = new CacheEntry<BitcoinFeeEstimatesResponse>(value,
                    now.AddSeconds(freshSeconds), now.AddSeconds(Math.Max(freshSeconds, _options.CurrentValue.StaleCacheSeconds)));
                return value;
            }
            catch (BitcoinGatewayException ex) when (ex.Retryable && _fees is { } stale && stale.StaleUntil > now)
            {
                return stale.Value with { Cached = true, Stale = true, NodeLatencyMilliseconds = 0 };
            }
        }
        finally
        {
            _feeLock.Release();
        }
    }

    public async Task<BitcoinMempoolSummary> GetMempoolSummaryAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var freshSeconds = Math.Clamp(_options.CurrentValue.MempoolSummaryCacheSeconds, 1, 300);
        if (_mempool is { } cached && cached.FreshUntil > now)
            return cached.Value with { Cached = true, NodeLatencyMilliseconds = 0 };

        await _mempoolLock.WaitAsync(ct);
        try
        {
            now = DateTime.UtcNow;
            if (_mempool is { } lockedCached && lockedCached.FreshUntil > now)
                return lockedCached.Value with { Cached = true, NodeLatencyMilliseconds = 0 };
            try
            {
                var rpc = Stopwatch.StartNew();
                var info = await _node.GetMempoolInfoAsync(ct);
                var value = new BitcoinMempoolSummary(
                    info.Size,
                    info.Vsize ?? info.Bytes,
                    info.Usage,
                    info.TotalFeeBtc.HasValue ? BtcToSats(info.TotalFeeBtc.Value) : null,
                    BtcPerKvBToSatPerVbyte(info.MempoolMinFeeBtcPerKvB),
                    info.IncrementalRelayFeeBtcPerKvB.HasValue
                        ? BtcPerKvBToSatPerVbyte(info.IncrementalRelayFeeBtcPerKvB.Value)
                        : null,
                    now,
                    Source,
                    false)
                {
                    NodeLatencyMilliseconds = rpc.ElapsedMilliseconds
                };
                _mempool = new CacheEntry<BitcoinMempoolSummary>(value,
                    now.AddSeconds(freshSeconds), now.AddSeconds(Math.Max(freshSeconds, _options.CurrentValue.StaleCacheSeconds)));
                return value;
            }
            catch (BitcoinGatewayException ex) when (ex.Retryable && _mempool is { } stale && stale.StaleUntil > now)
            {
                return stale.Value with { Cached = true, Stale = true, NodeLatencyMilliseconds = 0 };
            }
        }
        finally
        {
            _mempoolLock.Release();
        }
    }

    public async Task<BitcoinPreflightResult> PreflightAsync(string rawTransaction, CancellationToken ct)
    {
        var identity = ValidateRawTransaction(rawTransaction);
        try
        {
            var rpc = Stopwatch.StartNew();
            var nodeResult = await _node.TestMempoolAcceptAsync(rawTransaction.Trim(), ct);
            if (!string.IsNullOrWhiteSpace(nodeResult.Txid) &&
                !string.Equals(nodeResult.Txid, identity.Txid, StringComparison.OrdinalIgnoreCase))
                throw new BitcoinGatewayException(BitcoinErrorCodes.NodeUnavailable,
                    "The Bitcoin node returned an inconsistent transaction identifier.", true,
                    StatusCodes.Status503ServiceUnavailable);

            var observedAt = DateTime.UtcNow;
            var rejectReason = nodeResult.RejectReason ?? nodeResult.PackageError;
            var rejectCode = nodeResult.Allowed ? null : NormalizeRejectCode(rejectReason);
            long? baseSats = nodeResult.BaseFeeBtc.HasValue ? BtcToSats(nodeResult.BaseFeeBtc.Value) : null;
            decimal? effective = nodeResult.EffectiveFeeRateBtcPerKvB.HasValue
                ? BtcPerKvBToSatPerVbyte(nodeResult.EffectiveFeeRateBtcPerKvB.Value)
                : baseSats.HasValue && nodeResult.Vsize > 0
                    ? Math.Round((decimal)baseSats.Value / nodeResult.Vsize.Value, 3)
                    : null;
            return new BitcoinPreflightResult(nodeResult.Allowed, nodeResult.Txid ?? identity.Txid,
                nodeResult.Wtxid ?? identity.Wtxid, nodeResult.Vsize,
                baseSats.HasValue || effective.HasValue ? new BitcoinTransactionFees(baseSats, effective) : null,
                rejectCode, SanitizeRejectReason(rejectReason), observedAt, Source)
            {
                NodeLatencyMilliseconds = rpc.ElapsedMilliseconds
            };
        }
        catch (BitcoinNodeRpcException ex)
        {
            throw MapRpcException(ex);
        }
    }

    public async Task<BitcoinPreflightResult> PrepareBroadcastAsync(string rawTransaction, CancellationToken ct)
    {
        var preflight = await PreflightAsync(rawTransaction, ct);
        if (!preflight.Accepted) return preflight;

        var options = _options.CurrentValue;
        if (preflight.Fees?.BaseSats > Math.Max(0, options.MaxAbsoluteFeeSats) ||
            preflight.Fees?.EffectiveSatPerVbyte > Math.Max(0, options.MaxFeeRateSatPerVbyte))
            return preflight with
            {
                Accepted = false,
                RejectCode = BitcoinErrorCodes.FeeLimitExceeded,
                RejectReason = "Transaction fee exceeds the configured LiveAuth broadcast safety limit."
            };
        var identity = ValidateRawTransaction(rawTransaction);
        _broadcastPermits[identity.Txid] = new BroadcastPermit(
            SHA256.HashData(Convert.FromHexString(rawTransaction.Trim())),
            DateTime.UtcNow.AddSeconds(Math.Clamp(options.IdempotencyLeaseSeconds, 5, 300)));
        return preflight;
    }

    public async Task<BitcoinBroadcastResult> SubmitAsync(
        string rawTransaction,
        BitcoinPreflightResult preflight,
        CancellationToken ct)
    {
        if (!preflight.Accepted || string.IsNullOrWhiteSpace(preflight.Txid))
            throw new BitcoinGatewayException(preflight.RejectCode ?? BitcoinErrorCodes.TransactionRejected,
                preflight.RejectReason ?? "Bitcoin transaction preflight was rejected.");
        var identity = ValidateRawTransaction(rawTransaction);
        if (!string.Equals(identity.Txid, preflight.Txid, StringComparison.OrdinalIgnoreCase) ||
            !_broadcastPermits.TryRemove(identity.Txid, out var permit) ||
            permit.ExpiresAt < DateTime.UtcNow ||
            !CryptographicOperations.FixedTimeEquals(permit.RequestHash,
                SHA256.HashData(Convert.FromHexString(rawTransaction.Trim()))))
            throw new BitcoinGatewayException(BitcoinErrorCodes.TransactionRejected,
                "Broadcast requires a fresh successful LiveAuth preflight for the same transaction.");
        try
        {
            var rpc = Stopwatch.StartNew();
            var txid = await _node.SendRawTransactionAsync(rawTransaction.Trim(), ct);
            if (!string.Equals(txid, preflight.Txid, StringComparison.OrdinalIgnoreCase))
                throw new BitcoinGatewayException(BitcoinErrorCodes.NodeUnavailable,
                    "The Bitcoin node returned an inconsistent broadcast transaction identifier.", true,
                    StatusCodes.Status503ServiceUnavailable);
            var now = DateTime.UtcNow;
            return new BitcoinBroadcastResult(true, true, false, false, txid,
                preflight.Wtxid, preflight.Vsize, preflight.Fees, now, now, Source)
            {
                NodeLatencyMilliseconds = rpc.ElapsedMilliseconds
            };
        }
        catch (BitcoinNodeRpcException ex) when (IsAlreadyKnown(ex.RpcMessage))
        {
            return new BitcoinBroadcastResult(true, false, true, false, preflight.Txid!,
                preflight.Wtxid, preflight.Vsize, preflight.Fees, null, DateTime.UtcNow, Source,
                BitcoinErrorCodes.AlreadyKnown, "Transaction is already known to the Bitcoin node.");
        }
        catch (BitcoinNodeRpcException ex)
        {
            throw MapRpcException(ex);
        }
    }

    public async Task<BitcoinTransactionStatus> GetTransactionStatusAsync(string txid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(txid) || txid.Length != 64 || txid.Any(ch => !Uri.IsHexDigit(ch)))
            throw new BitcoinGatewayException(BitcoinErrorCodes.InvalidTransaction,
                "txid must be a 64-character hexadecimal Bitcoin transaction ID.");

        var normalized = txid.ToLowerInvariant();
        try
        {
            var rpc = Stopwatch.StartNew();
            var mempool = await _node.GetMempoolEntryAsync(normalized, ct);
            if (mempool != null)
            {
                long? feeSats = mempool.BaseFeeBtc.HasValue ? BtcToSats(mempool.BaseFeeBtc.Value) : null;
                decimal? rate = mempool.EffectiveFeeRateBtcPerKvB.HasValue
                    ? BtcPerKvBToSatPerVbyte(mempool.EffectiveFeeRateBtcPerKvB.Value)
                    : feeSats.HasValue && mempool.Vsize > 0
                        ? Math.Round((decimal)feeSats.Value / mempool.Vsize.Value, 3)
                        : null;
                return new BitcoinTransactionStatus(normalized, "mempool", 0, null, null,
                    new BitcoinMempoolTransactionDetails(feeSats, mempool.Vsize, rate,
                        mempool.AncestorCount, mempool.DescendantCount), DateTime.UtcNow, Source)
                {
                    NodeLatencyMilliseconds = rpc.ElapsedMilliseconds
                };
            }

            var raw = await _node.GetRawTransactionAsync(normalized, ct);
            if (raw?.BlockHash != null)
            {
                var header = await _node.GetBlockHeaderAsync(raw.BlockHash, ct);
                return new BitcoinTransactionStatus(normalized, "confirmed",
                    Math.Max(1, raw.Confirmations ?? header?.Confirmations ?? 1), header?.Height,
                    raw.BlockHash, null, DateTime.UtcNow, Source)
                {
                    NodeLatencyMilliseconds = rpc.ElapsedMilliseconds
                };
            }
            return new BitcoinTransactionStatus(normalized, "not_found", 0, null, null,
                null, DateTime.UtcNow, Source)
            {
                NodeLatencyMilliseconds = rpc.ElapsedMilliseconds
            };
        }
        catch (BitcoinNodeRpcException ex)
        {
            throw MapRpcException(ex);
        }
    }

    private Network ConfiguredNetwork() => _options.CurrentValue.Network.Trim().ToLowerInvariant() switch
    {
        "main" or "mainnet" => Network.Main,
        "test" or "testnet" or "testnet3" => Network.TestNet,
        "regtest" => Network.RegTest,
        _ => Network.Main
    };

    private static BitcoinGatewayException InvalidTransaction(string message, Exception? inner = null)
        => new(BitcoinErrorCodes.InvalidTransaction, message, false,
            StatusCodes.Status400BadRequest, innerException: inner);

    private static long BtcToSats(decimal btc)
        => checked((long)Math.Round(btc * 100_000_000m, MidpointRounding.AwayFromZero));

    internal static decimal BtcPerKvBToSatPerVbyte(decimal btcPerKvB)
        => Math.Round(btcPerKvB * 100_000m, 3, MidpointRounding.AwayFromZero);

    private static string NormalizeRejectCode(string? reason)
    {
        var normalized = reason?.ToLowerInvariant() ?? string.Empty;
        if (normalized.Contains("missing-input") || normalized.Contains("inputs-missing"))
            return BitcoinErrorCodes.MissingInput;
        if (normalized.Contains("mempool-conflict") || normalized.Contains("txn-mempool-conflict"))
            return BitcoinErrorCodes.MempoolConflict;
        if (IsAlreadyKnown(normalized)) return BitcoinErrorCodes.AlreadyKnown;
        return BitcoinErrorCodes.TransactionRejected;
    }

    private static string? SanitizeRejectReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var trimmed = reason.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300];
    }

    private static bool IsAlreadyKnown(string? message)
    {
        var normalized = message?.ToLowerInvariant() ?? string.Empty;
        return normalized.Contains("already known") || normalized.Contains("already in block chain") ||
               normalized.Contains("txn-already-known") || normalized.Contains("transaction already in block chain");
    }

    private static BitcoinGatewayException MapRpcException(BitcoinNodeRpcException ex)
    {
        var message = ex.RpcMessage.ToLowerInvariant();
        if (ex.RpcCode == -22 || message.Contains("decode failed") || message.Contains("tx decode"))
            return InvalidTransaction("Bitcoin Core could not decode the supplied transaction.");
        var normalizedCode = NormalizeRejectCode(message);
        if (normalizedCode == BitcoinErrorCodes.MissingInput)
            return new BitcoinGatewayException(BitcoinErrorCodes.MissingInput,
                "The transaction references an input unavailable to the Bitcoin node.");
        if (normalizedCode == BitcoinErrorCodes.MempoolConflict)
            return new BitcoinGatewayException(BitcoinErrorCodes.MempoolConflict,
                "The transaction conflicts with a transaction in the node mempool.");
        if (normalizedCode == BitcoinErrorCodes.AlreadyKnown)
            return new BitcoinGatewayException(BitcoinErrorCodes.AlreadyKnown,
                "The transaction is already known to the Bitcoin node.");
        return new BitcoinGatewayException(BitcoinErrorCodes.TransactionRejected,
            "The Bitcoin node rejected the transaction.", false,
            StatusCodes.Status400BadRequest, innerException: ex);
    }

    private sealed record CacheEntry<T>(T Value, DateTime FreshUntil, DateTime StaleUntil);
    private sealed record BroadcastPermit(byte[] RequestHash, DateTime ExpiresAt);
}
