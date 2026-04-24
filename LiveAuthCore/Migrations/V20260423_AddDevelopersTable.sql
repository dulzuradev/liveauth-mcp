-- Migration: Create Developers table for email/password auth
-- Run once to create the Developers table

CREATE TABLE IF NOT EXISTS Developers (
    Id TEXT NOT NULL PRIMARY KEY,
    Email TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    LightningAuthKey TEXT,
    GitHubId TEXT,
    GitHubUsername TEXT,
    PasswordHash TEXT,
    PasswordSalt TEXT,
    EmailVerified INTEGER NOT NULL DEFAULT 0,
    VerificationToken TEXT,
    VerificationExpiresAt TEXT
);

CREATE INDEX IF NOT EXISTS IX_Developers_Email ON Developers(Email);
CREATE INDEX IF NOT EXISTS IX_Developers_GitHubId ON Developers(GitHubId);
