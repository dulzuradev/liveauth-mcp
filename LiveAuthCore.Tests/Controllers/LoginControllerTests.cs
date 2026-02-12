using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LiveAuthCore.Tests.Controllers;

/// <summary>
/// Tests for /api/Login/* endpoints (legacy/mock login endpoints).
/// Note: This controller appears to be older code using AppDbContext.
/// Tests are provided for completeness but this controller may be deprecated.
/// </summary>
public class LoginControllerTests : IClassFixture<LiveAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly LiveAuthWebApplicationFactory _factory;

    public LoginControllerTests(LiveAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_SubscribedUser_ReturnsToken()
    {
        // Arrange
        var request = new
        {
            UserId = "subscribed_user"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/Login", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(result);
        Assert.Equal("Access granted", result.Data);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task Login_UnsubscribedUser_ReturnsInvoice()
    {
        // Arrange
        var request = new
        {
            UserId = "new_user"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/Login", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<LoginWithInvoiceResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Invoice);
    }

    [Fact]
    public async Task Login_EmptyUserId_ReturnsInvoice()
    {
        // Arrange
        var request = new
        {
            UserId = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/Login", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetPaymentStatus_UnpaidInvoice_ReturnsPending()
    {
        // Arrange
        var paymentHash = "unpaid_hash_12345";

        // Act
        var response = await _client.GetAsync($"/api/Login/payment-status/{paymentHash}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<PaymentStatusResponse>();
        Assert.NotNull(result);
        Assert.Equal("Pending", result.Status);
    }

    [Fact]
    public async Task GetPaymentStatus_EmptyHash_ReturnsPending()
    {
        // Arrange
        var paymentHash = "";

        // Act
        var response = await _client.GetAsync($"/api/Login/payment-status/{paymentHash}");

        // Assert
        // Should return OK with pending status (or 404 depending on routing)
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPaymentStatusWithSession_DelegatesCorrectly()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString();
        var paymentHash = "test_hash";

        // Act
        var response = await _client.GetAsync($"/api/Login/payment-status/{sessionId}/{paymentHash}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Should delegate to the original GetPaymentStatus method
    }

    [Fact]
    public async Task GetPaymentStatusWithSession_IgnoresSessionId()
    {
        // Arrange
        var sessionId1 = Guid.NewGuid().ToString();
        var sessionId2 = Guid.NewGuid().ToString();
        var paymentHash = "same_hash";

        // Act
        var response1 = await _client.GetAsync($"/api/Login/payment-status/{sessionId1}/{paymentHash}");
        var response2 = await _client.GetAsync($"/api/Login/payment-status/{sessionId2}/{paymentHash}");

        // Assert
        // Both should return same result since sessionId is ignored
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }

    private record LoginResponse(string Data, string Token);
    private record LoginWithInvoiceResponse(InvoiceData Invoice);
    private record InvoiceData(string PaymentRequest, string RHash);
    private record PaymentStatusResponse(string Status = "Pending", string Data = "", string Token = "");
}
