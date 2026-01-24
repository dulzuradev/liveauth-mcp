using System.Text;
using System.Security.Claims;
using LiveAuthCore.Auth;
using LiveAuthCore.Data;
using LiveAuthCore.Entities;
using LiveAuthCore.Middleware;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------
// DbContexts
// --------------------------------------------------
builder.Services.AddDbContext<LiveAuthDbContext>(opts =>
    opts.UseSqlite("Data Source=liveauth.db"));

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite("Data Source=lightningcaptcha.db"));

// --------------------------------------------------
// Core services
// --------------------------------------------------
builder.Services.AddControllers();

builder.Services.AddSingleton<StripeService>();
builder.Services.AddSingleton<OpenNodeService>();
builder.Services.AddSingleton<PowReplayProtectionService>();
builder.Services.AddSingleton<PowAttemptLogger>();
builder.Services.AddSingleton<PowChallengeSigner>();

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

builder.Services.AddHttpClient("webhooks");
builder.Services.AddHttpContextAccessor();
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
            // 🔐 Signature
            ValidateIssuerSigningKey = true,
            IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),

            // 🔥 MUST MATCH TOKEN CONTENTS
            ValidateIssuer = true,
            ValidIssuer = "LiveAuth",

            ValidateAudience = true,
            ValidAudience = "LiveAuthUsers",

            // ⏱ Lifetime
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),

            // 🔥 CRITICAL FIXES
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = "userId"
        };

        // Optional but VERY useful for debugging
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                Console.WriteLine($"JWT auth failed: {ctx.Exception.Message}");
                return Task.CompletedTask;
            }
        };
    });

// --------------------------------------------------
// Authorization
// --------------------------------------------------
builder.Services.AddAuthorization();

// --------------------------------------------------
// CORS (single authoritative policy)
// --------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("LiveAuthCors", policy =>
        policy.WithOrigins(
                "https://liveauth.app",
                "https://dev.liveauth.app",
                "http://localhost:49247"
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

    scope.ServiceProvider
        .GetRequiredService<AppDbContext>()
        .Database.EnsureCreated();
}

// --------------------------------------------------
// Global exception handling (ONCE)
// --------------------------------------------------
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        context.Response.StatusCode =
            exception is UnauthorizedAccessException
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status500InternalServerError;

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("""
        {
            "error": "Unauthorized or invalid token"
        }
        """);
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

// Custom auth middleware
app.UseMiddleware<PublicKeyAuthMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapControllers();

app.Run();
