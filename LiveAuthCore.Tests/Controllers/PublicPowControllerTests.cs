using System.Net;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/public/pow/* endpoints (Proof-of-Work challenge and verification).
/// </summary>
public class PublicPowControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public PublicPowControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetChallenge_ValidRequest_ReturnsChallenge()
    {
        var project = await SeedTestProject();
        using var request = CreateChallengeRequest(project.PublicKey!);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var challenge = await response.Content.ReadFromJsonAsync<ChallengeResponse>();
        Assert.NotNull(challenge);
        Assert.Equal(project.PublicKey, challenge.ProjectPublicKey);
        Assert.NotEmpty(challenge.ChallengeHex);
        Assert.NotEmpty(challenge.TargetHex);
        Assert.InRange(challenge.DifficultyBits, 16, 24);
        Assert.True(challenge.ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.NotEmpty(challenge.Sig);
    }

    [Fact]
    public async Task GetChallenge_PostMethod_ReturnsMethodNotAllowed()
    {
        var project = await SeedTestProject();
        _client.DefaultRequestHeaders.Add("X-LW-Public", project.PublicKey);

        var response = await _client.PostAsJsonAsync("/api/public/pow/challenge", new { });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task GetChallenge_InvalidPublicKey_ReturnsUnauthorized()
    {
        using var request = CreateChallengeRequest($"la_pk_missing_{Guid.NewGuid():N}");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyPow_InvalidChallenge_ReturnsNotVerified()
    {
        var project = await SeedTestProject();
        _client.DefaultRequestHeaders.Add("X-LW-Public", project.PublicKey);

        var response = await _client.PostAsJsonAsync("/api/public/pow/verify", new
        {
            challengeHex = "abc123",
            nonce = 1L,
            hashHex = new string('0', 64),
            expiresAtUnix = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
            difficultyBits = 16,
            sig = "bad-signature"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<VerifyResponse>();
        Assert.NotNull(result);
        Assert.False(result.Verified);
    }

    [Fact]
    public async Task VerifyPow_MissingFields_ReturnsBadRequest()
    {
        var project = await SeedTestProject();
        _client.DefaultRequestHeaders.Add("X-LW-Public", project.PublicKey);

        var response = await _client.PostAsJsonAsync("/api/public/pow/verify", new
        {
            challengeHex = "",
            nonce = 0L,
            hashHex = "",
            expiresAtUnix = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
            difficultyBits = 16,
            sig = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetChallenge_MultipleRequests_ReturnsUniqueChallenges()
    {
        var project = await SeedTestProject();
        var challenges = new List<string>();

        for (var i = 0; i < 5; i++)
        {
            using var request = CreateChallengeRequest(project.PublicKey!);
            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var challenge = await response.Content.ReadFromJsonAsync<ChallengeResponse>();
            Assert.NotNull(challenge);
            challenges.Add(challenge.ChallengeHex);
        }

        Assert.Equal(5, challenges.Distinct().Count());
    }

    [Fact]
    public async Task PublicChallenge_WithProjectApiKey_RecordsAuthEventForRequestingProject()
    {
        var (project, publicKey) = await SeedTestProjectWithApiKey();
        using var request = CreateChallengeRequest(publicKey);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var challenge = await response.Content.ReadFromJsonAsync<ChallengeResponse>();
        Assert.NotNull(challenge);
        Assert.Equal(project.PublicKey, challenge.ProjectPublicKey);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var authEvent = db.AuthEvents
            .SingleOrDefault(e =>
                e.EventType == AuthEventType.PowChallengeIssued &&
                e.ProjectId == project.Id);

        Assert.NotNull(authEvent);
        Assert.NotEqual(Guid.Parse("00000000-0000-0000-0000-000000000002"), authEvent.ProjectId);
    }

    private static HttpRequestMessage CreateChallengeRequest(string publicKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/public/pow/challenge");
        request.Headers.Add("X-LW-Public", publicKey);
        return request;
    }

    private async Task<Project> SeedTestProject()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var developer = new Developer
        {
            Id = Guid.NewGuid(),
            Email = $"test-{Guid.NewGuid():N}@liveauth.app",
            CreatedAt = DateTime.UtcNow
        };

        var project = new Project
        {
            Id = Guid.NewGuid(),
            DeveloperId = developer.Id,
            Name = "Test Project",
            PublicKey = $"la_pk_project_{Guid.NewGuid():N}",
            SecretKeyHash = "unused-in-public-pow-tests",
            Plan = "free",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Developers.Add(developer);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        return project;
    }

    private async Task<(Project Project, string PublicKey)> SeedTestProjectWithApiKey()
    {
        var project = await SeedTestProject();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var publicKey = $"la_pk_key_{Guid.NewGuid():N}";

        db.ProjectApiKeys.Add(new ProjectApiKey
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Label = "Regression key",
            PublicKey = publicKey,
            SecretKeyHash = "unused-in-public-pow-tests",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });

        await db.SaveChangesAsync();
        return (project, publicKey);
    }

    private record ChallengeResponse(string ProjectPublicKey, string ChallengeHex, string TargetHex, int DifficultyBits, long ExpiresAtUnix, string Sig);
    private record VerifyResponse(bool Verified, string? Token, string? Fallback);
}
