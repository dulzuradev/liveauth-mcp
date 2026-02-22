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
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        
        builder.ConfigureAppConfiguration(config =>
        {
            // Add test-specific configuration to enable mocks
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lnd:UseMock"] = "true",
                ["DevLogin:MockLightningIdentity"] = "true",
                ["UseMockLightning"] = "true"
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
        });
    }
}
