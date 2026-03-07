namespace LiveAuthCore.Tests;

using LiveAuthCore.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Custom WebApplicationFactory for integration testing.
/// Uses in-memory database to avoid hitting real Postgres.
/// </summary>
public class LiveAuthWebApplicationFactory : WebApplicationFactory<Program>
{
    static LiveAuthWebApplicationFactory()
    {
        // Set environment variables BEFORE the app starts
        // This is required because Program.cs validates config at startup
        Environment.SetEnvironmentVariable("DB_PROVIDER", "sqlite");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", "Data Source=test.db");
        Environment.SetEnvironmentVariable("LiveAuth__PowHmacSecret", "test-pow-secret-key-for-testing-only-32bytes");
        Environment.SetEnvironmentVariable("LiveAuth__DemoProjectId", "00000000-0000-0000-0000-000000000002");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "test-jwt-signing-key-that-is-at-least-32-bytes-long");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "LiveAuthTest");
        Environment.SetEnvironmentVariable("Jwt__Audience", "LiveAuthTestUsers");
        Environment.SetEnvironmentVariable("Lnd__UseMock", "true");
        Environment.SetEnvironmentVariable("DevLogin__MockLightningIdentity", "true");
        Environment.SetEnvironmentVariable("UseMockLightning", "true");
        Environment.SetEnvironmentVariable("Admin__SkipPayment", "true");
        Environment.SetEnvironmentVariable("GitHub__ClientId", "test-client-id");
        Environment.SetEnvironmentVariable("GitHub__ClientSecret", "test-client-secret");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        
        builder.ConfigureAppConfiguration(config =>
        {
            // Add test-specific configuration to enable mocks
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Database
                ["DB_PROVIDER"] = "sqlite",
                ["ConnectionStrings:Default"] = "Data Source=test.db",
                
                // LiveAuth required
                ["LiveAuth:PowHmacSecret"] = "test-pow-secret-key-for-testing-only-32bytes",
                ["LiveAuth:DemoProjectId"] = "00000000-0000-0000-0000-000000000002",
                
                // JWT required
                ["Jwt:SigningKey"] = "test-jwt-signing-key-that-is-at-least-32-bytes-long",
                ["Jwt:Issuer"] = "LiveAuthTest",
                ["Jwt:Audience"] = "LiveAuthTestUsers",
                
                // Lightning (mock)
                ["Lnd:UseMock"] = "true",
                ["DevLogin:MockLightningIdentity"] = "true",
                ["UseMockLightning"] = "true",
                
                // Admin
                ["Admin:SkipPayment"] = "true",
                
                // GitHub OAuth (mock)
                ["GitHub:ClientId"] = "test-client-id",
                ["GitHub:ClientSecret"] = "test-client-secret",
                
                // LND
                ["Lnd:BaseUrl"] = "https://localhost:9739",
                ["Lnd:Macaroon"] = "",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the real DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<LiveAuthDbContext>));
            
            if (descriptor != null)
                services.Remove(descriptor);

            // Add in-memory database for testing
            services.AddDbContext<LiveAuthDbContext>(options =>
            {
                options.UseInMemoryDatabase("LiveAuthTestDb");
            });

            // Build the service provider
            var sp = services.BuildServiceProvider();

            // Create a scope to get the DbContext and seed data
            using var scope = sp.CreateScope();
            var scopedServices = scope.ServiceProvider;
            var db = scopedServices.GetRequiredService<LiveAuthDbContext>();

            // Ensure the database is created
            db.Database.EnsureCreated();
            
            // Seed a demo project
            SeedTestData(db);
        });
    }
    
    private static void SeedTestData(LiveAuthDbContext db)
    {
        // Add demo project
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
            db.SaveChanges();
        }
    }
}
