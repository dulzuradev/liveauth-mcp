using System.Security.Cryptography;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Services.Meter;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LiveAuthCore.Tests.Services.Meter;

public sealed class MeterPaymentServiceTests
{
    [Fact]
    public async Task Paid_credential_is_route_bound_and_one_shot_replay_is_rejected()
    {
        await using var fixture = await Fixture.Create(maximumUses: 1);
        var issued = await fixture.Service.CreateOrReuseChallengeAsync(fixture.Project, fixture.Settings,
            fixture.Decision, "POST", "/research", "caller", "request-1", fixture.BodyHash, default);
        var header = $"L402 {issued.Challenge.Macaroon}:{Convert.ToHexString(fixture.Preimage)}";

        var wrongRoute = fixture.Decision with { NormalizedRoute = "/other" };
        var wrong = await fixture.Service.AuthorizeAsync(fixture.Settings, wrongRoute, "POST", "/other", fixture.BodyHash, header, default);
        Assert.False(wrong.Authorized);
        Assert.Equal("credential_caveat_mismatch", wrong.Error);

        var badPreimage = await fixture.Service.AuthorizeAsync(fixture.Settings, fixture.Decision, "POST", "/research",
            fixture.BodyHash, $"L402 {issued.Challenge.Macaroon}:{new string('0', 64)}", default);
        Assert.False(badPreimage.Authorized);
        Assert.Equal("invalid_preimage", badPreimage.Error);

        var valid = await fixture.Service.AuthorizeAsync(fixture.Settings, fixture.Decision, "POST", "/research",
            fixture.BodyHash, header, default);
        Assert.True(valid.Authorized);

        var replay = await fixture.Service.AuthorizeAsync(fixture.Settings, fixture.Decision, "POST", "/research",
            fixture.BodyHash, header, default);
        Assert.False(replay.Authorized);
        Assert.Equal("credential_exhausted", replay.Error);
    }

    [Fact]
    public async Task Multi_use_credential_decrements_to_zero_without_exceeding_allowance()
    {
        await using var fixture = await Fixture.Create(maximumUses: 2);
        var issued = await fixture.Service.CreateOrReuseChallengeAsync(fixture.Project, fixture.Settings,
            fixture.Decision, "POST", "/research", "caller", "request-1", fixture.BodyHash, default);
        var header = $"L402 {issued.Challenge.Macaroon}:{Convert.ToHexString(fixture.Preimage)}";
        Assert.True((await fixture.Service.AuthorizeAsync(fixture.Settings, fixture.Decision, "POST", "/research", fixture.BodyHash, header, default)).Authorized);
        Assert.True((await fixture.Service.AuthorizeAsync(fixture.Settings, fixture.Decision, "POST", "/research", fixture.BodyHash, header, default)).Authorized);
        Assert.False((await fixture.Service.AuthorizeAsync(fixture.Settings, fixture.Decision, "POST", "/research", fixture.BodyHash, header, default)).Authorized);
        var challenge = await fixture.Db.MeterPaymentChallenges.AsNoTracking().SingleAsync();
        Assert.Equal(0, challenge.RemainingUses);
        Assert.Equal(MeterChallengeStatuses.Exhausted, challenge.Status);
    }

