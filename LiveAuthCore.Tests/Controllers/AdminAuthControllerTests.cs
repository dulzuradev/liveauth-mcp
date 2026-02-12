using System.Net;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/admin/auth/* endpoints (admin authentication via Lightning).
/// </summary>
public class AdminAuthControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public AdminAuthControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Start_ValidEmail_ReturnsInvoice()
    {
        // Arrange
        var request = new
        {
            Email = "admin@liveauth.app" // Assuming this is in AllowedEmails config
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/admin/auth/start", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AdminStartLoginResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.NotEmpty(result.Invoice);
        Assert.Equal(21L, result.AmountSats);
        Assert.True(result.ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Start_EmptyEmail_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            Email = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/admin/auth/start", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("invalid_email", error?.Error);
    }

    [Fact]
    public async Task Start_NullEmail_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            Email = (string?)null
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/admin/auth/start", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Start_UnauthorizedEmail_ReturnsForbidden()
    {
        // Arrange
        var request = new
        {
            Email = "unauthorized@example.com"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/admin/auth/start", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("not_allowed", error?.Error);
    }

    [Fact]
    public async Task Start_ExistingUnpaidSession_ReusesSession()
    {
        // Arrange
        var email = "admin@liveauth.app";
        var existingSession = await SeedAdminLoginSession(email, isPaid: false, expiresInMinutes: 5);

        var request = new
        {
            Email = email
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/admin/auth/start", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AdminStartLoginResponse>();
        Assert.Equal(existingSession.Id, result.SessionId);
        Assert.Equal(existingSession.InvoiceBolt11, result.Invoice);
    }

    [Fact]
    public async Task Start_ExpiredSession_CreatesNewSession()
    {
        // Arrange
        var email = "admin@liveauth.app";
        var expiredSession = await SeedAdminLoginSession(email, isPaid: false, expiresInMinutes: -5);

        var request = new
        {
            Email = email
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/admin/auth/start", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AdminStartLoginResponse>();
        Assert.NotEqual(expiredSession.Id, result.SessionId);
    }

    [Fact]
    public async Task Confirm_NonExistentSession_ReturnsNotVerified()
    {
        // Arrange
        var request = new
        {
            SessionId = Guid.NewGuid()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/admin/auth/confirm", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AdminConfirmLoginResponse>();
        Assert.False(result.Verified);
    }

    [Fact]
    public async Task Confirm_AlreadyPaidSession_ReturnsToken()
    {
        // Arrange
        var session = await SeedAdminLoginSession("admin@liveauth.app", isPaid: true, expiresInMinutes: 5);

        var request = new
        {
            SessionId = session.Id
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/admin/auth/confirm", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AdminConfirmLoginResponse>();
        Assert.True(result.Verified);
        Assert.NotEmpty(result.Token);
        Assert.True(result.ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Confirm_ExpiredSession_ReturnsNotVerified()
    {
        // Arrange
        var session = await SeedAdminLoginSession("admin@liveauth.app", isPaid: false, expiresInMinutes: -5);

        var request = new
        {
            SessionId = session.Id
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/admin/auth/confirm", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AdminConfirmLoginResponse>();
        Assert.False(result.Verified);
    }

    [Fact]
    public async Task Confirm_UnpaidInvoice_ReturnsNotVerified()
    {
        // Arrange
        var session = await SeedAdminLoginSession("admin@liveauth.app", isPaid: false, expiresInMinutes: 5);

        var request = new
        {
            SessionId = session.Id
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/admin/auth/confirm", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<AdminConfirmLoginResponse>();
        // Note: This will return false because the mock Lightning service won't show it as paid
        Assert.False(result.Verified);
    }

    /// <summary>
    /// Helper to seed an admin login session in the database.
    /// </summary>
    private async Task<AdminLoginSession> SeedAdminLoginSession(string email, bool isPaid, int expiresInMinutes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var now = DateTime.UtcNow;
        var session = new AdminLoginSession
        {
            Id = Guid.NewGuid(),
            Email = email,
            AmountSats = 21L,
            InvoiceBolt11 = $"lnbc{Guid.NewGuid():N}",
            InvoiceRHash = Guid.NewGuid().ToString("N"),
            IsPaid = isPaid,
            PaidAt = isPaid ? now : null,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(expiresInMinutes)
        };

        db.AdminLoginSessions.Add(session);
        await db.SaveChangesAsync();

        return session;
    }

    private record AdminStartLoginResponse(Guid SessionId, string Invoice, long AmountSats, long ExpiresAtUnix);
    private record AdminConfirmLoginResponse(bool Verified, string Token = "", long ExpiresAtUnix = 0);
    private record ErrorResponse(string Error, string Message);
}
