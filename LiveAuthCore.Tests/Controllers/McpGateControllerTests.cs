using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        var revenueEventId = body.RevenueEventId.GetValueOrDefault();
        body.Receipt.Should().NotBeNull();
        body.Receipt!.Version.Should().Be("mcp-call-receipt-v1");
        body.Receipt.SignatureAlgorithm.Should().Be("HMAC-SHA256");
        body.Receipt.KeyId.Should().Be("liveauth-mcp-receipt-v1");
        body.Receipt.Body.RevenueEventId.Should().Be(revenueEventId);
        body.Receipt.Body.McpToolId.Should().Be(seed.ToolId);
        body.Receipt.Body.ToolName.Should().Be(seed.ToolName);
        body.Receipt.Body.ToolSlug.Should().StartWith("test-web-fetch-");
        body.Receipt.Body.ToolMethodName.Should().Be("web_fetch");
        body.Receipt.Body.McpGateTokenId.Should().Be(seed.TokenId);
        body.Receipt.Body.McpGateSessionId.Should().Be(seed.SessionId);
        body.Receipt.Body.PayingProjectId.Should().Be(seed.ProjectId);
        body.Receipt.Body.AgentId.Should().Be("agent-1");
        body.Receipt.Body.GrossSats.Should().Be(5);
        body.Receipt.Body.PlatformFeeSats.Should().Be(1);
        body.Receipt.Body.NetSats.Should().Be(4);
        body.Receipt.Body.IdempotencyKey.Should().Be("call-1");
        VerifyReceiptSignature(body.Receipt);

        using var payload = DecodeReceiptPayload(body.Receipt);
        payload.RootElement.GetProperty("version").GetString().Should().Be("mcp-call-receipt-v1");
        payload.RootElement.GetProperty("receiptId").GetString().Should().Be($"mcp_receipt_{revenueEventId:N}");
        payload.RootElement.GetProperty("revenueEventId").GetString().Should().Be(revenueEventId.ToString("D"));
        payload.RootElement.GetProperty("idempotencyKey").GetString().Should().Be("call-1");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var revenueEvent = await db.McpToolRevenueEvents.FindAsync(revenueEventId);
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
    public async Task Charge_WithToolName_UsesRegisteredDefaultPrice_WhenCostOmitted()
    {
        var seed = await SeedChargeStateAsync(toolDefaultCostSats: 9);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/mcp/charge")
        {
            Content = JsonContent.Create(new
            {
                toolName = seed.ToolSlug,
                idempotencyKey = "priced-by-tool"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", seed.Jwt);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<McpChargeResponseBody>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("ok");
        body.CallsUsed.Should().Be(1);
        body.SatsUsed.Should().Be(9);
        body.GrossSats.Should().Be(9);
        body.NetSats.Should().Be(8);
        body.ToolId.Should().Be(seed.ToolId);
        body.ToolName.Should().Be(seed.ToolName);
        body.ToolSlug.Should().Be(seed.ToolSlug);
        body.Receipt.Should().NotBeNull();
        body.Receipt!.Body.ToolName.Should().Be(seed.ToolName);
        body.Receipt.Body.ToolSlug.Should().Be(seed.ToolSlug);
        body.Receipt.Body.GrossSats.Should().Be(9);
    }

    [Fact]
    public async Task ChargeTool_EnqueuesPaidCallWebhook_WhenToolWebhookConfigured()
    {
        const string webhookUrl = "https://seller.example.com/liveauth/mcp";
        var seed = await SeedChargeStateAsync(toolWebhookUrl: webhookUrl);

        var body = await SendToolChargeAsync(seed, new
        {
            toolMethodName = "web_fetch",
            callCostSats = 5,
            idempotencyKey = "webhook-call",
            agentId = "agent-webhook",
            metadata = new { urlHost = "example.com" }
        });

        body.Status.Should().Be("ok");
        body.RevenueEventId.Should().NotBeNull();
        var revenueEventId = body.RevenueEventId.GetValueOrDefault();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var webhook = db.WebhookEvents.Single(e =>
            e.ProjectId == seed.ProjectId &&
            e.EventType == "liveauth.mcp.tool.paid_call");
        webhook.ProjectId.Should().Be(seed.ProjectId);
        webhook.DestinationUrl.Should().Be(webhookUrl);

        using var payload = JsonDocument.Parse(webhook.PayloadJson);
        var root = payload.RootElement;
        root.GetProperty("type").GetString().Should().Be("liveauth.mcp.tool.paid_call");
        root.GetProperty("projectId").GetString().Should().Be(seed.ProjectId.ToString());
        root.GetProperty("mcpToolId").GetString().Should().Be(seed.ToolId.ToString());
        root.GetProperty("toolName").GetString().Should().Be(seed.ToolName);
        root.GetProperty("toolSlug").GetString().Should().Be(seed.ToolSlug);
        root.GetProperty("toolMethodName").GetString().Should().Be("web_fetch");
        root.GetProperty("revenueEventId").GetString().Should().Be(revenueEventId.ToString());
        root.GetProperty("grossSats").GetInt32().Should().Be(5);
        root.GetProperty("platformFeeSats").GetInt32().Should().Be(1);
        root.GetProperty("netSats").GetInt32().Should().Be(4);
        root.GetProperty("agentId").GetString().Should().Be("agent-webhook");
        root.GetProperty("metadata").GetProperty("urlHost").GetString().Should().Be("example.com");
        root.GetProperty("receipt").GetProperty("body").GetProperty("revenueEventId")
            .GetString().Should().Be(revenueEventId.ToString());
    }

    [Fact]
    public async Task ChargeTool_EnqueuesPaidCallWebhook_UsingProjectWebhookFallback()
    {
        const string webhookUrl = "https://project.example.com/hooks/liveauth";
        var seed = await SeedChargeStateAsync(projectWebhookUrl: webhookUrl);

        var body = await SendToolChargeAsync(seed, new
        {
            toolMethodName = "web_fetch",
            callCostSats = 5,
            idempotencyKey = "project-webhook-call"
        });

        body.Status.Should().Be("ok");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var webhook = db.WebhookEvents.Single(e =>
            e.ProjectId == seed.ProjectId &&
            e.EventType == "liveauth.mcp.tool.paid_call");
        webhook.ProjectId.Should().Be(seed.ProjectId);
        webhook.DestinationUrl.Should().Be(webhookUrl);
    }

    [Fact]
    public async Task Charge_WithoutToolOrCost_FallsBackToProjectGlobalPrice()
    {
        var seed = await SeedChargeStateAsync(mcpSatsPerCall: 4);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/mcp/charge")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", seed.Jwt);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<McpChargeResponseBody>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("ok");
        body.CallsUsed.Should().Be(1);
        body.SatsUsed.Should().Be(4);
        body.RevenueEventId.Should().BeNull();
        body.Receipt.Should().BeNull();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        db.McpToolRevenueEvents.Count(e => e.McpToolId == seed.ToolId).Should().Be(0);
    }

    [Fact]
    public async Task ChargeTool_ReturnsOriginalCharge_ForDuplicateIdempotencyKey()
    {
        var seed = await SeedChargeStateAsync(toolWebhookUrl: "https://seller.example.com/liveauth/mcp");
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
        second.Receipt.Should().BeEquivalentTo(first.Receipt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        db.McpToolRevenueEvents.Count(e => e.McpToolId == seed.ToolId).Should().Be(1);
        db.McpGateTokens.Single(t => t.Id == seed.TokenId).CallsUsed.Should().Be(1);
        db.McpGateTokens.Single(t => t.Id == seed.TokenId).SatsUsed.Should().Be(5);
        db.WebhookEvents.Count(e =>
            e.ProjectId == seed.ProjectId &&
            e.EventType == "liveauth.mcp.tool.paid_call").Should().Be(1);
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
        var denied = db.McpToolRevenueEvents.Single(e => e.McpToolId == seed.ToolId);
        denied.Status.Should().Be("Denied");
        denied.GrossSats.Should().Be(5);
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
        var denied = db.McpToolRevenueEvents.Single(e => e.McpToolId == seed.ToolId);
        denied.Status.Should().Be("Denied");
        denied.MetadataJson.Should().Contain("tool_inactive");
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
        body.Receipt.Should().BeNull();
    }

    private static void VerifyReceiptSignature(McpSignedReceiptResponse receipt)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestJwtKey));
        var signature = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(receipt.Payload)));
        signature.Should().Be(receipt.Signature);
    }

    private static JsonDocument DecodeReceiptPayload(McpSignedReceiptResponse receipt)
    {
        var json = Encoding.UTF8.GetString(Base64UrlDecode(receipt.Payload));
        return JsonDocument.Parse(json);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value
            .Replace('-', '+')
            .Replace('_', '/');

        switch (padded.Length % 4)
        {
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }

        return Convert.FromBase64String(padded);
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
        string toolStatus = "Active",
        int toolDefaultCostSats = 5,
        int mcpSatsPerCall = 1,
        string? projectWebhookUrl = null,
        string? toolWebhookUrl = null)
    {
        var developerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var toolName = "Test Web Fetch";
        var toolSlug = $"test-web-fetch-{toolId:N}";
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
            McpSatsPerCall = mcpSatsPerCall,
            WebhookUrl = projectWebhookUrl,
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
            Name = toolName,
            Slug = toolSlug,
            Description = "Test paid MCP tool",
            Status = toolStatus,
            Visibility = "Unlisted",
            DefaultCostSats = toolDefaultCostSats,
            MinCostSats = 1,
            MaxCostSats = 0,
            WebhookUrl = toolWebhookUrl,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        return new TestChargeSeed(projectId, sessionId, tokenId, toolId, toolName, toolSlug, CreateJwt(projectId, jwtId));
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
        string ToolName,
        string ToolSlug,
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
        string? Reason,
        McpSignedReceiptResponse? Receipt,
        Guid? ToolId,
        string? ToolName,
        string? ToolSlug);

    private sealed record McpSignedReceiptResponse(
        string Version,
        string Payload,
        string Signature,
        string SignatureAlgorithm,
        string KeyId,
        McpCallReceiptResponse Body);

    private sealed record McpCallReceiptResponse(
        string ReceiptId,
        Guid RevenueEventId,
        Guid McpToolId,
        string ToolName,
        string ToolSlug,
        string ToolMethodName,
        Guid? McpGateTokenId,
        Guid? McpGateSessionId,
        Guid? PayingProjectId,
        string? AgentId,
        int GrossSats,
        int PlatformFeeSats,
        int NetSats,
        int FeeBasisPoints,
        string Status,
        string? IdempotencyKey,
        string? RequestId,
        DateTime CreatedAt);
}
