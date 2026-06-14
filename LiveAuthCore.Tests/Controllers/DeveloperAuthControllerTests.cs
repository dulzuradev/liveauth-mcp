using System.Net;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
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
    public async Task GitHubStart_UsesUrlSafeStateCookie()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/dev/auth/github/start");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var state = GetQueryParam(response.Headers.Location!, "state");
        Assert.NotNull(state);
        Assert.Matches("^[a-f0-9]{64}$", state!);

        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(setCookies, cookie =>
            cookie.StartsWith($"github_oauth_state={state};", StringComparison.Ordinal) &&
            cookie.Contains("path=/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookies, cookie =>
            cookie.StartsWith("github_oauth_state=;", StringComparison.Ordinal) &&
            cookie.Contains("path=/api/dev/auth/github", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GitHubCallback_WithInvalidState_RedirectsToLoginAndClearsStateCookies()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/dev/auth/github/callback?code=fake-code&state=wrong-state");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "https://liveauth.app/dev/projects?githubError=invalid_state",
            response.Headers.Location?.ToString());

        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(setCookies, cookie =>
            cookie.StartsWith("github_oauth_state=;", StringComparison.Ordinal) &&
            cookie.Contains("path=/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookies, cookie =>
            cookie.StartsWith("github_oauth_state=;", StringComparison.Ordinal) &&
            cookie.Contains("path=/api/dev/auth/github", StringComparison.OrdinalIgnoreCase));
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
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.DeveloperId);
        Assert.True(result.EmailVerificationRequired);
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
        var email = $"dev-{Guid.NewGuid():N}@liveauth.app";
        await SeedDeveloper(email, password);

        var request = new
        {
            Email = email,
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
        var email = $"dev-{Guid.NewGuid():N}@liveauth.app";
        await SeedDeveloper(email, "CorrectPassword123!");

        var request = new
        {
            Email = email,
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
        var response = await _client.PostAsJsonAsync("/api/dev/auth/resend-verification", request);

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
        var response = await _client.PostAsJsonAsync("/api/dev/auth/resend-verification", request);

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
            CreatedAt = DateTime.UtcNow
        };

        if (!string.IsNullOrWhiteSpace(password))
        {
            var (hash, salt) = TestAuth.HashPasswordWithSalt(password);
            developer.PasswordHash = hash;
            developer.PasswordSalt = salt;
            developer.EmailVerified = true;
        }

        db.Developers.Add(developer);
        await db.SaveChangesAsync();

        return developer;
    }

    private static string? GetQueryParam(Uri uri, string name)
    {
        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
            return null;

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pieces[0]);
            if (!string.Equals(key, name, StringComparison.Ordinal))
                continue;

            return pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1]) : string.Empty;
        }

        return null;
    }

    private record RegisterResponse(Guid DeveloperId, string Message, bool EmailVerificationRequired, bool EmailSent);
    private record LoginResponse(bool Verified, string? Token, string Message);
}
