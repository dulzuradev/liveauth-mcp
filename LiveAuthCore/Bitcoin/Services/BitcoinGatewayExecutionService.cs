using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveAuthCore.Bitcoin.Configuration;
using LiveAuthCore.Bitcoin.Models;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models.Mcp;
using LiveAuthCore.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LiveAuthCore.Bitcoin.Services;

public interface IBitcoinGatewayExecutionService
{
    Task<BitcoinPaidResult<BitcoinFeeEstimatesResponse>> GetFeeEstimatesAsync(
        ClaimsPrincipal caller, string? idempotencyKey, string requestId, CancellationToken ct);
    Task<BitcoinPaidResult<BitcoinMempoolSummary>> GetMempoolSummaryAsync(
        ClaimsPrincipal caller, string? idempotencyKey, string requestId, CancellationToken ct);
    Task<BitcoinPaidResult<BitcoinPreflightResult>> PreflightAsync(
        ClaimsPrincipal caller, string rawTransaction, string? idempotencyKey, string requestId, CancellationToken ct);
    Task<BitcoinPaidResult<BitcoinBroadcastResult>> BroadcastAsync(
        ClaimsPrincipal caller, string rawTransaction, string? idempotencyKey, string requestId, CancellationToken ct);
    Task<BitcoinPaidResult<BitcoinTransactionStatus>> GetTransactionStatusAsync(
        ClaimsPrincipal caller, string txid, string? idempotencyKey, string requestId, CancellationToken ct);
}

public sealed class BitcoinGatewayExecutionService : IBitcoinGatewayExecutionService
{
    private const string Product = "Bitcoin Agent Gateway";
    private static readonly ConcurrentDictionary<string, OperationLockEntry> OperationLocks = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IBitcoinGatewayService _gateway;
    private readonly IMcpToolMeteringService _meter;
    private readonly IBitcoinGatewayRateLimiter _rateLimiter;
    private readonly LiveAuthDbContext _db;
    private readonly WebhookService _webhooks;
    private readonly IOptionsMonitor<BitcoinGatewayOptions> _options;
    private readonly ILogger<BitcoinGatewayExecutionService> _logger;

    public BitcoinGatewayExecutionService(
        IBitcoinGatewayService gateway,
        IMcpToolMeteringService meter,
        IBitcoinGatewayRateLimiter rateLimiter,
        LiveAuthDbContext db,
        WebhookService webhooks,
        IOptionsMonitor<BitcoinGatewayOptions> options,
        ILogger<BitcoinGatewayExecutionService> logger)
    {
        _gateway = gateway;
        _meter = meter;
        _rateLimiter = rateLimiter;
        _db = db;
        _webhooks = webhooks;
        _options = options;
        _logger = logger;
    }

    public async Task<BitcoinPaidResult<BitcoinFeeEstimatesResponse>> GetFeeEstimatesAsync(
        ClaimsPrincipal caller, string? idempotencyKey, string requestId, CancellationToken ct)
    {
        _rateLimiter.Acquire(caller, false);
        var started = DateTime.UtcNow;
        var value = await _gateway.GetFeeEstimatesAsync(ct);
        var metadata = Metadata(BitcoinGatewayTools.FeeEstimates, started, new
        {
            cacheHit = value.Cached,
            staleCache = value.Stale,
            estimateCount = value.Estimates.Count
        });
        metadata["bitcoinRpcLatencyMilliseconds"] = value.NodeLatencyMilliseconds;
        var meter = await ChargeAsync(caller, BitcoinGatewayTools.FeeEstimates,
            _options.CurrentValue.Tools.FeeEstimates.PriceSats, idempotencyKey, requestId,
            metadata, Attestation("observation", "bitcoin.get_fee_estimates", value.ObservedAt,
                null, new { value.Estimates, value.Cached, value.Stale }), ct);
        value = value with { Receipt = meter.Receipt };
        return new BitcoinPaidResult<BitcoinFeeEstimatesResponse>(value, meter.PriceSats,
            meter.RevenueEventId, meter.Duplicate);
    }

