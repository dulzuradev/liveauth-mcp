using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services.Meter;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LiveAuthCore.Tests.Middleware;

public sealed class MeterGatewayIntegrationTests : IClassFixture<MeterGatewayIntegrationTests.MeterFactory>
{
    private readonly MeterFactory _factory;
    private readonly HttpClient _client;

    public MeterGatewayIntegrationTests(MeterFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new() { AllowAutoRedirect = false });
        Seed();
    }

    [Fact]
    public async Task Free_route_is_forwarded_and_metered()
    {
        var response = await _client.GetAsync("/gateway/demo_pk_test/free");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("forwarded");
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>().MeterUsageEvents
            .Should().Contain(x => x.ProjectId == ProjectId && x.Kind == "FREE" && x.NormalizedRoute == "/free");
    }

    [Fact]
    public async Task Paid_route_returns_standard_l402_challenge_and_reuses_invoice()
    {
        var first = await _client.GetAsync("/gateway/demo_pk_test/paid");
        var second = await _client.GetAsync("/gateway/demo_pk_test/paid");
        first.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        first.Headers.WwwAuthenticate.Single().ToString().Should().StartWith("L402 macaroon=").And.Contain("invoice=");
        first.Headers.GetValues("X-LiveAuth-Price-Sats").Single().Should().Be("5");
        second.Headers.GetValues("X-LiveAuth-Challenge-Id").Single()
            .Should().Be(first.Headers.GetValues("X-LiveAuth-Challenge-Id").Single());
        _factory.Provider.CreateCalls.Should().Be(1);
    }

    private void Seed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        if (db.MeterProjectSettings.Any(x => x.ProjectId == ProjectId)) return;
        var project = db.Projects.Single(x => x.Id == ProjectId);
        var connection = new MerchantLightningConnection
        { Id = Guid.NewGuid(), ProjectId = project.Id, ProviderType = "LND_REST", RestUrl = "https://lnd.test", EncryptedMacaroon = "not-used" };
        db.MerchantLightningConnections.Add(connection);
        db.MeterProjectSettings.Add(new MeterProjectSettings
        {
            ProjectId = project.Id, Enabled = true, Environment = MeterEnvironments.Test,
            OriginBaseUrl = "https://origin.test", LightningConnectionId = connection.Id,
            UnmatchedRouteBehavior = MeterUnmatchedRouteBehaviors.Block
        });
        db.MeterRouteRules.AddRange(
            new MeterRouteRule { ProjectId = project.Id, HttpMethod = "GET", PathPattern = "/free", PriceSats = 0, Priority = 10 },
            new MeterRouteRule { ProjectId = project.Id, HttpMethod = "GET", PathPattern = "/paid", PriceSats = 5, Priority = 10 });
        db.SaveChanges();
    }

    private static readonly Guid ProjectId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    public sealed class MeterFactory : LiveAuthWebApplicationFactory
    {
        public FakeInvoiceProvider Provider { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILightningInvoiceProvider>();
                services.RemoveAll<ILightningInvoiceProviderFactory>();
                services.RemoveAll<IMeterOriginProxy>();
                services.AddSingleton<ILightningInvoiceProvider>(Provider);
                services.AddSingleton<ILightningInvoiceProviderFactory, LightningInvoiceProviderFactory>();
                services.AddSingleton<IMeterOriginProxy, FakeOriginProxy>();
            });
        }
    }

    public sealed class FakeInvoiceProvider : ILightningInvoiceProvider
    {
        private static readonly byte[] Preimage = Enumerable.Repeat((byte)7, 32).ToArray();
        public int CreateCalls { get; private set; }
        public string ProviderType => "LND_REST";
        public Task<MeterInvoice> CreateInvoiceAsync(MerchantLightningConnection connection, long amountSats, string memo, TimeSpan expiry, CancellationToken ct)
        { CreateCalls++; return Task.FromResult(new MeterInvoice(Convert.ToHexString(SHA256.HashData(Preimage)).ToLowerInvariant(), "lntb50n1integration", amountSats, DateTime.UtcNow.Add(expiry))); }
        public Task<MeterInvoiceStatus> LookupInvoiceAsync(MerchantLightningConnection connection, string paymentHash, CancellationToken ct)
            => Task.FromResult(new MeterInvoiceStatus(true, DateTime.UtcNow));
        public Task<MeterLightningConnectionStatus> ValidateConnectionAsync(MerchantLightningConnection connection, CancellationToken ct)
            => Task.FromResult(new MeterLightningConnectionStatus(true, "mock", "test", null));
    }

    private sealed class FakeOriginProxy : IMeterOriginProxy
    {
        public async Task<MeterProxyResult> ForwardAsync(HttpContext context, MeterProjectSettings settings,
            string path, byte[] body, Stopwatch gatewayClock,
            Func<int, long, long, Task<IReadOnlyDictionary<string, string>>> beforeHeaders, CancellationToken ct)
        {
            var metadata = await beforeHeaders(200, 2, gatewayClock.ElapsedMilliseconds);
            foreach (var pair in metadata) context.Response.Headers[pair.Key] = pair.Value;
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"forwarded\":true}", ct);
            return new(200, 2, gatewayClock.ElapsedMilliseconds);
        }
    }
}
