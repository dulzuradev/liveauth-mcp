CREATE TABLE IF NOT EXISTS BitcoinGatewayOperations (
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
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_BitcoinGatewayOperations_ProjectId_Operation_IdempotencyKey
    ON BitcoinGatewayOperations (ProjectId, Operation, IdempotencyKey);

CREATE INDEX IF NOT EXISTS IX_BitcoinGatewayOperations_Txid_UpdatedAt
    ON BitcoinGatewayOperations (Txid, UpdatedAt);

CREATE INDEX IF NOT EXISTS IX_BitcoinGatewayOperations_RevenueEventId
    ON BitcoinGatewayOperations (RevenueEventId);