    public async Task<BitcoinPaidResult<BitcoinMempoolSummary>> GetMempoolSummaryAsync(
        ClaimsPrincipal caller, string? idempotencyKey, string requestId, CancellationToken ct)
    {
        _rateLimiter.Acquire(caller, false);
        var started = DateTime.UtcNow;
        var value = await _gateway.GetMempoolSummaryAsync(ct);
        var metadata = Metadata(BitcoinGatewayTools.MempoolSummary, started, new
        {
            cacheHit = value.Cached,
            staleCache = value.Stale,
            value.TransactionCount,
            value.VirtualSize
        });
        metadata["bitcoinRpcLatencyMilliseconds"] = value.NodeLatencyMilliseconds;
        var meter = await ChargeAsync(caller, BitcoinGatewayTools.MempoolSummary,
            _options.CurrentValue.Tools.MempoolSummary.PriceSats, idempotencyKey, requestId,
            metadata, Attestation("observation", "bitcoin.get_mempool_summary", value.ObservedAt,
                null, new { value.TransactionCount, value.VirtualSize, value.MempoolMinFeeSatVb }), ct);
        value = value with { Receipt = meter.Receipt };
        return new BitcoinPaidResult<BitcoinMempoolSummary>(value, meter.PriceSats,
            meter.RevenueEventId, meter.Duplicate);
    }

    public async Task<BitcoinPaidResult<BitcoinPreflightResult>> PreflightAsync(
        ClaimsPrincipal caller,
        string rawTransaction,
        string? idempotencyKey,
        string requestId,
        CancellationToken ct)
    {
        _rateLimiter.Acquire(caller, false);
        var started = DateTime.UtcNow;
        var value = await _gateway.PreflightAsync(rawTransaction, ct);
        var metadata = Metadata(BitcoinGatewayTools.PreflightTransaction, started, new
        {
            value.Accepted,
            value.Txid,
            value.RejectCode,
            value.Vsize,
            baseFeeSats = value.Fees?.BaseSats,
            effectiveFeeRate = value.Fees?.EffectiveSatPerVbyte
        });
        metadata["bitcoinRpcLatencyMilliseconds"] = value.NodeLatencyMilliseconds;
        var meter = await ChargeAsync(caller, BitcoinGatewayTools.PreflightTransaction,
            _options.CurrentValue.Tools.PreflightTransaction.PriceSats, idempotencyKey, requestId,
            metadata, Attestation("observation", "bitcoin.preflight_transaction", value.ObservedAt,
                value.Txid, new
                {
                    value.Accepted, value.Txid, value.Wtxid, value.Vsize, value.Fees,
                    value.RejectCode, value.RejectReason
                }), ct);
        value = value with { Receipt = meter.Receipt };
        await EnqueueBitcoinWebhookAsync(caller, "liveauth.bitcoin.preflight.completed", new
        {
            type = "liveauth.bitcoin.preflight.completed",
            projectId = ProjectId(caller),
            requestId,
            value.Txid,
            value.Accepted,
            value.RejectCode,
            observedAt = value.ObservedAt,
            priceSats = meter.PriceSats,
            receiptId = meter.Receipt?.Body.ReceiptId
        }, ct);
        return new BitcoinPaidResult<BitcoinPreflightResult>(value, meter.PriceSats,
            meter.RevenueEventId, meter.Duplicate);
    }

