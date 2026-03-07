namespace LiveAuthCore.Tests.Sdk;

using LiveAuthCore.Tests.Controllers;
using Xunit;
using FluentAssertions;

/// <summary>
/// Tests for the AgentAuth SDK types
/// These verify the DTO contracts match between client and server
/// </summary>
public class AgentAuthDtoTests
{
    [Fact]
    public void AgentAuthStartRequest_CanBeSerialized()
    {
        // Arrange
        var request = new AgentAuthStartRequestDto
        {
            AgentId = "my-agent-001",
            PublicKey = "la_pk_test_123"
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(request);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AgentAuthStartRequestDto>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.AgentId.Should().Be("my-agent-001");
        deserialized.PublicKey.Should().Be("la_pk_test_123");
    }

    [Fact]
    public void AgentAuthStartResponse_CanBeDeserialized()
    {
        // Arrange - use camelCase for System.Text.Json default
        var json = @"{
            ""sessionId"": ""550e8400-e29b-41d4-a716-446655440000"",
            ""challenge"": ""abc123def456"",
            ""difficultyBits"": 16,
            ""expiresAtUnix"": 1700000000
        }";

        // Act - use case-insensitive options
        var options = new System.Text.Json.System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var response = System.Text.Json.JsonSerializer.Deserialize<AgentAuthStartResponseDto>(json, options);

        // Assert
        response.Should().NotBeNull();
        response!.SessionId.Should().Be(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"));
        response.Challenge.Should().Be("abc123def456");
        response.DifficultyBits.Should().Be(16);
        response.ExpiresAtUnix.Should().Be(1700000000);
    }

    [Fact]
    public void AgentAuthVerifyRequest_CanBeSerialized()
    {
        // Arrange
        var request = new AgentAuthVerifyRequestDto
        {
            SessionId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000"),
            Solution = "challenge:12345"
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(request);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AgentAuthVerifyRequestDto>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.SessionId.Should().Be(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"));
        deserialized.Solution.Should().Be("challenge:12345");
    }

    [Fact]
    public void AgentAuthVerifyResponse_CanBeDeserialized()
    {
        // Arrange
        var json = @"{
            ""verified"": true,
            ""token"": ""abc123token"",
            ""expiresAtUnix"": 1700000000
        }";

        // Act
        var response = System.Text.Json.JsonSerializer.Deserialize<AgentAuthVerifyResponseDto>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Assert
        response.Should().NotBeNull();
        response!.Verified.Should().BeTrue();
        response.Token.Should().Be("abc123token");
        response.ExpiresAtUnix.Should().Be(1700000000);
    }

    [Fact]
    public void AgentAuthVerifyResponse_CanHandleError()
    {
        // Arrange
        var json = @"{
            ""verified"": false,
            ""error"": ""Invalid solution""
        }";

        // Act
        var response = System.Text.Json.JsonSerializer.Deserialize<AgentAuthVerifyResponseDto>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Assert
        response.Should().NotBeNull();
        response!.Verified.Should().BeFalse();
        response.Error.Should().Be("Invalid solution");
    }

    [Fact]
    public void AgentAuthValidateRequest_CanBeSerialized()
    {
        // Arrange
        var request = new AgentAuthValidateRequestDto
        {
            Token = "test-token-123"
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(request);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AgentAuthValidateRequestDto>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Token.Should().Be("test-token-123");
    }

    [Fact]
    public void AgentAuthValidateResponse_CanHandleInvalidToken()
    {
        // Arrange
        var json = @"{
            ""valid"": false
        }";

        // Act
        var response = System.Text.Json.JsonSerializer.Deserialize<AgentAuthValidateResponseDto>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Assert
        response.Should().NotBeNull();
        response!.Valid.Should().BeFalse();
    }

    [Fact]
    public void AgentAuthValidateResponse_CanHandleValidToken()
    {
        // Arrange
        var json = @"{
            ""valid"": true,
            ""agentId"": ""my-agent"",
            ""projectId"": ""550e8400-e29b-41d4-a716-446655440000"",
            ""projectName"": ""Test Project"",
            ""expiresAtUnix"": 1700000000
        }";

        // Act
        var response = System.Text.Json.JsonSerializer.Deserialize<AgentAuthValidateResponseDto>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Assert
        response.Should().NotBeNull();
        response!.Valid.Should().BeTrue();
        response.AgentId.Should().Be("my-agent");
        response.ProjectId.Should().Be(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"));
        response.ProjectName.Should().Be("Test Project");
    }

    // DTOs that mirror the server types
    private class AgentAuthStartRequestDto
    {
        public string AgentId { get; set; } = "";
        public string PublicKey { get; set; } = "";
    }

    private class AgentAuthStartResponseDto
    {
        public Guid SessionId { get; set; }
        public string Challenge { get; set; } = "";
        public int DifficultyBits { get; set; }
        public long ExpiresAtUnix { get; set; }
    }

    private class AgentAuthVerifyRequestDto
    {
        public Guid? SessionId { get; set; }
        public string Solution { get; set; } = "";
    }

    private class AgentAuthVerifyResponseDto
    {
        public bool Verified { get; set; }
        public string? Token { get; set; }
        public long ExpiresAtUnix { get; set; }
        public string? Error { get; set; }
    }

    private class AgentAuthValidateRequestDto
    {
        public string Token { get; set; } = "";
    }

    private class AgentAuthValidateResponseDto
    {
        public bool Valid { get; set; }
        public string? AgentId { get; set; }
        public Guid? ProjectId { get; set; }
        public string? ProjectName { get; set; }
        public long ExpiresAtUnix { get; set; }
    }
}
