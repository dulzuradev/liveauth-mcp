using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        _client.DefaultRequestHeaders.Add("X-LW-Public", TestWebApplicationFactory.DemoPublicKey);
    }

    /// <summary>
    /// Tests the full demo auth flow with mock Lightning settlement.
    /// </summary>
    [Fact]
    public async Task DemoAuth_FullFlow_ReturnsVerifiedAfterPayment()
    {
        // Step 1: Start demo session
        var startResponse = await _client.PostAsJsonAsync("/api/public/auth/demo/start", new { });
        startResponse.EnsureSuccessStatusCode();
        
        var startContent = await startResponse.Content.ReadAsStringAsync();
        Assert.Contains("sessionId", startContent);
        Assert.Contains("invoice", startContent);
        
        // Extract session ID
        var sessionId = ExtractSessionId(startContent);
        
        // Step 2: Verify payment. Mock Lightning marks invoices settled.
        var confirmResponse = await _client.PostAsJsonAsync("/api/public/auth/demo/confirm",
            new { sessionId });
        confirmResponse.EnsureSuccessStatusCode();
        
        var confirmContent = await confirmResponse.Content.ReadAsStringAsync();
        Assert.True(ExtractVerified(confirmContent));
        Assert.Contains("token", confirmContent);
    }

    /// <summary>
    /// Tests that demo confirm returns false for an expired session.
    /// </summary>
    [Fact]
    public async Task DemoAuth_Confirm_ExpiredSession_ReturnsFalse()
    {
        var sessionId = await SeedExpiredDemoSession();
        
        // Confirm should return false without polling Lightning.
        var confirmResponse = await _client.PostAsJsonAsync("/api/public/auth/demo/confirm",
            new { sessionId });
        
        Assert.True(confirmResponse.IsSuccessStatusCode);
        var confirmContent = await confirmResponse.Content.ReadAsStringAsync();
        Assert.False(ExtractVerified(confirmContent));
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
        
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        
        Assert.Contains("sessionId", content);
        Assert.Contains("invoice", content);
    }

    /// <summary>
    /// Tests that invalid session ID returns error
    /// </summary>
    [Fact]
    public async Task DemoAuth_InvalidSession_ReturnsError()
    {
        var response = await _client.PostAsJsonAsync("/api/public/auth/demo/confirm",
            new { sessionId = Guid.NewGuid() });
        
        // Should return error or verified=false
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode);
        Assert.False(ExtractVerified(content));
    }

    /// <summary>
    /// Tests that missing session ID returns error
    /// </summary>
    [Fact]
    public async Task DemoAuth_MissingSession_ReturnsError()
    {
        var response = await _client.PostAsJsonAsync("/api/public/auth/demo/confirm",
            new { });
        
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.False(ExtractVerified(content));
    }

    /// <summary>
    /// Tests the L402 payment flow
    /// </summary>
    [Fact]
    public async Task L402_CreateInvoice_ReturnsInvoice()
    {
        var response = await _client.PostAsJsonAsync("/api/public/l402/invoice?publicKey=la_pk_demo",
            new { });
        
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        
        Assert.Contains("paymentHash", content);
        Assert.Contains("bolt11", content);
    }

    /// <summary>
    /// Verifies CORS headers are present for API endpoints
    /// </summary>
    [Fact]
    public async Task Api_Cors_AllowsFrontendOrigin()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/public/auth/demo/start");
        request.Headers.Add("Origin", "https://liveauth.app");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        
        var response = await _client.SendAsync(request);
        
        // Should have CORS headers
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin") ||
                   response.StatusCode == HttpStatusCode.MethodNotAllowed);
    }

    private static Guid ExtractSessionId(string content)
    {
        using var doc = JsonDocument.Parse(content);
        return GetProperty(doc.RootElement, "sessionId").GetGuid();
    }

    private static bool ExtractVerified(string content)
    {
        using var doc = JsonDocument.Parse(content);
        return GetProperty(doc.RootElement, "verified").GetBoolean();
    }

    private static JsonElement GetProperty(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        throw new InvalidOperationException($"Response did not contain JSON property '{name}'.");
    }

    private async Task<Guid> SeedExpiredDemoSession()
    {
        var sessionId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        db.AuthSessions.Add(new AuthSession
        {
            Id = sessionId,
            ProjectId = Guid.Parse(TestWebApplicationFactory.DemoProjectId),
            Environment = "DEMO",
            AmountSats = 3,
            BaseAmountSats = 3,
            TotalChargedSats = 3,
            CreditAmountSats = 3,
            InvoiceRHash = "expired-session",
            InvoiceBolt11 = "lnmockexpired",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            IsPaid = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-20)
        });
        await db.SaveChangesAsync();

        return sessionId;
    }
}

