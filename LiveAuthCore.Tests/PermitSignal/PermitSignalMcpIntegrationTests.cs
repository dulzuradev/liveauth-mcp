using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities.Mcp;
using LiveAuthCore.Data.Entities.PermitSignal;
using LiveAuthCore.Services.PermitSignal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace LiveAuthCore.Tests.PermitSignal;

public sealed class PermitSignalMcpIntegrationTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private const string TestJwtKey = "test-jwt-signing-key-that-is-at-least-32-bytes-long";
    private static readonly Guid DemoProjectId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private readonly LiveAuthWebApplicationFactory _factory;

    public PermitSignalMcpIntegrationTests(LiveAuthWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Mcp_discovers_and_executes_paid_search_with_receipt()
    {
        var client = _factory.CreateClient();
        var jwtId = await SeedAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt(jwtId));

        var list = await client.PostAsJsonAsync("/api/permitsignal/mcp", new
        {
            jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { }
        });
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var names = listJson.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()).ToArray();
        Assert.Equal(new[] { "search_projects", "find_opportunities", "analyze_project", "property_history" }, names);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/permitsignal/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0", id = 2, method = "tools/call",
                @params = new { name = "search_projects", arguments = new { location = "Austin, TX", work_category = "Electrical", commercial_only = true } }
            })
        };
        request.Headers.TryAddWithoutValidation("X-LiveAuth-Idempotency-Key", "integration-search");
        var call = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        using var callJson = JsonDocument.Parse(await call.Content.ReadAsStringAsync());
        var result = callJson.RootElement.GetProperty("result");
        Assert.Equal(1, result.GetProperty("structuredContent").GetProperty("count").GetInt32());
        var liveauth = result.GetProperty("_meta").GetProperty("liveauth");
        Assert.True(liveauth.GetProperty("paid").GetBoolean());
        Assert.Equal(5, liveauth.GetProperty("priceSats").GetInt32());
        Assert.Equal("mcp-call-receipt-v1", liveauth.GetProperty("receipt").GetProperty("version").GetString());
    }

    private async Task<string> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        await scope.ServiceProvider.GetRequiredService<IPermitSignalBootstrapper>().SeedAsync();
        var sourceId = "mcp-integration-source";
        var source = db.PermitSources.SingleOrDefault(item => item.SourceIdentifier == sourceId);
        if (source == null)
        {
            source = new PermitSource { SourceIdentifier = sourceId, Municipality = "Austin", State = "TX", AdapterType = "Test", OfficialDatasetUrl = "https://example.invalid" };
            db.PermitSources.Add(source);
            db.PermitProjects.Add(new PermitProject
            {
                PermitSource = source, Source = sourceId, SourceRecordId = "mcp-electric-1", Municipality = "Austin", State = "TX",
                Address = "901 E 6TH ST", NormalizedAddress = "901 E 6TH ST", PermitNumber = "MCP-ELECTRIC-1",
                PermitType = "Electrical Permit", PermitSubtype = "Commercial Upgrade", Description = "600A service upgrade with switchgear",
                Status = "Issued", IssueDate = DateTime.UtcNow.AddDays(-1), EstimatedProjectValue = 420_000,
                ResidentialOrCommercial = "Commercial", WorkCategory = PermitWorkCategories.Electrical,
                RawSourceUrl = "https://example.invalid/mcp-electric-1",
                Categories = [new PermitProjectCategory { Category = PermitWorkCategories.Electrical }]
            });
        }
        var session = new McpGateSession { ProjectId = DemoProjectId, Status = "confirmed", SatsPerCallAtStart = 1 };
        var jwtId = Guid.NewGuid().ToString("N");
        db.McpGateSessions.Add(session);
        db.McpGateTokens.Add(new McpGateToken
        {
            ProjectId = DemoProjectId, SessionId = session.Id, JwtId = jwtId, Status = "active",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10), MaxSatsPerDay = 100, MaxCallsPerMinute = 60
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
                new Claim("projectId", DemoProjectId.ToString()), new Claim("jti", jwtId),
                new Claim(ClaimTypes.Role, "McpClient")
            }),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey)), SecurityAlgorithms.HmacSha256)
        };
        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }
}
