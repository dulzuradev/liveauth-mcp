using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models.CostShield;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

public sealed class DeveloperCostShieldAnalyticsControllerTests
    : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public DeveloperCostShieldAnalyticsControllerTests(
        LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Overview_ReturnsCostShieldMetricsAndEstimatedValues()
    {
        var seed = await SeedProjectWithEventsAsync();
        Authorize(seed.DeveloperId);

        var response = await _client.GetAsync(
            $"/api/dev/projects/{seed.ProjectId}/costshield/overview?windowHours=24");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var overview = await response.Content
            .ReadFromJsonAsync<CostShieldOverviewResponse>();
        overview.Should().NotBeNull();
        overview!.ProtectedActionCount.Should().Be(1);
        overview.EnabledActionCount.Should().Be(1);
        overview.ChallengesIssued.Should().Be(2);
        overview.ChallengesCompleted.Should().Be(1);
        overview.AuthorizationsIssued.Should().Be(1);
        overview.ProtectedRequests.Should().Be(1);
        overview.RequestsDenied.Should().Be(1);
        overview.RateLimitedRequests.Should().Be(1);
        overview.EstimatedProviderCostAuthorized.Should().Be(0.02m);
        overview.EstimatedCostAvoided.Should().Be(0.02m);
        overview.ChallengeSuccessRate.Should().Be(50);
        overview.EstimatedValues.Should().BeTrue();
        overview.TopActions.Should().ContainSingle(action =>
            action.ProtectedActionId == seed.ActionId &&
            action.EstimatedCostAvoided == 0.02m);
    }

    [Fact]
    public async Task Events_ReturnsMaskedSourcesAndActionContext()
    {
        var seed = await SeedProjectWithEventsAsync();
        Authorize(seed.DeveloperId);

        var response = await _client.GetAsync(
            $"/api/dev/projects/{seed.ProjectId}/costshield/events?limit=10&offset=0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var events = await response.Content
            .ReadFromJsonAsync<CostShieldEventListResponse>();
        events.Should().NotBeNull();
        events!.Total.Should().Be(6);
        events.Events.Should().HaveCount(6);
        events.Events.Should().OnlyContain(item =>
            item.ProtectedActionId == seed.ActionId &&
            item.Action == "ai.generate_image" &&
            item.Source == "source_abcdef0123");
    }

    [Fact]
    public async Task ProjectEndpoints_OtherDeveloperCannotReadCostShieldData()
    {
        var owner = await SeedProjectWithEventsAsync();
        var attacker = await SeedProjectWithEventsAsync();
        Authorize(attacker.DeveloperId);

        var overview = await _client.GetAsync(
            $"/api/dev/projects/{owner.ProjectId}/costshield/overview");
        var events = await _client.GetAsync(
            $"/api/dev/projects/{owner.ProjectId}/costshield/events");

        overview.StatusCode.Should().Be(HttpStatusCode.NotFound);
        events.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Overview_InvalidWindow_ReturnsBadRequest()
    {
        var seed = await SeedProjectWithEventsAsync();
        Authorize(seed.DeveloperId);

        var response = await _client.GetAsync(
            $"/api/dev/projects/{seed.ProjectId}/costshield/overview?windowHours=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<CostShieldAnalyticsSeed> SeedProjectWithEventsAsync()
    {
        var developerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        db.Developers.Add(new Developer
        {
            Id = developerId,
            Email = $"{developerId:N}@analytics.costshield.test",
            CreatedAt = now
        });
        db.Projects.Add(new Project
        {
            Id = projectId,
            DeveloperId = developerId,
            Name = "CostShield analytics project",
            PublicKey = $"la_pk_{projectId:N}",
            SecretKeyHash = $"hash_{projectId:N}",
            IsActive = true,
            Plan = "free",
            Environment = "TEST",
            CreatedAt = now
        });
        db.ProtectedActions.Add(new ProtectedAction
        {
            Id = actionId,
            ProjectId = projectId,
            Environment = "TEST",
            Name = "ai.generate_image",
            DisplayName = "Generate Image",
            Description = "Protect image generation.",
            IsEnabled = true,
            BaseDifficulty = 8,
            SuspiciousDifficulty = 10,
            MaximumDifficulty = 12,
            AnonymousRequestLimit = 10,
            AnonymousLimitWindowSeconds = 3600,
            TokenLifetimeSeconds = 120,
            EstimatedCostPerExecution = 0.02m,
            CreatedAt = now,
            UpdatedAt = now
        });

        var eventTypes = new[]
        {
            AuthEventType.CostShieldChallengeIssued,
            AuthEventType.CostShieldChallengeIssued,
            AuthEventType.CostShieldChallengeCompleted,
            AuthEventType.CostShieldAuthorizationIssued,
            AuthEventType.CostShieldAuthorizationConsumed,
            AuthEventType.CostShieldRateLimited
        };
        for (var index = 0; index < eventTypes.Length; index++)
        {
            var eventType = eventTypes[index];
            db.AuthEvents.Add(new AuthEvent
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                ProtectedActionId = actionId,
                EventType = eventType,
                Environment = "TEST",
                VerificationMethod = eventType is
                    AuthEventType.CostShieldChallengeCompleted or
                    AuthEventType.CostShieldAuthorizationIssued or
                    AuthEventType.CostShieldAuthorizationConsumed
                    ? "pow"
                    : null,
                CreatedAt = now.AddMinutes(-index),
                ClientIp = null,
                IpAddressHash = "abcdef0123456789",
                ClientContextHash = "context-hash",
                Success = eventType != AuthEventType.CostShieldRateLimited,
                Reason = eventType.ToString(),
                EstimatedCostProtected =
                    eventType == AuthEventType.CostShieldAuthorizationConsumed
                        ? 0.02m
                        : null
            });
        }

        await db.SaveChangesAsync();
        return new CostShieldAnalyticsSeed(developerId, projectId, actionId);
    }

    private void Authorize(Guid developerId)
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                TestAuth.GenerateDeveloperJwt(_factory, developerId));
    }

    private sealed record CostShieldAnalyticsSeed(
        Guid DeveloperId,
        Guid ProjectId,
        Guid ActionId);
}
