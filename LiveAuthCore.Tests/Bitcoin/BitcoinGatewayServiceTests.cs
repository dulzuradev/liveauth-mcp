using LiveAuthCore.Bitcoin;
using LiveAuthCore.Bitcoin.Configuration;
using LiveAuthCore.Bitcoin.Rpc;
using LiveAuthCore.Bitcoin.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace LiveAuthCore.Tests.Bitcoin;

public sealed class BitcoinGatewayServiceTests
{
    [Fact]
    public async Task Fee_estimates_are_normalized_to_sat_vbyte_and_cached()
    {
        var node = new CountingNode();
        var service = Service(node);

        var first = await service.GetFeeEstimatesAsync(default);
        var second = await service.GetFeeEstimatesAsync(default);

        Assert.Equal(8.4m, first.Estimates[0].SatPerVbyte);
        Assert.False(first.Cached);
        Assert.True(second.Cached);
        Assert.Equal(5, node.FeeCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zz")]
    [InlineData("00")]
    public void Raw_transaction_validation_rejects_malformed_input(string raw)
    {
        var error = Assert.Throws<BitcoinGatewayException>(() => Service(new CountingNode()).ValidateRawTransaction(raw));
        Assert.Equal(BitcoinErrorCodes.InvalidTransaction, error.Code);
    }

    [Fact]
    public async Task Broadcast_preparation_enforces_absolute_fee_limit()
    {
        var node = new CountingNode
        {
            Preflight = new BitcoinNodePreflightResult(true, null, null, 141,
                0.01000000m, 0.070921m, null, null)
        };
        var service = Service(node, options => options.MaxAbsoluteFeeSats = 10_000);

        var result = await service.PrepareBroadcastAsync(BitcoinTestTransactions.CreateRaw(), default);

        Assert.False(result.Accepted);
        Assert.Equal(BitcoinErrorCodes.FeeLimitExceeded, result.RejectCode);
        Assert.Equal(0, node.SendCalls);
    }

    [Fact]
    public async Task Broadcast_preparation_enforces_fee_rate_limit()
    {
        var node = new CountingNode
        {
            Preflight = new BitcoinNodePreflightResult(true, null, null, 141,
                0.00000141m, 0.001001m, null, null)
        };
        var service = Service(node, options => options.MaxFeeRateSatPerVbyte = 100);

        var result = await service.PrepareBroadcastAsync(BitcoinTestTransactions.CreateRaw(), default);

        Assert.False(result.Accepted);
        Assert.Equal(BitcoinErrorCodes.FeeLimitExceeded, result.RejectCode);
        Assert.Equal(0, node.SendCalls);
    }

    [Theory]
    [InlineData("bad-txns-inputs-missingorspent", BitcoinErrorCodes.MissingInput)]
    [InlineData("txn-mempool-conflict", BitcoinErrorCodes.MempoolConflict)]
    [InlineData("txn-already-known", BitcoinErrorCodes.AlreadyKnown)]
    [InlineData("min relay fee not met", BitcoinErrorCodes.TransactionRejected)]
    [InlineData("non-mandatory-script-verify-flag", BitcoinErrorCodes.TransactionRejected)]
    public async Task Preflight_policy_rejections_are_normalized(string reason, string expectedCode)
    {
        var node = new CountingNode
        {
            Preflight = new BitcoinNodePreflightResult(false, null, null, 141,
                null, null, reason, null)
        };

        var result = await Service(node).PreflightAsync(BitcoinTestTransactions.CreateRaw(), default);

        Assert.False(result.Accepted);
        Assert.Equal(expectedCode, result.RejectCode);
        Assert.Equal(reason, result.RejectReason);
        Assert.Equal(0, node.SendCalls);
    }

    [Theory]
    [InlineData(-22, "TX decode failed", BitcoinErrorCodes.InvalidTransaction)]
    [InlineData(-26, "bad-txns-inputs-missingorspent: missing input", BitcoinErrorCodes.MissingInput)]
    [InlineData(-26, "txn-mempool-conflict", BitcoinErrorCodes.MempoolConflict)]
    [InlineData(-27, "transaction already in block chain", BitcoinErrorCodes.AlreadyKnown)]
    [InlineData(-26, "nonstandard transaction", BitcoinErrorCodes.TransactionRejected)]
    public async Task Rpc_errors_are_normalized_without_leaking_node_details(
        int rpcCode,
        string rpcMessage,
        string expectedCode)
    {
        var service = Service(new CountingNode
        {
            PreflightException = new BitcoinNodeRpcException(rpcCode, rpcMessage)
        });

        var error = await Assert.ThrowsAsync<BitcoinGatewayException>(() =>
            service.PreflightAsync(BitcoinTestTransactions.CreateRaw(), default));

        Assert.Equal(expectedCode, error.Code);
        Assert.DoesNotContain(rpcMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submission_cannot_bypass_fresh_preflight()
    {
        var node = new CountingNode();
        var service = Service(node);
        var raw = BitcoinTestTransactions.CreateRaw();
        var transaction = NBitcoin.Transaction.Parse(raw, NBitcoin.Network.RegTest);
        var forged = new LiveAuthCore.Bitcoin.Models.BitcoinPreflightResult(true,
            transaction.GetHash().ToString(), transaction.GetWitHash().ToString(), 141,
            null, null, null, DateTime.UtcNow, "test");

        var error = await Assert.ThrowsAsync<BitcoinGatewayException>(() =>
            service.SubmitAsync(raw, forged, default));

        Assert.Equal(BitcoinErrorCodes.TransactionRejected, error.Code);
        Assert.Equal(0, node.SendCalls);
    }

    [Fact]
    public async Task Status_normalizes_mempool_confirmed_and_not_found()
    {
        var txid = new string('a', 64);
        var mempoolNode = new CountingNode
        {
            Mempool = new BitcoinNodeMempoolEntry(100, 0.000001m, 0.000010m, 2, 3)
        };
        var mempool = await Service(mempoolNode).GetTransactionStatusAsync(txid, default);
        Assert.Equal("mempool", mempool.Status);
        Assert.Equal(1m, mempool.Mempool!.EffectiveSatPerVbyte);

        var confirmedNode = new CountingNode
        {
            Raw = new BitcoinNodeRawTransaction(txid, new string('b', 64), 3),
            Header = new BitcoinNodeBlockHeader(new string('b', 64), 912_345, 3)
        };
        var confirmed = await Service(confirmedNode).GetTransactionStatusAsync(txid, default);
        Assert.Equal("confirmed", confirmed.Status);
        Assert.Equal(912_345, confirmed.BlockHeight);
        Assert.Equal(3, confirmed.Confirmations);

        var missing = await Service(new CountingNode()).GetTransactionStatusAsync(txid, default);
        Assert.Equal("not_found", missing.Status);
    }

    private static BitcoinGatewayService Service(
        IBitcoinNodeClient node,
        Action<BitcoinGatewayOptions>? configure = null)
    {
        var options = new BitcoinGatewayOptions
        {
            Enabled = true,
            Network = "regtest",
            FeeEstimateCacheSeconds = 30,
            MaxAbsoluteFeeSats = 10_000_000,
            MaxFeeRateSatPerVbyte = 1_000
        };
        configure?.Invoke(options);
        return new BitcoinGatewayService(node, new StaticOptionsMonitor<BitcoinGatewayOptions>(options));
    }

    private sealed class CountingNode : IBitcoinNodeClient
    {
        public int FeeCalls { get; private set; }
        public int SendCalls { get; private set; }
        public BitcoinNodePreflightResult? Preflight { get; init; }
        public BitcoinNodeRpcException? PreflightException { get; init; }
        public BitcoinNodeMempoolEntry? Mempool { get; init; }
        public BitcoinNodeRawTransaction? Raw { get; init; }
        public BitcoinNodeBlockHeader? Header { get; init; }

        public Task<BitcoinNodeFeeEstimate> EstimateSmartFeeAsync(int targetBlocks, CancellationToken ct)
        {
            FeeCalls++;
            return Task.FromResult(new BitcoinNodeFeeEstimate(0.000084m, targetBlocks, []));
        }
        public Task<BitcoinNodeMempoolInfo> GetMempoolInfoAsync(CancellationToken ct)
            => Task.FromResult(new BitcoinNodeMempoolInfo(1, 100, 100, 200, 0.000001m, 0.00001m, 0.00001m));
        public Task<BitcoinNodePreflightResult> TestMempoolAcceptAsync(string rawTransaction, CancellationToken ct)
        {
            if (PreflightException != null) throw PreflightException;
            var identity = NBitcoin.Transaction.Parse(rawTransaction, NBitcoin.Network.RegTest);
            return Task.FromResult(Preflight ?? new BitcoinNodePreflightResult(true,
                identity.GetHash().ToString(), identity.GetWitHash().ToString(), 141,
                0.000012m, 0.000085m, null, null));
        }
        public Task<string> SendRawTransactionAsync(string rawTransaction, CancellationToken ct)
        {
            SendCalls++;
            return Task.FromResult(NBitcoin.Transaction.Parse(rawTransaction, NBitcoin.Network.RegTest).GetHash().ToString());
        }
        public Task<BitcoinNodeMempoolEntry?> GetMempoolEntryAsync(string txid, CancellationToken ct) => Task.FromResult(Mempool);
        public Task<BitcoinNodeRawTransaction?> GetRawTransactionAsync(string txid, CancellationToken ct) => Task.FromResult(Raw);
        public Task<BitcoinNodeBlockHeader?> GetBlockHeaderAsync(string blockHash, CancellationToken ct) => Task.FromResult(Header);
    }
}

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
