-- Migration: AddSatsPrinterTables
-- Description: Adds tables for the Sats Printer feature (MintRequests, UserEcashBalances, MintProviders).

CREATE TABLE IF NOT EXISTS "MintRequests" (
    "Id" UUID NOT NULL,
    "UserId" TEXT NOT NULL,
    "MintUrl" TEXT NOT NULL,
    "Amount" BIGINT NOT NULL,
    "PaymentHash" TEXT NULL,
    "Invoice" TEXT NULL,
    "Status" INTEGER NOT NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL,
    "UpdatedAt" TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT "PK_MintRequests" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_MintRequests_UserId" ON "MintRequests" ("UserId");
CREATE INDEX IF NOT EXISTS "IX_MintRequests_PaymentHash" ON "MintRequests" ("PaymentHash");
CREATE INDEX IF NOT EXISTS "IX_MintRequests_Status" ON "MintRequests" ("Status");

CREATE TABLE IF NOT EXISTS "UserEcashBalances" (
    "Id" UUID NOT NULL,
    "UserId" TEXT NOT NULL,
    "MintUrl" TEXT NOT NULL,
    "Balance" BIGINT NOT NULL,
    "LastUpdated" TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT "PK_UserEcashBalances" PRIMARY KEY ("Id"),
    CONSTRAINT "AK_UserEcashBalances_UserId_MintUrl" UNIQUE ("UserId", "MintUrl")
);

CREATE TABLE IF NOT EXISTS "MintProviders" (
    "Id" UUID NOT NULL,
    "Name" TEXT NOT NULL,
    "MintUrl" TEXT NOT NULL,
    "IsActive" BOOLEAN NOT NULL,
    "AddedAt" TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT "PK_MintProviders" PRIMARY KEY ("Id")
);
