using System.Net;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/public/auth/* endpoints (public end-user authentication).
/// </summary>
public class PublicAuthControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public PublicAuthControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Start_WithProjectPublicKey_ReturnsSession()
    {
        var testProject = await SeedTestProject();
        _client.DefaultRequestHeaders.Add("X-LW-Public", testProject.PublicKey);

        var response = await _client.PostAsJsonAsync("/api/public/auth/start", new
        {
            UserHint = "user123"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<StartAuthResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.Equal("TEST", result.Mode);
        Assert.Equal(21L, result.BaseAmountSats);
        Assert.True(result.ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Confirm_NonExistentSession_ReturnsNotVerified()
    {
        var testProject = await SeedTestProject();
        _client.DefaultRequestHeaders.Add("X-LW-Public", testProject.PublicKey);

        var response = await _client.PostAsJsonAsync("/api/public/auth/confirm", new
        {
            SessionId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ConfirmAuthResponse>();
        Assert.NotNull(result);
        Assert.False(result.Verified);
        Assert.Null(result.Token);
    }

    [Fact]
    public async Task Confirm_TestModeSession_ReturnsToken()
    {
        var testProject = await SeedTestProject();
        _client.DefaultRequestHeaders.Add("X-LW-Public", testProject.PublicKey);

        var startResponse = await _client.PostAsJsonAsync("/api/public/auth/start", new
        {
            UserHint = "user123"
        });
        var start = await startResponse.Content.ReadFromJsonAsync<StartAuthResponse>();
        Assert.NotNull(start);

        var confirmResponse = await _client.PostAsJsonAsync("/api/public/auth/confirm", new
        {
            start.SessionId
        });

        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        var result = await confirmResponse.Content.ReadFromJsonAsync<ConfirmAuthResponse>();
        Assert.NotNull(result);
        Assert.True(result.Verified);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task Start_NoPublicKey_UsesDemoProjectFallback()
    {
        var response = await _client.PostAsJsonAsync("/api/public/auth/start", new
        {
            UserHint = "demo-user"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<StartAuthResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.SessionId);
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
            PublicKey = $"la_pk_public_auth_{Guid.NewGuid():N}",
            SecretKeyHash = "unused-in-public-auth-tests",
            Environment = "TEST",
            Plan = "free",
            AllowDemoAuth = true,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Developers.Add(developer);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        return project;
    }

    private record StartAuthResponse(Guid SessionId, string? Invoice, long AmountSats, long BaseAmountSats, long ExpiresAtUnix, string Mode);
    private record ConfirmAuthResponse(bool Verified, string? Token);
}
