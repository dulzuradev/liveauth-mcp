using System.Security.Claims;
using LiveAuthCore.Auth;
using LiveAuthCore.Data;
using LiveAuthCore.Middleware;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace LiveAuthCore.Extensions;

public static class PipelineExtensions
{
    /// <summary>
    /// Initializes the database and applies any pending migrations.
    /// Creates custom tables if they don't exist (SQLite only).
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveAuthDbContext>();
        
        await db.Database.EnsureCreatedAsync();

        // Create MCP/custom tables for existing databases
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = GetSqliteMigrations();
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// SQL migrations for SQLite (EF Core doesn't handle all custom tables).
    /// </summary>
    private static string GetSqliteMigrations() => @"
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

    /// <summary>
    /// Configures global exception handling middleware.
    /// </summary>
    public static void UseLiveAuthExceptionHandler(this WebApplication app)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                var logger = context.RequestServices.GetService<ILogger<Program>>();
                
                if (ex is UnauthorizedAccessException)
                {
                    logger?.LogWarning(ex, "Unauthorized access attempt");
                }
                else
                {
                    logger?.LogError(ex, "Unhandled exception in request {Method} {Path}", 
                        context.Request.Method, context.Request.Path);
                }

                if (app.Environment.IsDevelopment())
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

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = ex is UnauthorizedAccessException 
                        ? "Unauthorized or invalid token" 
                        : "An unexpected error occurred"
                });
            });
        });
    }

    /// <summary>
    /// Configures the LiveAuth middleware pipeline.
    /// </summary>
    public static void UseLiveAuthPipeline(this WebApplication app)
    {
        app.UseHttpsRedirection();
        app.UseCors("AllowAngular");
        app.UseRouting();

        // Custom auth middleware BEFORE ASP.NET authentication
        // This handles public endpoints (pow, auth) that need API key validation
        app.UseMiddleware<PublicKeyAuthMiddleware>();
        app.UseMiddleware<ApiKeyAuthMiddleware>();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseL402();
        app.UseMcpProxy();

        app.MapControllers();
    }
}
