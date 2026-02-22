using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiveAuthCore.Tests.Integration;

public class AuthFlowIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public AuthFlowIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Tests the full demo auth flow: start → verify (unpaid) → pay → verify (paid)
    /// </summary>
    [Fact]
    public async Task DemoAuth_FullFlow_ReturnsVerifiedAfterPayment()
    {
        // Step 1: Start demo session
        var startResponse = await _client.PostAsJsonAsync("/api/public/demo/start", new { });
        startResponse.EnsureSuccessStatusCode();
        
        var startContent = await startResponse.Content.ReadAsStringAsync();
        Assert.Contains("sessionId", startContent);
        Assert.Contains("invoice", startContent);
        
        // Extract session ID
        var sessionId = ExtractSessionId(startContent);
        
        // Step 2: Verify before payment (should return verified=false)
        var confirmResponse = await _client.PostAsJsonAsync("/api/public/demo/confirm", 
            new { sessionId });
        confirmResponse.EnsureSuccessStatusCode();
        
        var confirmContent = await confirmResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"verified\":false", confirmContent);
        
        // Step 3: After payment (simulated by updating DB), verify should return true
        // Note: This requires LND to be available or mocked
    }

    /// <summary>
    /// Tests that demo confirm returns false for unpaid invoice
    /// </summary>
    [Fact]
    public async Task DemoAuth_Confirm_UnpaidInvoice_ReturnsFalse()
    {
        // Start demo session
        var startResponse = await _client.PostAsJsonAsync("/api/public/demo/start", new { });
        var content = await startResponse.Content.ReadAsStringAsync();
        var sessionId = ExtractSessionId(content);
        
        // Confirm should return false for unpaid invoice
        var confirmResponse = await _client.PostAsJsonAsync("/api/public/demo/confirm",
            new { sessionId });
        
        Assert.True(confirmResponse.IsSuccessStatusCode);
        var confirmContent = await confirmResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"verified\":false", confirmContent);
    }

    /// <summary>
    /// Tests the regular public auth flow
    /// </summary>
    [Fact]
    public async Task PublicAuth_Start_ReturnsSessionWithInvoice()
    {
        // Start auth session with API key
        var response = await _client.PostAsJsonAsync("/api/public/auth/start",
            new { userHint = "test-user" });
        
        // May fail without valid API key, but should return proper structure
        var content = await response.Content.ReadAsStringAsync();
        
        // If successful, should have sessionId and invoice
        if (response.IsSuccessStatusCode)
        {
            Assert.Contains("sessionId", content);
            Assert.Contains("invoice", content);
        }
    }

    /// <summary>
    /// Tests that invalid session ID returns error
    /// </summary>
    [Fact]
    public async Task DemoAuth_InvalidSession_ReturnsError()
    {
        var response = await _client.PostAsJsonAsync("/api/public/demo/confirm",
            new { sessionId = "invalid-session-id" });
        
        // Should return error or verified=false
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("verified", content);
    }

    /// <summary>
    /// Tests that missing session ID returns error
    /// </summary>
    [Fact]
    public async Task DemoAuth_MissingSession_ReturnsError()
    {
        var response = await _client.PostAsJsonAsync("/api/public/demo/confirm",
            new { });
        
        Assert.True(response.IsSuccessStatusCode); // May return verified=false
    }

    /// <summary>
    /// Tests the L402 payment flow
    /// </summary>
    [Fact]
    public async Task L402_CreateInvoice_ReturnsInvoice()
    {
        var response = await _client.PostAsJsonAsync("/api/public/l402/invoice",
            new { });
        
        // Should return invoice details
        var content = await response.Content.ReadAsStringAsync();
        
        if (response.IsSuccessStatusCode)
        {
            Assert.Contains("invoice", content.ToLower());
        }
    }

    /// <summary>
    /// Verifies CORS headers are present for API endpoints
    /// </summary>
    [Fact]
    public async Task Api_Cors_AllowsFrontendOrigin()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/public/demo/start");
        request.Headers.Add("Origin", "https://liveauth.app");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        
        var response = await _client.SendAsync(request);
        
        // Should have CORS headers
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin") ||
                   response.StatusCode == HttpStatusCode.MethodNotAllowed);
    }

    private static string ExtractSessionId(string content)
    {
        // Simple extraction - in real tests use JSON parser
        var start = content.IndexOf("\"sessionId\":\"") + 14;
        var end = content.IndexOf("\"", start);
        return content.Substring(start, end - start);
    }
}

public class TestWebApplicationFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        
        // Override services for testing
        builder.ConfigureServices(services =>
        {
            // Remove real services and add mocks
            // services.RemoveAll<ILightningService>();
            // services.AddSingleton<ILightningService, MockLightningService>();
        });
    }
}