    public async Task<BitcoinPaidResult<BitcoinBroadcastResult>> BroadcastAsync(
        ClaimsPrincipal caller,
        string rawTransaction,
        string? idempotencyKey,
        string requestId,
        CancellationToken ct)
    {
        _rateLimiter.Acquire(caller, true);
        var identity = _gateway.ValidateRawTransaction(rawTransaction);
        var projectId = ProjectId(caller);
        var operationKey = OperationKey(idempotencyKey, identity.Txid);
        var requestHash = Convert.ToHexString(SHA256.HashData(Convert.FromHexString(rawTransaction.Trim()))).ToLowerInvariant();
        var lockKey = $"{projectId:N}:{operationKey}";
        var operationLease = await AcquireOperationLockAsync(lockKey, ct);
        try
        {
            var operation = await _db.BitcoinGatewayOperations.SingleOrDefaultAsync(item =>
                item.ProjectId == projectId && item.Operation == BitcoinGatewayTools.BroadcastTransaction &&
                item.IdempotencyKey == operationKey, ct);
            var operationExisted = operation != null;
            if (operation != null && !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(operation.RequestHash), Encoding.ASCII.GetBytes(requestHash)))
                throw new BitcoinGatewayException(BitcoinErrorCodes.IdempotencyConflict,
                    "The idempotency key was already used with a different transaction.", false,
                    StatusCodes.Status409Conflict);

            if (operation?.Status == "Succeeded" && !string.IsNullOrWhiteSpace(operation.ResultJson))
            {
                var previous = DeserializeBroadcast(operation.ResultJson);
                return new BitcoinPaidResult<BitcoinBroadcastResult>(previous,
                    previous.Receipt?.Body.GrossSats ?? _options.CurrentValue.Tools.BroadcastTransaction.PriceSats,
                    operation.RevenueEventId, true);
            }
            if (operation?.Status == "Rejected" && !string.IsNullOrWhiteSpace(operation.ResultJson))
                return new BitcoinPaidResult<BitcoinBroadcastResult>(DeserializeBroadcast(operation.ResultJson), 0,
                    operation.RevenueEventId, true);

            var lease = TimeSpan.FromSeconds(Math.Clamp(_options.CurrentValue.IdempotencyLeaseSeconds, 5, 300));
            if (operation?.Status == "Processing" && DateTime.UtcNow - operation.UpdatedAt < lease)
                throw new BitcoinGatewayException(BitcoinErrorCodes.OperationInProgress,
                    "A broadcast with this idempotency key is already in progress.", true,
                    StatusCodes.Status409Conflict, Math.Max(1, (int)lease.TotalSeconds));

            operation ??= new BitcoinGatewayOperation
            {
                ProjectId = projectId,
                McpGateTokenId = await GateTokenIdAsync(caller, projectId, ct),
                Operation = BitcoinGatewayTools.BroadcastTransaction,
                IdempotencyKey = operationKey,
                RequestHash = requestHash,
                RequestId = requestId,
                Txid = identity.Txid
            };
            if (_db.Entry(operation).State == EntityState.Detached)
                _db.BitcoinGatewayOperations.Add(operation);

            BitcoinBroadcastResult? recovered = null;
            if (operationExisted && operation.Status == "Processing")
                recovered = await RecoverSubmittedTransactionAsync(identity, null, ct);

            if (recovered == null)
            {
                if (operation.RevenueEventId.HasValue)
                    await _meter.CancelReservationAsync(operation.RevenueEventId.Value, "stale_broadcast_recovery", ct);
                operation.RevenueEventId = null;
                operation.Status = "Processing";
                operation.ErrorCode = null;
                operation.ResultJson = null;
                operation.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            var started = DateTime.UtcNow;
            var preflight = recovered == null
                ? await _gateway.PrepareBroadcastAsync(rawTransaction, ct)
                : null;
            if (preflight is { Accepted: false })
            {
                var rejected = RejectedBroadcast(identity, preflight);
                await CompleteOperationAsync(operation, "Rejected", rejected, preflight.RejectCode, null, ct);
                await EnqueueBitcoinWebhookAsync(caller, "liveauth.bitcoin.transaction.rejected", new
                {
                    type = "liveauth.bitcoin.transaction.rejected",
                    projectId,
                    requestId,
                    txid = identity.Txid,
                    rejectCode = preflight.RejectCode,
                    rejectReason = preflight.RejectReason,
                    observedAt = preflight.ObservedAt,
                    priceSats = 0
                }, ct);
                return new BitcoinPaidResult<BitcoinBroadcastResult>(rejected, 0, null, false);
            }

            McpToolMeterResult reservation;
            if (operation.RevenueEventId.HasValue)
            {
                reservation = new McpToolMeterResult(true, null,
                    _options.CurrentValue.Tools.BroadcastTransaction.PriceSats, 0, 0,
                    operation.RevenueEventId, null, ReservationId: operation.RevenueEventId);
            }
            else
            {
                reservation = await _meter.ReserveCallAsync(caller, BitcoinGatewayTools.BroadcastTransaction,
                    _options.CurrentValue.Tools.BroadcastTransaction.PriceSats, operationKey, requestId, Product,
                    Metadata(BitcoinGatewayTools.BroadcastTransaction, started, new
                    {
                        phase = "preflight_accepted",
                        txid = identity.Txid,
                        vsize = preflight?.Vsize,
                        baseFeeSats = preflight?.Fees?.BaseSats
                    }), ct);
                EnsureAuthorized(reservation);
                operation.RevenueEventId = reservation.ReservationId;
                operation.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            BitcoinBroadcastResult submitted;
            try
            {
                submitted = recovered ?? await _gateway.SubmitAsync(rawTransaction, preflight!, ct);
                if (submitted.AlreadyKnown && !submitted.Broadcasted)
                {
                    submitted = await RecoverSubmittedTransactionAsync(identity, preflight, ct)
                        ?? throw new BitcoinGatewayException(BitcoinErrorCodes.AlreadyKnown,
                            "Transaction is already known, but its state could not be verified.", false,
                            StatusCodes.Status409Conflict);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The caller disconnecting during sendrawtransaction is ambiguous. Keep
                // the reservation and recover against the node without the aborted token;
                // a later idempotent retry can finish if the node is also unavailable.
                var recoveredAfterCancellation = await RecoverSubmittedTransactionAsync(
                    identity, preflight, CancellationToken.None);
                if (recoveredAfterCancellation == null) throw;
                submitted = recoveredAfterCancellation;
            }
            catch (BitcoinGatewayException ex)
            {
                if (ex.Retryable)
                {
                    submitted = await RecoverSubmittedTransactionAsync(identity, preflight, ct)
                        ?? await FailBroadcastAsync(operation, reservation, "RetryableFailed", ex, ct);
                }
                else
                {
                    await FailBroadcastAsync(operation, reservation, "Rejected", ex, ct);
                    throw;
                }
            }
            catch
            {
                if (reservation.ReservationId.HasValue)
                    await _meter.CancelReservationAsync(reservation.ReservationId.Value, "broadcast_internal_failure", ct);
                operation.Status = "RetryableFailed";
                operation.ErrorCode = BitcoinErrorCodes.NodeUnavailable;
                operation.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                throw;
            }

            var metadata = Metadata(BitcoinGatewayTools.BroadcastTransaction, started, new
            {
                submitted.Txid,
                submitted.Broadcasted,
                submitted.AlreadyKnown,
                submitted.Recovered,
                submitted.Vsize,
                baseFeeSats = submitted.Fees?.BaseSats,
                effectiveFeeRate = submitted.Fees?.EffectiveSatPerVbyte,
                requestId
            });
            metadata["bitcoinRpcLatencyMilliseconds"] = (preflight?.NodeLatencyMilliseconds ?? 0) +
                                                          submitted.NodeLatencyMilliseconds;
            var attestation = Attestation("execution", "bitcoin.broadcast_transaction",
                submitted.BroadcastAt ?? submitted.ObservedAt, submitted.Txid, new
                {
                    submitted.Txid,
                    submitted.Broadcasted,
                    submitted.AlreadyKnown,
                    submitted.Recovered,
                    submitted.Vsize,
                    submitted.Fees,
                    requestId,
                    idempotencyKey
                });
            // Once the node accepted the transaction, finalizing the charge and durable
            // idempotency result must not be cancelled by a client disconnect.
            var finalizationCt = CancellationToken.None;
            var committed = reservation.Duplicate && reservation.Receipt != null
                ? reservation
                : await _meter.CommitReservationAsync(reservation.ReservationId!.Value, metadata, attestation, finalizationCt);
            EnsureAuthorized(committed);
            submitted = submitted with { Receipt = committed.Receipt };
            await CompleteOperationAsync(operation, "Succeeded", submitted, null,
                committed.RevenueEventId, finalizationCt);
            await EnqueueBitcoinWebhookAsync(caller, "liveauth.bitcoin.transaction.broadcast", new
            {
                type = "liveauth.bitcoin.transaction.broadcast",
                projectId,
                requestId,
                submitted.Txid,
                submitted.BroadcastAt,
                submitted.Recovered,
                priceSats = committed.PriceSats,
                receiptId = committed.Receipt?.Body.ReceiptId
            }, finalizationCt);
            return new BitcoinPaidResult<BitcoinBroadcastResult>(submitted, committed.PriceSats,
                committed.RevenueEventId, committed.Duplicate);
        }
        finally
        {
            operationLease.Dispose();
        }
    }

    public async Task<BitcoinPaidResult<BitcoinTransactionStatus>> GetTransactionStatusAsync(
        ClaimsPrincipal caller,
        string txid,
        string? idempotencyKey,
        string requestId,
        CancellationToken ct)
    {
        _rateLimiter.Acquire(caller, false);
        var started = DateTime.UtcNow;
        var value = await _gateway.GetTransactionStatusAsync(txid, ct);
        var metadata = Metadata(BitcoinGatewayTools.TransactionStatus, started, new
        {
            value.Txid,
            value.Status,
            value.Confirmations,
            value.BlockHeight
        });
        metadata["bitcoinRpcLatencyMilliseconds"] = value.NodeLatencyMilliseconds;
        var meter = await ChargeAsync(caller, BitcoinGatewayTools.TransactionStatus,
            _options.CurrentValue.Tools.TransactionStatus.PriceSats, idempotencyKey, requestId,
            metadata, Attestation("observation", "bitcoin.get_transaction_status", value.ObservedAt,
                value.Txid, new
                {
                    value.Txid, value.Status, value.Confirmations, value.BlockHeight,
                    value.BlockHash, value.Mempool
                }), ct);
        value = value with { Receipt = meter.Receipt };
        return new BitcoinPaidResult<BitcoinTransactionStatus>(value, meter.PriceSats,
            meter.RevenueEventId, meter.Duplicate);
    }

    private async Task<McpToolMeterResult> ChargeAsync(
        ClaimsPrincipal caller,
        string tool,
        int price,
        string? idempotencyKey,
        string requestId,
        IReadOnlyDictionary<string, object?> metadata,
        McpReceiptAttestation attestation,
        CancellationToken ct)
    {
        var result = await _meter.ChargeSuccessfulCallAsync(caller, tool, price, idempotencyKey,
            requestId, Product, metadata, attestation, ct);
        EnsureAuthorized(result);
        return result;
    }

    private static void EnsureAuthorized(McpToolMeterResult result)
    {
        if (result.Authorized) return;
        throw new BitcoinGatewayException(BitcoinErrorCodes.PaymentDenied,
            $"LiveAuth Meter denied the paid call: {result.Reason}.",
            result.Reason == "call_in_progress", StatusCodes.Status402PaymentRequired);
    }

    private async Task<BitcoinBroadcastResult?> RecoverSubmittedTransactionAsync(
        BitcoinTransactionIdentity identity,
        BitcoinPreflightResult? preflight,
        CancellationToken ct)
    {
        var status = await _gateway.GetTransactionStatusAsync(identity.Txid, ct);
        if (status.Status == "not_found") return null;
        var now = DateTime.UtcNow;
        return new BitcoinBroadcastResult(true, false, true, true, identity.Txid,
            preflight?.Wtxid ?? identity.Wtxid, preflight?.Vsize,
            preflight?.Fees, now, now, "liveauth-bitcoin-node")
        {
            NodeLatencyMilliseconds = status.NodeLatencyMilliseconds
        };
    }

    private async Task<BitcoinBroadcastResult> FailBroadcastAsync(
        BitcoinGatewayOperation operation,
        McpToolMeterResult reservation,
        string status,
        BitcoinGatewayException error,
        CancellationToken ct)
    {
        if (reservation.ReservationId.HasValue)
            await _meter.CancelReservationAsync(reservation.ReservationId.Value, error.Code, ct);
        operation.Status = status;
        operation.ErrorCode = error.Code;
        operation.RevenueEventId = null;
        operation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        throw error;
    }

    private async Task CompleteOperationAsync(
        BitcoinGatewayOperation operation,
        string status,
        BitcoinBroadcastResult result,
        string? errorCode,
        Guid? revenueEventId,
        CancellationToken ct)
    {
        operation.Status = status;
        operation.ErrorCode = errorCode;
        operation.RevenueEventId = revenueEventId ?? operation.RevenueEventId;
        operation.ResultJson = JsonSerializer.Serialize(result, JsonOptions);
        operation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static BitcoinBroadcastResult RejectedBroadcast(
        BitcoinTransactionIdentity identity,
        BitcoinPreflightResult preflight)
        => new(false, false, preflight.RejectCode == BitcoinErrorCodes.AlreadyKnown, false,
            preflight.Txid ?? identity.Txid, preflight.Wtxid ?? identity.Wtxid,
            preflight.Vsize, preflight.Fees, null, preflight.ObservedAt, preflight.Source,
            preflight.RejectCode, preflight.RejectReason);

    private static BitcoinBroadcastResult DeserializeBroadcast(string json)
        => JsonSerializer.Deserialize<BitcoinBroadcastResult>(json, JsonOptions)
           ?? throw new InvalidOperationException("Stored Bitcoin broadcast result is invalid.");

    private static Dictionary<string, object?> Metadata(string tool, DateTime started, object details)
        => new()
        {
            ["product"] = Product,
            ["tool"] = tool,
            ["durationMilliseconds"] = (long)(DateTime.UtcNow - started).TotalMilliseconds,
            ["details"] = details
        };

    private McpReceiptAttestation Attestation(
        string kind,
        string operation,
        DateTime observedAt,
        string? subjectId,
        object claims)
    {
        var canonicalClaims = JsonSerializer.Serialize(claims, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalClaims))).ToLowerInvariant();
        return new McpReceiptAttestation(kind, operation,
            DateTime.SpecifyKind(observedAt, DateTimeKind.Utc), "liveauth-bitcoin-node",
            _options.CurrentValue.Network, subjectId, canonicalClaims, hash);
    }

    private async Task EnqueueBitcoinWebhookAsync(
        ClaimsPrincipal caller,
        string eventType,
        object payload,
        CancellationToken ct)
    {
        try
        {
            var project = await _db.Projects.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == ProjectId(caller), ct);
            if (project != null) await _webhooks.EnqueueAsync(project, eventType, payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Bitcoin Gateway event {EventType} could not be queued.", eventType);
        }
    }

    private static Guid ProjectId(ClaimsPrincipal caller)
    {
        if (Guid.TryParse(caller.FindFirst("projectId")?.Value, out var projectId)) return projectId;
        throw new BitcoinGatewayException(BitcoinErrorCodes.PaymentDenied,
            "The authenticated MCP identity does not contain a LiveAuth project.", false,
            StatusCodes.Status401Unauthorized);
    }

    private async Task<Guid?> GateTokenIdAsync(ClaimsPrincipal caller, Guid projectId, CancellationToken ct)
    {
        if (Guid.TryParse(caller.FindFirst("mcpGateTokenId")?.Value, out var tokenId)) return tokenId;
        var jti = caller.FindFirst("jti")?.Value;
        return string.IsNullOrWhiteSpace(jti)
            ? null
            : await _db.McpGateTokens.AsNoTracking()
                .Where(item => item.ProjectId == projectId && item.JwtId == jti)
                .Select(item => (Guid?)item.Id)
                .SingleOrDefaultAsync(ct);
    }

    private static string OperationKey(string? idempotencyKey, string txid)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return $"txid:{txid}";
        var trimmed = idempotencyKey.Trim();
        if (trimmed.Length > 160) trimmed = trimmed[..160];
        return $"key:{trimmed}";
    }

