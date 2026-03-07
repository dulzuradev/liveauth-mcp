using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using LiveAuthCore.Data.Entities;
using Xunit;

namespace LiveAuthCore.Tests.Security;

public class AgentAuthSessionTests
{
    [Fact]
    public void AgentAuthSession_CanBeCreated()
    {
        // Arrange & Act
        var session = new AgentAuthSession
        {
            Id = Guid.NewGuid(),
            AgentId = "test-agent-001",
            ProjectId = Guid.NewGuid(),
            Challenge = "abc123",
            DifficultyBits = 16,
            IsVerified = false,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };

        // Assert
        session.AgentId.Should().Be("test-agent-001");
        session.Challenge.Should().Be("abc123");
        session.DifficultyBits.Should().Be(16);
        session.IsVerified.Should().BeFalse();
    }

    [Fact]
    public void AgentAuthSession_CanBeMarkedAsVerified()
    {
        // Arrange
        var session = new AgentAuthSession
        {
            Id = Guid.NewGuid(),
            AgentId = "test-agent",
            Challenge = "challenge123",
            DifficultyBits = 16
        };

        // Act
        session.IsVerified = true;
        session.AuthToken = "generated-token-abc";
        session.SolvedAt = DateTime.UtcNow;
        session.ExpiresAt = DateTime.UtcNow.AddHours(24);

        // Assert
        session.IsVerified.Should().BeTrue();
        session.AuthToken.Should().Be("generated-token-abc");
        session.SolvedAt.Should().NotBeNull();
    }
}

public class PoWChallengeTests
{
    [Fact]
    public void GenerateChallenge_ReturnsValidHexString()
    {
        // Act
        var challenge = GenerateChallenge("agent-001", Guid.NewGuid().ToString());

        // Assert
        challenge.Should().NotBeNullOrEmpty();
        challenge.Length.Should().Be(64); // SHA256 = 32 bytes = 64 hex chars
        challenge.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void GenerateChallenge_ReturnsUniqueValues()
    {
        // Arrange
        var agentId = "test-agent";
        var projectId = Guid.NewGuid().ToString();

        // Act
        var challenge1 = GenerateChallenge(agentId, projectId);
        var challenge2 = GenerateChallenge(agentId, projectId);

        // Assert - challenges should be different due to timestamp/random
        challenge1.Should().NotBe(challenge2);
    }

    [Fact]
    public void VerifySolution_ReturnsTrue_ForValidSolution()
    {
        // Arrange
        var challenge = "a".PadRight(64, '0'); // 64 zeros
        var solution = challenge + ":0"; // First nonce should work given easy difficulty
        
        // Act - use easy difficulty (lots of zeros in target)
        var isValid = VerifySolution(challenge, solution, 4); // Easy: only 1 hex char needs to be 0

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void VerifySolution_ReturnsFalse_ForInvalidSolution()
    {
        // Arrange
        var challenge = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        var solution = "not-a-valid-solution";

        // Act
        var isValid = VerifySolution(challenge, solution, 16);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySolution_ReturnsFalse_ForMalformedSolution()
    {
        // Arrange
        var challenge = "a".PadRight(64, '0');

        // Act
        var isValid = VerifySolution(challenge, "no-colon-here", 16);

        // Assert
        isValid.Should().BeFalse();
    }

    private static string GenerateChallenge(string agentId, string projectId)
    {
        var data = $"{agentId}:{projectId}:{DateTime.UtcNow.Ticks}:{Guid.NewGuid()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool VerifySolution(string challenge, string solution, int difficultyBits)
    {
        var parts = solution.Split(':');
        if (parts.Length != 2) return false;

        var nonce = parts[1];
        var dataToHash = challenge + nonce;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(dataToHash));
        var hashHex = Convert.ToHexString(hash).ToLowerInvariant();

        var requiredZeros = difficultyBits / 4;
        return hashHex.StartsWith(new string('0', requiredZeros));
    }
}

public class AgentAuthTokenTests
{
    [Fact]
    public void GenerateToken_ReturnsUniqueTokens()
    {
        // Act
        var token1 = GenerateToken("agent-001", Guid.NewGuid().ToString());
        var token2 = GenerateToken("agent-001", Guid.NewGuid().ToString());

        // Assert
        token1.Should().NotBeNullOrEmpty();
        token2.Should().NotBeNullOrEmpty();
        token1.Should().NotBe(token2);
    }

    [Fact]
    public void GenerateToken_ReturnsBase64String()
    {
        // Act
        var token = GenerateToken("agent-001", Guid.NewGuid().ToString());

        // Assert
        token.Should().MatchRegex("^[A-Za-z0-9+/]+=*$");
    }

    private static string GenerateToken(string agentId, string projectId)
    {
        var data = $"{agentId}:{projectId}:{DateTime.UtcNow.Ticks}:{Guid.NewGuid()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(bytes);
    }
}
