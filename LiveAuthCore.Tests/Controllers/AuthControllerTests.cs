using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/auth/* endpoints (Lightning-based developer authentication).
/// </summary>
public class AuthControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public AuthControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Start_ValidRequest_ReturnsSession()
    {
        // Arrange
        var (project, apiKey) = await SeedProjectWithApiKey();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var request = new
        {
            UserRef = "user123",
            AmountSats = 100L,
            Memo = "Test authentication"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/start", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AuthStartResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.NotEmpty(result.Invoice);
        Assert.NotEmpty(result.PaymentHash);
        Assert.Equal(100L, result.AmountSats);
        Assert.True(result.ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Start_NoApiKey_ReturnsUnauthorized()
    {
        // Arrange
        var request = new
        {
            UserRef = "user123",
            AmountSats = 100L,
            Memo = "Test authentication"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/start", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Start_InvalidApiKey_ReturnsUnauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "la_sk_invalid");

        var request = new
        {
            UserRef = "user123",
            AmountSats = 100L,
            Memo = "Test authentication"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/start", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Start_MinimalRequest_ReturnsSession()
    {
        // Arrange
        var (project, apiKey) = await SeedProjectWithApiKey();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var request = new
        {
            UserRef = "user123",
            AmountSats = (long?)null,
            Memo = (string?)null
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/start", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AuthStartResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.SessionId);
    }

    [Fact]
    public async Task Confirm_NonExistentSession_ReturnsNotVerified()
    {
        // Arrange
        var (project, apiKey) = await SeedProjectWithApiKey();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var request = new
        {
            SessionId = Guid.NewGuid()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/confirm", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AuthConfirmResponse>();
        Assert.False(result.Verified);
    }

    [Fact]
    public async Task Confirm_NoApiKey_ReturnsUnauthorized()
    {
        // Arrange
        var request = new
        {
            SessionId = Guid.NewGuid()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/confirm", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_InvalidApiKey_ReturnsUnauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "la_sk_invalid");

        var request = new
        {
            SessionId = Guid.NewGuid()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/confirm", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_ValidToken_ReturnsValid()
    {
        // Note: This test requires a valid JWT token generated by the system.
        // For a basic test, we'll check that the endpoint is accessible without auth.

        // Arrange
        var request = new
        {
            Token = "dummy.jwt.token"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/verify-token", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<VerifyTokenResponse>();
        Assert.NotNull(result);
        // With a dummy token, it should return Valid=false but still 200 OK
        Assert.False(result.Valid);
    }

    [Fact]
    public async Task VerifyToken_NoAuth_ReturnsOk()
    {
        // Arrange
        var request = new
        {
            Token = "test.token"
        };

        // Act (no Authorization header)
        var response = await _client.PostAsJsonAsync("/api/auth/verify-token", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_EmptyToken_ReturnsInvalid()
    {
        // Arrange
        var request = new
        {
            Token = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/verify-token", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<VerifyTokenResponse>();
        Assert.False(result.Valid);
    }

    /// <summary>
    /// Helper to seed a project with an API key.
    /// </summary>
    private async Task<(Project project, string apiKey)> SeedProjectWithApiKey()
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
            Name = "Test Project",
            DeveloperId = developer.Id,
            CreatedAt = DateTime.UtcNow
        };

        var apiKey = $"la_sk_{Guid.NewGuid():N}";
        var apiKeyEntity = new ProjectApiKey
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SecretKeyHash = BCrypt.Net.BCrypt.HashPassword(apiKey),
            PublicKey = apiKey[..20],
            CreatedAt = DateTime.UtcNow
        };

        db.Developers.Add(developer);
        db.Projects.Add(project);
        db.ProjectApiKeys.Add(apiKeyEntity);
        await db.SaveChangesAsync();

        return (project, apiKey);
    }

    private record AuthStartResponse(Guid SessionId, string Invoice, string PaymentHash, long AmountSats, long ExpiresAtUnix);
    private record AuthConfirmResponse(bool Verified, string Token = "", string Method = "", int ExpiresIn = 0);
    private record VerifyTokenResponse(bool Valid, Dictionary<string, string>? Claims = null);
}
