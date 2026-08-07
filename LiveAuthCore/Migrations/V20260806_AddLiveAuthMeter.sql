-- LiveAuth Meter MVP. The application bootstrap applies the equivalent idempotent
-- table/index guards for existing SQLite databases.
CREATE TABLE IF NOT EXISTS MeterProjectSettings (
    Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL UNIQUE,
    Enabled INTEGER NOT NULL, OriginBaseUrl TEXT, Environment TEXT NOT NULL,
    PublicGatewayHostname TEXT, OriginTimeoutSeconds INTEGER NOT NULL DEFAULT 30,
    MonthlyFreeRequestAllowance INTEGER NOT NULL DEFAULT 0,
    DefaultPriceSats INTEGER NOT NULL DEFAULT 1,
    UnmatchedRouteBehavior TEXT NOT NULL DEFAULT 'BLOCK',
    ReceiptSigningEnabled INTEGER NOT NULL DEFAULT 1, WebhookUrl TEXT,
    LightningConnectionId TEXT, AllowPrivateOriginInTest INTEGER NOT NULL DEFAULT 0,
    MaximumRequestBodyBytes INTEGER NOT NULL DEFAULT 2097152,
    MaximumResponseBodyBytes INTEGER NOT NULL DEFAULT 10485760,
    CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE,
    FOREIGN KEY (LightningConnectionId) REFERENCES MerchantLightningConnections (Id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS MerchantLightningConnections (
    Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL, ProviderType TEXT NOT NULL,
    DisplayName TEXT NOT NULL, RestUrl TEXT NOT NULL, EncryptedTlsCertificate TEXT,
    EncryptedMacaroon TEXT NOT NULL, SupportsPaymentLookup INTEGER NOT NULL,
    LastValidatedAt TEXT, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS MeterRouteRules (
    Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL, HttpMethod TEXT NOT NULL,
    PathPattern TEXT NOT NULL, PriceSats INTEGER NOT NULL, FreeRequestAllowance INTEGER NOT NULL,
    Enabled INTEGER NOT NULL, Priority INTEGER NOT NULL, CredentialLifetimeSeconds INTEGER,
    MaximumCredentialUses INTEGER, BindRequestBody INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS MeterPaymentChallenges (
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
);

CREATE TABLE IF NOT EXISTS MeterAllowanceCounters (
    Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL, Environment TEXT NOT NULL,
    MonthUtc TEXT NOT NULL, CallerKey TEXT NOT NULL, ScopeKey TEXT NOT NULL,
    Used INTEGER NOT NULL, UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS MeterUsageEvents (
    Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL, RouteRuleId TEXT,
    ChallengeId TEXT, Environment TEXT NOT NULL, Kind TEXT NOT NULL, HttpMethod TEXT NOT NULL,
    Path TEXT NOT NULL, NormalizedRoute TEXT NOT NULL, AmountSats INTEGER NOT NULL,
    OriginStatusCode INTEGER, GatewayLatencyMilliseconds INTEGER NOT NULL,
    OriginLatencyMilliseconds INTEGER, CorrelationId TEXT NOT NULL, CallerKey TEXT NOT NULL,
    ErrorCode TEXT, CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS MeterReceipts (
    Id TEXT NOT NULL PRIMARY KEY, ProjectId TEXT NOT NULL, ChallengeId TEXT NOT NULL,
    RequestCorrelationId TEXT NOT NULL, Version TEXT NOT NULL, CanonicalPayload TEXT NOT NULL,
    Signature TEXT NOT NULL, SignatureAlgorithm TEXT NOT NULL, KeyId TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_MeterProjectSettings_ProjectId ON MeterProjectSettings (ProjectId);
CREATE UNIQUE INDEX IF NOT EXISTS IX_MeterProjectSettings_PublicGatewayHostname
    ON MeterProjectSettings (PublicGatewayHostname) WHERE PublicGatewayHostname IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_MerchantLightningConnections_ProjectId_ProviderType
    ON MerchantLightningConnections (ProjectId, ProviderType);
CREATE INDEX IF NOT EXISTS IX_MeterRouteRules_ProjectId_HttpMethod_Priority_Enabled
    ON MeterRouteRules (ProjectId, HttpMethod, Priority, Enabled);
CREATE UNIQUE INDEX IF NOT EXISTS IX_MeterPaymentChallenges_ChallengeKey
    ON MeterPaymentChallenges (ChallengeKey);
CREATE UNIQUE INDEX IF NOT EXISTS IX_MeterPaymentChallenges_PaymentHash
    ON MeterPaymentChallenges (PaymentHash);
CREATE INDEX IF NOT EXISTS IX_MeterPaymentChallenges_ProjectId_Environment_Status_ExpiresAt
    ON MeterPaymentChallenges (ProjectId, Environment, Status, ExpiresAt);
CREATE UNIQUE INDEX IF NOT EXISTS IX_MeterAllowanceCounters_Unique
    ON MeterAllowanceCounters (ProjectId, Environment, MonthUtc, CallerKey, ScopeKey);
CREATE INDEX IF NOT EXISTS IX_MeterUsageEvents_ProjectId_Environment_CreatedAt
    ON MeterUsageEvents (ProjectId, Environment, CreatedAt);
CREATE INDEX IF NOT EXISTS IX_MeterReceipts_ProjectId_CreatedAt
    ON MeterReceipts (ProjectId, CreatedAt);
CREATE UNIQUE INDEX IF NOT EXISTS IX_MeterReceipts_ChallengeId_RequestCorrelationId
    ON MeterReceipts (ChallengeId, RequestCorrelationId);

-- PipelineExtensions.RunMeterTableMigrationsAsync applies equivalent idempotent
-- guards on application startup, matching this repository's existing convention.
