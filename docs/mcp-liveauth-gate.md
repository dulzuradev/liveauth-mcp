# LiveAuth MCP Gate

LiveAuth MCP Gate lets an MCP server require a short-lived LiveAuth JWT, meter each call in sats, and optionally attribute calls to a registered MCP tool revenue ledger.

The current implementation has two charge modes:

- **Generic session metering:** `POST /api/mcp/charge`
- **Paid tool revenue attribution:** `POST /api/mcp/tools/{toolId}/charge`

The generic endpoint is backward-compatible and updates session budget counters. The tool endpoint performs the same JWT and budget checks, then records an immutable revenue event with gross sats, LiveAuth platform fee, net sats, method name, session/token attribution, and idempotency key.

---

## Authentication Flow

### 1. Start a Session

```http
POST /api/mcp/start
X-LW-Public: la_pk_your_public_key
Content-Type: application/json
```

Request:

```json
{
  "forceLightning": false,
  "forceL402": false
}
```

Response for proof-of-work:

```json
{
  "quoteId": "session-guid",
  "powChallenge": {
    "projectPublicKey": "la_pk_your_public_key",
    "challengeHex": "abc123",
    "targetHex": "0000ffff...",
    "difficultyBits": 18,
    "expiresAtUnix": 1745032800,
    "signature": "signed-challenge"
  },
  "invoice": null,
  "authHint": null
}
```

Set `forceLightning: true` to request a Lightning invoice. Set `forceL402: true` to start a session that will be confirmed with an L402 macaroon.

### 2. Confirm the Session

```http
POST /api/mcp/confirm
X-LW-Public: la_pk_your_public_key
Content-Type: application/json
```

Proof-of-work request:

```json
{
  "quoteId": "session-guid",
  "challengeHex": "abc123",
  "nonce": 42,
  "hashHex": "0000...",
  "difficultyBits": 18,
  "expiresAtUnix": 1745032800,
  "sig": "signed-challenge"
}
```

L402 request:

```json
{
  "quoteId": "session-guid",
  "macaroon": "l402-macaroon"
}
```

Response:

```json
{
  "jwt": "eyJhbG...",
  "expiresIn": 600,
  "remainingBudgetSats": 10000,
  "paymentStatus": "paid",
  "refreshToken": "refresh-token"
}
```

### 3. Refresh or Inspect Usage

```http
POST /api/mcp/refresh
GET  /api/mcp/usage
GET  /api/mcp/status/{quoteId}
```

`/api/mcp/usage` returns counters for the active token:

```json
{
  "status": "active",
  "callsUsed": 5,
  "satsUsed": 15,
  "maxSatsPerDay": 10000,
  "remainingBudgetSats": 9985,
  "maxCallsPerMinute": 60,
  "expiresAt": "2026-02-17T12:00:00Z",
  "dayWindowStart": "2026-02-17T00:00:00Z"
}
```

---

## Generic Charge Endpoint

Use the generic endpoint when you only need session budget metering.

```http
POST /api/mcp/charge
Authorization: Bearer <mcp-jwt>
X-LW-Public: la_pk_your_public_key
Content-Type: application/json
```

Request:

```json
{
  "callCostSats": 1
}
```

Response:

```json
{
  "status": "ok",
  "callsUsed": 6,
  "satsUsed": 16
}
```

Denied response:

```json
{
  "status": "deny",
  "callsUsed": 6,
  "satsUsed": 16,
  "reason": "budget_exceeded"
}
```

---

## Paid Tool Charge Endpoint

Use the tool endpoint when an MCP tool call should create revenue attribution.

```http
POST /api/mcp/tools/{toolId}/charge
Authorization: Bearer <mcp-jwt>
X-LW-Public: la_pk_your_public_key
Content-Type: application/json
```

Request:

```json
{
  "toolMethodName": "web_fetch",
  "callCostSats": 5,
  "idempotencyKey": "request-or-call-id",
  "agentId": "optional-agent-id",
  "metadata": {
    "urlHost": "example.com"
  }
}
```

