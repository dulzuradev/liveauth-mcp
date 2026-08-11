using System.Security.Claims;
using LiveAuthCore.Auth;
using LiveAuthCore.Data;
using LiveAuthCore.Data.Entities;
using LiveAuthCore.Middleware;
using LiveAuthCore.Services;
using LiveAuthCore.Services.PermitSignal;
using LiveAuthCore.Bitcoin.Services;
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

        if (!db.Database.IsRelational())
        {
            await SeedLightningFeeSettingsAsync(db, app.Configuration);
            await SeedFirstPartyMcpToolsAsync(db, app.Configuration);
            await scope.ServiceProvider.GetRequiredService<IPermitSignalBootstrapper>().SeedAsync();
            await scope.ServiceProvider.GetRequiredService<IBitcoinGatewayBootstrapper>().SeedAsync();
            return;
        }

        // Create MCP/custom tables for existing databases
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = GetSqliteMigrations();
        await cmd.ExecuteNonQueryAsync();

        // Run column migrations separately (ALTER TABLE is not idempotent in SQLite)
        await RunColumnMigrationsAsync(connection);

        await SeedLightningFeeSettingsAsync(db, app.Configuration);
        await SeedFirstPartyMcpToolsAsync(db, app.Configuration);
        await scope.ServiceProvider.GetRequiredService<IPermitSignalBootstrapper>().SeedAsync();
        await scope.ServiceProvider.GetRequiredService<IBitcoinGatewayBootstrapper>().SeedAsync();
    }

    private static async Task RunColumnMigrationsAsync(System.Data.Common.DbConnection connection)
    {
        await EnsureColumnAsync(connection, "Projects", "L402BalanceSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Projects", "McpSatsPerCall", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(connection, "Projects", "McpInvoiceCallCredits", "INTEGER NOT NULL DEFAULT 10");
        await EnsureColumnAsync(connection, "Projects", "McpMaxSatsPerDay", "INTEGER NOT NULL DEFAULT 10000");
        await EnsureColumnAsync(connection, "Projects", "McpMaxCallsPerMinute", "INTEGER NOT NULL DEFAULT 60");
        await EnsureColumnAsync(connection, "WebhookEvents", "DestinationUrl", "TEXT");
        await EnsureColumnAsync(connection, "LightningFeeSettings", "McpPaidToolFeeBasisPoints", "INTEGER NOT NULL DEFAULT 500");
        await EnsureColumnAsync(connection, "LightningFeeSettings", "McpPaidToolMinimumFeeSats", "INTEGER NOT NULL DEFAULT 1");

        await RunTableMigrationsAsync(connection);
        await EnsureColumnAsync(connection, "BitcoinGatewayOperations", "RequestId", "TEXT NOT NULL DEFAULT ''");

        await EnsureColumnAsync(connection, "AuthEvents", "ProtectedActionId", "TEXT");
        await EnsureColumnAsync(connection, "AuthEvents", "Environment", "TEXT");
        await EnsureColumnAsync(connection, "AuthEvents", "IpAddressHash", "TEXT");
        await EnsureColumnAsync(connection, "AuthEvents", "ClientContextHash", "TEXT");
        await EnsureColumnAsync(connection, "AuthEvents", "SubjectHash", "TEXT");
        await EnsureColumnAsync(connection, "AuthEvents", "VerificationMethod", "TEXT");
        await EnsureColumnAsync(connection, "AuthEvents", "DurationMilliseconds", "INTEGER");
        await EnsureColumnAsync(connection, "AuthEvents", "EstimatedCostProtected", "TEXT");
        await EnsureColumnAsync(connection, "AuthEvents", "MetadataJson", "TEXT");

        await EnsureIndexAsync(connection, "IX_AuthEvents_ProjectId_ProtectedActionId_CreatedAt", @"
            CREATE INDEX IX_AuthEvents_ProjectId_ProtectedActionId_CreatedAt
            ON AuthEvents (ProjectId, ProtectedActionId, CreatedAt)"
        );

        await EnsureIndexAsync(connection, "IX_AuthEvents_ProtectedActionId_IpAddressHash_CreatedAt", @"
            CREATE INDEX IX_AuthEvents_ProtectedActionId_IpAddressHash_CreatedAt
            ON AuthEvents (ProtectedActionId, IpAddressHash, CreatedAt)"
        );

        await EnsureColumnAsync(connection, "AuthSessions", "BaseAmountSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "AuthSessions", "InvoiceFeeBasisPoints", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "AuthSessions", "InvoiceFeeMinimumSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "AuthSessions", "InvoiceFeeSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "AuthSessions", "TotalChargedSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "AuthSessions", "CreditAmountSats", "INTEGER NOT NULL DEFAULT 0");

        await EnsureColumnAsync(connection, "VerificationSessions", "BaseAmountSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "VerificationSessions", "InvoiceFeeBasisPoints", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "VerificationSessions", "InvoiceFeeMinimumSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "VerificationSessions", "InvoiceFeeSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "VerificationSessions", "TotalChargedSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "VerificationSessions", "CreditAmountSats", "INTEGER NOT NULL DEFAULT 0");

        await EnsureColumnAsync(connection, "DevLoginSessions", "BaseAmountSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "DevLoginSessions", "InvoiceFeeBasisPoints", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "DevLoginSessions", "InvoiceFeeMinimumSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "DevLoginSessions", "InvoiceFeeSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "DevLoginSessions", "TotalChargedSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "DevLoginSessions", "CreditAmountSats", "INTEGER NOT NULL DEFAULT 0");

        await EnsureColumnAsync(connection, "DeveloperLoginSessions", "BaseAmountSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "DeveloperLoginSessions", "InvoiceFeeBasisPoints", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "DeveloperLoginSessions", "InvoiceFeeMinimumSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "DeveloperLoginSessions", "InvoiceFeeSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "DeveloperLoginSessions", "TotalChargedSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "DeveloperLoginSessions", "CreditAmountSats", "INTEGER NOT NULL DEFAULT 0");

        await EnsureColumnAsync(connection, "McpGateSessions", "LightningBaseAmountSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "McpGateSessions", "LightningInvoiceFeeBasisPoints", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "McpGateSessions", "LightningInvoiceFeeMinimumSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "McpGateSessions", "LightningInvoiceFeeSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "McpGateSessions", "LightningTotalChargedSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "McpGateSessions", "LightningCreditAmountSats", "INTEGER NOT NULL DEFAULT 0");

        await EnsureColumnAsync(connection, "L402Purchases", "BaseAmountSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "L402Purchases", "InvoiceFeeBasisPoints", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "L402Purchases", "InvoiceFeeMinimumSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "L402Purchases", "InvoiceFeeSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "L402Purchases", "TotalChargedSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "L402Purchases", "CreditAmountSats", "INTEGER NOT NULL DEFAULT 0");

        await EnsureColumnAsync(connection, "L402Bundles", "BaseAmountSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "L402Bundles", "MarkupBasisPoints", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "L402Bundles", "MarkupMinimumFeeSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "L402Bundles", "MarkupSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "L402Bundles", "TotalChargedSats", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "L402Bundles", "CreditAmountSats", "INTEGER NOT NULL DEFAULT 0");

    }

    private static async Task EnsureColumnAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        string columnName,
        string definition)
    {
        using var tableCheck = connection.CreateCommand();
        tableCheck.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}' LIMIT 1";
        var tableExists = await tableCheck.ExecuteScalarAsync();
        if (tableExists == null)
            return;

        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = $"SELECT 1 FROM pragma_table_info('{tableName}') WHERE name='{columnName}' LIMIT 1";
        var exists = await checkCmd.ExecuteScalarAsync();
        if (exists != null)
            return;

        using var alterCmd = connection.CreateCommand();
        alterCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}";
        await alterCmd.ExecuteNonQueryAsync();
    }

    internal static async Task RunTableMigrationsAsync(System.Data.Common.DbConnection connection)
    {
        await EnsureTableAsync(connection, "BitcoinGatewayOperations", @"
            CREATE TABLE BitcoinGatewayOperations (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                McpGateTokenId TEXT,
                Operation TEXT NOT NULL,
                IdempotencyKey TEXT NOT NULL,
                RequestHash TEXT NOT NULL,
                RequestId TEXT NOT NULL,
                Txid TEXT,
                Status TEXT NOT NULL DEFAULT 'Processing',
                ErrorCode TEXT,
                ResultJson TEXT,
                RevenueEventId TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )"
        );
        await EnsureIndexAsync(connection, "IX_BitcoinGatewayOperations_ProjectId_Operation_IdempotencyKey", @"
            CREATE UNIQUE INDEX IX_BitcoinGatewayOperations_ProjectId_Operation_IdempotencyKey
            ON BitcoinGatewayOperations (ProjectId, Operation, IdempotencyKey)"
        );
        await EnsureIndexAsync(connection, "IX_BitcoinGatewayOperations_Txid_UpdatedAt", @"
            CREATE INDEX IX_BitcoinGatewayOperations_Txid_UpdatedAt
            ON BitcoinGatewayOperations (Txid, UpdatedAt)"
        );
        await EnsureIndexAsync(connection, "IX_BitcoinGatewayOperations_RevenueEventId", @"
            CREATE INDEX IX_BitcoinGatewayOperations_RevenueEventId
            ON BitcoinGatewayOperations (RevenueEventId)"
        );

        await EnsureTableAsync(connection, "ProtectedActions", @"
            CREATE TABLE ProtectedActions (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                Environment TEXT NOT NULL,
                Name TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                Description TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                BaseDifficulty INTEGER NOT NULL DEFAULT 17,
                SuspiciousDifficulty INTEGER NOT NULL DEFAULT 20,
                MaximumDifficulty INTEGER NOT NULL DEFAULT 24,
                AnonymousRequestLimit INTEGER NOT NULL DEFAULT 5,
                AnonymousLimitWindowSeconds INTEGER NOT NULL DEFAULT 3600,
                AuthenticatedRequestLimit INTEGER,
                AuthenticatedLimitWindowSeconds INTEGER,
                RequireSingleUseToken INTEGER NOT NULL DEFAULT 1,
                TokenLifetimeSeconds INTEGER NOT NULL DEFAULT 120,
                AllowedOriginsRaw TEXT NOT NULL DEFAULT '[]',
                FailureBehavior TEXT NOT NULL DEFAULT 'Deny',
                AllowLightningFallback INTEGER NOT NULL DEFAULT 0,
                LightningPriceSats INTEGER NOT NULL DEFAULT 25,
                LightningFallbackMode TEXT NOT NULL DEFAULT 'RateLimitOnly',
                LightningBypassesProofOfWork INTEGER NOT NULL DEFAULT 1,
                EstimatedCostPerExecution TEXT NOT NULL DEFAULT '0.0',
                ConfigurationVersion INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE
            )"
        );

        await EnsureIndexAsync(connection, "IX_ProtectedActions_ProjectId_Environment_Name", @"
            CREATE UNIQUE INDEX IX_ProtectedActions_ProjectId_Environment_Name
            ON ProtectedActions (ProjectId, Environment, Name)"
        );

        await EnsureIndexAsync(connection, "IX_ProtectedActions_ProjectId_Environment_IsEnabled", @"
            CREATE INDEX IX_ProtectedActions_ProjectId_Environment_IsEnabled
            ON ProtectedActions (ProjectId, Environment, IsEnabled)"
        );

        await EnsureTableAsync(connection, "CostShieldAuthorizations", @"
            CREATE TABLE CostShieldAuthorizations (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                ProtectedActionId TEXT NOT NULL,
                ChallengeId TEXT NOT NULL,
                TokenId TEXT NOT NULL,
                Environment TEXT NOT NULL,
                VerificationMethod TEXT NOT NULL,
                Difficulty INTEGER NOT NULL,
                Origin TEXT,
                ClientContextHash TEXT NOT NULL,
                SubjectHash TEXT,
                RequireSingleUse INTEGER NOT NULL DEFAULT 1,
                ConfigurationVersion INTEGER NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Active',
                IssuedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                ConsumedAt TEXT,
                ConcurrencyStamp TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE,
                FOREIGN KEY (ProtectedActionId) REFERENCES ProtectedActions (Id) ON DELETE RESTRICT
            )"
        );

        await EnsureIndexAsync(connection, "IX_CostShieldAuthorizations_TokenId", @"
            CREATE UNIQUE INDEX IX_CostShieldAuthorizations_TokenId
            ON CostShieldAuthorizations (TokenId)"
        );

        await EnsureIndexAsync(connection, "IX_CostShieldAuthorizations_ProjectId_ChallengeId", @"
            CREATE UNIQUE INDEX IX_CostShieldAuthorizations_ProjectId_ChallengeId
            ON CostShieldAuthorizations (ProjectId, ChallengeId)"
        );

        await EnsureIndexAsync(connection, "IX_CostShieldAuthorizations_ProjectId_ProtectedActionId_IssuedAt", @"
            CREATE INDEX IX_CostShieldAuthorizations_ProjectId_ProtectedActionId_IssuedAt
            ON CostShieldAuthorizations (ProjectId, ProtectedActionId, IssuedAt)"
        );

        await EnsureIndexAsync(connection, "IX_CostShieldAuthorizations_ExpiresAt", @"
            CREATE INDEX IX_CostShieldAuthorizations_ExpiresAt
            ON CostShieldAuthorizations (ExpiresAt)"
        );

        await EnsureTableAsync(connection, "LightningFeeSettings", @"
            CREATE TABLE LightningFeeSettings (
                Id INTEGER NOT NULL PRIMARY KEY,
                InvoiceFeeBasisPoints INTEGER NOT NULL,
                InvoiceMinimumFeeSats INTEGER NOT NULL,
                BundleMarkupBasisPoints INTEGER NOT NULL,
                BundleMarkupMinimumFeeSats INTEGER NOT NULL,
                McpPaidToolFeeBasisPoints INTEGER NOT NULL DEFAULT 500,
                McpPaidToolMinimumFeeSats INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )"
        );

        await EnsureTableAsync(connection, "L402Purchases", @"
            CREATE TABLE L402Purchases (
                Id TEXT NOT NULL PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                DeveloperId TEXT NOT NULL,
                AmountSats INTEGER NOT NULL,
                BaseAmountSats INTEGER NOT NULL DEFAULT 0,
                InvoiceFeeBasisPoints INTEGER NOT NULL DEFAULT 0,
                InvoiceFeeMinimumSats INTEGER NOT NULL DEFAULT 0,
                InvoiceFeeSats INTEGER NOT NULL DEFAULT 0,
                TotalChargedSats INTEGER NOT NULL DEFAULT 0,
                CreditAmountSats INTEGER NOT NULL DEFAULT 0,
                InvoiceId TEXT NOT NULL,
                Bolt11 TEXT NOT NULL,
                ExpiresAtUnix INTEGER NOT NULL,
                Status TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                SettledAt TEXT
            )"
        );

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
                BaseAmountSats INTEGER NOT NULL DEFAULT 0,
                MarkupBasisPoints INTEGER NOT NULL DEFAULT 0,
                MarkupMinimumFeeSats INTEGER NOT NULL DEFAULT 0,
                MarkupSats INTEGER NOT NULL DEFAULT 0,
                TotalChargedSats INTEGER NOT NULL DEFAULT 0,
                CreditAmountSats INTEGER NOT NULL DEFAULT 0,
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

        await EnsureTableAsync(connection, "McpTools", @"
            CREATE TABLE McpTools (
                Id TEXT NOT NULL PRIMARY KEY,
                DeveloperId TEXT,
                ProjectId TEXT,
                Name TEXT NOT NULL,
                Slug TEXT NOT NULL,
                Description TEXT NOT NULL,
                Category TEXT,
                IconUrl TEXT,
                WebsiteUrl TEXT,
                DocsUrl TEXT,
                ManifestJson TEXT,
                Status TEXT NOT NULL,
                Visibility TEXT NOT NULL,
                DefaultCostSats INTEGER NOT NULL,
                MinCostSats INTEGER NOT NULL,
                MaxCostSats INTEGER NOT NULL,
                WebhookUrl TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                RemovedAt TEXT
            )"
        );

        await EnsureTableAsync(connection, "McpToolRevenueEvents", @"
            CREATE TABLE McpToolRevenueEvents (
                Id TEXT NOT NULL PRIMARY KEY,
                McpToolId TEXT NOT NULL,
                McpGateTokenId TEXT,
                McpGateSessionId TEXT,
                PayingProjectId TEXT,
                AgentId TEXT,
                ToolMethodName TEXT NOT NULL,
                GrossSats INTEGER NOT NULL,
                PlatformFeeSats INTEGER NOT NULL,
                NetSats INTEGER NOT NULL,
                FeeBasisPoints INTEGER NOT NULL,
                Status TEXT NOT NULL,
                IdempotencyKey TEXT,
                RequestId TEXT,
                MetadataJson TEXT,
                CreatedAt TEXT NOT NULL,
                ReversalOfEventId TEXT
            )"
        );

        await EnsureIndexAsync(connection, "IX_McpTools_Slug", @"
            CREATE UNIQUE INDEX IX_McpTools_Slug
            ON McpTools (Slug)"
        );

        await EnsureIndexAsync(connection, "IX_McpToolRevenueEvents_McpToolId_CreatedAt", @"
            CREATE INDEX IX_McpToolRevenueEvents_McpToolId_CreatedAt
            ON McpToolRevenueEvents (McpToolId, CreatedAt)"
        );

        await EnsureIndexAsync(connection, "IX_McpToolRevenueEvents_PayingProjectId_CreatedAt", @"
            CREATE INDEX IX_McpToolRevenueEvents_PayingProjectId_CreatedAt
            ON McpToolRevenueEvents (PayingProjectId, CreatedAt)"
        );

        await EnsureIndexAsync(connection, "IX_McpToolRevenueEvents_McpGateTokenId", @"
            CREATE INDEX IX_McpToolRevenueEvents_McpGateTokenId
            ON McpToolRevenueEvents (McpGateTokenId)"
        );

        await EnsureIndexAsync(connection, "IX_McpToolRevenueEvents_McpToolId_IdempotencyKey", @"
            CREATE UNIQUE INDEX IX_McpToolRevenueEvents_McpToolId_IdempotencyKey
            ON McpToolRevenueEvents (McpToolId, IdempotencyKey)
            WHERE IdempotencyKey IS NOT NULL"
        );

        await RunMeterTableMigrationsAsync(connection);
        await RunPermitSignalTableMigrationsAsync(connection);
    }

    private static async Task RunPermitSignalTableMigrationsAsync(System.Data.Common.DbConnection connection)
    {
        await EnsureTableAsync(connection, "PermitSources", @"
            CREATE TABLE PermitSources (
                Id TEXT NOT NULL PRIMARY KEY, SourceIdentifier TEXT NOT NULL,
                Municipality TEXT NOT NULL, State TEXT NOT NULL, AdapterType TEXT NOT NULL,
                OfficialDatasetUrl TEXT NOT NULL, Enabled INTEGER NOT NULL DEFAULT 1,
                HealthStatus TEXT NOT NULL, LastSuccessfulSync TEXT, LastError TEXT,
                CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL
            )");

        await EnsureTableAsync(connection, "PermitSyncStates", @"
            CREATE TABLE PermitSyncStates (
                Id TEXT NOT NULL PRIMARY KEY, PermitSourceId TEXT NOT NULL,
                LastAttemptAt TEXT, LastSuccessfulSyncAt TEXT, SourceCursorUtc TEXT,
                ContinuationToken TEXT, ConsecutiveFailures INTEGER NOT NULL DEFAULT 0,
                RecordsProcessed INTEGER NOT NULL DEFAULT 0, LastError TEXT, UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (PermitSourceId) REFERENCES PermitSources (Id) ON DELETE CASCADE
            )");

        await EnsureTableAsync(connection, "PermitProjects", @"
            CREATE TABLE PermitProjects (
                Id TEXT NOT NULL PRIMARY KEY, PermitSourceId TEXT NOT NULL,
                Source TEXT NOT NULL, SourceRecordId TEXT NOT NULL, Municipality TEXT NOT NULL,
                State TEXT NOT NULL, Address TEXT NOT NULL, NormalizedAddress TEXT NOT NULL,
                Latitude TEXT, Longitude TEXT, PermitNumber TEXT NOT NULL, PermitType TEXT,
                PermitSubtype TEXT, Description TEXT, Status TEXT, ApplicationDate TEXT,
                IssueDate TEXT, ExpirationDate TEXT, EstimatedProjectValue TEXT,
                ContractorName TEXT, ContractorLicense TEXT, OwnerName TEXT,
                ResidentialOrCommercial TEXT, WorkCategory TEXT NOT NULL, RawSourceUrl TEXT,
                LastSourceUpdate TEXT, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (PermitSourceId) REFERENCES PermitSources (Id) ON DELETE CASCADE
            )");

        await EnsureTableAsync(connection, "PermitProjectCategories", @"
            CREATE TABLE PermitProjectCategories (
                PermitProjectId TEXT NOT NULL, Category TEXT NOT NULL,
                PRIMARY KEY (PermitProjectId, Category),
                FOREIGN KEY (PermitProjectId) REFERENCES PermitProjects (Id) ON DELETE CASCADE
            )");

        await EnsureIndexAsync(connection, "IX_PermitSources_SourceIdentifier",
            "CREATE UNIQUE INDEX IX_PermitSources_SourceIdentifier ON PermitSources (SourceIdentifier)");
        await EnsureIndexAsync(connection, "IX_PermitSources_State_Municipality",
            "CREATE INDEX IX_PermitSources_State_Municipality ON PermitSources (State, Municipality)");
        await EnsureIndexAsync(connection, "IX_PermitSyncStates_PermitSourceId",
            "CREATE UNIQUE INDEX IX_PermitSyncStates_PermitSourceId ON PermitSyncStates (PermitSourceId)");
        await EnsureIndexAsync(connection, "IX_PermitProjects_PermitSourceId_SourceRecordId",
            "CREATE UNIQUE INDEX IX_PermitProjects_PermitSourceId_SourceRecordId ON PermitProjects (PermitSourceId, SourceRecordId)");
        await EnsureIndexAsync(connection, "IX_PermitProjects_IssueDate",
            "CREATE INDEX IX_PermitProjects_IssueDate ON PermitProjects (IssueDate)");
        await EnsureIndexAsync(connection, "IX_PermitProjects_Municipality_State_IssueDate",
            "CREATE INDEX IX_PermitProjects_Municipality_State_IssueDate ON PermitProjects (Municipality, State, IssueDate)");
        await EnsureIndexAsync(connection, "IX_PermitProjects_EstimatedProjectValue",
            "CREATE INDEX IX_PermitProjects_EstimatedProjectValue ON PermitProjects (EstimatedProjectValue)");
        await EnsureIndexAsync(connection, "IX_PermitProjects_PermitType",
            "CREATE INDEX IX_PermitProjects_PermitType ON PermitProjects (PermitType)");
        await EnsureIndexAsync(connection, "IX_PermitProjects_ResidentialOrCommercial",
            "CREATE INDEX IX_PermitProjects_ResidentialOrCommercial ON PermitProjects (ResidentialOrCommercial)");
        await EnsureIndexAsync(connection, "IX_PermitProjects_ContractorName",
            "CREATE INDEX IX_PermitProjects_ContractorName ON PermitProjects (ContractorName)");
        await EnsureIndexAsync(connection, "IX_PermitProjects_NormalizedAddress",
            "CREATE INDEX IX_PermitProjects_NormalizedAddress ON PermitProjects (NormalizedAddress)");
        await EnsureIndexAsync(connection, "IX_PermitProjectCategories_Category_PermitProjectId",
            "CREATE INDEX IX_PermitProjectCategories_Category_PermitProjectId ON PermitProjectCategories (Category, PermitProjectId)");
    }

    private static async Task RunMeterTableMigrationsAsync(System.Data.Common.DbConnection connection)
    {
        await EnsureTableAsync(connection, "MerchantLightningConnections", @"
            CREATE TABLE MerchantLightningConnections (
                Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL,
                ProviderType TEXT NOT NULL, DisplayName TEXT NOT NULL, RestUrl TEXT NOT NULL,
                EncryptedTlsCertificate TEXT, EncryptedMacaroon TEXT NOT NULL,
                SupportsPaymentLookup INTEGER NOT NULL, LastValidatedAt TEXT,
                CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE
            )");

        await EnsureTableAsync(connection, "MeterProjectSettings", @"
            CREATE TABLE MeterProjectSettings (
                Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL,
                Enabled INTEGER NOT NULL, OriginBaseUrl TEXT, Environment TEXT NOT NULL,
                PublicGatewayHostname TEXT, OriginTimeoutSeconds INTEGER NOT NULL,
                MonthlyFreeRequestAllowance INTEGER NOT NULL, DefaultPriceSats INTEGER NOT NULL,
                UnmatchedRouteBehavior TEXT NOT NULL, ReceiptSigningEnabled INTEGER NOT NULL,
                WebhookUrl TEXT, LightningConnectionId TEXT, AllowPrivateOriginInTest INTEGER NOT NULL,
                MaximumRequestBodyBytes INTEGER NOT NULL, MaximumResponseBodyBytes INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE,
                FOREIGN KEY (LightningConnectionId) REFERENCES MerchantLightningConnections (Id) ON DELETE SET NULL
            )");

        await EnsureTableAsync(connection, "MeterRouteRules", @"
            CREATE TABLE MeterRouteRules (
                Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL,
                HttpMethod TEXT NOT NULL, PathPattern TEXT NOT NULL, PriceSats INTEGER NOT NULL,
                FreeRequestAllowance INTEGER NOT NULL, Enabled INTEGER NOT NULL, Priority INTEGER NOT NULL,
                CredentialLifetimeSeconds INTEGER, MaximumCredentialUses INTEGER,
                BindRequestBody INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE
            )");

        await EnsureTableAsync(connection, "MeterPaymentChallenges", @"
            CREATE TABLE MeterPaymentChallenges (
                Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL, Environment TEXT NOT NULL,
                RouteRuleId TEXT, HttpMethod TEXT NOT NULL, RequestedPath TEXT NOT NULL,
                NormalizedRoute TEXT NOT NULL, PriceSats INTEGER NOT NULL, PaymentHash TEXT NOT NULL,
                Invoice TEXT NOT NULL, MerchantLightningProviderId TEXT NOT NULL, CreatedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL, PaidAt TEXT, CredentialExpiresAt TEXT NOT NULL,
                MaximumUses INTEGER NOT NULL, RemainingUses INTEGER NOT NULL, Status TEXT NOT NULL,
                RequestCorrelationId TEXT NOT NULL, ChallengeKey TEXT NOT NULL, CredentialNonce TEXT NOT NULL,
                RequestBodyHash TEXT, Macaroon TEXT NOT NULL,
                FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE,
                FOREIGN KEY (RouteRuleId) REFERENCES MeterRouteRules (Id) ON DELETE SET NULL,
                FOREIGN KEY (MerchantLightningProviderId) REFERENCES MerchantLightningConnections (Id) ON DELETE RESTRICT
            )");

        await EnsureTableAsync(connection, "MeterAllowanceCounters", @"
            CREATE TABLE MeterAllowanceCounters (
                Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL, Environment TEXT NOT NULL,
                MonthUtc TEXT NOT NULL, CallerKey TEXT NOT NULL, ScopeKey TEXT NOT NULL,
                Used INTEGER NOT NULL, UpdatedAt TEXT NOT NULL
            )");

        await EnsureTableAsync(connection, "MeterUsageEvents", @"
            CREATE TABLE MeterUsageEvents (
                Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL, RouteRuleId TEXT,
                ChallengeId TEXT, Environment TEXT NOT NULL, Kind TEXT NOT NULL, HttpMethod TEXT NOT NULL,
                Path TEXT NOT NULL, NormalizedRoute TEXT NOT NULL, AmountSats INTEGER NOT NULL,
                OriginStatusCode INTEGER, GatewayLatencyMilliseconds INTEGER NOT NULL,
                OriginLatencyMilliseconds INTEGER, CorrelationId TEXT NOT NULL, CallerKey TEXT NOT NULL,
                ErrorCode TEXT, CreatedAt TEXT NOT NULL
            )");

        await EnsureTableAsync(connection, "MeterReceipts", @"
            CREATE TABLE MeterReceipts (
                Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL, ChallengeId TEXT NOT NULL,
                RequestCorrelationId TEXT NOT NULL, Version TEXT NOT NULL, CanonicalPayload TEXT NOT NULL,
                Signature TEXT NOT NULL, SignatureAlgorithm TEXT NOT NULL, KeyId TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            )");

        await EnsureIndexAsync(connection, "IX_MeterProjectSettings_ProjectId",
            "CREATE UNIQUE INDEX IX_MeterProjectSettings_ProjectId ON MeterProjectSettings (ProjectId)");
        await EnsureIndexAsync(connection, "IX_MeterProjectSettings_PublicGatewayHostname",
            "CREATE UNIQUE INDEX IX_MeterProjectSettings_PublicGatewayHostname ON MeterProjectSettings (PublicGatewayHostname) WHERE PublicGatewayHostname IS NOT NULL");
        await EnsureIndexAsync(connection, "IX_MerchantLightningConnections_ProjectId_ProviderType",
            "CREATE INDEX IX_MerchantLightningConnections_ProjectId_ProviderType ON MerchantLightningConnections (ProjectId, ProviderType)");
        await EnsureIndexAsync(connection, "IX_MeterRouteRules_ProjectId_HttpMethod_Priority_Enabled",
            "CREATE INDEX IX_MeterRouteRules_ProjectId_HttpMethod_Priority_Enabled ON MeterRouteRules (ProjectId, HttpMethod, Priority, Enabled)");
        await EnsureIndexAsync(connection, "IX_MeterPaymentChallenges_ChallengeKey",
            "CREATE UNIQUE INDEX IX_MeterPaymentChallenges_ChallengeKey ON MeterPaymentChallenges (ChallengeKey)");
        await EnsureIndexAsync(connection, "IX_MeterPaymentChallenges_PaymentHash",
            "CREATE UNIQUE INDEX IX_MeterPaymentChallenges_PaymentHash ON MeterPaymentChallenges (PaymentHash)");
        await EnsureIndexAsync(connection, "IX_MeterPaymentChallenges_ProjectId_Environment_Status_ExpiresAt",
            "CREATE INDEX IX_MeterPaymentChallenges_ProjectId_Environment_Status_ExpiresAt ON MeterPaymentChallenges (ProjectId, Environment, Status, ExpiresAt)");
        await EnsureIndexAsync(connection, "IX_MeterAllowanceCounters_Unique",
            "CREATE UNIQUE INDEX IX_MeterAllowanceCounters_Unique ON MeterAllowanceCounters (ProjectId, Environment, MonthUtc, CallerKey, ScopeKey)");
        await EnsureIndexAsync(connection, "IX_MeterUsageEvents_ProjectId_Environment_CreatedAt",
            "CREATE INDEX IX_MeterUsageEvents_ProjectId_Environment_CreatedAt ON MeterUsageEvents (ProjectId, Environment, CreatedAt)");
        await EnsureIndexAsync(connection, "IX_MeterReceipts_ProjectId_CreatedAt",
            "CREATE INDEX IX_MeterReceipts_ProjectId_CreatedAt ON MeterReceipts (ProjectId, CreatedAt)");
        await EnsureIndexAsync(connection, "IX_MeterReceipts_ChallengeId_RequestCorrelationId",
            "CREATE UNIQUE INDEX IX_MeterReceipts_ChallengeId_RequestCorrelationId ON MeterReceipts (ChallengeId, RequestCorrelationId)");
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

    private static async Task EnsureIndexAsync(System.Data.Common.DbConnection connection, string indexName, string createSql)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT name FROM sqlite_master WHERE type='index' AND name='{indexName}'";
        var result = await check.ExecuteScalarAsync();
        if (result != null)
            return;

        using var create = connection.CreateCommand();
        create.CommandText = createSql;
        await create.ExecuteNonQueryAsync();
    }

    private static async Task SeedLightningFeeSettingsAsync(LiveAuthDbContext db, IConfiguration configuration)
    {
        if (await db.LightningFeeSettings.AnyAsync(s => s.Id == LightningFeeSettingsService.SettingsRowId))
            return;

        var snapshot = LightningFeeSettingsService.GetFallbackSnapshot(configuration);
        var now = DateTime.UtcNow;

        db.LightningFeeSettings.Add(new LightningFeeSettings
        {
            Id = LightningFeeSettingsService.SettingsRowId,
            InvoiceFeeBasisPoints = snapshot.InvoiceFeeBasisPoints,
            InvoiceMinimumFeeSats = snapshot.InvoiceMinimumFeeSats,
            BundleMarkupBasisPoints = snapshot.BundleMarkupBasisPoints,
            BundleMarkupMinimumFeeSats = snapshot.BundleMarkupMinimumFeeSats,
            McpPaidToolFeeBasisPoints = snapshot.McpPaidToolFeeBasisPoints,
            McpPaidToolMinimumFeeSats = snapshot.McpPaidToolMinimumFeeSats,
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedFirstPartyMcpToolsAsync(LiveAuthDbContext db, IConfiguration configuration)
    {
        const string webFetchSlug = "liveauth-web-fetch";

        if (await db.McpTools.AnyAsync(t => t.Slug == webFetchSlug))
            return;

        Guid? projectId = null;
        var configuredProjectId = configuration["LiveAuth:WebFetchToolProjectId"] ?? configuration["LiveAuth:DemoProjectId"];
        if (Guid.TryParse(configuredProjectId, out var parsedProjectId))
            projectId = parsedProjectId;

        db.McpTools.Add(new LiveAuthCore.Data.Entities.Mcp.McpTool
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000005"),
            ProjectId = projectId,
            Name = "LiveAuth Web Fetch MCP",
            Slug = webFetchSlug,
            Description = "First-party paid MCP web fetch tool for demonstrating LiveAuth tool monetization.",
            Category = "web",
            Status = "Active",
            Visibility = "Unlisted",
            DefaultCostSats = 5,
            MinCostSats = 1,
            MaxCostSats = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
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
        app.UseForwardedHeaders();
        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseCors();
        app.UseRateLimiter();

        // The public Meter gateway resolves its own project from the local gateway
        // identifier or hostname. It must run before the general X-LW-Public guard.
        app.UseMiddleware<MeterGatewayMiddleware>();

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
