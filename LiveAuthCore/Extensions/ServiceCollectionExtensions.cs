using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;
using LiveAuthCore.Auth;
using LiveAuthCore.Data;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace LiveAuthCore.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Validates required configuration and returns missing config names.
    /// Only truly critical configs that must be set: PowHmacSecret, DemoProjectId, Jwt:SigningKey.
    /// DB_PROVIDER and ConnectionStrings:Default have sensible defaults (SQLite).
    /// </summary>
    public static List<string> GetMissingConfigs(this WebApplicationBuilder builder)
    {
        var requiredConfigs = new (string Name, string? Value)[]
        {
            ("LiveAuth:PowHmacSecret", builder.Configuration["LiveAuth:PowHmacSecret"]),
            ("LiveAuth:DemoProjectId", builder.Configuration["LiveAuth:DemoProjectId"]),
            ("Jwt:SigningKey", builder.Configuration["Jwt:SigningKey"] ?? builder.Configuration["Jwt:Key"]),
        };

        return requiredConfigs
            .Where(c => string.IsNullOrWhiteSpace(c.Value))
            .Select(c => c.Name)
            .ToList();
    }

    /// <summary>
    /// Logs current configuration state for debugging.
    /// </summary>
    public static void LogConfigState(this WebApplicationBuilder builder)
    {
        var lndUseMock = builder.Configuration["Lnd:UseMock"]?.ToLowerInvariant() == "true";
        
        Console.WriteLine($"[CONFIG] DB Provider: sqlite");
        Console.WriteLine($"[CONFIG] Demo Project ID: {builder.Configuration["LiveAuth:DemoProjectId"] ?? "(not set)"}");
        Console.WriteLine($"[CONFIG] LND UseMock: {lndUseMock}");
        Console.WriteLine($"[CONFIG] JWT Issuer: {builder.Configuration["Jwt:Issuer"] ?? "(not set, using default)"}");
    }

    /// <summary>
    /// Validates Lightning config and logs warnings if LND is configured without mock.
    /// </summary>
    public static void ValidateLightningConfig(this WebApplicationBuilder builder)
    {
        var lndUseMock = builder.Configuration["Lnd:UseMock"]?.ToLowerInvariant() == "true";
        if (!lndUseMock)
        {
            if (string.IsNullOrWhiteSpace(builder.Configuration["Lnd:BaseUrl"]))
            {
                Console.Error.WriteLine("[WARNING] Lnd:UseMock is false but Lnd:BaseUrl is not configured. Lightning payments will fail.");
            }
            if (string.IsNullOrWhiteSpace(builder.Configuration["Lnd:Macaroon"]))
            {
                Console.Error.WriteLine("[WARNING] Lnd:UseMock is false but Lnd:Macaroon is not configured. Lightning payments will fail.");
            }
        }
    }

    /// <summary>
    /// Validates that dangerous debug flags (SkipPayment, UseMock) are not enabled in production.
    /// Throws on first dangerous flag found to prevent accidental production misconfigurations.
    /// </summary>
    public static void ValidateProductionSafety(this WebApplicationBuilder builder)
    {
        if (builder.Environment.IsProduction())
        {
            var dangerousFlags = new List<(string Name, string? Value, string Message)>();

            // Check Admin__SkipPayment
            var skipPayment = builder.Configuration["Admin:SkipPayment"] ?? 
                             builder.Configuration["Admin__SkipPayment"];
            if (skipPayment?.ToLowerInvariant() == "true")
            {
                dangerousFlags.Add(("Admin:SkipPayment", skipPayment, 
                    "Payment verification is BYPASSED - users can upgrade without paying!"));
            }

            // Check Lnd__UseMock
            var useMock = builder.Configuration["Lnd:UseMock"] ?? 
                         builder.Configuration["Lnd__UseMock"];
            if (useMock?.ToLowerInvariant() == "true")
            {
                dangerousFlags.Add(("Lnd:UseMock", useMock, 
                    "Lightning is in MOCK mode - no real payments accepted!"));
            }

            // Check Lnd__UseSimulated
            var useSimulated = builder.Configuration["Lnd:UseSimulated"] ?? 
                              builder.Configuration["Lnd__UseSimulated"];
            if (useSimulated?.ToLowerInvariant() == "true")
            {
                dangerousFlags.Add(("Lnd:UseSimulated", useSimulated, 
                    "Lightning is in SIMULATED mode - payments are fake!"));
            }

            if (dangerousFlags.Any())
            {
                var errorLines = new[]
                {
                    "",
                    "[FATAL] DANGEROUS CONFIGURATION IN PRODUCTION!",
                    "=========================================="
                };
                foreach (var flag in dangerousFlags)
                {
                    errorLines = errorLines.Append($"[FATAL] {flag.Name}={flag.Value} - {flag.Message}").ToArray();
                }
                errorLines = errorLines.Append("==========================================").ToArray();
                errorLines = errorLines.Append("Remove the flag or set it to 'false' to proceed.").ToArray();
                errorLines = errorLines.Append("").ToArray();

                foreach (var line in errorLines)
                {
                    Console.Error.WriteLine(line);
                }

                throw new InvalidOperationException(
                    $"Dangerous configuration flags detected: {string.Join(", ", dangerousFlags.Select(f => f.Name))}. " +
                    "Fix before deploying to production.");
            }
        }
    }

    /// <summary>
    /// Adds LiveAuth database context (SQLite only).
    /// </summary>
    public static WebApplicationBuilder AddLiveAuthDb(this WebApplicationBuilder builder)
    {
        var sqliteConn = builder.Configuration.GetConnectionString("Default");
        var dbPath = !string.IsNullOrWhiteSpace(sqliteConn) ? sqliteConn : "Data Source=liveauth.db";
        
        builder.Services.AddDbContextFactory<LiveAuthDbContext>(
            opts => opts.UseSqlite(dbPath),
            ServiceLifetime.Scoped);
        builder.Services.AddDbContext<LiveAuthDbContext>(
            opts => opts.UseSqlite(dbPath));

        return builder;
    }

    /// <summary>
    /// Adds LiveAuth core services to the DI container.
    /// </summary>
    public static WebApplicationBuilder AddLiveAuthServices(this WebApplicationBuilder builder)
    {
        // Singleton services (shared state)
        builder.Services.AddSingleton<StripeService>();
        builder.Services.AddSingleton<PowAttemptLogger>();
        builder.Services.AddSingleton<PowChallengeSigner>();
        builder.Services.AddSingleton<PowRateLimitService>();
        builder.Services.AddSingleton<NostrService>();

        // Scoped services (per-request)
        builder.Services.AddScoped<LightningService>();
        builder.Services.AddScoped<L402Service>();
        builder.Services.AddScoped<ApiKeyService>();
        builder.Services.AddScoped<DeveloperVerificationService>();
        builder.Services.AddScoped<DeveloperAuthService>();
        builder.Services.AddScoped<AuthEventService>();
        builder.Services.AddScoped<PowDifficultyService>();
        builder.Services.AddScoped<BillingService>();
        builder.Services.AddScoped<PowReplayService>();
        builder.Services.AddScoped<WebhookService>();
        builder.Services.AddScoped<SatsPrinterService>();
        builder.Services.AddScoped<AgentSatsService>();
        builder.Services.AddHttpClient<EmailService>();

        // Hosted services
        builder.Services.AddHostedService<DevLoginSessionCleanupService>();
        builder.Services.AddHostedService<PowNonceCleanupService>();

        // HTTP clients
        builder.Services.AddHttpClient("webhooks", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        builder.Services.AddHttpClient("cashu");
        builder.Services.AddHttpClient<BtcExchangeRateService>();
        builder.Services.AddScoped<BtcExchangeRateService>();

        // Standard ASP.NET Core
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddMemoryCache();
        builder.Services.AddDistributedMemoryCache();

        // Rate limiting for auth endpoints
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellation) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                await Task.CompletedTask;
            };

            options.AddPolicy("auth:x10", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));
        });

        // Webhook delivery worker
        builder.Services.AddWebhookDeliveryWorker();

        return builder;
    }

    /// <summary>
    /// Adds CORS policy for Angular developer dashboard.
    /// </summary>
    public static WebApplicationBuilder AddLiveAuthCors(this WebApplicationBuilder builder)
    {
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAngular", policy =>
            {
                policy.WithOrigins("https://liveauth.app", "https://admin.liveauth.app")
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        return builder;
    }

    /// <summary>
    /// Adds Swagger/OpenAPI documentation.
    /// </summary>
    public static WebApplicationBuilder AddLiveAuthSwagger(this WebApplicationBuilder builder)
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "LiveAuth API",
                Version = "v1"
            });
        });

        return builder;
    }
}