    [Fact]
    public async Task Repeated_unpaid_request_in_window_reuses_challenge()
    {
        await using var fixture = await Fixture.Create(maximumUses: 1);
        var first = await fixture.Service.CreateOrReuseChallengeAsync(fixture.Project, fixture.Settings,
            fixture.Decision, "POST", "/research", "caller", "request-1", fixture.BodyHash, default);
        var second = await fixture.Service.CreateOrReuseChallengeAsync(fixture.Project, fixture.Settings,
            fixture.Decision, "POST", "/research", "caller", "request-2", fixture.BodyHash, default);
        Assert.Equal(first.Challenge.Id, second.Challenge.Id);
        Assert.True(second.Reused);
        Assert.Equal(1, fixture.Provider.CreateCalls);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public required LiveAuthDbContext Db { get; init; }
        public required MeterPaymentService Service { get; init; }
        public required FakeProvider Provider { get; init; }
        public required Project Project { get; init; }
        public required MeterProjectSettings Settings { get; init; }
        public required MeterRouteDecision Decision { get; init; }
        public required byte[] Preimage { get; init; }
        public string BodyHash { get; } = Convert.ToHexString(SHA256.HashData("body"u8.ToArray())).ToLowerInvariant();

        private Fixture(SqliteConnection connection) => _connection = connection;

        public static async Task<Fixture> Create(int maximumUses)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new LiveAuthDbContext(new DbContextOptionsBuilder<LiveAuthDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var developer = new Developer { Id = Guid.NewGuid(), Email = $"meter-{Guid.NewGuid():N}@test.invalid" };
            var project = new Project { Id = Guid.NewGuid(), DeveloperId = developer.Id, Developer = developer,
                Name = "Meter", PublicKey = "la_pk_" + Guid.NewGuid().ToString("N"), SecretKeyHash = "hash" };
            var merchant = new MerchantLightningConnection { Id = Guid.NewGuid(), ProjectId = project.Id,
                Project = project, RestUrl = "https://lnd.invalid", EncryptedMacaroon = "unused" };
            var settings = new MeterProjectSettings { ProjectId = project.Id, Project = project,
                Environment = MeterEnvironments.Test, LightningConnectionId = merchant.Id, LightningConnection = merchant };
            var rule = new MeterRouteRule { Id = Guid.NewGuid(), ProjectId = project.Id, Project = project,
                HttpMethod = "POST", PathPattern = "/research", PriceSats = 500, BindRequestBody = true,
                MaximumCredentialUses = maximumUses };
            if (maximumUses > 1) rule.BindRequestBody = false;
            db.AddRange(developer, project, merchant, settings, rule);
            await db.SaveChangesAsync();
            var preimage = RandomNumberGenerator.GetBytes(32);
            var provider = new FakeProvider(preimage);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["LiveAuth:PowHmacSecret"] = "payment-tests-secret", ["Jwt:SigningKey"] = "credential-tests-secret" }).Build();
            var credentials = new MeterCredentialService(config);
            var factory = new LightningInvoiceProviderFactory(new[] { provider });
            var service = new MeterPaymentService(db, factory, credentials, config);
            var decision = new MeterRouteDecision(rule, rule.PathPattern, rule.PriceSats, false);
            return new Fixture(connection) { Db = db, Service = service, Provider = provider, Project = project,
                Settings = settings, Decision = decision, Preimage = preimage };
        }

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class FakeProvider : ILightningInvoiceProvider
    {
        private readonly byte[] _preimage;
        public int CreateCalls { get; private set; }
        public string ProviderType => "LND_REST";
        public FakeProvider(byte[] preimage) => _preimage = preimage;
        public Task<MeterInvoice> CreateInvoiceAsync(MerchantLightningConnection connection, long amountSats, string memo, TimeSpan expiry, CancellationToken ct)
        { CreateCalls++; return Task.FromResult(new MeterInvoice(Convert.ToHexString(SHA256.HashData(_preimage)).ToLowerInvariant(), "lntb500n1test", amountSats, DateTime.UtcNow.Add(expiry))); }
        public Task<MeterInvoiceStatus> LookupInvoiceAsync(MerchantLightningConnection connection, string paymentHash, CancellationToken ct)
            => Task.FromResult(new MeterInvoiceStatus(true, DateTime.UtcNow));
        public Task<MeterLightningConnectionStatus> ValidateConnectionAsync(MerchantLightningConnection connection, CancellationToken ct)
            => Task.FromResult(new MeterLightningConnectionStatus(true, "mock", "test", null));
    }
}