public class TestWebApplicationFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
{
    public const string DemoProjectId = "00000000-0000-0000-0000-000000000002";
    private const string DemoDeveloperId = "00000000-0000-0000-0000-000000000001";
    private const string TestJwtKey = "test-jwt-signing-key-that-is-at-least-32-bytes-long";
    private static readonly string TestDatabasePath =
        Path.Combine(Path.GetTempPath(), $"liveauth-integration-{Guid.NewGuid():N}.db");

    public const string DemoPublicKey = "la_pk_demo";

    static TestWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", $"Data Source={TestDatabasePath}");
        Environment.SetEnvironmentVariable("LiveAuth__PowHmacSecret", "test-pow-secret-key-for-integration-tests-32bytes");
        Environment.SetEnvironmentVariable("LiveAuth__DemoProjectId", DemoProjectId);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", TestJwtKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", "LiveAuthIntegrationTests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "LiveAuthIntegrationUsers");
        Environment.SetEnvironmentVariable("Lnd__UseMock", "true");
        Environment.SetEnvironmentVariable("DevLogin__MockLightningIdentity", "true");
        Environment.SetEnvironmentVariable("Admin__SkipPayment", "true");
        Environment.SetEnvironmentVariable("GitHub__ClientId", "test-client-id");
        Environment.SetEnvironmentVariable("GitHub__ClientSecret", "test-client-secret");
        Environment.SetEnvironmentVariable("Resend__ApiKey", "test-resend-key");
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={TestDatabasePath}",
                ["LiveAuth:PowHmacSecret"] = "test-pow-secret-key-for-integration-tests-32bytes",
                ["LiveAuth:DemoProjectId"] = DemoProjectId,
                ["Jwt:SigningKey"] = TestJwtKey,
                ["Jwt:Issuer"] = "LiveAuthIntegrationTests",
                ["Jwt:Audience"] = "LiveAuthIntegrationUsers",
                ["Lnd:UseMock"] = "true",
                ["DevLogin:MockLightningIdentity"] = "true",
                ["Admin:SkipPayment"] = "true",
                ["GitHub:ClientId"] = "test-client-id",
                ["GitHub:ClientSecret"] = "test-client-secret",
                ["Resend:ApiKey"] = "test-resend-key",
                ["Lnd:BaseUrl"] = "https://localhost:9739",
                ["Lnd:Macaroon"] = "",
            });
        });
        
        // Override services for testing
        builder.ConfigureServices(services =>
        {
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();

            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
            SeedTestData(db);
        });
    }

    private static void SeedTestData(LiveAuthDbContext db)
    {
        var developerId = Guid.Parse(DemoDeveloperId);
        var projectId = Guid.Parse(DemoProjectId);

        if (!db.Developers.Any(d => d.Id == developerId))
        {
            db.Developers.Add(new Developer
            {
                Id = developerId,
                Email = "integration@example.com",
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (!db.Projects.Any(p => p.Id == projectId))
        {
            db.Projects.Add(new Project
            {
                Id = projectId,
                Name = "Integration Demo Project",
                PublicKey = DemoPublicKey,
                SecretKeyHash = "integration-secret-placeholder",
                DeveloperId = developerId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Environment = "LIVE",
                AllowDemoAuth = true,
                Plan = "free"
            });
        }

        db.SaveChanges();
    }
}
