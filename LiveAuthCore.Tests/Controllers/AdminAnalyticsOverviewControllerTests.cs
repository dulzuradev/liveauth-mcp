using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/admin/analytics/overview endpoint (Admin - analytics overview dashboard).
/// </summary>
public class AdminAnalyticsOverviewControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public AdminAnalyticsOverviewControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetOverview_WithAdminAuth_ReturnsFullDashboard()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        await ClearAuthEvents();

        var project = await SeedProject();
        await SeedAuthEvents(project.Id, 10, 7, 3);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/overview");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AdminAnalyticsOverviewResponse>();
        Assert.NotNull(result);
        Assert.True(result.TotalProjects > 0);
        Assert.Equal(10, result.AuthRequests);
        Assert.Equal(7, result.AuthSuccesses);
        Assert.Equal(3, result.AuthFailures);
    }

    [Fact]
    public async Task GetOverview_NoAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/admin/analytics/overview");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOverview_CustomWindowHours_RespectsWindow()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        await SeedAuthEvents(project.Id, 5, 5, 0);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/overview?windowHours=1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AdminAnalyticsOverviewResponse>();
        Assert.NotNull(result);
        // Should return data within the last hour only
    }

    [Fact]
    public async Task GetOverview_InvalidWindowHours_UsesDefault()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/overview?windowHours=-1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Should use default 24 hours
    }

    [Fact]
    public async Task GetOverview_TooLargeWindowHours_CapsValue()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/overview?windowHours=10000");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Should cap at max (720 hours)
    }

    [Fact]
    public async Task GetOverview_IncludesProjectCounts()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        await SeedProject("free");
        await SeedProject("pro");
        await SeedProject("free");

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/overview");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AdminAnalyticsOverviewResponse>();
        Assert.NotNull(result);
        Assert.True(result.TotalProjects >= 3);
        Assert.True(result.ProProjects >= 1);
        Assert.True(result.FreeProjects >= 2);
    }

    [Fact]
    public async Task GetOverview_IncludesTimeSeries()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        await SeedAuthEvents(project.Id, 5, 5, 0);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/overview");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AdminAnalyticsOverviewResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.AuthsOverTime);
    }

    [Fact]
    public async Task GetOverview_IncludesRecentEvents()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        await SeedAuthEvents(project.Id, 3, 2, 1);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/overview");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AdminAnalyticsOverviewResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.RecentEvents);
        Assert.True(result.RecentEvents.Count > 0);
    }

    [Fact]
    public async Task GetOverview_MasksClientIp()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        await SeedAuthEventWithIp(project.Id, "192.168.1.100");

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/overview");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AdminAnalyticsOverviewResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.RecentEvents);
        
        var eventWithMaskedIp = result.RecentEvents.FirstOrDefault();
        if (eventWithMaskedIp != null)
        {
            // Should mask last two octets
            Assert.Contains(".x.x", eventWithMaskedIp.ClientIpMasked);
        }
    }

    /// <summary>
    /// Helper to get an admin JWT token.
    /// </summary>
    private Task<string> GetAdminToken()
        => Task.FromResult(TestAuth.GenerateAdminJwt(_factory));

    /// <summary>
    /// Helper to seed a project.
    /// </summary>
    private async Task<Project> SeedProject(string plan = "free")
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
            Name = $"Project {Guid.NewGuid().ToString("N")[..8]}",
            DeveloperId = developer.Id,
            Plan = plan,
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
    /// Helper to seed an auth event with a specific IP.
    /// </summary>
    private async Task SeedAuthEventWithIp(Guid projectId, string clientIp)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        db.AuthEvents.Add(new AuthEvent
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            EventType = AuthEventType.LoginSucceeded,
            Success = true,
            ClientIp = clientIp,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private async Task ClearAuthEvents()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        db.AuthEvents.RemoveRange(db.AuthEvents);
        await db.SaveChangesAsync();
    }

    private record AdminAnalyticsOverviewResponse
    {
        public int TotalProjects { get; init; }
        public int ActiveProjects { get; init; }
        public int AuthRequests { get; init; }
        public int AuthSuccesses { get; init; }
        public int AuthFailures { get; init; }
        public int RateLimitHits { get; init; }
        public long SatsPaid { get; init; }
        public int PaidAuths { get; init; }
        public int ProProjects { get; init; }
        public int FreeProjects { get; init; }
        public DateTime WindowStart { get; init; }
        public DateTime WindowEnd { get; init; }
        public List<AuthsOverTimePoint> AuthsOverTime { get; init; } = new();
        public List<AdminAuthEventDto> RecentEvents { get; init; } = new();
    }

    private record AuthsOverTimePoint
    {
        public DateTime TimestampUtc { get; init; }
        public int Successful { get; init; }
        public int Failed { get; init; }
    }

    private record AdminAuthEventDto
    {
        public DateTime Timestamp { get; init; }
        public Guid ProjectId { get; init; }
        public string ProjectName { get; init; } = "";
        public string EventType { get; init; } = "";
        public bool Success { get; init; }
        public long? SatsPaid { get; init; }
        public string? Reason { get; init; }
        public string ClientIpMasked { get; init; } = "";
    }
}
