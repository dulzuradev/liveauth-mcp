using System.Text;
using LiveAuthCore.Auth;
using LiveAuthCore.Data;
using LiveAuthCore.Entities;
using LiveAuthCore.Middleware;
using LiveAuthCore.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authorization;


var builder = WebApplication.CreateBuilder(args);

// --------------------
// DbContexts
// --------------------
// NOTE: Use a dedicated SQLite file for the LiveAuth (developers/projects) context.
// Using EnsureCreated() below with a shared database can skip table creation
// for additional contexts if the DB file already exists. A separate file avoids
// cross-context schema conflicts and fixes "no such table: Developers".
builder.Services.AddDbContext<LiveAuthDbContext>(opts =>
    opts.UseSqlite("Data Source=liveauth.db"));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=lightningcaptcha.db"));

// --------------------
// Core services / DI
// --------------------
builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<StripeService>();
builder.Services.AddSingleton<OpenNodeService>();
builder.Services.AddSingleton<PowReplayProtectionService>();
builder.Services.AddSingleton<PowAttemptLogger>();

builder.Services.AddScoped<LightningService>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddScoped<DeveloperVerificationService>();
builder.Services.AddScoped<DeveloperAuthService>();
builder.Services.AddHostedService<DevLoginSessionCleanupService>();
builder.Services.AddScoped<WebhookService>();
builder.Services.AddHostedService<WebhookDeliveryWorker>();
builder.Services.AddHttpClient("webhooks");
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthEventService>();
builder.Services.AddScoped<PowDifficultyService>();
builder.Services.AddScoped<BillingService>();

// --------------------
// Authentication (API Key + JWT)
// --------------------

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

            // Developer API key (secret)
            if (!string.IsNullOrWhiteSpace(auth) &&
                auth.StartsWith("Bearer la_sk_", StringComparison.OrdinalIgnoreCase))
                return ApiKeyAuthOptions.SchemeName;

            // Otherwise expect admin JWT
            return JwtBearerDefaults.AuthenticationScheme;
        };
    })
    .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(ApiKeyAuthOptions.SchemeName, _ => { })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        var jwtKey =
            builder.Configuration["Jwt:SigningKey"] ??
            builder.Configuration["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            throw new InvalidOperationException(
                "JWT signing key missing. Configure Jwt:SigningKey (preferred) or Jwt:Key.");
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            ),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Admin");
        policy.RequireClaim("scope", "admin");
    });
});

if (builder.Environment.IsDevelopment())
{
// --------------------
// Swagger / OpenAPI
// --------------------
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "LiveAuth API",
            Version = "v1",
            Description = "API documentation for developer Lightning verification + admin features."
        });

        // Admin JWT bearer
        options.AddSecurityDefinition("JwtBearer", new OpenApiSecurityScheme
        {
            In = ParameterLocation.Header,
            Description = "Admin JWT token. Format: Bearer {token}",
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            BearerFormat = "JWT",
            Scheme = "bearer"
        });

        // Developer API secret key
        options.AddSecurityDefinition("ApiKeyBearer", new OpenApiSecurityScheme
        {
            In = ParameterLocation.Header,
            Description = "Developer API secret key. Format: Bearer la_sk_...",
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
                new List<string>()
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
                new List<string>()
            }
        });
    });
}


// --------------------
// CORS
// --------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        policy.WithOrigins(
                "http://localhost:54383", 
                "http://localhost:4200",
                "https://liveauth.io",
                "https://www.liveauth.io",
                "http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });

    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddDistributedMemoryCache(); // v1 local dev
builder.Services.AddScoped<PowReplayService>();
builder.Services.AddSingleton<PowChallengeSigner>();

var app = builder.Build();

