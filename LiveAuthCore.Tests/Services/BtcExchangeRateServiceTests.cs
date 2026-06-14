using System.Net;
using System.Text;
using FluentAssertions;
using LiveAuthCore.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LiveAuthCore.Tests.Services;

public class BtcExchangeRateServiceTests
{
    [Fact]
    public async Task GetBtcUsdRateAsync_UsesCoinGeckoAndCachesSuccessfulRate()
    {
        var requestCount = 0;
        var service = CreateService((_, _) =>
        {
            requestCount++;
            return JsonResponse(HttpStatusCode.OK, """{"bitcoin":{"usd":65000.0}}""");
        });

        var first = await service.GetBtcUsdRateAsync();
        var second = await service.GetBtcUsdRateAsync();

        first.Should().Be(65000.0);
        second.Should().Be(65000.0);
        requestCount.Should().Be(1);
    }

    [Fact]
    public async Task GetBtcUsdRateAsync_FallsBackToCoinbaseWhenCoinGeckoFails()
    {
        var requestedClients = new List<string>();
        var service = CreateService((clientName, _) =>
        {
            requestedClients.Add(clientName);
            return clientName switch
            {
                "coingecko" => JsonResponse(HttpStatusCode.Forbidden, "{}"),
                "coinbase" => JsonResponse(HttpStatusCode.OK, """{"data":{"amount":"64000.50"}}"""),
                _ => JsonResponse(HttpStatusCode.NotFound, "{}")
            };
        });

        var rate = await service.GetBtcUsdRateAsync();

        rate.Should().Be(64000.50);
        requestedClients.Should().Equal("coingecko", "coinbase");
    }

    [Fact]
    public async Task GetBtcUsdRateAsync_ReturnsNullWhenAllSourcesFail()
    {
        var service = CreateService((_, _) => JsonResponse(HttpStatusCode.ServiceUnavailable, "{}"));

        var rate = await service.GetBtcUsdRateAsync();

        rate.Should().BeNull();
    }

    private static BtcExchangeRateService CreateService(
        Func<string, HttpRequestMessage, HttpResponseMessage> responder)
    {
        return new BtcExchangeRateService(
            new StubHttpClientFactory(responder),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<BtcExchangeRateService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<string, HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpClientFactory(
            Func<string, HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StubHttpMessageHandler(request => _responder(name, request)));
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
