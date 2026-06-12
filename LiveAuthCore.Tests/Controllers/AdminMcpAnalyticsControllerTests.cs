using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Data.Entities.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

public class AdminMcpAnalyticsControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private const string TestJwtKey = "test-jwt-signing-key-that-is-at-least-32-bytes-long";
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public AdminMcpAnalyticsControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetMcpRevenue_WithAdminAuth_ReturnsPaidAndTopToolStats()
    {
        var seed = await SeedMcpRevenueAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateAdminJwt());

        var response = await _client.GetAsync("/api/admin/analytics/mcp?windowHours=24&limit=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<McpRevenueOverviewResponse>();
        body.Should().NotBeNull();
        body!.PaidCalls.Should().Be(3);
        body.GrossSats.Should().Be(14);
        body.PlatformFeeSats.Should().Be(3);
        body.NetSats.Should().Be(11);
        body.DeniedCharges.Should().Be(1);
        body.TopTools.Should().NotBeEmpty();
        body.TopTools[0].ToolId.Should().Be(seed.TopToolId);
        body.TopTools[0].Calls.Should().Be(2);
        body.TopTools[0].GrossSats.Should().Be(12);
    }

    private async Task<(Guid ProjectId, Guid TopToolId)> SeedMcpRevenueAsync()
    {
        var developerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var topToolId = Guid.NewGuid();
        var otherToolId = Guid.NewGuid();

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
            Name = "Admin MCP analytics project",
            PublicKey = $"la_pk_{projectId:N}",
            SecretKeyHash = $"la_sk_{projectId:N}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        db.McpTools.AddRange(
            new McpTool
            {
                Id = topToolId,
                ProjectId = projectId,
                Name = "Top Paid Tool",
                Slug = $"top-paid-tool-{topToolId:N}",
                Description = "Top tool",
                Status = "Active",
                Visibility = "Private",
                DefaultCostSats = 5,
                MinCostSats = 1,
                MaxCostSats = 20,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new McpTool
            {
                Id = otherToolId,
                ProjectId = projectId,
                Name = "Other Paid Tool",
                Slug = $"other-paid-tool-{otherToolId:N}",
                Description = "Other tool",
                Status = "Active",
                Visibility = "Private",
                DefaultCostSats = 2,
                MinCostSats = 1,
                MaxCostSats = 20,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        db.McpToolRevenueEvents.AddRange(
            ChargedEvent(topToolId, projectId, "top_search", 5, 1, 4),
            ChargedEvent(topToolId, projectId, "top_search", 7, 1, 6),
            ChargedEvent(otherToolId, projectId, "other_lookup", 2, 1, 1),
            new McpToolRevenueEvent
            {
                McpToolId = otherToolId,
                PayingProjectId = projectId,
                ToolMethodName = "other_lookup",
                GrossSats = 2,
                PlatformFeeSats = 0,
                NetSats = 0,
                FeeBasisPoints = 0,
                Status = "Denied",
                MetadataJson = "{\"denyReason\":\"budget_exceeded\"}",
                CreatedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();
        return (projectId, topToolId);
    }

    private static McpToolRevenueEvent ChargedEvent(
        Guid toolId,
        Guid projectId,
        string methodName,
        int grossSats,
        int platformFeeSats,
        int netSats)
    {
        return new McpToolRevenueEvent
        {
            McpToolId = toolId,
            PayingProjectId = projectId,
            ToolMethodName = methodName,
            GrossSats = grossSats,
            PlatformFeeSats = platformFeeSats,
            NetSats = netSats,
            FeeBasisPoints = 500,
            Status = "Charged",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string CreateAdminJwt()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "Admin")
            }),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = credentials
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    private sealed record McpRevenueOverviewResponse(
        int WindowHours,
        long PaidCalls,
        long GrossSats,
        long PlatformFeeSats,
        long NetSats,
        long DeniedCharges,
        IReadOnlyList<McpRevenueTopToolResponse> TopTools);

    private sealed record McpRevenueTopToolResponse(
        Guid ToolId,
        string ToolName,
        string ToolSlug,
        string ToolStatus,
        long Calls,
        long GrossSats,
        long PlatformFeeSats,
        long NetSats,
        long DeniedCharges,
        double AverageGrossSatsPerCall);
}
