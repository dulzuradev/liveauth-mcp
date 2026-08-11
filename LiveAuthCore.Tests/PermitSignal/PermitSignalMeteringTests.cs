using System.Security.Claims;
using System.Text.Json;
using LiveAuthCore.Controllers.PermitSignal;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Data.Entities.Mcp;
using LiveAuthCore.Models.PermitSignal;
using LiveAuthCore.Services;
using LiveAuthCore.Services.PermitSignal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LiveAuthCore.Tests.PermitSignal;

public sealed class PermitSignalMeteringTests
{
    [Fact]
    public async Task Successful_call_uses_existing_revenue_fee_and_receipt_records_once()
    {
        await using var fixture = await MeterFixture.CreateAsync(maxBudget: 100);
        var first = await fixture.Service.ChargeSuccessfulCallAsync(fixture.Principal,
            fixture.Tool.Slug, 10, "request-1", "trace-1", new Dictionary<string, object?> { ["resultCount"] = 2 }, default);
        var repeated = await fixture.Service.ChargeSuccessfulCallAsync(fixture.Principal,
            fixture.Tool.Slug, 10, "request-1", "trace-2", new Dictionary<string, object?> { ["resultCount"] = 2 }, default);

        Assert.True(first.Authorized);
        Assert.NotNull(first.Receipt);
        Assert.Equal(10, first.PriceSats);
        Assert.Equal(first.RevenueEventId, repeated.RevenueEventId);
        Assert.Equal(1, await fixture.Db.McpToolRevenueEvents.CountAsync(item => item.Status == "Charged"));
        var token = await fixture.Db.McpGateTokens.AsNoTracking().SingleAsync();
        Assert.Equal(1, token.CallsUsed);
        Assert.Equal(10, token.SatsUsed);
    }

