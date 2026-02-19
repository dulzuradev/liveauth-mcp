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

var builder = WebApplication.CreateBuilder(args);

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

// Sats Printer Service
builder.Services.AddScoped<SatsPrinterService>(); // It will resolve IHttpClientFactory automatically


// --------------------------------------------------
// Authentication (API Key OR JWT)
// --------------------------------------------------
builder.Services
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

// --------------------------------------------------
// Authorization
// --------------------------------------------------
builder.Services.AddAuthorization();

// --------------------------------------------------
// CORS
// --------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("LiveAuthCors", policy =>
        policy.WithOrigins(
                "https://liveauth.app",
                "https://dev.liveauth.app",
                "http://localhost:4200"
            )
            .AllowAnyHeader()
            .AllowAnyMethod());
});

// --------------------------------------------------
// Swagger (dev only)
// --------------------------------------------------
if (builder.Environment.IsDevelopment())
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

        context.Response.StatusCode =
            ex is UnauthorizedAccessException
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status500InternalServerError;

        await context.Response.WriteAsJsonAsync(new
        {
            error = "Unauthorized or invalid token"
        });
    });
});

// --------------------------------------------------
// Pipeline
// --------------------------------------------------
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("LiveAuthCors");

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
