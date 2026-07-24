using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Models.CostShield;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

public sealed class DeveloperCostShieldActionsControllerTests
    : IClassFixture<LiveAuthWebApplicationFactory>
{
    private const string TestJwtKey = "test-jwt-signing-key-that-is-at-least-32-bytes-long";
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public DeveloperCostShieldActionsControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CrudFlow_UsesProjectRoutesAndIncrementsConfigurationVersion()
    {
        var seed = await SeedDeveloperProjectAsync();
        Authorize(seed.DeveloperId);

        var request = ValidRequest();
        request.Environment = "live";
        request.Name = "AI.Generate_Image";
        request.AllowedOrigins = new List<string> { "HTTPS://App.Example.com/" };

        var create = await _client.PostAsJsonAsync(
            $"/api/dev/projects/{seed.ProjectId}/costshield/actions",
            request);

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<ProtectedActionDto>();
        created.Should().NotBeNull();
        created!.ProjectId.Should().Be(seed.ProjectId);
        created.Environment.Should().Be("LIVE");
        created.Name.Should().Be("ai.generate_image");
        created.AllowedOrigins.Should().Equal("https://app.example.com");
        created.ConfigurationVersion.Should().Be(1);
        create.Headers.Location.Should().NotBeNull();

        var list = await _client.GetFromJsonAsync<ProtectedActionListResponse>(
            $"/api/dev/projects/{seed.ProjectId}/costshield/actions?environment=LIVE");
        list.Should().NotBeNull();
        list!.Actions.Should().ContainSingle(action => action.Id == created.Id);

        request.BaseDifficulty = 18;
        request.SuspiciousDifficulty = 21;
        var update = await _client.PutAsJsonAsync(
            $"/api/dev/projects/{seed.ProjectId}/costshield/actions/{created.Id}",
            request);

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<ProtectedActionDto>();
        updated.Should().NotBeNull();
        updated!.BaseDifficulty.Should().Be(18);
        updated.ConfigurationVersion.Should().Be(2);

        var delete = await _client.DeleteAsync(
            $"/api/dev/projects/{seed.ProjectId}/costshield/actions/{created.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getDeleted = await _client.GetAsync(
            $"/api/dev/projects/{seed.ProjectId}/costshield/actions/{created.Id}");
        getDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateAction_DuplicateNameInSameEnvironment_ReturnsConflict()
    {
        var seed = await SeedDeveloperProjectAsync();
        Authorize(seed.DeveloperId);
        var request = ValidRequest();

        var first = await _client.PostAsJsonAsync(
            $"/api/dev/projects/{seed.ProjectId}/costshield/actions",
            request);
        var duplicate = await _client.PostAsJsonAsync(
            $"/api/dev/projects/{seed.ProjectId}/costshield/actions",
            request);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        request.Environment = "LIVE";
        var liveEnvironment = await _client.PostAsJsonAsync(
            $"/api/dev/projects/{seed.ProjectId}/costshield/actions",
            request);
        liveEnvironment.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateAction_InvalidConfiguration_ReturnsValidationProblem()
    {
        var seed = await SeedDeveloperProjectAsync();
        Authorize(seed.DeveloperId);
        var request = ValidRequest();
        request.BaseDifficulty = 23;
        request.SuspiciousDifficulty = 19;
        request.MaximumDifficulty = 20;

        var response = await _client.PostAsJsonAsync(
            $"/api/dev/projects/{seed.ProjectId}/costshield/actions",
            request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("BaseDifficulty");
    }

    [Fact]
    public async Task CreateAction_FreePlanFourthAction_ReturnsPaymentRequired()
    {
        var seed = await SeedDeveloperProjectAsync();
        Authorize(seed.DeveloperId);

        for (var index = 1; index <= 3; index++)
        {
            var request = ValidRequest($"ai.generate_{index}");
            var response = await _client.PostAsJsonAsync(
                $"/api/dev/projects/{seed.ProjectId}/costshield/actions",
                request);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var fourth = await _client.PostAsJsonAsync(
            $"/api/dev/projects/{seed.ProjectId}/costshield/actions",
            ValidRequest("ai.generate_4"));

        fourth.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        var body = await fourth.Content.ReadAsStringAsync();
        body.Should().Contain("protected_action_limit_reached");
        body.Should().Contain("\"limit\":3");
    }

    [Fact]
    public async Task ProjectRoutes_OtherDeveloperCannotReadOrMutateActions()
    {
        var owner = await SeedDeveloperProjectAsync();
        var attacker = await SeedDeveloperProjectAsync();
        Authorize(owner.DeveloperId);

        var create = await _client.PostAsJsonAsync(
            $"/api/dev/projects/{owner.ProjectId}/costshield/actions",
            ValidRequest());
        var action = await create.Content.ReadFromJsonAsync<ProtectedActionDto>();
        action.Should().NotBeNull();

        Authorize(attacker.DeveloperId);

        var list = await _client.GetAsync(
            $"/api/dev/projects/{owner.ProjectId}/costshield/actions");
        var get = await _client.GetAsync(
            $"/api/dev/projects/{owner.ProjectId}/costshield/actions/{action!.Id}");
        var update = await _client.PutAsJsonAsync(
            $"/api/dev/projects/{owner.ProjectId}/costshield/actions/{action.Id}",
            ValidRequest());
        var delete = await _client.DeleteAsync(
            $"/api/dev/projects/{owner.ProjectId}/costshield/actions/{action.Id}");

        list.StatusCode.Should().Be(HttpStatusCode.NotFound);
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        update.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
            Email = $"{developerId:N}@costshield.test",
            CreatedAt = DateTime.UtcNow
        });
        db.Projects.Add(new Project
        {
            Id = projectId,
            DeveloperId = developerId,
            Name = "CostShield test project",
            PublicKey = $"la_pk_{projectId:N}",
            SecretKeyHash = $"hash_{projectId:N}",
            IsActive = true,
            Plan = "free",
            Environment = "TEST",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return (developerId, projectId);
    }

    private void Authorize(Guid developerId)
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateJwt(developerId));
    }

    private static string CreateJwt(Guid developerId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: new[]
            {
                new Claim("userId", developerId.ToString()),
                new Claim(JwtRegisteredClaimNames.Sub, developerId.ToString())
            },
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static UpsertProtectedActionRequest ValidRequest(
        string name = "ai.generate_image")
    {
        return new UpsertProtectedActionRequest
        {
            Environment = "TEST",
            Name = name,
            DisplayName = "Generate AI Image",
            Description = "Protect an expensive image generation endpoint.",
            IsEnabled = true,
            BaseDifficulty = 17,
            SuspiciousDifficulty = 20,
            MaximumDifficulty = 24,
            AnonymousRequestLimit = 5,
            AnonymousLimitWindowSeconds = 3600,
            RequireSingleUseToken = true,
            TokenLifetimeSeconds = 120,
            AllowedOrigins = new List<string> { "https://app.example.com" },
            FailureBehavior = "Deny",
            AllowLightningFallback = true,
            LightningPriceSats = 25,
            LightningFallbackMode = "RateLimitOnly",
            LightningBypassesProofOfWork = true,
            EstimatedCostPerExecution = 0.02m
        };
    }
}
