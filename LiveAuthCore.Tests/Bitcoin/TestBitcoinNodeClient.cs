using LiveAuthCore.Bitcoin;
using LiveAuthCore.Bitcoin.Rpc;
using NBitcoin;

namespace LiveAuthCore.Tests.Bitcoin;

internal sealed class TestBitcoinNodeClient : IBitcoinNodeClient
{
    private readonly HashSet<string> _mempool = new(StringComparer.OrdinalIgnoreCase);
    public int PreflightCalls { get; private set; }
    public int SendCalls { get; private set; }
    public BitcoinNodePreflightResult? PreflightResult { get; set; }
    public BitcoinNodeMempoolEntry? MempoolEntry { get; set; }
    public BitcoinNodeRawTransaction? RawTransaction { get; set; }
    public BitcoinNodeBlockHeader? BlockHeader { get; set; }
    public BitcoinGatewayException? SendExceptionBeforeAcceptance { get; set; }
    public BitcoinGatewayException? SendExceptionAfterAcceptance { get; set; }

    public Task<BitcoinNodeFeeEstimate> EstimateSmartFeeAsync(int targetBlocks, CancellationToken ct)
        => Task.FromResult(new BitcoinNodeFeeEstimate(targetBlocks switch
        {
            1 => 0.000084m,
            3 => 0.000062m,
            6 => 0.000041m,
            25 => 0.000025m,
            _ => 0.000010m
        }, targetBlocks, []));

    public Task<BitcoinNodeMempoolInfo> GetMempoolInfoAsync(CancellationToken ct)
        => Task.FromResult(new BitcoinNodeMempoolInfo(48_321, 183_948_201, 183_948_201,
            421_337_600, 1.25m, 0.000011m, 0.000010m));

    public Task<BitcoinNodePreflightResult> TestMempoolAcceptAsync(string rawTransaction, CancellationToken ct)
    {
        PreflightCalls++;
        var transaction = Transaction.Parse(rawTransaction, Network.RegTest);
        return Task.FromResult(PreflightResult ?? new BitcoinNodePreflightResult(true,
            transaction.GetHash().ToString(), transaction.GetWitHash().ToString(),
            transaction.GetVirtualSize(), 0.000012m, 0.000085m, null, null));
    }

    public Task<string> SendRawTransactionAsync(string rawTransaction, CancellationToken ct)
    {
        SendCalls++;
        if (SendExceptionBeforeAcceptance != null) throw SendExceptionBeforeAcceptance;
        var txid = Transaction.Parse(rawTransaction, Network.RegTest).GetHash().ToString();
        _mempool.Add(txid);
        if (SendExceptionAfterAcceptance != null) throw SendExceptionAfterAcceptance;
        return Task.FromResult(txid);
    }

    public Task<BitcoinNodeMempoolEntry?> GetMempoolEntryAsync(string txid, CancellationToken ct)
        => Task.FromResult(_mempool.Contains(txid)
            ? MempoolEntry ?? new BitcoinNodeMempoolEntry(141, 0.000012m, 0.000085m, 1, 1)
            : MempoolEntry);

    public Task<BitcoinNodeRawTransaction?> GetRawTransactionAsync(string txid, CancellationToken ct)
        => Task.FromResult(RawTransaction);

    public Task<BitcoinNodeBlockHeader?> GetBlockHeaderAsync(string blockHash, CancellationToken ct)
        => Task.FromResult(BlockHeader);
}

internal static class BitcoinTestTransactions
{
    public static string CreateRaw(uint inputIndex = 0)
    {
        var transaction = Network.RegTest.CreateTransaction();
        transaction.Inputs.Add(new TxIn(new OutPoint(uint256.One, inputIndex)));
        transaction.Outputs.Add(Money.Satoshis(10_000), Script.Empty);
        return transaction.ToHex();
    }
}
