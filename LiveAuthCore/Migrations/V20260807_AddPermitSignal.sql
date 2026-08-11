CREATE TABLE IF NOT EXISTS PermitSources (
    Id TEXT NOT NULL PRIMARY KEY,
    SourceIdentifier TEXT NOT NULL UNIQUE,
    Municipality TEXT NOT NULL,
    State TEXT NOT NULL,
    AdapterType TEXT NOT NULL,
    OfficialDatasetUrl TEXT NOT NULL,
    Enabled INTEGER NOT NULL DEFAULT 1,
    HealthStatus TEXT NOT NULL,
    LastSuccessfulSync TEXT,
    LastError TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS PermitSyncStates (
    Id TEXT NOT NULL PRIMARY KEY,
    PermitSourceId TEXT NOT NULL UNIQUE,
    LastAttemptAt TEXT,
    LastSuccessfulSyncAt TEXT,
    SourceCursorUtc TEXT,
    ContinuationToken TEXT,
    ConsecutiveFailures INTEGER NOT NULL DEFAULT 0,
    RecordsProcessed INTEGER NOT NULL DEFAULT 0,
    LastError TEXT,
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (PermitSourceId) REFERENCES PermitSources (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS PermitProjects (
    Id TEXT NOT NULL PRIMARY KEY,
    PermitSourceId TEXT NOT NULL,
    Source TEXT NOT NULL,
    SourceRecordId TEXT NOT NULL,
    Municipality TEXT NOT NULL,
    State TEXT NOT NULL,
    Address TEXT NOT NULL,
    NormalizedAddress TEXT NOT NULL,
    Latitude TEXT,
    Longitude TEXT,
    PermitNumber TEXT NOT NULL,
    PermitType TEXT,
    PermitSubtype TEXT,
    Description TEXT,
    Status TEXT,
    ApplicationDate TEXT,
    IssueDate TEXT,
    ExpirationDate TEXT,
    EstimatedProjectValue TEXT,
    ContractorName TEXT,
    ContractorLicense TEXT,
    OwnerName TEXT,
    ResidentialOrCommercial TEXT,
    WorkCategory TEXT NOT NULL,
    RawSourceUrl TEXT,
    LastSourceUpdate TEXT,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (PermitSourceId) REFERENCES PermitSources (Id) ON DELETE CASCADE,
    UNIQUE (PermitSourceId, SourceRecordId)
);

CREATE TABLE IF NOT EXISTS PermitProjectCategories (
    PermitProjectId TEXT NOT NULL,
    Category TEXT NOT NULL,
    PRIMARY KEY (PermitProjectId, Category),
    FOREIGN KEY (PermitProjectId) REFERENCES PermitProjects (Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_PermitProjects_IssueDate ON PermitProjects (IssueDate);
CREATE INDEX IF NOT EXISTS IX_PermitProjects_Municipality_State_IssueDate ON PermitProjects (Municipality, State, IssueDate);
CREATE INDEX IF NOT EXISTS IX_PermitProjects_EstimatedProjectValue ON PermitProjects (EstimatedProjectValue);
CREATE INDEX IF NOT EXISTS IX_PermitProjects_PermitType ON PermitProjects (PermitType);
CREATE INDEX IF NOT EXISTS IX_PermitProjects_ResidentialOrCommercial ON PermitProjects (ResidentialOrCommercial);
CREATE INDEX IF NOT EXISTS IX_PermitProjects_ContractorName ON PermitProjects (ContractorName);
CREATE INDEX IF NOT EXISTS IX_PermitProjects_NormalizedAddress ON PermitProjects (NormalizedAddress);
CREATE INDEX IF NOT EXISTS IX_PermitProjectCategories_Category_PermitProjectId ON PermitProjectCategories (Category, PermitProjectId);
