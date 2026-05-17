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

        // Run column migrations separately (ALTER TABLE is not idempotent in SQLite)
        await RunColumnMigrationsAsync(connection);
    }

    private static async Task RunColumnMigrationsAsync(System.Data.Common.DbConnection connection)
    {
        await EnsureColumnAsync(connection, "Projects", "L402BalanceSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Projects", "McpSatsPerCall", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(connection, "Projects", "McpInvoiceCallCredits", "INTEGER NOT NULL DEFAULT 10");
        await EnsureColumnAsync(connection, "Projects", "McpMaxSatsPerDay", "INTEGER NOT NULL DEFAULT 10000");
        await EnsureColumnAsync(connection, "Projects", "McpMaxCallsPerMinute", "INTEGER NOT NULL DEFAULT 60");

        // Add remaining table creations from GetSqliteMigrations that need separate handling
        // (CREATE TABLE IF NOT EXISTS is already in the SQL; these just need table-check guard)
        await RunTableMigrationsAsync(connection);
    }

    private static async Task EnsureColumnAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        string columnName,
        string definition)
    {
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = $"SELECT 1 FROM pragma_table_info('{tableName}') WHERE name='{columnName}' LIMIT 1";
        var exists = await checkCmd.ExecuteScalarAsync();
        if (exists != null)
            return;

        using var alterCmd = connection.CreateCommand();
        alterCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}";
        await alterCmd.ExecuteNonQueryAsync();
    }

    private static async Task RunTableMigrationsAsync(System.Data.Common.DbConnection connection)
    {
        // L402Bundles — check via pragma
        await EnsureTableAsync(connection, "L402Bundles", @"
            CREATE TABLE L402Bundles (
                Id TEXT NOT NULL PRIMARY KEY,
                BundleId TEXT NOT NULL UNIQUE,
                ProjectId TEXT NOT NULL,
                DeveloperId TEXT NOT NULL,
                Tier TEXT NOT NULL,
                TotalCalls INTEGER NOT NULL,
                RemainingCalls INTEGER NOT NULL,
                ExpiresAtUnix INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                PaymentHash TEXT,
                Bolt11 TEXT,
                AmountSats INTEGER NOT NULL,
                Status TEXT NOT NULL,
                AgentId TEXT
            )"
        );

        // L402Macaroons
        await EnsureTableAsync(connection, "L402Macaroons", @"
            CREATE TABLE L402Macaroons (
                Id TEXT NOT NULL PRIMARY KEY,
                Jti TEXT NOT NULL UNIQUE,
                BundleId TEXT NOT NULL,
                ProjectId TEXT NOT NULL,
                AgentId TEXT,
                ScopesJson TEXT NOT NULL,
                ExpiresAtUnix INTEGER NOT NULL,
                IssuedAt TEXT NOT NULL,
                IsRevoked INTEGER NOT NULL DEFAULT 0,
                SignatureB64 TEXT NOT NULL
            )"
        );
    }

    private static async Task EnsureTableAsync(System.Data.Common.DbConnection connection, string tableName, string createSql)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}'";
        var result = await check.ExecuteScalarAsync();
        if (result == null)
        {
            using var create = connection.CreateCommand();
            create.CommandText = createSql;
            await create.ExecuteNonQueryAsync();
        }
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
        
        -- Add L402BalanceSats column to existing Projects table if not present
        -- (WebhookDeliveryWorker queries this column; it was missing from the SQLite schema)
        -- Column migration is handled in RunColumnMigrationsAsync (uses pragma_table_info)
        -- NOTE: L402Bundles and L402Macaroons table creations are handled
        -- in RunTableMigrationsAsync for better idempotency control
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
