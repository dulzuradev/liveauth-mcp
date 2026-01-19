using System.Text;
using LiveAuthCore.Auth;
using LiveAuthCore.Data;
using LiveAuthCore.Entities;
using LiveAuthCore.Middleware;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------
// DbContexts (separate SQLite files by design)
// --------------------------------------------------
builder.Services.AddDbContext<LiveAuthDbContext>(opts =>
    opts.UseSqlite("Data Source=liveauth.db"));

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite("Data Source=lightningcaptcha.db"));

// --------------------------------------------------
// Core services / DI
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

            if (!string.IsNullOrWhiteSpace(auth) &&
                auth.StartsWith("Bearer la_sk_", StringComparison.OrdinalIgnoreCase))
            {
                return ApiKeyAuthOptions.SchemeName;
            }

            return JwtBearerDefaults.AuthenticationScheme;
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
        {
            throw new InvalidOperationException(
                "JWT signing key missing. Configure Jwt:SigningKey or Jwt:Key.");
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),

            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],

            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

// --------------------------------------------------
// Authorization
// --------------------------------------------------
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Admin");
        policy.RequireClaim("scope", "admin");
    });
});

// --------------------------------------------------
// CORS (SINGLE authoritative policy)
// --------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("LiveAuthCors", policy =>
    {
        policy
            .WithOrigins(
                "https://liveauth.app",
                "https://dev.liveauth.app",
                "http://localhost:4200"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// --------------------------------------------------
// Swagger (Development only)
// --------------------------------------------------
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "LiveAuth API",
            Version = "v1",
            Description = "Developer Lightning verification + admin APIs"
        });

        options.AddSecurityDefinition("JwtBearer", new OpenApiSecurityScheme
        {
            In = ParameterLocation.Header,
            Description = "Bearer {JWT}",
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });

        options.AddSecurityDefinition("ApiKeyBearer", new OpenApiSecurityScheme
        {
            In = ParameterLocation.Header,
            Description = "Bearer la_sk_...",
            Name = "Authorization",
            Type = SecuritySchemeType.ApiKey
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "JwtBearer"
                    }
                },
                Array.Empty<string>()
            },
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "ApiKeyBearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });
}

var app = builder.Build();

// --------------------------------------------------
// DB Initialization / Safety Guards
// --------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var liveAuthDb = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
    liveAuthDb.Database.EnsureCreated();

    try
    {
        liveAuthDb.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ProjectApiKeys (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                Label TEXT NOT NULL,
                PublicKey TEXT NOT NULL,
                SecretKeyHash TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                LastUsedAt TEXT NULL,
                IsActive INTEGER NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_ProjectApiKeys_ProjectId
            ON ProjectApiKeys (ProjectId);

            CREATE TABLE IF NOT EXISTS DevLoginSessions (
                Id TEXT NOT NULL PRIMARY KEY,
                Email TEXT NOT NULL,
                InvoiceId TEXT NOT NULL,
                InvoiceBolt11 TEXT NOT NULL,
                AmountSats INTEGER NOT NULL,
                ExpiresAt TEXT NOT NULL,
                IsPaid INTEGER NOT NULL,
                PaidAt TEXT NULL,
                PayerLightningAuthKey TEXT NULL
            );
        ");
    }
    catch { /* guard only */ }

    var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    appDb.Database.EnsureCreated();
}

// --------------------------------------------------
// HTTP Pipeline (CORRECT ORDER)
// --------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "LiveAuth API v1");
        c.RoutePrefix = string.Empty;
    });
}

// Caddy terminates TLS; safe to keep but only once
app.UseHttpsRedirection();

app.UseRouting();

app.UseCors("LiveAuthCors");

app.UseAuthentication();
app.UseAuthorization();

// Custom auth middleware AFTER auth + CORS
app.UseMiddleware<PublicKeyAuthMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapControllers();

app.Run();
