# LiveAuth Gate (MCP Pay‑Per‑Call) — Spec v0

## Goal
Make LiveAuth a **sats-denominated toll booth** for AI agent tool usage (MCP), using **PoW-first** with optional **Lightning pay-per-call**, plus enforceable budgets.

**Paid unit:** MCP tool invocation (“agent action”).

## Design principles
- **PoW-first**: always offer PoW; Lightning unlocks higher limits / better UX.
- **Idempotent debits**: every charge call must be safe to retry.
- **Replay-proof**: tokens are short-lived, audience-bound, project-bound.
- **Budget enforcement**: hard caps per project / per agent key.

## Terminology
- **Quote**: a short-lived offer to obtain access (PoW challenge or LN invoice).
- **Session JWT**: short-lived token authorizing a caller to invoke MCP tools.
- **Charge**: per-tool-call debit + allow/deny decision.

## Phase 1 API (backend)

### 1) POST `/mcp/start`
Initiate access for a caller.

**Request**
```json
{
  "projectPublicKey": "...",
  "tool": "optional-tool-name",
  "mode": "auto|pow|ln" 
}
```

**Response (PoW path)**
```json
{
  "quoteId": "uuid",
  "method": "pow",
  "costSats": 1,
  "pow": {
    "challengeHex": "...",
    "targetHex": "...",
    "difficultyBits": 22,
    "expiresAtUnix": 123
  },
  "expiresAtUnix": 123
}
```

**Response (Lightning path)**
```json
{
  "quoteId": "uuid",
  "method": "ln",
  "costSats": 5,
  "ln": {
    "invoiceBolt11": "...",
    "paymentHash": "...",
    "expiresAtUnix": 123
  },
  "expiresAtUnix": 123
}
```

**Notes**
- `mode=auto` chooses LN if caller requests it and project allows / pricing requires.
- `costSats` is the *per-call* cost used later by `/mcp/charge` (or embedded into JWT claims).

### 2) POST `/mcp/confirm`
Confirm PoW solution or LN payment to mint a short-lived session JWT.

**Request (PoW)**
```json
{
  "quoteId": "uuid",
  "pow": {
    "nonce": 123,
    "hashHex": "...",
    "sig": "optional-if-needed"
  }
}
```

**Request (LN)**
```json
{
  "quoteId": "uuid"
}
```

**Response**
```json
{
  "jwt": "...",
  "expiresIn": 300,
  "remaining": {
    "satsToday": 1000,
    "callsThisMinute": 50
  }
}
```

**Notes**
- LN confirm checks invoice status for the quote’s payment hash.
- JWT claims should include: projectId/publicKey, method, issuedAt, expiresAt, costSats, and optionally an agentKeyId.

### 3) POST `/mcp/charge`
Debit one tool call (idempotent) and return allow/deny.

**Request**
```json
{
  "idempotencyKey": "uuid-or-hash",
  "tool": "tool-name",
  "costSats": 1
}
```

**Auth**
- `Authorization: Bearer <jwt>`

**Response (allow)**
```json
{ "ok": true }
```

**Response (deny)**
```json
{ "ok": false, "reason": "budget_exceeded|rate_limited|expired|invalid" }
```

## Phase 1 data model (DB)

### Table: `ProjectMcpSettings`
- `ProjectId` (PK/FK)
- `SatsPerCall` (int)
- `AllowPowFallback` (bool)
- `MaxSatsPerDay` (int)
- `MaxCallsPerMinute` (int)
- `CreatedAt`, `UpdatedAt`

### Table: `McpQuotes`
- `Id` (UUID)
- `ProjectId` (FK)
- `Method` (pow|ln)
- `CostSats`
- `Status` (pending|confirmed|expired|failed)
- `PaymentHash` (nullable)
- `PowChallengeId`/fields (nullable) (or reuse existing PoW tables)
- `ExpiresAt`
- `CreatedAt`

### Table: `McpCharges`
- `Id` (UUID)
- `ProjectId` (FK)
- `IdempotencyKey` (unique per project)
- `Tool` (text)
- `CostSats`
- `CreatedAt`

## Abuse / security checklist
- JWT audience binding to `mcp`.
- Short TTL (e.g. 5 min) + refresh via new quote.
- Idempotency unique constraint: `(ProjectId, IdempotencyKey)`.
- Budget checks must be atomic.
- Tie charges to a project + optionally agent identity.

## Reference MCP integration
Provide a tiny wrapper that:
1. calls `/mcp/start`
2. solves PoW or pays invoice
3. calls `/mcp/confirm` to obtain JWT
4. wraps each tool call with `/mcp/charge`
