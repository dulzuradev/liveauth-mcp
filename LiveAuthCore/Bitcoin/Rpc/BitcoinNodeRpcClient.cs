using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveAuthCore.Bitcoin.Configuration;
using Microsoft.Extensions.Options;

namespace LiveAuthCore.Bitcoin.Rpc;

public sealed class BitcoinNodeRpcClient : IBitcoinNodeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IHttpClientFactory _clients;
    private readonly IOptionsMonitor<BitcoinGatewayOptions> _options;
    private readonly BitcoinRpcCircuitBreaker _circuit;

    public BitcoinNodeRpcClient(
        IHttpClientFactory clients,
        IOptionsMonitor<BitcoinGatewayOptions> options,
        BitcoinRpcCircuitBreaker circuit)
    {
        _clients = clients;
        _options = options;
        _circuit = circuit;
    }

    public async Task<BitcoinNodeFeeEstimate> EstimateSmartFeeAsync(int targetBlocks, CancellationToken ct)
    {
        var result = await CallAsync<EstimateSmartFeeRpc>("estimatesmartfee", [targetBlocks], true, ct);
        return new BitcoinNodeFeeEstimate(result.FeeRate, result.Blocks, result.Errors ?? []);
    }

    public async Task<BitcoinNodeMempoolInfo> GetMempoolInfoAsync(CancellationToken ct)
    {
        var result = await CallAsync<MempoolInfoRpc>("getmempoolinfo", [], true, ct);
        return new BitcoinNodeMempoolInfo(result.Size, result.Vsize, result.Bytes, result.Usage,
            result.TotalFee, result.MempoolMinFee, result.IncrementalRelayFee);
    }

    public async Task<BitcoinNodePreflightResult> TestMempoolAcceptAsync(string rawTransaction, CancellationToken ct)
    {
        var results = await CallAsync<TestMempoolAcceptRpc[]>("testmempoolaccept", [new[] { rawTransaction }], true, ct);
        var result = results.SingleOrDefault() ?? throw new BitcoinGatewayException(
            BitcoinErrorCodes.NodeUnavailable, "The Bitcoin node returned no preflight result.", true,
            StatusCodes.Status503ServiceUnavailable);
        return new BitcoinNodePreflightResult(result.Allowed, result.Txid, result.Wtxid, result.Vsize,
            result.Fees?.Base, result.Fees?.EffectiveFeeRate, result.RejectReason, result.PackageError);
    }

    public Task<string> SendRawTransactionAsync(string rawTransaction, CancellationToken ct)
        => CallAsync<string>("sendrawtransaction", [rawTransaction], false, ct);

    public async Task<BitcoinNodeMempoolEntry?> GetMempoolEntryAsync(string txid, CancellationToken ct)
    {
        try
        {
            var result = await CallAsync<MempoolEntryRpc>("getmempoolentry", [txid], true, ct);
            return new BitcoinNodeMempoolEntry(result.Vsize, result.Fees?.Base,
                result.Fees?.EffectiveFeeRate, result.AncestorCount, result.DescendantCount);
        }
        catch (BitcoinNodeRpcException ex) when (ex.RpcCode == -5)
        {
            return null;
        }
    }

    public async Task<BitcoinNodeRawTransaction?> GetRawTransactionAsync(string txid, CancellationToken ct)
    {
        try
        {
            var result = await CallAsync<RawTransactionRpc>("getrawtransaction", [txid, true], true, ct);
            return new BitcoinNodeRawTransaction(result.Txid ?? txid, result.BlockHash, result.Confirmations);
        }
        catch (BitcoinNodeRpcException ex) when (ex.RpcCode is -5 or -8)
        {
            return null;
        }
    }

    public async Task<BitcoinNodeBlockHeader?> GetBlockHeaderAsync(string blockHash, CancellationToken ct)
    {
        try
        {
            var result = await CallAsync<BlockHeaderRpc>("getblockheader", [blockHash, true], true, ct);
            return new BitcoinNodeBlockHeader(result.Hash ?? blockHash, result.Height, result.Confirmations);
        }
        catch (BitcoinNodeRpcException ex) when (ex.RpcCode == -5)
        {
            return null;
        }
    }

    private async Task<T> CallAsync<T>(string method, object[] parameters, bool safeToRetry, CancellationToken ct)
    {
        EnsureEnabledAndConfigured();
        var attempts = safeToRetry ? 2 : 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            _circuit.ThrowIfOpen();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await SendOnceAsync<T>(method, parameters, ct);
                _circuit.RecordSuccess(stopwatch.ElapsedMilliseconds);
                return result;
            }
            catch (BitcoinNodeRpcException)
            {
                // A valid JSON-RPC policy/application error means the node is healthy.
                _circuit.RecordSuccess(stopwatch.ElapsedMilliseconds);
                throw;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _circuit.RecordFailure();
                if (attempt < attempts) continue;
                throw new BitcoinGatewayException(BitcoinErrorCodes.RpcTimeout,
                    "The LiveAuth Bitcoin node did not respond before the RPC timeout.", true,
                    StatusCodes.Status503ServiceUnavailable);
            }
            catch (HttpRequestException ex)
            {
                _circuit.RecordFailure();
                if (attempt < attempts) continue;
                throw new BitcoinGatewayException(BitcoinErrorCodes.NodeUnavailable,
                    "The LiveAuth Bitcoin node is temporarily unavailable.", true,
                    StatusCodes.Status503ServiceUnavailable, innerException: ex);
            }
            catch (JsonException ex)
            {
                _circuit.RecordFailure();
                throw new BitcoinGatewayException(BitcoinErrorCodes.NodeUnavailable,
                    "The LiveAuth Bitcoin node returned an invalid response.", true,
                    StatusCodes.Status503ServiceUnavailable, innerException: ex);
            }
        }
        throw new InvalidOperationException("Unreachable Bitcoin RPC retry state.");
    }

    private async Task<T> SendOnceAsync<T>(string method, object[] parameters, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(options.RpcTimeoutMs, 100, 120_000)));
        using var request = new HttpRequestMessage(HttpMethod.Post, ValidatedRpcUri(options))
        {
            Content = JsonContent.Create(new { jsonrpc = "1.0", id = "liveauth", method, @params = parameters })
        };
        request.Headers.Authorization = Authentication(options);

        using var response = await _clients.CreateClient("bitcoin-node")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var envelope = await JsonSerializer.DeserializeAsync<RpcEnvelope>(stream, JsonOptions, timeout.Token)
            ?? throw new JsonException("Empty JSON-RPC response.");
        if (envelope.Error != null)
            throw new BitcoinNodeRpcException(envelope.Error.Code, envelope.Error.Message ?? "Bitcoin RPC error");
        response.EnsureSuccessStatusCode();
        if (!envelope.Result.HasValue)
            throw new JsonException("JSON-RPC response did not include a result.");
        return envelope.Result.Value.Deserialize<T>(JsonOptions)
            ?? throw new JsonException("JSON-RPC result could not be deserialized.");
    }

    private void EnsureEnabledAndConfigured()
    {
        if (!_options.CurrentValue.Enabled)
            throw new BitcoinGatewayException(BitcoinErrorCodes.Disabled,
                "The LiveAuth Bitcoin Agent Gateway is not enabled.", false,
                StatusCodes.Status503ServiceUnavailable);
    }

    private static Uri ValidatedRpcUri(BitcoinGatewayOptions options)
    {
        if (!Uri.TryCreate(options.RpcUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new BitcoinGatewayException(BitcoinErrorCodes.NodeUnavailable,
                "The configured Bitcoin RPC endpoint is invalid.", false,
                StatusCodes.Status503ServiceUnavailable);
        return uri;
    }

    private static AuthenticationHeaderValue Authentication(BitcoinGatewayOptions options)
    {
        string? credential = null;
        if (!string.IsNullOrWhiteSpace(options.RpcCookieFile))
        {
            try { credential = File.ReadAllText(options.RpcCookieFile).Trim(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new BitcoinGatewayException(BitcoinErrorCodes.NodeUnavailable,
                    "The configured Bitcoin RPC credential is unavailable.", false,
                    StatusCodes.Status503ServiceUnavailable, innerException: ex);
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.RpcUser) || !string.IsNullOrWhiteSpace(options.RpcPassword))
        {
            credential = $"{options.RpcUser}:{options.RpcPassword}";
        }

        if (string.IsNullOrWhiteSpace(credential))
            throw new BitcoinGatewayException(BitcoinErrorCodes.NodeUnavailable,
                "Bitcoin RPC credentials are not configured.", false,
                StatusCodes.Status503ServiceUnavailable);
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credential)));
    }

    private sealed class RpcEnvelope
    {
        public JsonElement? Result { get; set; }
        public RpcError? Error { get; set; }
    }

    private sealed class RpcError
    {
        public int Code { get; set; }
        public string? Message { get; set; }
    }

    private sealed class EstimateSmartFeeRpc
    {
        [JsonPropertyName("feerate")] public decimal? FeeRate { get; set; }
        public int? Blocks { get; set; }
        public string[]? Errors { get; set; }
    }

    private sealed class MempoolInfoRpc
    {
        public long Size { get; set; }
        public long? Vsize { get; set; }
        public long Bytes { get; set; }
        public long Usage { get; set; }
        [JsonPropertyName("total_fee")] public decimal? TotalFee { get; set; }
        [JsonPropertyName("mempoolminfee")] public decimal MempoolMinFee { get; set; }
        [JsonPropertyName("incrementalrelayfee")] public decimal? IncrementalRelayFee { get; set; }
    }

    private sealed class TestMempoolAcceptRpc
    {
        public string? Txid { get; set; }
        public string? Wtxid { get; set; }
        public bool Allowed { get; set; }
        public int? Vsize { get; set; }
        [JsonPropertyName("reject-reason")] public string? RejectReason { get; set; }
        [JsonPropertyName("package-error")] public string? PackageError { get; set; }
        public FeeRpc? Fees { get; set; }
    }

    private sealed class MempoolEntryRpc
    {
        public int? Vsize { get; set; }
        public FeeRpc? Fees { get; set; }
        [JsonPropertyName("ancestorcount")] public int? AncestorCount { get; set; }
        [JsonPropertyName("descendantcount")] public int? DescendantCount { get; set; }
    }

    private sealed class FeeRpc
    {
        public decimal? Base { get; set; }
        [JsonPropertyName("effective-feerate")] public decimal? EffectiveFeeRate { get; set; }
    }

    private sealed class RawTransactionRpc
    {
        public string? Txid { get; set; }
        public int? Confirmations { get; set; }
        [JsonPropertyName("blockhash")] public string? BlockHash { get; set; }
    }

    private sealed class BlockHeaderRpc
    {
        public string? Hash { get; set; }
        public int? Height { get; set; }
        public int? Confirmations { get; set; }
    }
}
