using System.Net;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/public/auth/demo/* endpoints (public demo authentication).
/// </summary>
public class PublicDemoAuthControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public PublicDemoAuthControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Start_ValidRequest_ReturnsInvoice()
    {
        // Arrange
        await SeedDemoProject();

        // Act
        var response = await _client.PostAsync("/api/public/auth/demo/start", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<PublicStartAuthResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.NotEmpty(result.Invoice);
        Assert.Equal(3L, result.BaseAmountSats);
        Assert.Equal("DEMO", result.Mode);
        Assert.True(result.ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Start_NoDemoProject_ReturnsInternalServerError()
    {
        // Arrange
        await RemoveDemoProject();

        // Act
        var response = await _client.PostAsync("/api/public/auth/demo/start", null);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Start_InactiveProject_ReturnsForbidden()
    {
        // Arrange
        await SeedDemoProject(isActive: false);

        // Act
        var response = await _client.PostAsync("/api/public/auth/demo/start", null);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_NonExistentSession_ReturnsNotVerified()
    {
        // Arrange
        await SeedDemoProject();

        var request = new
        {
            SessionId = Guid.NewGuid()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/public/auth/demo/confirm", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<PublicConfirmAuthResponse>();
        Assert.False(result.Verified);
        Assert.Null(result.Token);
    }

    [Fact]
    public async Task Confirm_ExpiredSession_ReturnsNotVerified()
    {
        // Arrange
        var project = await SeedDemoProject();
        var session = await SeedAuthSession(project.Id, isPaid: false, expiresInMinutes: -5);

        var request = new
        {
            SessionId = session.Id
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/public/auth/demo/confirm", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<PublicConfirmAuthResponse>();
        Assert.False(result.Verified);
        Assert.Null(result.Token);
    }

    [Fact]
    public async Task Confirm_AlreadyPaidSession_ReturnsToken()
    {
        // Arrange
        var project = await SeedDemoProject();
        var session = await SeedAuthSession(project.Id, isPaid: true, expiresInMinutes: 10);

        var request = new
        {
            SessionId = session.Id
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/public/auth/demo/confirm", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<PublicConfirmAuthResponse>();
        Assert.True(result.Verified);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task Confirm_UnpaidSessionInMockMode_ReturnsToken()
    {
        // Arrange
        var project = await SeedDemoProject();
        var session = await SeedAuthSession(project.Id, isPaid: false, expiresInMinutes: 10);

        var request = new
        {
            SessionId = session.Id
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/public/auth/demo/confirm", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<PublicConfirmAuthResponse>();
        Assert.True(result.Verified);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task Confirm_WrongProject_ReturnsNotVerified()
    {
        // Arrange
        await SeedDemoProject();
        var project2 = await SeedProject("Other Project");
        var session = await SeedAuthSession(project2.Id, isPaid: true, expiresInMinutes: 10);

        var request = new
        {
            SessionId = session.Id
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/public/auth/demo/confirm", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<PublicConfirmAuthResponse>();
        // Should fail because session belongs to different project
        Assert.False(result.Verified);
    }

    /// <summary>
    /// Helper to seed the demo project.
    /// </summary>
    private async Task<Project> SeedDemoProject(bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var developerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var demoProjectId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var developer = await db.Developers.FindAsync(developerId);
        if (developer == null)
        {
            developer = new Developer
            {
                Id = developerId,
                Email = "demo@liveauth.app",
                CreatedAt = DateTime.UtcNow
            };
            db.Developers.Add(developer);
        }

        var project = await db.Projects.FindAsync(demoProjectId);
        if (project == null)
        {
            project = new Project
            {
                Id = demoProjectId,
                DeveloperId = developer.Id,
                CreatedAt = DateTime.UtcNow
            };
            db.Projects.Add(project);
        }

        project.Name = "Demo Project";
        project.PublicKey = "demo_pk_test";
        project.SecretKeyHash = "demo_sk_test";
        project.DeveloperId = developer.Id;
        project.IsActive = isActive;
        project.Plan = "free";

        await db.SaveChangesAsync();

        return project;
    }

    private async Task RemoveDemoProject()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var demoProjectId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var project = await db.Projects.FindAsync(demoProjectId);
        if (project != null)
        {
            db.Projects.Remove(project);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Helper to seed a regular project (not demo).
    /// </summary>
    private async Task<Project> SeedProject(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var developer = new Developer
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@liveauth.app",
            CreatedAt = DateTime.UtcNow
        };

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            DeveloperId = developer.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Developers.Add(developer);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        return project;
    }

    /// <summary>
    /// Helper to seed an auth session.
    /// </summary>
    private async Task<AuthSession> SeedAuthSession(Guid projectId, bool isPaid, int expiresInMinutes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var now = DateTime.UtcNow;
        var session = new AuthSession
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Environment = "DEMO",
            AmountSats = 3L,
            InvoiceRHash = Guid.NewGuid().ToString("N"),
            InvoiceBolt11 = $"lnbc{Guid.NewGuid():N}",
            IsPaid = isPaid,
            PaidAt = isPaid ? now : null,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(expiresInMinutes)
        };

        db.AuthSessions.Add(session);
        await db.SaveChangesAsync();

        return session;
    }

    private record PublicStartAuthResponse(Guid SessionId, string Invoice, long AmountSats, long BaseAmountSats, long ExpiresAtUnix, string Mode);
    private record PublicConfirmAuthResponse(bool Verified, string? Token);
}
