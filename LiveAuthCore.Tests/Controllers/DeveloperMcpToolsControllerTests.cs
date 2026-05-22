using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
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
}
