using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using FluentAssertions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Data.Entities.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

public class McpGateControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private const string TestJwtKey = "test-jwt-signing-key-that-is-at-least-32-bytes-long";
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public McpGateControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ChargeTool_RecordsRevenueEvent_WithFeeAccounting()
    {
        var seed = await SeedChargeStateAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/mcp/tools/{seed.ToolId}/charge")
        {
            Content = JsonContent.Create(new
            {
                toolMethodName = "web_fetch",
                callCostSats = 5,
                idempotencyKey = "call-1",
                agentId = "agent-1",
                metadata = new { urlHost = "example.com" }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", seed.Jwt);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<McpChargeResponseBody>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("ok");
        body.CallsUsed.Should().Be(1);
        body.SatsUsed.Should().Be(5);
        body.GrossSats.Should().Be(5);
        body.PlatformFeeSats.Should().Be(1);
        body.NetSats.Should().Be(4);
        body.FeeBasisPoints.Should().Be(500);
        body.RevenueEventId.Should().NotBeNull();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var revenueEvent = await db.McpToolRevenueEvents.FindAsync(body.RevenueEventId.Value);
        revenueEvent.Should().NotBeNull();
        revenueEvent!.McpToolId.Should().Be(seed.ToolId);
        revenueEvent.McpGateTokenId.Should().Be(seed.TokenId);
        revenueEvent.McpGateSessionId.Should().Be(seed.SessionId);
        revenueEvent.PayingProjectId.Should().Be(seed.ProjectId);
        revenueEvent.ToolMethodName.Should().Be("web_fetch");
        revenueEvent.IdempotencyKey.Should().Be("call-1");
        revenueEvent.MetadataJson.Should().Contain("example.com");
    }

    [Fact]
    public async Task ChargeTool_ReturnsOriginalCharge_ForDuplicateIdempotencyKey()
    {
        var seed = await SeedChargeStateAsync();
        var payload = new
        {
            toolMethodName = "web_fetch",
            callCostSats = 5,
            idempotencyKey = "duplicate-call"
        };

        var first = await SendToolChargeAsync(seed, payload);
        var second = await SendToolChargeAsync(seed, payload);

        first.Status.Should().Be("ok");
        second.Status.Should().Be("ok");
        second.RevenueEventId.Should().Be(first.RevenueEventId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        db.McpToolRevenueEvents.Count(e => e.McpToolId == seed.ToolId).Should().Be(1);
        db.McpGateTokens.Single(t => t.Id == seed.TokenId).CallsUsed.Should().Be(1);
        db.McpGateTokens.Single(t => t.Id == seed.TokenId).SatsUsed.Should().Be(5);
    }

    [Fact]
    public async Task ChargeTool_Denies_WhenBudgetExceeded()
    {
        var seed = await SeedChargeStateAsync(maxSatsPerDay: 4);

        var body = await SendToolChargeAsync(seed, new
        {
            toolMethodName = "web_fetch",
            callCostSats = 5,
            idempotencyKey = "over-budget"
        });

        body.Status.Should().Be("deny");
        body.Reason.Should().Be("budget_exceeded");
        body.CallsUsed.Should().Be(0);
        body.SatsUsed.Should().Be(0);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        db.McpToolRevenueEvents.Count(e => e.McpToolId == seed.ToolId).Should().Be(0);
    }

    [Fact]
    public async Task ChargeTool_Denies_WhenToolPaused()
    {
        var seed = await SeedChargeStateAsync(toolStatus: "Paused");

        var body = await SendToolChargeAsync(seed, new
        {
            toolMethodName = "web_fetch",
            callCostSats = 5,
            idempotencyKey = "paused-tool"
        });

        body.Status.Should().Be("deny");
        body.Reason.Should().Be("tool_inactive");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        db.McpToolRevenueEvents.Count(e => e.McpToolId == seed.ToolId).Should().Be(0);
    }

    [Fact]
    public async Task ChargeTool_PreservesGenericChargeEndpoint()
    {
        var seed = await SeedChargeStateAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/mcp/charge")
        {
            Content = JsonContent.Create(new { callCostSats = 2 })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", seed.Jwt);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<McpChargeResponseBody>();
        body!.Status.Should().Be("ok");
        body.CallsUsed.Should().Be(1);
        body.SatsUsed.Should().Be(2);
        body.RevenueEventId.Should().BeNull();
    }

    private async Task<McpChargeResponseBody> SendToolChargeAsync(TestChargeSeed seed, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/mcp/tools/{seed.ToolId}/charge")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", seed.Jwt);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<McpChargeResponseBody>())!;
    }

    private async Task<TestChargeSeed> SeedChargeStateAsync(
        int maxSatsPerDay = 100,
        string toolStatus = "Active")
    {
        var developerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var jwtId = Guid.NewGuid().ToString("N");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        db.Developers.Add(new Developer
        {
            Id = developerId,
            Email = $"{developerId:N}@example.com",
            CreatedAt = DateTime.UtcNow
        });

        db.Projects.Add(new Project
        {
            Id = projectId,
            DeveloperId = developerId,
            Name = "MCP charge test",
            PublicKey = $"la_pk_{projectId:N}",
            SecretKeyHash = $"la_sk_{projectId:N}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        db.McpGateSessions.Add(new McpGateSession
        {
            Id = sessionId,
            ProjectId = projectId,
            SatsPerCallAtStart = 5,
            Status = "confirmed",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });

        db.McpGateTokens.Add(new McpGateToken
        {
            Id = tokenId,
            ProjectId = projectId,
            SessionId = sessionId,
            JwtId = jwtId,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            MaxSatsPerDay = maxSatsPerDay,
            MaxCallsPerMinute = 60,
            DayWindowStart = DateTime.UtcNow.Date,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        });

        db.McpTools.Add(new McpTool
        {
            Id = toolId,
            ProjectId = projectId,
            Name = "Test Web Fetch",
            Slug = $"test-web-fetch-{toolId:N}",
            Description = "Test paid MCP tool",
            Status = toolStatus,
            Visibility = "Unlisted",
            DefaultCostSats = 5,
            MinCostSats = 1,
            MaxCostSats = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        return new TestChargeSeed(projectId, sessionId, tokenId, toolId, CreateJwt(projectId, jwtId));
    }

    private static string CreateJwt(Guid projectId, string jwtId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("projectId", projectId.ToString()),
                new Claim("jti", jwtId),
                new Claim(ClaimTypes.Role, "McpClient")
            }),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = credentials
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    private sealed record TestChargeSeed(
        Guid ProjectId,
        Guid SessionId,
        Guid TokenId,
        Guid ToolId,
        string Jwt);

    private sealed record McpChargeResponseBody(
        string Status,
        long CallsUsed,
        long SatsUsed,
        int? GrossSats,
        int? PlatformFeeSats,
        int? NetSats,
        int? FeeBasisPoints,
        Guid? RevenueEventId,
        string? Reason);
}
