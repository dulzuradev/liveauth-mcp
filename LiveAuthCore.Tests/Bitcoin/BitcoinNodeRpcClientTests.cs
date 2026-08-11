using System.Net;
using System.Text;
using LiveAuthCore.Bitcoin;
using LiveAuthCore.Bitcoin.Configuration;
using LiveAuthCore.Bitcoin.Rpc;
using Xunit;

namespace LiveAuthCore.Tests.Bitcoin;

public sealed class BitcoinNodeRpcClientTests
{
    [Fact]
    public async Task Json_rpc_error_is_normalized_even_when_bitcoin_core_uses_http_500()
    {
        var handler = new SequenceHandler(_ => Json(HttpStatusCode.InternalServerError,
            """{"result":null,"error":{"code":-26,"message":"txn-mempool-conflict"},"id":"liveauth"}"""));
        var client = Client(handler);

        var error = await Assert.ThrowsAsync<BitcoinNodeRpcException>(() =>
            client.SendRawTransactionAsync(BitcoinTestTransactions.CreateRaw(), default));

        Assert.Equal(-26, error.RpcCode);
        Assert.Equal("txn-mempool-conflict", error.RpcMessage);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Broadcast_transport_failure_is_never_automatically_retried()
    {
        var handler = new SequenceHandler(_ => throw new HttpRequestException("node offline"));
        var client = Client(handler);

        var error = await Assert.ThrowsAsync<BitcoinGatewayException>(() =>
            client.SendRawTransactionAsync(BitcoinTestTransactions.CreateRaw(), default));

        Assert.Equal(BitcoinErrorCodes.NodeUnavailable, error.Code);
        Assert.True(error.Retryable);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Read_only_rpc_retries_once_after_transport_failure()
    {
        var handler = new SequenceHandler(
            _ => throw new HttpRequestException("temporary outage"),
            _ => Json(HttpStatusCode.OK,
                """{"result":{"feerate":0.000084,"blocks":3,"errors":[]},"error":null,"id":"liveauth"}"""));
        var client = Client(handler);

        var estimate = await client.EstimateSmartFeeAsync(3, default);

        Assert.Equal(0.000084m, estimate.FeeRateBtcPerKvB);
        Assert.Equal(2, handler.Calls);
    }

    private static BitcoinNodeRpcClient Client(HttpMessageHandler handler)
    {
        var options = new StaticOptionsMonitor<BitcoinGatewayOptions>(new BitcoinGatewayOptions
        {
            Enabled = true,
            RpcUrl = "http://127.0.0.1:8332",
            RpcUser = "rpc-user",
            RpcPassword = "rpc-password",
            RpcTimeoutMs = 1_000,
            CircuitBreakerFailureThreshold = 100
        });
        return new BitcoinNodeRpcClient(new FixedHttpClientFactory(new HttpClient(handler)),
            options, new BitcoinRpcCircuitBreaker(options));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            var response = responses[Math.Min(call, responses.Length) - 1](request);
            return Task.FromResult(response);
        }
    }
}
