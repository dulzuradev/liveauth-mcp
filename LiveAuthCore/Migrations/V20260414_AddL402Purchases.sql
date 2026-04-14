-- Migration: V20260414_AddL402Purchases
-- Add L402Purchase table for tracking Lightning balance purchases

CREATE TABLE IF NOT EXISTS L402Purchases (
    Id TEXT NOT NULL PRIMARY KEY,
    ProjectId TEXT NOT NULL,
    DeveloperId TEXT NOT NULL,
    AmountSats INTEGER NOT NULL,
    InvoiceId TEXT NOT NULL,
    Bolt11 TEXT NOT NULL,
    ExpiresAtUnix INTEGER NOT NULL,
    Status TEXT NOT NULL DEFAULT 'pending',
    CreatedAt TEXT NOT NULL,
    SettledAt TEXT NULL,
    FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
    FOREIGN KEY (DeveloperId) REFERENCES Developers(Id)
);

CREATE INDEX IF NOT EXISTS IX_L402Purchases_ProjectId ON L402Purchases(ProjectId);
CREATE INDEX IF NOT EXISTS IX_L402Purchases_InvoiceId ON L402Purchases(InvoiceId);
CREATE INDEX IF NOT EXISTS IX_L402Purchases_Status ON L402Purchases(Status);
