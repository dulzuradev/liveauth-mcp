using System.Net;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/dev/auth/* endpoints (developer registration, login, password reset).
/// </summary>
public class DeveloperAuthControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public DeveloperAuthControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var request = new
        {
            Email = "newdev@liveauth.app",
            Password = "SecurePassword123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/dev/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.Token);
        Assert.Equal("newdev@liveauth.app", result.Email);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        // Arrange
        await SeedDeveloper("existing@liveauth.app");

        var request = new
        {
            Email = "existing@liveauth.app",
            Password = "SecurePassword123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/dev/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_WeakPassword_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            Email = "test@liveauth.app",
            Password = "weak"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/dev/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidEmail_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            Email = "not-an-email",
            Password = "SecurePassword123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/dev/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        // Arrange
        var password = "SecurePassword123!";
        await SeedDeveloper("dev@liveauth.app", password);

        var request = new
        {
            Email = "dev@liveauth.app",
            Password = password
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/dev/auth/login", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task Login_InvalidPassword_ReturnsUnauthorized()
    {
        // Arrange
        await SeedDeveloper("dev@liveauth.app", "CorrectPassword123!");

        var request = new
        {
            Email = "dev@liveauth.app",
            Password = "WrongPassword123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/dev/auth/login", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_NonExistentUser_ReturnsUnauthorized()
    {
        // Arrange
        var request = new
        {
            Email = "nonexistent@liveauth.app",
            Password = "SomePassword123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/dev/auth/login", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestPasswordReset_ValidEmail_ReturnsOk()
    {
        // Arrange
        await SeedDeveloper("dev@liveauth.app");

        var request = new
        {
            Email = "dev@liveauth.app"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/dev/auth/request-password-reset", request);

        // Assert
        // Should return OK even if email doesn't exist (security best practice)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RequestPasswordReset_NonExistentEmail_ReturnsOk()
    {
        // Arrange
        var request = new
        {
            Email = "nonexistent@liveauth.app"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/dev/auth/request-password-reset", request);

        // Assert
        // Should return OK to prevent email enumeration
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Helper to seed a developer in the database.
    /// </summary>
    private async Task<Developer> SeedDeveloper(string email, string? password = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var developer = new Developer
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password ?? "DefaultPassword123!"),
            CreatedAt = DateTime.UtcNow
        };

        db.Developers.Add(developer);
        await db.SaveChangesAsync();

        return developer;
    }

    private record RegisterResponse(string Token, string Email, Guid DeveloperId);
    private record LoginResponse(string Token);
}
