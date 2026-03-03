using System.Text;
using System.Security.Claims;
using LiveAuthCore.Auth;
using LiveAuthCore.Controllers;
using LiveAuthCore.Data;
using LiveAuthCore.Middleware;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using AspNet.Security.OAuth.GitHub;

// --------------------------------------------------
// CONFIGURATION VALIDATION - Fail fast on missing required config
// --------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// Validate required configs
var requiredConfigs = new (string Name, string? Value)[]
{
    ("DB_PROVIDER", builder.Configuration["DB_PROVIDER"]),
    ("ConnectionStrings:Default", builder.Configuration["ConnectionStrings:Default"]),
    ("LiveAuth:PowHmacSecret", builder.Configuration["LiveAuth:PowHmacSecret"]),
    ("LiveAuth:DemoProjectId", builder.Configuration["LiveAuth:DemoProjectId"]),
    ("Jwt:SigningKey", builder.Configuration["Jwt:SigningKey"] ?? builder.Configuration["Jwt:Key"]),
};

var missingConfigs = requiredConfigs.Where(c => string.IsNullOrWhiteSpace(c.Value)).Select(c => c.Name).ToList();

if (missingConfigs.Any())
{
    var error = $"[FATAL] Missing required configuration: {string.Join(", ", missingConfigs)}. Set via environment variables.";
    Console.Error.WriteLine(error);
    // Fail fast in dev, or if critical configs missing
    if (builder.Environment.IsDevelopment() || missingConfigs.Any(c => c is "LiveAuth:DemoProjectId" or "LiveAuth:PowHmacSecret"))
    {
        throw new InvalidOperationException(error);
    }
}

// Validate Lightning config
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

Console.WriteLine($"[CONFIG] DB Provider: {builder.Configuration["DB_PROVIDER"] ?? "sqlite"}");
Console.WriteLine($"[CONFIG] Demo Project ID: {builder.Configuration["LiveAuth:DemoProjectId"] ?? "(not set)"}");
Console.WriteLine($"[CONFIG] LND UseMock: {lndUseMock}");
Console.WriteLine($"[CONFIG] JWT Issuer: {builder.Configuration["Jwt:Issuer"] ?? "(not set, using default)"}");

// --------------------------------------------------
// DbContext (PostgreSQL or SQLite via env)
// --------------------------------------------------
var pg = builder.Configuration.GetConnectionString("LiveAuth");
var sqlite = builder.Configuration.GetConnectionString("Default");
var provider = (builder.Configuration["DB_PROVIDER"] ?? (pg != null ? "postgres" : "sqlite")).ToLowerInvariant();

if (provider == "postgres")
{
    if (string.IsNullOrWhiteSpace(pg))
        throw new InvalidOperationException("Missing LiveAuth (Postgres) connection string");

    builder.Services.AddDbContextFactory<LiveAuthDbContext>(
        opts => opts.UseNpgsql(pg),
        ServiceLifetime.Scoped);
    builder.Services.AddDbContext<LiveAuthDbContext>(
        opts => opts.UseNpgsql(pg));
}
else
{
    var sqliteConn = !string.IsNullOrWhiteSpace(sqlite) ? sqlite : "Data Source=liveauth.db";
    builder.Services.AddDbContextFactory<LiveAuthDbContext>(
        opts => opts.UseSqlite(sqliteConn),
        ServiceLifetime.Scoped);
    builder.Services.AddDbContext<LiveAuthDbContext>(
        opts => opts.UseSqlite(sqliteConn));
}

// --------------------------------------------------
// Core services
// --------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Accept camelCase from clients
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    })
    .AddApplicationPart(typeof(HealthController).Assembly);

builder.Services.AddSingleton<StripeService>();
builder.Services.AddSingleton<OpenNodeService>();
builder.Services.AddSingleton<PowAttemptLogger>();
builder.Services.AddSingleton<PowChallengeSigner>();
builder.Services.AddSingleton<PowRateLimitService>();

builder.Services.AddScoped<LightningService>();
builder.Services.AddScoped<L402Service>(); // L402 Payment Gateway
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddScoped<DeveloperVerificationService>();
builder.Services.AddScoped<DeveloperAuthService>();
builder.Services.AddScoped<AuthEventService>();
builder.Services.AddScoped<PowDifficultyService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<PowReplayService>();
builder.Services.AddScoped<WebhookService>();

builder.Services.AddHostedService<DevLoginSessionCleanupService>();
builder.Services.AddHostedService<WebhookDeliveryWorker>();
builder.Services.AddHostedService<PowNonceCleanupService>();

builder.Services.AddHttpClient("webhooks");
builder.Services.AddHttpClient("cashu");
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();

// Sats Printer Services
builder.Services.AddScoped<SatsPrinterService>(); // Cashu-based
builder.Services.AddScoped<AgentSatsService>();    // LND-based


// --------------------------------------------------
// Authentication (API Key OR JWT OR GitHub)
// --------------------------------------------------
var githubClientId = builder.Configuration["GitHub:ClientId"];
var githubClientSecret = builder.Configuration["GitHub:ClientSecret"];

