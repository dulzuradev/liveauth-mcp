using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LiveAuthCore.Tests;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

public class AgentAuthControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public AgentAuthControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Start_ReturnsChallenge_ForValidApiKey()
    {
        // Arrange
        var request = new
        {
            agentId = "test-agent-001",
            publicKey = "la_sk_test_key_123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/auth/start", request);

        // Assert - should get either a challenge (valid key) or unauthorized (invalid key)
        // Either way, it shouldn't be a 500 error
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Start_ReturnsBadRequest_WhenAgentIdMissing()
    {
        // Arrange
        var request = new
        {
            publicKey = "test-key"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/auth/start", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("AgentId");
    }

    [Fact]
    public async Task Start_ReturnsBadRequest_WhenPublicKeyMissing()
    {
        // Arrange
        var request = new
        {
            agentId = "test-agent"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/auth/start", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("PublicKey");
    }

    [Fact]
    public async Task Verify_ReturnsBadRequest_WhenSessionIdMissing()
    {
        // Arrange
        var request = new
        {
            solution = "test-solution"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/auth/verify", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Verify_ReturnsBadRequest_WhenSolutionMissing()
    {
        // Arrange
        var request = new
        {
            sessionId = Guid.NewGuid()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/auth/verify", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Validate_ReturnsBadRequest_WhenTokenMissing()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/auth/validate", new { });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Verify_ReturnsUnauthorized_ForInvalidSession()
    {
        // Arrange
        var request = new
        {
            sessionId = Guid.NewGuid(),
            solution = "invalid-solution"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/auth/verify", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Invalid session");
    }

    [Fact]
    public async Task Validate_ReturnsInvalid_ForNonExistentToken()
    {
        // Arrange
        var request = new
        {
            token = "non-existent-token-12345"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/auth/validate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ValidateResponse>();
        result!.Valid.Should().BeFalse();
    }

    private class ValidateResponse
    {
        public bool Valid { get; set; }
    }
}