// --------------------
// DB Initialization
// --------------------
using (var scope = app.Services.CreateScope())
{
    var liveAuthDb = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
    liveAuthDb.Database.EnsureCreated();

    // TEMP schema guard for LiveAuthDbContext (SQLite):
    // If the ProjectApiKeys table hasn't been created yet (older DB file created via EnsureCreated),
    // create it to avoid runtime failures when inserting API keys.
    try
    {
        liveAuthDb.Database.ExecuteSqlRaw(
            @"CREATE TABLE IF NOT EXISTS ProjectApiKeys (
                Id TEXT NOT NULL CONSTRAINT PK_ProjectApiKeys PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                Label TEXT NOT NULL,
                PublicKey TEXT NOT NULL,
                SecretKeyHash TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                LastUsedAt TEXT NULL,
                IsActive INTEGER NOT NULL,
                CONSTRAINT FK_ProjectApiKeys_Projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_ProjectApiKeys_ProjectId ON ProjectApiKeys (ProjectId);

            -- Safety net for DevLoginSessions table when DB was created via EnsureCreated()
            CREATE TABLE IF NOT EXISTS DevLoginSessions (
                Id TEXT NOT NULL CONSTRAINT PK_DevLoginSessions PRIMARY KEY,
                Email TEXT NOT NULL,
                InvoiceId TEXT NOT NULL,
                InvoiceBolt11 TEXT NOT NULL,
                AmountSats INTEGER NOT NULL,
                ExpiresAt TEXT NOT NULL,
                IsPaid INTEGER NOT NULL,
                PaidAt TEXT NULL,
                PayerLightningAuthKey TEXT NULL
            );

            -- Safety net for LightningAuthKey on Developers table (older DBs created before the column existed)
            -- Attempt to add the column; if it already exists this statement will throw and be ignored by outer catch
        ");

        // Webhooks: add missing columns on Projects if DB predates migration
        try
        {
            liveAuthDb.Database.ExecuteSqlRaw(
                @"ALTER TABLE Projects ADD COLUMN WebhookUrl TEXT NULL;");
        }
        catch
        {
            // Column likely already exists; ignore.
        }

        try
        {
            liveAuthDb.Database.ExecuteSqlRaw(
                @"ALTER TABLE Projects ADD COLUMN WebhookSecret TEXT NULL;");
        }
        catch
        {
            // Column likely already exists; ignore.
        }

        // Environment column for Projects (TEST/LIVE). Older DBs will be missing this.
        try
        {
            liveAuthDb.Database.ExecuteSqlRaw(
                @"ALTER TABLE Projects ADD COLUMN Environment TEXT NOT NULL DEFAULT 'TEST';");
        }
        catch
        {
            // Column likely already exists; ignore.
        }

        // Add missing settings columns for older DBs (guarded)
        try
        {
            liveAuthDb.Database.ExecuteSqlRaw(
                @"ALTER TABLE Projects ADD COLUMN AllowedDomainsRaw TEXT NOT NULL DEFAULT '';"
            );
        }
        catch
        {
            // Column likely already exists; ignore.
        }

        try
        {
            liveAuthDb.Database.ExecuteSqlRaw(
                @"ALTER TABLE Projects ADD COLUMN SatsPerLogin INTEGER NOT NULL DEFAULT 0;"
            );
        }
        catch
        {
            // Column likely already exists; ignore.
        }

        try
        {
            liveAuthDb.Database.ExecuteSqlRaw(
                @"ALTER TABLE Projects ADD COLUMN MaxAuthsPerIpPerHour INTEGER NOT NULL DEFAULT 100;"
            );
        }
        catch
        {
            // Column likely already exists; ignore.
        }

        // Ensure AllowedDomainsJson column exists for older DBs created without migrations
        try
        {
            liveAuthDb.Database.ExecuteSqlRaw(
                @"ALTER TABLE Projects ADD COLUMN AllowedDomainsJson TEXT NULL;"
            );
        }
        catch
        {
            // Column likely already exists; ignore.
        }

        // Webhooks: ensure WebhookEvents table and indexes exist for older DBs
        liveAuthDb.Database.ExecuteSqlRaw(
            @"CREATE TABLE IF NOT EXISTS WebhookEvents (
                Id TEXT NOT NULL CONSTRAINT PK_WebhookEvents PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                NextAttemptAt TEXT NOT NULL,
                AttemptCount INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                LastAttemptAt TEXT NULL,
                LastStatusCode INTEGER NULL,
                LastError TEXT NULL,
                CONSTRAINT FK_WebhookEvents_Projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_WebhookEvents_ProjectId ON WebhookEvents (ProjectId);
            CREATE INDEX IF NOT EXISTS IX_WebhookEvents_EventType ON WebhookEvents (EventType);
            ");

        // AuthEvents: ensure table and indexes for older DBs created via EnsureCreated()
        liveAuthDb.Database.ExecuteSqlRaw(
            @"CREATE TABLE IF NOT EXISTS AuthEvents (
                Id TEXT NOT NULL CONSTRAINT PK_AuthEvents PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                ApiKeyId TEXT NULL,
                EventType INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                ClientIp TEXT NULL,
                Success INTEGER NOT NULL,
                SatsPaid INTEGER NULL,
                Reason TEXT NULL,
                CONSTRAINT FK_AuthEvents_Projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE,
                CONSTRAINT FK_AuthEvents_ProjectApiKeys_ApiKeyId FOREIGN KEY (ApiKeyId) REFERENCES ProjectApiKeys (Id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS IX_AuthEvents_ProjectId ON AuthEvents (ProjectId);
            CREATE INDEX IF NOT EXISTS IX_AuthEvents_ApiKeyId ON AuthEvents (ApiKeyId);
            CREATE INDEX IF NOT EXISTS IX_AuthEvents_EventType ON AuthEvents (EventType);
            ");

        try
        {
            liveAuthDb.Database.ExecuteSqlRaw(
                @"ALTER TABLE Developers ADD COLUMN LightningAuthKey TEXT NULL;");
        }
        catch
        {
            // Column likely already exists; ignore.
        }

        // Ensure unique partial index exists for LightningAuthKey (non-null values only)
        // SQLite supports partial indexes with WHERE clause
        liveAuthDb.Database.ExecuteSqlRaw(
            @"CREATE UNIQUE INDEX IF NOT EXISTS IX_Developers_LightningAuthKey
               ON Developers (LightningAuthKey)
               WHERE LightningAuthKey IS NOT NULL;");
    }
    catch
    {
        // Swallow to avoid blocking startup if the table already exists with a slightly different shape.
        // Future migrations should replace this guard.
    }

    var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    appDb.Database.EnsureCreated();
}

// --------------------
// Pipeline
// --------------------
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
        options.RoutePrefix = string.Empty;
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseCors("AllowSpecificOrigins"); // MUST be first

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Custom auth middleware AFTER auth & cors
app.UseMiddleware<PublicKeyAuthMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapControllers();


app.Run();
