using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveAuthCore.Bitcoin;
using LiveAuthCore.Bitcoin.Configuration;
using LiveAuthCore.Bitcoin.Rpc;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace LiveAuthCore.Tests.Bitcoin;

public sealed class BitcoinGatewayMcpIntegrationTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private const string TestJwtKey = "test-jwt-signing-key-that-is-at-least-32-bytes-long";
    private static readonly Guid DemoProjectId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private readonly LiveAuthWebApplicationFactory _factory;

    public BitcoinGatewayMcpIntegrationTests(LiveAuthWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Authenticated_client_can_run_all_tools_with_signed_idempotent_broadcast()
    {
        var jwtId = await SeedTokenAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt(jwtId));
        using var baselineScope = _factory.Services.CreateScope();
        var baselineNode = (TestBitcoinNodeClient)baselineScope.ServiceProvider.GetRequiredService<IBitcoinNodeClient>();
        var preflightCallsBefore = baselineNode.PreflightCalls;
        var sendCallsBefore = baselineNode.SendCalls;

        var list = await client.PostAsJsonAsync("/api/bitcoin/mcp", new
        {
            jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { }
        });
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using (var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync()))
        {
            var tools = listJson.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
            Assert.Equal(5, tools.Length);
            var preflight = tools.Single(item => item.GetProperty("name").GetString() == BitcoinGatewayTools.PreflightTransaction);
            var broadcast = tools.Single(item => item.GetProperty("name").GetString() == BitcoinGatewayTools.BroadcastTransaction);
            Assert.Contains("NEVER broadcasts", preflight.GetProperty("description").GetString());
            Assert.Contains("CAN broadcast", broadcast.GetProperty("description").GetString());
        }

        var raw = BitcoinTestTransactions.CreateRaw();
        using var feeJson = await SuccessfulToolResultAsync(await CallToolAsync(client,
            BitcoinGatewayTools.FeeEstimates, new { }, "fees-safe-retry"));
        Assert.Equal(5, feeJson.RootElement.GetProperty("result").GetProperty("structuredContent")
            .GetProperty("estimates").GetArrayLength());

        using var mempoolJson = await SuccessfulToolResultAsync(await CallToolAsync(client,
            BitcoinGatewayTools.MempoolSummary, new { }, "mempool-safe-retry"));
        Assert.Equal(48_321, mempoolJson.RootElement.GetProperty("result").GetProperty("structuredContent")
            .GetProperty("transactionCount").GetInt64());

        using var preflightJson = await SuccessfulToolResultAsync(await CallToolAsync(client,
            BitcoinGatewayTools.PreflightTransaction, new { rawTransaction = raw }, "preflight-safe-retry"));
        var preflightStructured = preflightJson.RootElement.GetProperty("result").GetProperty("structuredContent");
        Assert.True(preflightStructured.GetProperty("accepted").GetBoolean());
        Assert.Equal("observation", preflightStructured.GetProperty("receipt").GetProperty("body")
            .GetProperty("attestation").GetProperty("kind").GetString());
        VerifyReceipt(preflightStructured.GetProperty("receipt"));

        var first = await BroadcastAsync(client, raw, "safe-retry");
        var replay = await BroadcastAsync(client, raw, "safe-retry");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync();
        using var firstJson = JsonDocument.Parse(firstBody);
        Assert.True(firstJson.RootElement.TryGetProperty("result", out var firstResult), firstBody);
        var structured = firstResult.GetProperty("structuredContent");
        Assert.True(structured.GetProperty("broadcasted").GetBoolean());
        Assert.Equal("execution", structured.GetProperty("receipt").GetProperty("body")
            .GetProperty("attestation").GetProperty("kind").GetString());
        VerifyReceipt(structured.GetProperty("receipt"));
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.True(replayJson.RootElement.GetProperty("result").GetProperty("_meta")
            .GetProperty("liveauth").GetProperty("idempotentReplay").GetBoolean());

        var txid = structured.GetProperty("txid").GetString()!;
        using var statusJson = await SuccessfulToolResultAsync(await CallToolAsync(client,
            BitcoinGatewayTools.TransactionStatus, new { txid }, "status-safe-retry"));
        var statusStructured = statusJson.RootElement.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal("mempool", statusStructured.GetProperty("status").GetString());
        Assert.Equal("observation", statusStructured.GetProperty("receipt").GetProperty("body")
            .GetProperty("attestation").GetProperty("kind").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var node = (TestBitcoinNodeClient)scope.ServiceProvider.GetRequiredService<IBitcoinNodeClient>();
        Assert.Equal(2, node.PreflightCalls - preflightCallsBefore);
        Assert.Equal(1, node.SendCalls - sendCallsBefore);
        Assert.Equal(1, db.McpToolRevenueEvents.Count(item =>
            item.ToolMethodName == BitcoinGatewayTools.BroadcastTransaction && item.Status == "Charged" &&
            item.IdempotencyKey != null && item.IdempotencyKey.Contains("safe-retry")));
        var operation = db.BitcoinGatewayOperations.Single(item => item.IdempotencyKey == "key:safe-retry");
        Assert.DoesNotContain(raw, operation.ResultJson ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(raw, db.McpToolRevenueEvents.Single(item =>
            item.ToolMethodName == BitcoinGatewayTools.BroadcastTransaction &&
            item.IdempotencyKey != null && item.IdempotencyKey.Contains("safe-retry")).MetadataJson ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        var prices = db.McpTools.Where(item => item.Slug.StartsWith("bitcoin_") && item.RemovedAt == null)
            .ToDictionary(item => item.Slug, item => item.DefaultCostSats);
        Assert.Equal(3, prices[BitcoinGatewayTools.FeeEstimates]);
        Assert.Equal(3, prices[BitcoinGatewayTools.MempoolSummary]);
        Assert.Equal(5, prices[BitcoinGatewayTools.PreflightTransaction]);
        Assert.Equal(25, prices[BitcoinGatewayTools.BroadcastTransaction]);
        Assert.Equal(3, prices[BitcoinGatewayTools.TransactionStatus]);
    }

    [Fact]
    public async Task Preflight_rejection_prevents_send_and_broadcast_charge()
    {
        var jwtId = await SeedTokenAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt(jwtId));
        using var beforeScope = _factory.Services.CreateScope();
        var node = (TestBitcoinNodeClient)beforeScope.ServiceProvider.GetRequiredService<IBitcoinNodeClient>();
        var sendsBefore = node.SendCalls;
        node.PreflightResult = new BitcoinNodePreflightResult(false, null, null, 141,
            null, null, "bad-txns-inputs-missingorspent", null);

        try
        {
            var idempotencyKey = $"rejected-{Guid.NewGuid():N}";
            var response = await BroadcastAsync(client, BitcoinTestTransactions.CreateRaw(), idempotencyKey);
            using var json = await SuccessfulToolResultAsync(response);
            var value = json.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.False(value.GetProperty("accepted").GetBoolean());
            Assert.False(value.GetProperty("broadcasted").GetBoolean());
            Assert.Equal(BitcoinErrorCodes.MissingInput, value.GetProperty("rejectCode").GetString());
            Assert.Equal(sendsBefore, node.SendCalls);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
            Assert.DoesNotContain(db.McpToolRevenueEvents, item =>
                item.ToolMethodName == BitcoinGatewayTools.BroadcastTransaction && item.Status == "Charged" &&
                item.IdempotencyKey != null && item.IdempotencyKey.Contains(idempotencyKey));
        }
        finally
        {
            node.PreflightResult = null;
        }
    }

    [Fact]
    public async Task Authentication_failure_does_not_create_a_charge()
    {
        using var beforeScope = _factory.Services.CreateScope();
        var beforeDb = beforeScope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var before = beforeDb.McpToolRevenueEvents.Count(item => item.ToolMethodName.StartsWith("bitcoin_"));

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/bitcoin/fees");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var afterScope = _factory.Services.CreateScope();
        var afterDb = afterScope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        Assert.Equal(before, afterDb.McpToolRevenueEvents.Count(item => item.ToolMethodName.StartsWith("bitcoin_")));
    }

    [Fact]
    public async Task Timeout_after_node_acceptance_is_recovered_and_charged_once()
    {
        var jwtId = await SeedTokenAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt(jwtId));
        using var beforeScope = _factory.Services.CreateScope();
        var node = (TestBitcoinNodeClient)beforeScope.ServiceProvider.GetRequiredService<IBitcoinNodeClient>();
        var sendsBefore = node.SendCalls;
        node.SendExceptionAfterAcceptance = new BitcoinGatewayException(BitcoinErrorCodes.RpcTimeout,
            "simulated timeout", true, StatusCodes.Status503ServiceUnavailable);
        var raw = BitcoinTestTransactions.CreateRaw(42);
        var idempotencyKey = $"ambiguous-{Guid.NewGuid():N}";

        try
        {
            using var first = await SuccessfulToolResultAsync(await BroadcastAsync(client, raw, idempotencyKey));
            var value = first.RootElement.GetProperty("result").GetProperty("structuredContent");
            Assert.True(value.GetProperty("accepted").GetBoolean());
            Assert.True(value.GetProperty("recovered").GetBoolean());
            Assert.Equal("execution", value.GetProperty("receipt").GetProperty("body")
                .GetProperty("attestation").GetProperty("kind").GetString());

            using var replay = await SuccessfulToolResultAsync(await BroadcastAsync(client, raw, idempotencyKey));
            Assert.True(replay.RootElement.GetProperty("result").GetProperty("_meta")
                .GetProperty("liveauth").GetProperty("idempotentReplay").GetBoolean());
            Assert.Equal(1, node.SendCalls - sendsBefore);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
            Assert.Equal(1, db.McpToolRevenueEvents.Count(item =>
                item.ToolMethodName == BitcoinGatewayTools.BroadcastTransaction && item.Status == "Charged" &&
                item.IdempotencyKey != null && item.IdempotencyKey.Contains(idempotencyKey)));
        }
        finally
        {
            node.SendExceptionAfterAcceptance = null;
        }
    }

    [Fact]
    public async Task Node_failure_before_acceptance_rolls_back_the_reserved_charge()
    {
        var jwtId = await SeedTokenAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt(jwtId));
        using var beforeScope = _factory.Services.CreateScope();
        var node = (TestBitcoinNodeClient)beforeScope.ServiceProvider.GetRequiredService<IBitcoinNodeClient>();
        var chargedBefore = beforeScope.ServiceProvider.GetRequiredService<LiveAuthDbContext>()
            .McpToolRevenueEvents.Count(item =>
                item.ToolMethodName == BitcoinGatewayTools.BroadcastTransaction && item.Status == "Charged");
        node.SendExceptionBeforeAcceptance = new BitcoinGatewayException(BitcoinErrorCodes.NodeUnavailable,
            "simulated node outage", true, StatusCodes.Status503ServiceUnavailable);

        try
        {
            var response = await BroadcastAsync(client, BitcoinTestTransactions.CreateRaw(43),
                $"unavailable-{Guid.NewGuid():N}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = json.RootElement.GetProperty("error").GetProperty("data");
            Assert.Equal(BitcoinErrorCodes.NodeUnavailable, data.GetProperty("code").GetString());
            Assert.True(data.GetProperty("retryable").GetBoolean());

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
            Assert.Equal(chargedBefore, db.McpToolRevenueEvents.Count(item =>
                item.ToolMethodName == BitcoinGatewayTools.BroadcastTransaction && item.Status == "Charged"));
            Assert.Contains(db.McpToolRevenueEvents, item =>
                item.ToolMethodName == BitcoinGatewayTools.BroadcastTransaction && item.Status == "Cancelled");
        }
        finally
        {
            node.SendExceptionBeforeAcceptance = null;
        }
    }

    private static async Task<HttpResponseMessage> BroadcastAsync(HttpClient client, string raw, string idempotencyKey)
        => await CallToolAsync(client, BitcoinGatewayTools.BroadcastTransaction,
            new { rawTransaction = raw }, idempotencyKey);

    private static async Task<HttpResponseMessage> CallToolAsync(
        HttpClient client,
        string name,
        object arguments,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bitcoin/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0", id = 2, method = "tools/call",
                @params = new
                {
                    name,
                    arguments
                }
            })
        };
        request.Headers.TryAddWithoutValidation("X-LiveAuth-Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> SuccessfulToolResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("result", out _), body);
        return json;
    }

    private static void VerifyReceipt(JsonElement receipt)
    {
        var payload = receipt.GetProperty("payload").GetString()!;
        var expected = Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(TestJwtKey),
            Encoding.UTF8.GetBytes(payload)));
        Assert.Equal(expected, receipt.GetProperty("signature").GetString());

        var attestation = receipt.GetProperty("body").GetProperty("attestation");
        var claims = attestation.GetProperty("canonicalClaims").GetString()!;
        var claimsHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(claims))).ToLowerInvariant();
        Assert.Equal(claimsHash, attestation.GetProperty("claimsSha256").GetString());
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<string> SeedTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var session = new McpGateSession { ProjectId = DemoProjectId, Status = "confirmed", SatsPerCallAtStart = 1 };
        var jwtId = Guid.NewGuid().ToString("N");
        db.McpGateSessions.Add(session);
        db.McpGateTokens.Add(new McpGateToken
        {
            ProjectId = DemoProjectId,
            SessionId = session.Id,
            JwtId = jwtId,
            Status = "active",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            MaxSatsPerDay = 100,
            MaxCallsPerMinute = 60
        });
        await db.SaveChangesAsync();
        return jwtId;
    }

    private static string CreateJwt(string jwtId)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("projectId", DemoProjectId.ToString()),
                new Claim("jti", jwtId),
                new Claim(ClaimTypes.Role, "McpClient")
            }),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey)), SecurityAlgorithms.HmacSha256)
        };
        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }
}
