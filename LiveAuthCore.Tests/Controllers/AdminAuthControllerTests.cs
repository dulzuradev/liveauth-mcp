using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/admin/auth/* endpoints.
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
    public async Task Payment_ReturnsInvoice()
    {
        var response = await _client.PostAsync("/api/admin/auth/payment", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<AdminPaymentResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.NotEmpty(result.Invoice);
        Assert.Equal(100L, result.AmountSats);
        Assert.False(result.IsSetup);
        Assert.True(result.ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Verify_NonExistentSession_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/auth/verify", new
        {
            SessionId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Verify_AlreadyPaidSession_ReturnsPaid()
    {
        var session = await SeedAdminPaymentSession(isPaid: true, expiresInMinutes: 5);

        var response = await _client.PostAsJsonAsync("/api/admin/auth/verify", new
        {
            SessionId = session.Id
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<AdminVerifyResponse>();
        Assert.NotNull(result);
        Assert.True(result.Paid);
        Assert.False(result.CanSetPassword);
    }

    [Fact]
    public async Task Verify_ExpiredSession_ReturnsNotPaid()
    {
        var session = await SeedAdminPaymentSession(isPaid: false, expiresInMinutes: -5);

        var response = await _client.PostAsJsonAsync("/api/admin/auth/verify", new
        {
            SessionId = session.Id
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<AdminVerifyResponse>();
        Assert.NotNull(result);
        Assert.False(result.Paid);
        Assert.Equal("Payment expired", result.Error);
    }

    [Fact]
    public async Task Verify_UnpaidInvoiceInMockMode_ReturnsPaid()
    {
        var session = await SeedAdminPaymentSession(isPaid: false, expiresInMinutes: 5);

        var response = await _client.PostAsJsonAsync("/api/admin/auth/verify", new
        {
            SessionId = session.Id
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<AdminVerifyResponse>();
        Assert.NotNull(result);
        Assert.True(result.Paid);
    }

    [Fact]
    public async Task Setup_WhenAdminAlreadyExists_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/auth/setup", new
        {
            Username = "admin",
            Password = "SecurePassword123!"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        const string password = "SecurePassword123!";
        var session = await SeedAdminSession("testadmin", password);

        var response = await _client.PostAsJsonAsync("/api/admin/auth/login", new
        {
            Username = session.Username,
            Password = password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<AdminLoginResponse>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotEmpty(result.Token);
        Assert.Equal(session.Username, result.Username);
    }

    [Fact]
    public async Task Status_WithStoredBearerToken_ReturnsAuthenticated()
    {
        var session = await SeedAdminSession("statusadmin", "SecurePassword123!", token: $"admin-token-{Guid.NewGuid():N}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

        var response = await _client.GetAsync("/api/admin/auth/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<AdminStatusResponse>();
        Assert.NotNull(result);
        Assert.True(result.IsAuthenticated);
        Assert.Equal(session.Username, result.Username);
        Assert.True(result.IsOwner);
    }

    private async Task<AdminPaymentSession> SeedAdminPaymentSession(bool isPaid, int expiresInMinutes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

        var now = DateTime.UtcNow;
        var session = new AdminPaymentSession
        {
            Id = Guid.NewGuid(),
            AmountSats = 100L,
            InvoiceBolt11 = $"lnbc{Guid.NewGuid():N}",
            InvoiceRHash = Guid.NewGuid().ToString("N"),
            IsPaid = isPaid,
            PaidAt = isPaid ? now : null,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(expiresInMinutes)
        };

        db.AdminPaymentSessions.Add(session);
        await db.SaveChangesAsync();

        return session;
    }

    private async Task<AdminSession> SeedAdminSession(string username, string password, string? token = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        var (hash, salt) = TestAuth.HashPasswordWithSalt(password);

        var session = new AdminSession
        {
            Id = Guid.NewGuid(),
            Username = username.ToLowerInvariant(),
            PasswordHash = hash,
            PasswordSalt = salt,
            IsOwner = true,
            Token = token,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        db.AdminSessions.Add(session);
        await db.SaveChangesAsync();

        return session;
    }

    private record AdminPaymentResponse(Guid SessionId, string Invoice, long AmountSats, bool IsSetup, long ExpiresAtUnix);
    private record AdminVerifyResponse(bool Paid, bool? CanSetPassword = null, string? Error = null);
    private record AdminLoginResponse(bool Success, string? Token, string? Username, string? Error);
    private record AdminStatusResponse(bool IsAuthenticated, string? Username, bool? IsOwner);
}