var authBuilder = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "LiveAuthPolicy";
        options.DefaultChallengeScheme = "LiveAuthPolicy";
    })
    .AddPolicyScheme("LiveAuthPolicy", "API Key or JWT", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var auth = context.Request.Headers["Authorization"].ToString();
            return auth.StartsWith("Bearer la_sk_", StringComparison.OrdinalIgnoreCase)
                ? ApiKeyAuthOptions.SchemeName
                : JwtBearerDefaults.AuthenticationScheme;
        };
    })
    .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(
        ApiKeyAuthOptions.SchemeName, _ => { })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        var jwtKey =
            builder.Configuration["Jwt:SigningKey"] ??
            builder.Configuration["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(jwtKey))
            throw new InvalidOperationException("JWT signing key missing.");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),

            ValidateIssuer = true,
            ValidIssuer = "LiveAuth",

            ValidateAudience = true,
            ValidAudience = "LiveAuthUsers",

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),

            RoleClaimType = ClaimTypes.Role,
            NameClaimType = "userId"
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                Console.Error.WriteLine($"JWT auth failed: {ctx.Exception}");
                return Task.CompletedTask;
            }
        };
    });

// GitHub OAuth is handled manually in DeveloperAuthController
// (not using middleware for this flow)

// --------------------------------------------------
// Authorization
// --------------------------------------------------
builder.Services.AddAuthorization();

// --------------------------------------------------
// CORS
// --------------------------------------------------
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
}

var app = builder.Build();

// --------------------------------------------------
// DB initialization
// --------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
    db.Database.EnsureCreated();
    
    // Create MCP tables if they don't exist (for existing databases)
    if (db.Database.IsSqlite())
    {
        var connection = db.Database.GetDbConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS PowUsedNonces (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId TEXT NOT NULL,
                ChallengeHex TEXT NOT NULL,
                Nonce TEXT NOT NULL,
                ExpiresAt INTEGER NOT NULL,
                UsedAt TEXT NOT NULL
            );
            
            CREATE TABLE IF NOT EXISTS McpGateSessions (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                PowChallengeHex TEXT,
                PowDifficultyBits INTEGER,
                PowExpiresAtUnix INTEGER,
                PowSignature TEXT,
                LightningInvoice TEXT,
                LightningPaymentHash TEXT,
                SatsPerCallAtStart INTEGER NOT NULL,
                Status TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS McpGateTokens (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                SessionId TEXT NOT NULL,
                JwtId TEXT NOT NULL,
                RefreshToken TEXT,
                IssuedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                CallsUsed INTEGER NOT NULL,
                SatsUsed INTEGER NOT NULL,
                MaxCallsPerMinute INTEGER NOT NULL,
                MaxSatsPerDay INTEGER NOT NULL,
                DayWindowStart TEXT NOT NULL,
                Status TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            
            CREATE TABLE IF NOT EXISTS AdminSessions (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                PasswordSalt TEXT NOT NULL,
                IsPaid INTEGER NOT NULL DEFAULT 0,
                Token TEXT,
                CreatedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL
            );
            
            CREATE TABLE IF NOT EXISTS AdminPaymentSessions (
                Id TEXT PRIMARY KEY,
                AmountSats INTEGER NOT NULL,
                InvoiceBolt11 TEXT NOT NULL,
                InvoiceRHash TEXT NOT NULL,
                IsPaid INTEGER NOT NULL DEFAULT 0,
                PaidAt TEXT,
                CreatedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL
            );
            -- Recreate MintRequests table if it exists (drop and recreate with all columns)
            DROP TABLE IF EXISTS MintRequests;
            CREATE TABLE MintRequests (
                Id TEXT PRIMARY KEY,
                UserId TEXT NOT NULL,
                MintUrl TEXT NOT NULL,
                Amount INTEGER NOT NULL,
                PaymentHash TEXT,
                Invoice TEXT,
                Status TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS UserEcashBalances (
                Id TEXT PRIMARY KEY,
                UserId TEXT NOT NULL,
                MintUrl TEXT NOT NULL,
                Balance INTEGER NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS MintProviders (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Url TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS EcashProofs (
                Id TEXT PRIMARY KEY,
                MintUrl TEXT NOT NULL,
                Amount INTEGER NOT NULL,
                Secret TEXT NOT NULL,
                C TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
        ";
        cmd.ExecuteNonQuery();
    }
}

// --------------------------------------------------
// Global exception handling
// --------------------------------------------------
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetService<ILogger<Program>>();
        
        // Log the actual error with appropriate level
        if (ex is UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Unauthorized access attempt");
        }
        else
        {
            logger?.LogError(ex, "Unhandled exception in request {Method} {Path}", 
                context.Request.Method, context.Request.Path);
        }

        if (builder.Environment.IsDevelopment())
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new
            {
                error = ex?.Message,
                stack = ex?.StackTrace
            });
            return;
        }

        // Production: return proper status code but don't leak details
        context.Response.StatusCode =
            ex is UnauthorizedAccessException
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status500InternalServerError;

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = ex is UnauthorizedAccessException 
                ? "Unauthorized or invalid token" 
                : "An unexpected error occurred"
        });
    });
});

// --------------------------------------------------
// Pipeline
// --------------------------------------------------
app.UseHttpsRedirection();
app.UseRouting();

// Custom auth middleware BEFORE ASP.NET authentication
// This handles public endpoints (pow, auth) that need API key validation
app.UseMiddleware<PublicKeyAuthMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseL402(); // L402 Payment Gateway

app.MapControllers();
app.Run();

// Make Program class accessible to test projects
public partial class Program { }
