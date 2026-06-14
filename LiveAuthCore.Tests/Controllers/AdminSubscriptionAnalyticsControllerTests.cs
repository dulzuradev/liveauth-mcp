using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/admin/analytics/subscriptions endpoint (Admin - subscription analytics).
/// </summary>
public class AdminSubscriptionAnalyticsControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public AdminSubscriptionAnalyticsControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSubscriptions_WithAdminAuth_ReturnsAll()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        await SeedSubscription(project.Id, isPaid: true, expiresInDays: 30);
        await SeedSubscription(project.Id, isPaid: false, expiresInDays: 5);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/subscriptions");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminSubscriptionDto>>();
        Assert.NotNull(results);
        Assert.True(results.Count >= 2);
    }

    [Fact]
    public async Task GetSubscriptions_NoAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/admin/analytics/subscriptions");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptions_FilterActive_ReturnsOnlyActive()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        var activeSub = await SeedSubscription(project.Id, isPaid: true, expiresInDays: 30);
        await SeedSubscription(project.Id, isPaid: true, expiresInDays: -5); // expired
        await SeedSubscription(project.Id, isPaid: false, expiresInDays: 5); // pending

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/subscriptions?status=active");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminSubscriptionDto>>();
        Assert.NotNull(results);
        Assert.All(results, r =>
        {
            Assert.True(r.IsPaid);
            Assert.True(r.ExpiresAt > DateTime.UtcNow);
        });
    }

    [Fact]
    public async Task GetSubscriptions_FilterExpired_ReturnsOnlyExpired()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        await SeedSubscription(project.Id, isPaid: true, expiresInDays: 30);
        var expiredSub = await SeedSubscription(project.Id, isPaid: true, expiresInDays: -5);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/subscriptions?status=expired");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminSubscriptionDto>>();
        Assert.NotNull(results);
        Assert.All(results, r =>
        {
            Assert.True(r.IsPaid);
            Assert.True(r.ExpiresAt <= DateTime.UtcNow);
        });
    }

    [Fact]
    public async Task GetSubscriptions_FilterPending_ReturnsOnlyPending()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var project = await SeedProject();
        await SeedSubscription(project.Id, isPaid: true, expiresInDays: 30);
        var pendingSub = await SeedSubscription(project.Id, isPaid: false, expiresInDays: 5);

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/subscriptions?status=pending");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminSubscriptionDto>>();
        Assert.NotNull(results);
        Assert.All(results, r =>
        {
            Assert.False(r.IsPaid);
            Assert.True(r.ExpiresAt > DateTime.UtcNow);
        });
    }

    [Fact]
    public async Task GetSubscriptions_NoSubscriptions_ReturnsEmptyList()
    {
        // Arrange
        var adminToken = await GetAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        await ClearSubscriptions();

        // Act
        var response = await _client.GetAsync("/api/admin/analytics/subscriptions");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<List<AdminSubscriptionDto>>();
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    /// <summary>
    /// Helper to get an admin JWT token.
    /// </summary>
    private Task<string> GetAdminToken()
        => Task.FromResult(TestAuth.GenerateAdminJwt(_factory));

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
            Name = $"Project {Guid.NewGuid().ToString("N")[..8]}",
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
    /// Helper to seed a billing subscription.
    /// </summary>
    private async Task<BillingSubscription> SeedSubscription(Guid projectId, bool isPaid, int expiresInDays)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var now = DateTime.UtcNow;
        var subscription = new BillingSubscription
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Plan = "pro",
            AmountSats = 100000L,
            InvoiceBolt11 = $"lnbc{Guid.NewGuid():N}",
            InvoiceRHash = Guid.NewGuid().ToString("N"),
            IsPaid = isPaid,
            PaidAt = isPaid ? now : null,
            CreatedAt = now,
            ExpiresAt = now.AddDays(expiresInDays)
        };

        db.BillingSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return subscription;
    }

    private async Task ClearSubscriptions()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        db.BillingSubscriptions.RemoveRange(db.BillingSubscriptions);
        await db.SaveChangesAsync();
    }

    private record AdminSubscriptionDto
    {
        public Guid SubscriptionId { get; init; }
        public Guid ProjectId { get; init; }
        public string ProjectName { get; init; } = "";
        public string Plan { get; init; } = "";
        public bool IsPaid { get; init; }
        public long AmountSats { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? PaidAt { get; init; }
        public DateTime ExpiresAt { get; init; }
    }
}
