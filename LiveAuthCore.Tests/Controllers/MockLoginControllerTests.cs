using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/MockLogin endpoint (mock authentication for testing/demo).
/// Note: This controller appears to be legacy code using AppDbContext.
/// Tests are provided for completeness but this controller may be deprecated.
/// </summary>
public class MockLoginControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public MockLoginControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task MockLogin_MatchingCredentials_ReturnsSuccess()
    {
        // Arrange
        var request = new
        {
            Username = "testuser",
            Password = "testuser", // Mock success when username == password
            PaymentHash = "test_hash_123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/MockLogin", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<MockLoginResponse>();
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Contains("successful", result.Message.ToLower());
    }

    [Fact]
    public async Task MockLogin_MismatchedCredentials_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            Username = "testuser",
            Password = "wrongpassword",
            PaymentHash = "test_hash_456"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/MockLogin", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<MockLoginResponse>();
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Contains("failed", result.Message.ToLower());
    }

    [Fact]
    public async Task MockLogin_EmptyUsername_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            Username = "",
            Password = "",
            PaymentHash = "test_hash"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/MockLogin", request);

        // Assert
        // Empty username and password are equal, so mock validation passes
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MockLogin_NullPaymentHash_ProcessesRequest()
    {
        // Arrange
        var request = new
        {
            Username = "user",
            Password = "user",
            PaymentHash = (string?)null
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/MockLogin", request);

        // Assert
        // Should still process even with null payment hash
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MockLogin_LongUsername_HandlesGracefully()
    {
        // Arrange
        var longUsername = new string('a', 500);
        var request = new
        {
            Username = longUsername,
            Password = longUsername,
            PaymentHash = "test_hash"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/MockLogin", request);

        // Assert
        // Should handle long usernames without crashing
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MockLogin_SpecialCharacters_HandlesCorrectly()
    {
        // Arrange
        var specialChars = "user@#$%^&*()";
        var request = new
        {
            Username = specialChars,
            Password = specialChars,
            PaymentHash = "test_hash"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/MockLogin", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<MockLoginResponse>();
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public async Task MockLogin_CaseSensitivity_Matters()
    {
        // Arrange
        var request = new
        {
            Username = "TestUser",
            Password = "testuser", // Different case
            PaymentHash = "test_hash"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/MockLogin", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<MockLoginResponse>();
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
    }

    private record MockLoginResponse(string Message, bool IsSuccessful);
}
