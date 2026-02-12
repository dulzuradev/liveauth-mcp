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
// DbContext (PostgreSQL)
// --------------------------------------------------
var connectionString =
    builder.Configuration.GetConnectionString("LiveAuth")
    ?? throw new InvalidOperationException("Missing LiveAuth connection string");

builder.Services.AddDbContextFactory<LiveAuthDbContext>(
    opts => opts.UseNpgsql(connectionString),
    ServiceLifetime.Scoped);

builder.Services.AddDbContext<LiveAuthDbContext>(
    opts => opts.UseNpgsql(connectionString));

// --------------------------------------------------
// Core services
// --------------------------------------------------
builder.Services.AddControllers().AddApplicationPart(typeof(HealthController).Assembly);

builder.Services.AddSingleton<StripeService>();
builder.Services.AddSingleton<OpenNodeService>();
builder.Services.AddSingleton<PowAttemptLogger>();
builder.Services.AddSingleton<PowChallengeSigner>();
builder.Services.AddSingleton<PowRateLimitService>();

builder.Services.AddScoped<LightningService>();
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
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();

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
    scope.ServiceProvider
        .GetRequiredService<LiveAuthDbContext>()
        .Database.EnsureCreated();
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

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<PublicKeyAuthMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapControllers();
app.Run();

// Make Program class accessible to test projects
public partial class Program { }