Validation:

- Tool exists and has not been removed.
- Tool status is `Active`.
- JWT contains a valid `projectId` and `jti`.
- MCP gate token is active and not expired.
- Paying project is active.
- `callCostSats` is positive and within the tool's min/max bounds when provided.
- If `callCostSats` is omitted, the registered tool's `DefaultCostSats` is used.
- Session budget or L402 balance is sufficient.
- If `idempotencyKey` was already charged for this tool, LiveAuth returns the original revenue event instead of double charging.

Response:

```json
{
  "status": "ok",
  "callsUsed": 3,
  "satsUsed": 15,
  "grossSats": 5,
  "platformFeeSats": 1,
  "netSats": 4,
  "feeBasisPoints": 500,
  "revenueEventId": "event-guid",
  "toolId": "tool-guid",
  "toolName": "Paid Research Tool",
  "toolSlug": "paid-research-tool",
  "receipt": {
    "version": "mcp-call-receipt-v1",
    "payload": "base64url-canonical-json",
    "signature": "base64url-hmac-sha256",
    "signatureAlgorithm": "HMAC-SHA256",
    "keyId": "liveauth-mcp-receipt-v1",
    "body": {
      "receiptId": "mcp_receipt_eventguid",
      "revenueEventId": "event-guid",
      "mcpToolId": "tool-guid",
      "toolName": "Paid Research Tool",
      "toolSlug": "paid-research-tool",
      "toolMethodName": "web_fetch",
      "grossSats": 5,
      "platformFeeSats": 1,
      "netSats": 4,
      "idempotencyKey": "request-or-call-id"
    }
  }
}
```

The receipt payload is base64url-encoded canonical JSON signed with HMAC-SHA256. It includes the revenue event ID, tool identity, session/token attribution, sats accounting, request ID, and idempotency key. Idempotent retries return the original receipt.

Denied response:

```json
{
  "status": "deny",
  "callsUsed": 3,
  "satsUsed": 15,
  "reason": "budget_exceeded"
}
```

Paused or inactive tools return:

```json
{
  "status": "deny",
  "callsUsed": 3,
  "satsUsed": 15,
  "reason": "tool_inactive"
}
```

---

## Revenue Ledger

Tool charges write `McpToolRevenueEvent` records. Events are append-only; reversals should be represented by a new event rather than changing the charged event.
Denied registered-tool charge attempts are recorded with `Status = Denied`, zero platform/net sats, and the denial reason in metadata so seller/admin analytics can report failed charge attempts without treating them as revenue.

Recorded fields include:

| Field | Meaning |
|-------|---------|
| `McpToolId` | Registered tool being charged. |
| `McpGateTokenId` | Token that authorized the charge. |
| `McpGateSessionId` | Session that produced the token. |
| `PayingProjectId` | Project paying for the call. |
| `AgentId` | Optional caller identity from the charge request. |
| `ToolMethodName` | Method or action, such as `web_fetch`. |
| `GrossSats` | Total sats charged. |
| `PlatformFeeSats` | LiveAuth fee. |
| `NetSats` | Gross minus platform fee. |
| `FeeBasisPoints` | Fee rate used for this event. |
| `IdempotencyKey` | Retry-safe key unique per tool when present. |
| `RequestId` | LiveAuth request trace identifier. |
| `MetadataJson` | Small audit metadata. Do not store fetched content or private output here. |

Current v1 fee model:

```text
platformFeeSats = max(1, floor(grossSats * 500 / 10000))
netSats = grossSats - platformFeeSats
```

For example, a 5 sat call records a 1 sat platform fee and 4 sats net.

---

## Revenue Visibility

Developer JWTs can register tools and query MCP tool revenue through the developer API:

```http
POST /api/dev/mcp-tools
GET /api/dev/mcp-tools
GET /api/dev/mcp-tools/revenue?projectId=<optional-project-guid>&windowHours=24
GET /api/dev/mcp-tools/{toolId}
PATCH /api/dev/mcp-tools/{toolId}
DELETE /api/dev/mcp-tools/{toolId}
GET /api/dev/mcp-tools/{toolId}/revenue?windowHours=24
GET /api/dev/mcp-tools/{toolId}/revenue/events?limit=50
GET /api/admin/analytics/mcp?windowHours=24
```

