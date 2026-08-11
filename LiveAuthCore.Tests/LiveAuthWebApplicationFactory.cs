namespace LiveAuthCore.Tests;

using LiveAuthCore.Data;
using LiveAuthCore.Tests.Mocks;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Text;
using LiveAuthCore.Bitcoin.Rpc;
using LiveAuthCore.Tests.Bitcoin;


/// <summary>
/// Custom WebApplicationFactory for integration testing.
/// Uses in-memory database with comprehensive mocking.
/// </summary>
public class LiveAuthWebApplicationFactory : WebApplicationFactory<Program>
{
    private static readonly string TestJwtKey = "test-jwt-signing-key-that-is-at-least-32-bytes-long";
    
    static LiveAuthWebApplicationFactory()
    {
        // Set environment variables BEFORE the app starts
        Environment.SetEnvironmentVariable("DB_PROVIDER", "sqlite");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", "Data Source=test.db");
        Environment.SetEnvironmentVariable("LiveAuth__PowHmacSecret", "test-pow-secret-key-for-testing-only-32bytes");
        Environment.SetEnvironmentVariable("LiveAuth__DemoProjectId", "00000000-0000-0000-0000-000000000002");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", TestJwtKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", "LiveAuthTest");
        Environment.SetEnvironmentVariable("Jwt__Audience", "LiveAuthTestUsers");
        Environment.SetEnvironmentVariable("Lnd__UseMock", "true");
        Environment.SetEnvironmentVariable("DevLogin__MockLightningIdentity", "true");
        Environment.SetEnvironmentVariable("UseMockLightning", "true");
        Environment.SetEnvironmentVariable("Admin__SkipPayment", "true");
        Environment.SetEnvironmentVariable("GitHub__ClientId", "test-client-id");
        Environment.SetEnvironmentVariable("GitHub__ClientSecret", "test-client-secret");
        Environment.SetEnvironmentVariable("Resend__ApiKey", "test-resend-key");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_PROVIDER"] = "sqlite",
                ["ConnectionStrings:Default"] = "Data Source=test.db",
                ["LiveAuth:PowHmacSecret"] = "test-pow-secret-key-for-testing-only-32bytes",
                ["LiveAuth:DemoProjectId"] = "00000000-0000-0000-0000-000000000002",
                ["Jwt:SigningKey"] = TestJwtKey,
                ["Jwt:Issuer"] = "LiveAuthTest",
                ["Jwt:Audience"] = "LiveAuthTestUsers",
                ["Lnd:UseMock"] = "true",
                ["DevLogin:MockLightningIdentity"] = "true",
                ["UseMockLightning"] = "true",
                ["Admin:SkipPayment"] = "true",
                ["GitHub:ClientId"] = "test-client-id",
                ["GitHub:ClientSecret"] = "test-client-secret",
                ["Resend:ApiKey"] = "test-resend-key",
                ["Lnd:BaseUrl"] = "https://localhost:9739",
                ["Lnd:Macaroon"] = "",
                ["BitcoinGateway:Enabled"] = "true",
                ["BitcoinGateway:Network"] = "regtest",
                ["BitcoinGateway:RpcUser"] = "test-rpc-user",
                ["BitcoinGateway:RpcPassword"] = "test-rpc-password",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<LiveAuthDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Add in-memory database. Keep one database name for this factory instance
            // so test setup scopes and the TestServer app see the same seeded data.
            var databaseName = $"LiveAuthTestDb_{Guid.NewGuid():N}";
            services.AddDbContext<LiveAuthDbContext>(options =>
            {
                options
                    .UseInMemoryDatabase(databaseName)
                    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });

            // Remove real Lightning service and add mock
            var lightningDescriptor = services.Single(d => d.ServiceType == typeof(LightningService));
            services.Remove(lightningDescriptor);
            
            services.AddSingleton<LightningService, MockLightningService>();

            // Configure the app's existing Bearer scheme for test-issued JWTs.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey))
                };
            });

            services.RemoveAll<IHostedService>();
            services.RemoveAll<IBitcoinNodeClient>();
            services.AddSingleton<IBitcoinNodeClient, TestBitcoinNodeClient>();
            ConfigureExternalHttpFakes(services);

            services.AddAuthorization();

            // Build and seed
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
            db.Database.EnsureCreated();
            SeedTestData(db);
        });
    }
    
    private static void SeedTestData(LiveAuthDbContext db)
    {
        // Demo project
        if (!db.Projects.Any(p => p.Id == Guid.Parse("00000000-0000-0000-0000-000000000002")))
        {
            db.Projects.Add(new Data.Entities.Project
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Name = "Demo Project",
                PublicKey = "demo_pk_test",
                SecretKeyHash = "demo_sk_test",
                DeveloperId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Plan = "free"
            });
            
            // Add developer for project
            db.Developers.Add(new Data.Entities.Developer
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Email = "test@example.com",
                CreatedAt = DateTime.UtcNow
            });
        }

        // Admin
        if (!db.AdminSessions.Any(a => a.Username == "admin"))
        {
            db.AdminSessions.Add(new Data.Entities.AdminSession
            {
                Id = Guid.NewGuid(),
                Username = "admin",
                PasswordHash = "hashedpassword",
                PasswordSalt = "testsalt",
                IsOwner = true,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddYears(1)
            });
        }

        db.SaveChanges();
    }

    private static void ConfigureExternalHttpFakes(IServiceCollection services)
    {
        services.AddHttpClient("coingecko")
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(_ =>
                JsonResponse(HttpStatusCode.OK, """{"bitcoin":{"usd":65000.0}}""")));

        services.AddHttpClient("coinbase")
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(_ =>
                JsonResponse(HttpStatusCode.OK, """{"data":{"amount":"65000.00"}}""")));

        services.AddHttpClient<EmailService>()
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(_ =>
                JsonResponse(HttpStatusCode.Accepted, "{}")));

        services.AddHttpClient("webhooks")
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(_ =>
                JsonResponse(HttpStatusCode.OK, "{}")));

        services.RemoveAll<IGitHubOAuthClient>();
        services.AddSingleton<IGitHubOAuthClient, TestGitHubOAuthClient>();
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class TestGitHubOAuthClient : IGitHubOAuthClient
    {
        public Task<GitHubOAuthProfile?> GetProfileAsync(
            string clientId,
            string clientSecret,
            string code,
            string redirectUri,
            CancellationToken ct)
        {
            if (string.Equals(code, "fail-token", StringComparison.Ordinal))
                return Task.FromResult<GitHubOAuthProfile?>(null);

            var normalizedCode = NormalizeCode(code);
            return Task.FromResult<GitHubOAuthProfile?>(new GitHubOAuthProfile(
                Id: $"github-{normalizedCode}",
                Login: $"login-{normalizedCode}",
                Email: $"{normalizedCode}@github.test"));
        }

        private static string NormalizeCode(string code)
        {
            var chars = code
                .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
                .ToArray();
            return new string(chars).Trim('-');
        }
    }
}
