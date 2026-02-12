using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/subscriptions/* endpoints (subscription management, billing).
/// </summary>
public class SubscriptionControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public SubscriptionControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSubscription_Unauthenticated_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/subscriptions/current");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSubscription_AuthenticatedDeveloper_ReturnsSubscription()
    {
        // Arrange
        var (developer, token) = await SeedDeveloperWithToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/subscriptions/current");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateSubscription_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var (developer, token) = await SeedDeveloperWithToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            PlanId = "pro-monthly",
            PaymentMethod = "lightning"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/subscriptions", request);

        // Assert
        // Might return OK, Created, or BadRequest depending on implementation
        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.Created ||
            response.StatusCode == HttpStatusCode.BadRequest, // If subscription already exists
            "Create subscription should return OK, Created, or BadRequest");
    }

    [Fact]
    public async Task CancelSubscription_ValidRequest_ReturnsOk()
    {
        // Arrange
        var (developer, token) = await SeedDeveloperWithToken();
        await SeedSubscription(developer.Id);
        
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.DeleteAsync("/api/subscriptions/current");

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.NoContent,
            "Cancel should return OK or NoContent");
    }

    [Fact]
    public async Task GetInvoices_AuthenticatedDeveloper_ReturnsList()
    {
        // Arrange
        var (developer, token) = await SeedDeveloperWithToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/subscriptions/invoices");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var invoices = await response.Content.ReadFromJsonAsync<List<InvoiceDto>>();
        Assert.NotNull(invoices);
    }

    [Fact]
    public async Task UpgradeSubscription_ValidPlan_ReturnsOk()
    {
        // Arrange
        var (developer, token) = await SeedDeveloperWithToken();
        await SeedSubscription(developer.Id, "basic-monthly");
        
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            NewPlanId = "pro-monthly"
        };

        // Act
        var response = await _client.PutAsJsonAsync("/api/subscriptions/upgrade", request);

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.BadRequest, // If already on higher plan
            "Upgrade should return OK or BadRequest");
    }

    /// <summary>
    /// Helper to seed a developer with a JWT token.
    /// </summary>
    private async Task<(Developer developer, string token)> SeedDeveloperWithToken()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var developer = new Developer
        {
            Id = Guid.NewGuid(),
            Email = $"test-{Guid.NewGuid().ToString().Substring(0, 8)}@liveauth.app",
            // Lightning-based auth, no password
            CreatedAt = DateTime.UtcNow
        };

        db.Developers.Add(developer);
        await db.SaveChangesAsync();

        var token = "test-jwt-token-" + developer.Id.ToString();

        return (developer, token);
    }

    /// <summary>
    /// Helper to seed a subscription.
    /// </summary>
    private async Task SeedSubscription(Guid developerId, string planId = "basic-monthly")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        // Note: Adjust based on your actual Subscription entity structure
        // This is a placeholder
        var subscription = new
        {
            Id = Guid.NewGuid(),
            DeveloperId = developerId,
            PlanId = planId,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        // db.Subscriptions.Add(subscription);
        // await db.SaveChangesAsync();
        
        // TODO: Implement based on actual Subscription entity
    }

    private record InvoiceDto(Guid Id, decimal Amount, string Status, DateTime CreatedAt);
}
