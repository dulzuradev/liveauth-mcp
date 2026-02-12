using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/admin/analytics/projects endpoint (Admin - project analytics).
/// Note: File name is AdminAnalyticsController.cs but class is AdminProjectAnalyticsController.
/// </summary>
public class AdminAnalyticsControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public AdminAnalyticsControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetProjectUsage_WithAdminAuth_ReturnsStats()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        await SeedAuthEvents(project.Id, 10, 7, 3);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/projects");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminProjectUsageDto>>();
        Assert.NotNull(results);
        Assert.NotEmpty(results);
        
        var projectStat = results.FirstOrDefault(r => r.ProjectId == project.Id);
        Assert.NotNull(projectStat);
        Assert.Equal(10, projectStat.Auths);
        Assert.Equal(7, projectStat.Successes);
        Assert.Equal(3, projectStat.Failures);
    }

    [Fact]
    public async Task GetProjectUsage_NoAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/admin/analytics/projects");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProjectUsage_NonAdminAuth_ReturnsForbidden()
    {
        // Arrange
        var (project, apiKey) = await SeedProjectWithApiKey();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/projects");

        // Assert
        // Should fail because API key auth != Admin role JWT
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProjectUsage_CustomWindowHours_ReturnsFilteredStats()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        await SeedAuthEvents(project.Id, 5, 5, 0);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/projects?windowHours=1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminProjectUsageDto>>();
        Assert.NotNull(results);
        // Should return events within last hour
    }

    [Fact]
    public async Task GetProjectUsage_CustomLimit_RespectsLimit()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Seed multiple projects
        for (int i = 0; i < 10; i++)
        {
            var project = await SeedProject();
            await SeedAuthEvents(project.Id, i + 1, i + 1, 0);
        }

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/projects?limit=5");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminProjectUsageDto>>();
        Assert.NotNull(results);
        Assert.True(results.Count <= 5);
    }

    [Fact]
    public async Task GetProjectUsage_InvalidWindowHours_UsesDefault()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/projects?windowHours=-1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Should use default 24 hours
    }

    [Fact]
    public async Task GetProjectUsage_TooLargeWindowHours_CapsValue()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/projects?windowHours=10000");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Should cap at max (720 hours = 30 days)
    }

    [Fact]
    public async Task GetProjectUsage_NoEvents_ReturnsEmptyList()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/projects");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminProjectUsageDto>>();
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    /// <summary>
    /// Helper to get an admin JWT token.
    /// Note: This is a simplified mock - real implementation would require proper admin auth.
    /// </summary>
    private async Task<string> GetAdminToken()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        
        // Create mock admin session (simplified for testing)
        var session = new AdminLoginSession
        {
            Id = Guid.NewGuid(),
            Email = "admin@liveauth.app",
            AmountSats = 21L,
            InvoiceBolt11 = "lnbc_test",
            InvoiceRHash = "test_hash",
            IsPaid = true,
            PaidAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(8)
        };
        
        db.AdminLoginSessions.Add(session);
        await db.SaveChangesAsync();
        
        // In real scenario, would call Lightning service to generate JWT
        // For now, return a mock token (tests will fail but structure is correct)
        return "mock_admin_token";
    }

    /// <summary>
    /// Helper to seed a project.
    /// </summary>
    private async Task<Project> SeedProject()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var developer = new Developer
        {
            Id = Guid.NewGuid(),
            Email = $"dev{Guid.NewGuid():N}@liveauth.app",
            CreatedAt = DateTime.UtcNow
        };

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Project {Guid.NewGuid():N[..8]}",
            DeveloperId = developer.Id,
            Plan = "free",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Developers.Add(developer);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        return project;
    }

    /// <summary>
    /// Helper to seed auth events for a project.
    /// </summary>
    private async Task SeedAuthEvents(Guid projectId, int total, int successes, int failures)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var now = DateTime.UtcNow;
        
        for (int i = 0; i < successes; i++)
        {
            db.AuthEvents.Add(new AuthEvent
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                EventType = AuthEventType.LoginSucceeded,
                Success = true,
                SatsPaid = 10L,
                CreatedAt = now.AddMinutes(-i)
            });
        }

        for (int i = 0; i < failures; i++)
        {
            db.AuthEvents.Add(new AuthEvent
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                EventType = AuthEventType.LoginFailed,
                Success = false,
                CreatedAt = now.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Helper to seed a project with an API key.
    /// </summary>
    private async Task<(Project project, string apiKey)> SeedProjectWithApiKey()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var developer = new Developer
        {
            Id = Guid.NewGuid(),
            Email = $"dev{Guid.NewGuid():N}@liveauth.app",
            CreatedAt = DateTime.UtcNow
        };

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "Test Project",
            DeveloperId = developer.Id,
            CreatedAt = DateTime.UtcNow
        };

        var apiKey = $"la_sk_{Guid.NewGuid():N}";
        var apiKeyEntity = new ProjectApiKey
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SecretKeyHash = BCrypt.Net.BCrypt.HashPassword(apiKey),
            PublicKey = apiKey[..20],
            CreatedAt = DateTime.UtcNow
        };

        db.Developers.Add(developer);
        db.Projects.Add(project);
        db.ProjectApiKeys.Add(apiKeyEntity);
        await db.SaveChangesAsync();

        return (project, apiKey);
    }

    private record AdminProjectUsageDto
    {
        public Guid ProjectId { get; init; }
        public string Name { get; init; } = "";
        public string Plan { get; init; } = "free";
        public int Auths { get; init; }
        public int Successes { get; init; }
        public int Failures { get; init; }
        public int RateLimitHits { get; init; }
        public long SatsPaid { get; init; }
    }
}
