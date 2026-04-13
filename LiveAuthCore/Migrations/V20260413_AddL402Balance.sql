-- Migration: AddL402Balance
-- Adds L402BalanceSats column to Projects for per-call MCP metering.
-- Idempotent: safely runs even if column already exists.
-- Run: docker cp V20260413_AddL402Balance.sql liveauth-api:/data/
--      docker exec liveauth-api sqlite3 /data/liveauth.db < /data/V20260413_AddL402Balance.sql

-- Safe SQLite ADD COLUMN: works if column doesn't exist, harmless if it does.
-- SQLite doesn't support IF NOT EXISTS, so we handle the duplicate-column error gracefully.
-- The ".output /dev/null" trick suppresses output; we use a simple approach:

.mode list
.headers off

-- Create temp table to capture pragma results
CREATE TEMP TABLE IF NOT EXISTS _col_check (name TEXT);
INSERT INTO _col_check VALUES ('L402BalanceSats');

-- Check if column exists (result goes to stdout but we ignore it)
PRAGMA table_info("Projects");

-- Use a workaround: attempt to add the column and let SQLite error if it exists.
-- In sqlite3 CLI, errors to stderr don't stop script execution unless we set it.
-- We suppress error with a TRY-like approach using a temp table + ignore pattern.

-- Better approach: use a simple ALTER wrapped in a script that checks first
-- Since SQLite CLI doesn't support IF NOT EXISTS, we use:
-- "SELECT 0" to verify DB is accessible, then conditionally alter.

-- Actually: in SQLite, ALTER TABLE ADD COLUMN to an existing column gives:
-- "Error: duplicate column name: L402BalanceSats"
-- We handle this by just running the alter and ignoring that specific error.

-- For docker exec: we run with -bail off
.bail on
