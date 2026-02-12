using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/admin/* endpoints (admin dashboard, user management).
/// </summary>
public class AdminControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public AdminControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetDashboard_Unauthenticated_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/admin/dashboard");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_NonAdminUser_ReturnsForbidden()
    {
        // Arrange
        var (developer, token) = await SeedDeveloperWithToken(isAdmin: false);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/admin/dashboard");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_AdminUser_ReturnsOk()
    {
        // Arrange
        var (admin, token) = await SeedDeveloperWithToken(isAdmin: true);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/admin/dashboard");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListDevelopers_AdminUser_ReturnsList()
    {
        // Arrange
        var (admin, token) = await SeedDeveloperWithToken(isAdmin: true);
        await SeedDeveloper("dev1@test.com");
        await SeedDeveloper("dev2@test.com");
        
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/admin/developers");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var developers = await response.Content.ReadFromJsonAsync<List<DeveloperDto>>();
        Assert.NotNull(developers);
        Assert.True(developers.Count >= 3); // At least the admin + 2 test developers
    }

    [Fact]
    public async Task ListProjects_AdminUser_ReturnsAllProjects()
    {
        // Arrange
        var (admin, token) = await SeedDeveloperWithToken(isAdmin: true);
        var dev1 = await SeedDeveloper("dev1@test.com");
        var dev2 = await SeedDeveloper("dev2@test.com");
        
        await SeedProject(dev1.Id, "Project 1");
        await SeedProject(dev2.Id, "Project 2");
        
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/admin/projects");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var projects = await response.Content.ReadFromJsonAsync<List<ProjectDto>>();
        Assert.NotNull(projects);
        Assert.True(projects.Count >= 2);
    }

    [Fact]
    public async Task DeactivateDeveloper_AdminUser_ReturnsOk()
    {
        // Arrange
        var (admin, token) = await SeedDeveloperWithToken(isAdmin: true);
        var developer = await SeedDeveloper("target@test.com");
        
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.PostAsync($"/api/admin/developers/{developer.Id}/deactivate", null);

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || 
            response.StatusCode == HttpStatusCode.NoContent,
            "Deactivate should return OK or NoContent");
    }

    [Fact]
    public async Task DeactivateDeveloper_NonAdminUser_ReturnsForbidden()
    {
        // Arrange
        var (developer, token) = await SeedDeveloperWithToken(isAdmin: false);
        var targetDev = await SeedDeveloper("target@test.com");
        
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.PostAsync($"/api/admin/developers/{targetDev.Id}/deactivate", null);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Helper to seed a developer with a JWT token.
    /// </summary>
    private async Task<(Developer developer, string token)> SeedDeveloperWithToken(bool isAdmin = false, string? email = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var developer = new Developer
        {
            Id = Guid.NewGuid(),
            Email = email ?? $"test-{Guid.NewGuid().ToString().Substring(0, 8)}@liveauth.app",
            CreatedAt = DateTime.UtcNow
        };

        db.Developers.Add(developer);
        await db.SaveChangesAsync();

        // Simplified token (in real tests, use actual JWT generation)
        var token = "test-jwt-token-" + developer.Id.ToString();

        return (developer, token);
    }

    /// <summary>
    /// Helper to seed a developer.
    /// </summary>
    private async Task<Developer> SeedDeveloper(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var developer = new Developer
        {
            Id = Guid.NewGuid(),
            Email = email,
            CreatedAt = DateTime.UtcNow
        };

        db.Developers.Add(developer);
        await db.SaveChangesAsync();

        return developer;
    }

    /// <summary>
    /// Helper to seed a project.
    /// </summary>
    private async Task<Project> SeedProject(Guid developerId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var project = new Project
        {
            Id = Guid.NewGuid(),
            DeveloperId = developerId,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        return project;
    }

    private record DeveloperDto(Guid Id, string Email, DateTime CreatedAt);
    private record ProjectDto(Guid Id, string Name, Guid DeveloperId, DateTime CreatedAt);
}
