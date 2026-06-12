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

public class DeveloperMcpToolsControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private const string TestJwtKey = "test-jwt-signing-key-that-is-at-least-32-bytes-long";
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public DeveloperMcpToolsControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateTool_WithOwnedProject_ReturnsRegisteredTool()
    {
        var seed = await SeedDeveloperProjectAsync();
        Authorize(seed.DeveloperId);

        var response = await _client.PostAsJsonAsync("/api/dev/mcp-tools", new
        {
            projectId = seed.ProjectId,
            name = "Paid Research Tool",
            slug = $"paid-research-{Guid.NewGuid():N}",
            description = "Searches an internal corpus.",
            visibility = "Private",
            status = "Draft",
            defaultCostSats = 3,
            minCostSats = 1,
            maxCostSats = 10
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<McpToolResponse>();
        body.Should().NotBeNull();
        body!.ProjectId.Should().Be(seed.ProjectId);
        body.DeveloperId.Should().Be(seed.DeveloperId);
        body.DefaultCostSats.Should().Be(3);
        body.Status.Should().Be("Draft");
    }

    [Fact]
    public async Task UpdateAndDeleteTool_RequiresDeveloperOwnership()
    {
        var owner = await SeedDeveloperProjectAsync();
        var otherDeveloperId = Guid.NewGuid();
        Authorize(owner.DeveloperId);

        var create = await _client.PostAsJsonAsync("/api/dev/mcp-tools", new
        {
            projectId = owner.ProjectId,
            name = "Editable Tool",
            slug = $"editable-{Guid.NewGuid():N}",
            description = "Original description",
            visibility = "Private",
            status = "Draft",
            defaultCostSats = 2,
            minCostSats = 1,
            maxCostSats = 8
        });
        var tool = (await create.Content.ReadFromJsonAsync<McpToolResponse>())!;

        Authorize(otherDeveloperId);
        var denied = await _client.PatchAsJsonAsync($"/api/dev/mcp-tools/{tool.Id}", new
        {
            name = "Not mine"
        });
        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);

        Authorize(owner.DeveloperId);
        var update = await _client.PatchAsJsonAsync($"/api/dev/mcp-tools/{tool.Id}", new
        {
            status = "Active",
            visibility = "Unlisted",
            defaultCostSats = 4,
            minCostSats = 1,
            maxCostSats = 12
        });

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<McpToolResponse>();
        updated!.Status.Should().Be("Active");
        updated.Visibility.Should().Be("Unlisted");
        updated.DefaultCostSats.Should().Be(4);

        var delete = await _client.DeleteAsync($"/api/dev/mcp-tools/{tool.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getDeleted = await _client.GetAsync($"/api/dev/mcp-tools/{tool.Id}");
        getDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PaidStarterKitFlow_RegistersToolChargesCallAndShowsRevenue()
    {
        var seed = await SeedDeveloperProjectAsync();
        Authorize(seed.DeveloperId);

        var create = await _client.PostAsJsonAsync("/api/dev/mcp-tools", new
        {
            projectId = seed.ProjectId,
            name = "Acceptance Paid Tool",
            slug = $"acceptance-{Guid.NewGuid():N}",
            description = "End-to-end paid MCP acceptance tool",
            visibility = "Unlisted",
            status = "Active",
            defaultCostSats = 7,
            minCostSats = 1,
            maxCostSats = 20
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var tool = (await create.Content.ReadFromJsonAsync<McpToolResponse>())!;

        var jwtId = Guid.NewGuid().ToString("N");
        await SeedMcpSessionAsync(seed.ProjectId, jwtId);

        var charge = new HttpRequestMessage(HttpMethod.Post, $"/api/mcp/tools/{tool.Id}/charge")
        {
            Content = JsonContent.Create(new
            {
                toolMethodName = "acceptance_paid_tool",
                callCostSats = 7,
                idempotencyKey = $"acceptance-{Guid.NewGuid():N}",
                metadata = new { operation = "acceptance" }
            })
        };
        charge.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateMcpJwt(seed.ProjectId, jwtId));

        var chargeResponse = await _client.SendAsync(charge);

        chargeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var chargeBody = await chargeResponse.Content.ReadFromJsonAsync<McpChargeResponse>();
        chargeBody.Should().NotBeNull();
        chargeBody!.Status.Should().Be("ok");
        chargeBody.GrossSats.Should().Be(7);
        chargeBody.PlatformFeeSats.Should().Be(1);
        chargeBody.NetSats.Should().Be(6);
        chargeBody.RevenueEventId.Should().NotBeNull();

        Authorize(seed.DeveloperId);
        var summary = await _client.GetFromJsonAsync<McpRevenueSummaryResponse>(
            $"/api/dev/mcp-tools/{tool.Id}/revenue?windowHours=24");

        summary.Should().NotBeNull();
        summary!.Calls.Should().Be(1);
        summary.GrossSats.Should().Be(7);
        summary.PlatformFeeSats.Should().Be(1);
        summary.NetSats.Should().Be(6);

        var events = await _client.GetFromJsonAsync<McpRevenueEventsResponse>(
            $"/api/dev/mcp-tools/{tool.Id}/revenue/events?limit=10");

        events.Should().NotBeNull();
        events!.Events.Should().ContainSingle(e =>
            e.Id == chargeBody.RevenueEventId &&
            e.ToolMethodName == "acceptance_paid_tool" &&
            e.GrossSats == 7);

        var overview = await _client.GetFromJsonAsync<McpRevenueOverviewResponse>(
            $"/api/dev/mcp-tools/revenue?projectId={seed.ProjectId}&windowHours=24");

        overview.Should().NotBeNull();
        overview!.PaidCalls.Should().Be(1);
        overview.GrossSats.Should().Be(7);
        overview.PlatformFeeSats.Should().Be(1);
        overview.NetSats.Should().Be(6);
        overview.TopTools.Should().ContainSingle(t =>
            t.ToolId == tool.Id &&
            t.Calls == 1 &&
            t.GrossSats == 7);
    }

    private async Task<(Guid DeveloperId, Guid ProjectId)> SeedDeveloperProjectAsync()
    {
        var developerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

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
            Name = "MCP tool project",
            PublicKey = $"la_pk_{projectId:N}",
            SecretKeyHash = $"hash_{projectId:N}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return (developerId, projectId);
    }

    private async Task SeedMcpSessionAsync(Guid projectId, string jwtId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var sessionId = Guid.NewGuid();

        db.McpGateSessions.Add(new McpGateSession
        {
            Id = sessionId,
            ProjectId = projectId,
            SatsPerCallAtStart = 7,
            Status = "confirmed",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });

        db.McpGateTokens.Add(new McpGateToken
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            SessionId = sessionId,
            JwtId = jwtId,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            MaxSatsPerDay = 100,
            MaxCallsPerMinute = 60,
            DayWindowStart = DateTime.UtcNow.Date,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private void Authorize(Guid developerId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt(developerId));
    }

    private static string CreateJwt(Guid developerId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("userId", developerId.ToString()),
                new Claim(JwtRegisteredClaimNames.Sub, developerId.ToString())
            }),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = credentials
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    private static string CreateMcpJwt(Guid projectId, string jwtId)
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

    private sealed record McpToolResponse(
        Guid Id,
        Guid? DeveloperId,
        Guid? ProjectId,
        string Name,
        string Slug,
        string Description,
        string Status,
        string Visibility,
        int DefaultCostSats,
        int MinCostSats,
        int MaxCostSats);

    private sealed record McpChargeResponse(
        string Status,
        long CallsUsed,
        long SatsUsed,
        int? GrossSats,
        int? PlatformFeeSats,
        int? NetSats,
        Guid? RevenueEventId);

    private sealed record McpRevenueSummaryResponse(
        Guid ToolId,
        int WindowHours,
        long Calls,
        long GrossSats,
        long PlatformFeeSats,
        long NetSats);

    private sealed record McpRevenueEventsResponse(
        Guid ToolId,
        IReadOnlyList<McpRevenueEventResponse> Events);

    private sealed record McpRevenueEventResponse(
        Guid Id,
        string ToolMethodName,
        int GrossSats,
        int PlatformFeeSats,
        int NetSats);

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