The dashboard uses these endpoints to register and edit developer-owned tools, show gross sats, LiveAuth platform fees, developer net sats, call count, denied attempts, top tools, and recent revenue events. Non-admin developers only see tools they own directly or through one of their projects; admins can see first-party tools such as LiveAuth Web Fetch MCP. Deleting a tool is a soft delete: the tool is removed from active listings, but existing revenue events remain in the ledger.

---

## Tool Model

Registered MCP tools are stored as `McpTool` records with:

- Name, slug, description, category, links, and manifest JSON.
- Status: `Draft`, `Active`, `Paused`, or `Removed`.
- Visibility: `Private`, `Unlisted`, or `Public`.
- Default, minimum, and maximum call cost.
- Optional developer and project ownership.

The backend seeds a first-party `LiveAuth Web Fetch MCP` tool and allows developers to register their own tools from the dashboard. Public marketplace listing is still separate future work.

---

## First-Party Hosted Web Fetch

The reference implementation lives in `examples/paid-web-fetch-mcp`. It can run in two modes:

- `npm start`: local stdio MCP server for clients such as Claude Desktop.
- `npm run start:hosted`: hosted HTTP service for a production Web Fetch deployment.

Hosted mode exposes:

```text
GET  /healthz
GET  /tools
POST /tools/web_fetch
POST /tools/web_fetch_metadata
```

Hosted calls require `Authorization: Bearer <mcp-jwt>`. Each call charges the seeded first-party tool ID through `/api/mcp/tools/{toolId}/charge`, then returns the fetch result plus the LiveAuth charge object, including `revenueEventId` and a signed receipt.

For stdio-only MCP clients, run the example `server.mjs` with `WEB_FETCH_HOSTED_URL` set. The local MCP process becomes a thin adapter that forwards tool calls to the hosted service while preserving the same LiveAuth JWT and idempotency key.

Developers can fork this example to build their own paid MCP tools: keep the LiveAuth gate and revenue attribution flow, replace the tool-specific handler.

---

## SDK Integration

Install the MCP package:

```bash
npm install @liveauth-labs/mcp-server
```

Wrap a paid tool handler:

```ts
import { createMcpGate } from '@liveauth-labs/mcp-server';

const gate = createMcpGate({
  publicKey: process.env.LIVEAUTH_PUBLIC_KEY!,
  baseUrl: process.env.LIVEAUTH_API_URL ?? 'https://api.liveauth.app',
  toolName: 'paid-research-tool',
});

const result = await gate.invoke(
  jwtFromMcpRequest,
  { url: 'https://example.com' },
  async (input, context) => {
    const text = await fetch(input.url).then(r => r.text());
    return {
      text,
      charge: context.liveAuth.charge
    };
  },
  { requestId: 'req_123' },
  {
    toolMethodName: 'web_fetch',
    idempotencyKey: 'req_123',
    agentId: 'agent_abc',
    metadata: {
      urlHost: 'example.com'
    }
  }
);
```

Use `toolId` to charge `/api/mcp/tools/{toolId}/charge`, or `toolName` to charge `/api/mcp/charge` with registered tool pricing. When no tool is configured, `/api/mcp/charge` remains backward-compatible generic metering and falls back to the project's global MCP price if `costSats` is omitted.

---

## Security Checklist

- Require `Authorization: Bearer <mcp-jwt>` for charge and usage calls.
- Send `X-LW-Public` for project context.
- Use short JWT lifetimes and refresh tokens.
- Always set an `idempotencyKey` for paid tool calls.
- Store the signed receipt with your tool response when you need an audit trail.
- Store only minimal metadata, such as host, content type, status, request ID, and client.
- Do not store fetched content, prompts, completions, credentials, or private tool output in revenue metadata.