    private static async Task<OperationLockLease> AcquireOperationLockAsync(string key, CancellationToken ct)
    {
        while (true)
        {
            var entry = OperationLocks.GetOrAdd(key, _ => new OperationLockEntry());
            Interlocked.Increment(ref entry.Users);
            if (!OperationLocks.TryGetValue(key, out var current) || !ReferenceEquals(entry, current))
            {
                ReleaseOperationLockReference(key, entry, false);
                continue;
            }

            try
            {
                await entry.Semaphore.WaitAsync(ct);
                return new OperationLockLease(key, entry);
            }
            catch
            {
                ReleaseOperationLockReference(key, entry, false);
                throw;
            }
        }
    }

    private static void ReleaseOperationLockReference(string key, OperationLockEntry entry, bool acquired)
    {
        if (acquired) entry.Semaphore.Release();
        if (Interlocked.Decrement(ref entry.Users) != 0) return;
        ((ICollection<KeyValuePair<string, OperationLockEntry>>)OperationLocks)
            .Remove(new KeyValuePair<string, OperationLockEntry>(key, entry));
    }

    private sealed class OperationLockEntry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Users;
    }

    private sealed class OperationLockLease(string key, OperationLockEntry entry) : IDisposable
    {
        private OperationLockEntry? _entry = entry;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _entry, null);
            if (current != null) ReleaseOperationLockReference(key, current, true);
        }
    }
}
