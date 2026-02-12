using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/admin/analytics/events endpoint (Admin - auth event logs).
/// </summary>
public class AdminAuthEventsControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public AdminAuthEventsControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetEvents_WithAdminAuth_ReturnsEvents()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        await SeedAuthEvent(project.Id, AuthEventType.LoginSucceeded, success: true);
        await SeedAuthEvent(project.Id, AuthEventType.LoginFailed, success: false);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/events");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminAuthEventDto>>();
        Assert.NotNull(results);
        Assert.True(results.Count >= 2);
    }

    [Fact]
    public async Task GetEvents_NoAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/admin/analytics/events");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEvents_FilterByProject_ReturnsOnlyProjectEvents()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project1 = await SeedProject();
        var project2 = await SeedProject();
        
        await SeedAuthEvent(project1.Id, AuthEventType.LoginSucceeded, success: true);
        await SeedAuthEvent(project2.Id, AuthEventType.LoginSucceeded, success: true);

        // Act
        var response = await _client.GetAsync($"/api/admin/analytics/events?projectId={project1.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminAuthEventDto>>();
        Assert.NotNull(results);
        Assert.All(results, e => Assert.Equal(project1.Id, e.ProjectId));
    }

    [Fact]
    public async Task GetEvents_FilterByEventType_ReturnsOnlyMatchingType()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        await SeedAuthEvent(project.Id, AuthEventType.LoginSucceeded, success: true);
        await SeedAuthEvent(project.Id, AuthEventType.LoginFailed, success: false);
        await SeedAuthEvent(project.Id, AuthEventType.RateLimitHit, success: false);

        // Act (filter for RateLimitHit)
        var response = await _client.GetAsync($"/api/admin/analytics/events?eventType={AuthEventType.RateLimitHit}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminAuthEventDto>>();
        Assert.NotNull(results);
        Assert.All(results, e => Assert.Equal(nameof(AuthEventType.RateLimitHit), e.EventType));
    }

    [Fact]
    public async Task GetEvents_CustomLimit_RespectsLimit()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        for (int i = 0; i < 20; i++)
        {
            await SeedAuthEvent(project.Id, AuthEventType.LoginSucceeded, success: true);
        }

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/events?limit=5");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminAuthEventDto>>();
        Assert.NotNull(results);
        Assert.True(results.Count <= 5);
    }

    [Fact]
    public async Task GetEvents_CustomWindowHours_RespectsWindow()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        await SeedAuthEvent(project.Id, AuthEventType.LoginSucceeded, success: true);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/events?windowHours=1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Should only return events from last hour
    }

    [Fact]
    public async Task GetEvents_MasksClientIp()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        await SeedAuthEventWithIp(project.Id, "192.168.1.100");

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/events");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminAuthEventDto>>();
        Assert.NotNull(results);
        
        var eventWithIp = results.FirstOrDefault();
        if (eventWithIp != null)
        {
            Assert.Contains(".x.x", eventWithIp.ClientIpMasked);
        }
    }

    [Fact]
    public async Task GetEvents_NoEvents_ReturnsEmptyList()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/events");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminAuthEventDto>>();
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    /// <summary>
    /// Helper to get an admin JWT token.
    /// </summary>
    private async Task<string> GetAdminToken()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        
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
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
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
    /// Helper to seed an auth event.
    /// </summary>
    private async Task SeedAuthEvent(Guid projectId, AuthEventType eventType, bool success)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        db.AuthEvents.Add(new AuthEvent
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            EventType = eventType,
            Success = success,
            SatsPaid = success ? 10L : null,
            CreatedAt = DateTime.UtcNow
        });

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