    [Fact]
    public async Task Budget_denial_is_a_denied_event_not_paid_usage()
    {
        await using var fixture = await MeterFixture.CreateAsync(maxBudget: 5);
        var result = await fixture.Service.ChargeSuccessfulCallAsync(fixture.Principal,
            fixture.Tool.Slug, 10, null, "trace-denied", new Dictionary<string, object?>(), default);

        Assert.False(result.Authorized);
        Assert.Equal("budget_exceeded", result.Reason);
        Assert.Equal(0, (await fixture.Db.McpGateTokens.AsNoTracking().SingleAsync()).CallsUsed);
        Assert.Equal("Denied", (await fixture.Db.McpToolRevenueEvents.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancelled_reservation_refunds_usage_and_is_not_charged()
    {
        await using var fixture = await MeterFixture.CreateAsync(maxBudget: 100);
        var reserved = await fixture.SharedMeter.ReserveCallAsync(fixture.Principal,
            fixture.Tool.Slug, 10, "broadcast-attempt", "trace-reserve", "Bitcoin Agent Gateway",
            new Dictionary<string, object?> { ["phase"] = "preflight_accepted" }, default);

        Assert.True(reserved.Authorized);
        Assert.NotNull(reserved.ReservationId);
        Assert.Equal("Reserved", (await fixture.Db.McpToolRevenueEvents.AsNoTracking().SingleAsync()).Status);

        await fixture.SharedMeter.CancelReservationAsync(reserved.ReservationId!.Value,
            "node_unavailable", default);

        var token = await fixture.Db.McpGateTokens.AsNoTracking().SingleAsync();
        Assert.Equal(0, token.CallsUsed);
        Assert.Equal(0, token.SatsUsed);
        Assert.Equal("Cancelled", (await fixture.Db.McpToolRevenueEvents.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Tool_price_configuration_seeds_registered_mcp_prices()
    {
        await using var fixture = await PermitSignalTestFixture.CreateAsync();
        var options = new PermitSignalOptions();
        options.Tools.SearchProjects.PriceSats = 7;
        options.SeedDemoData = false;
        var config = Config();
        var bootstrap = new PermitSignalBootstrapper(fixture.Db, PermitSignalTestFixture.Options(options), config,
            new PermitCategoryClassifier(), new AddressNormalizer());

        await bootstrap.SeedAsync();

        var tool = await fixture.Db.McpTools.SingleAsync(item => item.Slug == "permitsignal-search-projects");
        Assert.Equal(7, tool.DefaultCostSats);
        Assert.Equal(7, tool.MinCostSats);
        Assert.Equal(7, tool.MaxCostSats);
    }

    [Fact]
    public async Task Internal_tool_failure_does_not_invoke_metering()
    {
        var meter = new TrackingMeter();
        var controller = new PermitSignalMcpController(new ThrowingQuery(), meter,
            PermitSignalTestFixture.Options(), NullLogger<PermitSignalMcpController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        using var doc = JsonDocument.Parse("""{"name":"search_projects","arguments":{"location":"Austin, TX"}}""");
        var request = new McpJsonRpcRequest
        {
            Jsonrpc = "2.0", Method = "tools/call", Id = JsonDocument.Parse("1").RootElement.Clone(),
            Params = doc.RootElement.Clone()
        };

        var response = await controller.Handle(request, default);

        Assert.IsType<OkObjectResult>(response);
        Assert.Equal(0, meter.Calls);
    }

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:SigningKey"] = "permit-signal-receipt-signing-key-32-chars",
        ["LiveAuth:DemoProjectId"] = Guid.NewGuid().ToString(),
        ["LightningAuthFees:McpPaidToolFeeBps"] = "500",
        ["LightningAuthFees:McpPaidToolMinimumFeeSats"] = "1"
    }).Build();

    private sealed class MeterFixture : IAsyncDisposable
    {
        private readonly PermitSignalTestFixture _fixture;
        public LiveAuthCore.Data.LiveAuthDbContext Db => _fixture.Db;
        public required PermitSignalMeteringService Service { get; init; }
        public required McpToolMeteringService SharedMeter { get; init; }
        public required ClaimsPrincipal Principal { get; init; }
        public required McpTool Tool { get; init; }

        private MeterFixture(PermitSignalTestFixture fixture) => _fixture = fixture;

        public static async Task<MeterFixture> CreateAsync(long maxBudget)
        {
            var fixture = await PermitSignalTestFixture.CreateAsync();
            var developer = new Developer { Email = $"permit-{Guid.NewGuid():N}@test.invalid" };
            var project = new Project
            {
                Developer = developer, DeveloperId = developer.Id, Name = "PermitSignal payer",
                PublicKey = "la_pk_" + Guid.NewGuid().ToString("N"), SecretKeyHash = "hash", IsActive = true
            };
            var session = new McpGateSession { ProjectId = project.Id, Status = "confirmed", SatsPerCallAtStart = 1 };
            var token = new McpGateToken
            {
                ProjectId = project.Id, SessionId = session.Id, JwtId = Guid.NewGuid().ToString("N"),
                MaxSatsPerDay = maxBudget, ExpiresAt = DateTime.UtcNow.AddMinutes(10), Status = "active"
            };
            var tool = new McpTool
            {
                Name = "PermitSignal Find Opportunities", Slug = "permitsignal-find-opportunities",
                Description = "test", Status = "Active", DefaultCostSats = 10, MinCostSats = 10, MaxCostSats = 10
            };
            fixture.Db.AddRange(developer, project, session, token, tool);
            await fixture.Db.SaveChangesAsync();
            var configuration = Config();
            var sharedMeter = new McpToolMeteringService(fixture.Db,
                new LightningFeeSettingsService(fixture.Db, configuration), new McpReceiptService(configuration),
                new WebhookService(fixture.Db), NullLogger<McpToolMeteringService>.Instance);
            var service = new PermitSignalMeteringService(sharedMeter);
            var identity = new ClaimsIdentity(new[]
            {
                new Claim("projectId", project.Id.ToString()), new Claim("jti", token.JwtId),
                new Claim("sub", "test-agent"), new Claim(ClaimTypes.Role, "McpClient")
            }, "test");
            return new MeterFixture(fixture)
            {
                Service = service,
                SharedMeter = sharedMeter,
                Principal = new ClaimsPrincipal(identity),
                Tool = tool
            };
        }

        public ValueTask DisposeAsync() => _fixture.DisposeAsync();
    }

    private sealed class TrackingMeter : IPermitSignalMeteringService
    {
        public int Calls { get; private set; }
        public Task<PermitSignalMeterResult> ChargeSuccessfulCallAsync(ClaimsPrincipal caller, string toolSlug,
            int configuredPriceSats, string? idempotencyKey, string requestId,
            IReadOnlyDictionary<string, object?> metadata, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new PermitSignalMeterResult(true, null, configuredPriceSats, 1, configuredPriceSats, null, null));
        }
    }

    private sealed class ThrowingQuery : IPermitQueryService
    {
        public Task<SearchProjectsResponse> SearchAsync(SearchProjectsRequest request, CancellationToken ct)
            => throw new InvalidOperationException("simulated database failure");
        public Task<FindOpportunitiesResponse> FindOpportunitiesAsync(FindOpportunitiesRequest request, CancellationToken ct)
            => throw new InvalidOperationException();
        public Task<ProjectAnalysis?> AnalyzeProjectAsync(AnalyzeProjectRequest request, CancellationToken ct)
            => throw new InvalidOperationException();
        public Task<PropertyHistoryResponse> PropertyHistoryAsync(PropertyHistoryRequest request, CancellationToken ct)
            => throw new InvalidOperationException();
    }
}
