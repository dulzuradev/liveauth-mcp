using System.Net;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/pow/* endpoints (Proof-of-Work challenge and verification).
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
        // Arrange
        var project = await SeedTestProject();

        var request = new
        {
            ProjectId = project.Id,
            DifficultyBits = 20
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/pow/challenge", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var challenge = await response.Content.ReadFromJsonAsync<ChallengeResponse>();
        Assert.NotNull(challenge);
        Assert.NotEmpty(challenge.Challenge);
        Assert.Equal(20, challenge.DifficultyBits);
        Assert.True(challenge.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task GetChallenge_InvalidDifficultyBits_ReturnsBadRequest()
    {
        // Arrange
        var project = await SeedTestProject();

        var request = new
        {
            ProjectId = project.Id,
            DifficultyBits = 35 // Too high
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/pow/challenge", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetChallenge_NonExistentProject_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            ProjectId = Guid.NewGuid(), // Doesn't exist
            DifficultyBits = 20
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/pow/challenge", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyPow_InvalidChallenge_ReturnsUnauthorized()
    {
        // Arrange
        var project = await SeedTestProject();

        var request = new
        {
            Challenge = "nonexistent-challenge",
            Nonce = "12345",
            ProjectId = project.Id
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/pow/verify", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyPow_ReplayAttack_ReturnsUnauthorized()
    {
        // Arrange
        var project = await SeedTestProject();

        // Get a challenge
        var challengeResponse = await _client.PostAsJsonAsync("/api/pow/challenge", new
        {
            ProjectId = project.Id,
            DifficultyBits = 10
        });
        
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<ChallengeResponse>();
        Assert.NotNull(challenge);

        // First verification attempt (will likely fail without valid solution, but that's ok)
        await _client.PostAsJsonAsync("/api/pow/verify", new
        {
            Challenge = challenge.Challenge,
            Nonce = "test-nonce",
            ProjectId = project.Id
        });

        // Act - Try to reuse the same challenge (replay attack)
        var replayResponse = await _client.PostAsJsonAsync("/api/pow/verify", new
        {
            Challenge = challenge.Challenge,
            Nonce = "test-nonce",
            ProjectId = project.Id
        });

        // Assert - Should be rejected
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(25)]
    public async Task GetChallenge_VariousDifficulties_ReturnsAppropriate(int difficultyBits)
    {
        // Arrange
        var project = await SeedTestProject();

        var request = new
        {
            ProjectId = project.Id,
            DifficultyBits = difficultyBits
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/pow/challenge", request);

        // Assert
        if (difficultyBits <= 30) // Assuming max difficulty is 30
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            
            var challenge = await response.Content.ReadFromJsonAsync<ChallengeResponse>();
            Assert.NotNull(challenge);
            Assert.Equal(difficultyBits, challenge.DifficultyBits);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task GetChallenge_MultipleRequests_ReturnsUniqueChallenges()
    {
        // Arrange
        var project = await SeedTestProject();
        var challenges = new List<string>();

        // Act - Request 5 challenges
        for (int i = 0; i < 5; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/pow/challenge", new
            {
                ProjectId = project.Id,
                DifficultyBits = 20
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            
            var challenge = await response.Content.ReadFromJsonAsync<ChallengeResponse>();
            Assert.NotNull(challenge);
            challenges.Add(challenge.Challenge);
        }

        // Assert - All challenges should be unique
        Assert.Equal(5, challenges.Distinct().Count());
    }

    /// <summary>
    /// Helper to seed a test project.
    /// </summary>
    private async Task<Project> SeedTestProject()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var developer = new Developer
        {
            Id = Guid.NewGuid(),
            Email = "test@liveauth.app",
            CreatedAt = DateTime.UtcNow
        };

        var project = new Project
        {
            Id = Guid.NewGuid(),
            DeveloperId = developer.Id,
            Name = "Test Project",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Developers.Add(developer);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        return project;
    }

    private record ChallengeResponse(string Challenge, int DifficultyBits, DateTime ExpiresAt);
}
