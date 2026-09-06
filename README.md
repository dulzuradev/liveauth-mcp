# LiveAuth MCP Server

[![npm version](https://img.shields.io/npm/v/@liveauth-labs/mcp-server.svg)](https://www.npmjs.com/package/@liveauth-labs/mcp-server) [![MIT license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE) [![L402](https://img.shields.io/badge/auth-L402-F7931A.svg)](#l402-bundle-flow) [![MCP](https://img.shields.io/badge/protocol-MCP-7C3AED.svg)](https://modelcontextprotocol.io)

> **Authentication, pay-per-call metering, and signed receipts for AI agents and MCP tools: Bitcoin-native, Lightning-backed, and L402 compatible.**

This MCP server lets any AI agent authenticate against your API using **proof-of-work** (free, no account) or **Lightning Network micropayments** (sats), then **meter and monetize** subsequent tool calls with per-call pricing, idempotent revenue events, and HMAC-signed receipts that auditors can verify offline.

**Use it when you want to:**
- Gate an API or MCP tool behind real cost-of-compute or real sats (anti-spam by design, not by CAPTCHA).
- Charge AI agents per call without signing them up for an account.
- Issue a tamper-evident audit trail (signed `mcp-call-receipt-v1`) for every paid tool invocation.
- Offer Lightning-backed L402 bundle access for prepaid MCP sessions.

**Try it in 5 seconds — no account, no API key:**

```bash
npx @liveauth-labs/mcp-server
```

Without configuration, the server uses LiveAuth's anonymous demo project and the real PoW flow. Add `LIVEAUTH_API_KEY` only when you need a specific project's policy, pricing, or attribution.

---

## Available Tools (Glama / MCP auto-discovered)

| Tool | Purpose |
|---|---|
| `liveauth_mcp_start` | Begin a session. Returns a PoW challenge, a Lightning invoice, or an L402 bundle hint. |
| `liveauth_mcp_confirm` | Submit a solved PoW challenge, a paid Lightning invoice, or an L402 macaroon → receive a JWT. |
| `liveauth_mcp_charge` | Meter usage after a call. With `toolName`, resolves registered tool pricing and records a paid revenue event. |
| `liveauth_mcp_refresh` | Exchange a refresh token for a new JWT — no re-auth required. |
| `liveauth_mcp_status` | Poll session/payment status (Lightning confirmation, expiry). |
| `liveauth_mcp_lnurl` | Fetch the BOLT11 invoice for a session (lnget-compatible). |
| `liveauth_mcp_usage` | Query remaining budget, calls used, and rate-limit windows. |

Full parameter and response schemas are in the [Tool Reference](#tool-reference) below.

---

## 5-Minute Quick Start

### Option 1 — Credential-free PoW (no account, no key, no wallet)

```bash
npx @liveauth-labs/mcp-server
```

In an MCP client, call `liveauth_mcp_start`, then call `liveauth_mcp_confirm` with only the returned `quoteId`. The package reuses its existing PoW solver locally and the LiveAuth API verifies the signed challenge before issuing a short-lived session JWT.

### Option 2 — Production Mode

1. Grab an API key at [liveauth.app](https://liveauth.app).
2. Add to Claude Desktop's `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "liveauth": {
      "command": "npx",
      "args": ["-y", "@liveauth-labs/mcp-server"],
      "env": {
        "LIVEAUTH_API_BASE": "https://api.liveauth.app",
        "LIVEAUTH_API_KEY": "la_pk_your_public_key"
      }
    }
  }
}
```

3. Restart Claude. Done.

### Option 3 — Programmatic (CLI / SDK)

```bash
export LIVEAUTH_API_KEY=la_pk_xxx
npx @liveauth-labs/mcp-server
```

The package is also a TypeScript SDK — see [SDK Usage](#sdk-usage) below. The CLI bin is `liveauth-mcp`.

## Why LiveAuth?

**For API providers / tool developers:**
- Stop bots at the protocol layer. PoW and Lightning sats are non-replayable, non-phishable, and don't require user accounts.
- Charge per call in sats. We sign a receipt you can show auditors, your customers, or your accountant.
- Wrap any MCP tool with one line (`createMcpGate`) and you get per-tool revenue, per-tool min/max pricing, and idempotent retries.

**For AI agents / agent builders:**
- Permissionless access to paid APIs — solve a PoW or pay sats, get a JWT. No signup, no email, no OAuth dance.
- Use PoW, Lightning invoices, or L402 bundle macaroons for agent access.
- Projects can settle through a custom Lightning node when configured; otherwise payments use the LiveAuthCore-configured node.

**The math that matters:** if your tool is being scraped by a bot, charging 1 sat per call is enough to make the scraper unprofitable. We call this *cost-of-attack economics*, and it's the whole reason we exist.

## Installation

```bash
npm install -g @liveauth-labs/mcp-server
```

Or use directly with npx:

```bash
npx @liveauth-labs/mcp-server
```

## Goose

LiveAuth for Goose uses the same standards-based stdio MCP server as every other client—there is no Goose wrapper, daemon, or duplicate authentication runtime.

[Install in Goose](goose://extension?cmd=npx&arg=-y&arg=%40liveauth-labs%2Fmcp-server&timeout=300&id=liveauth&name=LiveAuth&description=Give+Goose+agents+authenticated+access+to+metered+and+paid+capabilities+through+LiveAuth.)

Or print the official deep link and current fallbacks:

```bash
npx @liveauth-labs/mcp-server setup goose
```

For a one-off Goose CLI session:

```bash
goose session --with-extension "liveauth:npx -y @liveauth-labs/mcp-server"
```

Manual Goose stdio configuration, when the deep link is unavailable:

```yaml
extensions:
  liveauth:
    type: stdio
    name: LiveAuth
    enabled: true
    cmd: npx
    args: ["-y", "@liveauth-labs/mcp-server"]
    env_keys: []
    envs: {}
    timeout: 300
```

Do not edit an existing Goose config destructively. Prefer the deep link or `goose configure`; if you add project configuration later, enter it through Goose's extension secret settings rather than shared plaintext YAML.

### Goose quick test

Ask Goose:

> Use LiveAuth to start the default authentication flow. Confirm the returned quote, then show my LiveAuth usage.

The initial flow uses the anonymous demo project's PoW challenge and does not require a wallet. A project public key is optional:

| Variable | When to set it |
|---|---|
| `LIVEAUTH_API_KEY` | Project-specific policy, pricing, and attribution. |
| `LIVEAUTH_API_BASE` | A self-hosted LiveAuth API instead of `https://api.liveauth.app`. |
| `LIVEAUTH_DEMO=true` | Explicitly opt into the older locally simulated Lightning demo. |

When a paid flow is requested, tool results retain the existing invoice fields and also include portable structured data:

```json
{
  "lightning": {
    "invoice": "lnbc...",
    "lightningUri": "lightning:lnbc...",
    "amountSats": 21,
    "expiresAt": "2030-03-17T17:46:40.000Z",
    "status": "pending"
  }
}
```

Clients with MCP Apps support can render the included QR, Open Wallet action, expiration, and live paid/pending/expired state. Other clients receive the JSON and QR image content as ordinary MCP results.

### Goose troubleshooting

- If the link does not open, run `npx @liveauth-labs/mcp-server setup goose` and use its one-session or manual fallback.
- If `npx` is unavailable, install a current Node.js release (Node 18 or newer).
- If a supplied project key is rejected, remove it to verify the anonymous PoW flow; invalid and revoked keys intentionally do not fall back to demo.
- If a Lightning invoice expires, call `liveauth_mcp_start` again to obtain a fresh quote.
- Keep refresh tokens and any non-public credentials out of logs and plaintext configuration.

LiveAuth lets agents acquire authorization at runtime instead of requiring every tool to be provisioned with permanent credentials in advance.

## SDK Usage

The package can also be imported as a TypeScript/JavaScript SDK. Importing the package does not start the stdio MCP server; the CLI lives at the `liveauth-mcp` bin.

### Client Auth Helper

```ts
import { createMcpClient } from '@liveauth-labs/mcp-server';

const liveauth = createMcpClient({
  publicKey: 'la_pk_xxx',
  baseUrl: 'https://api.liveauth.app',
  onInvoice(invoice) {
    // Render invoice.bolt11 as a QR code for a paid Lightning test.
    console.log(invoice.bolt11);
  },
});

const session = await liveauth.start();
const token = await liveauth.confirm(session);

console.log(token.jwt);
```

The client stores confirmed JWTs, refreshes them before expiry when a refresh token is returned, and exposes the current token through `liveauth.token`. Call `liveauth.destroy()` when your app is shutting down to clear token state and refresh timers.

For PoW, `config.publicKey` is the credential sent in `X-LW-Public`. It may be either the project's primary public key or an active API public key belonging to that project. The API returns the canonical project key in `session.powChallenge.projectPublicKey`; the solver hashes that returned key, and confirmation still sends the configured credential. These two key strings can legitimately differ, so comparing them for equality is not a project-isolation check.

Use sessions from your trusted LiveAuth API endpoint. The server binds the quote and signed challenge to the resolved project and issues a JWT with `projectId` and `authType`. For diagnostics, compare the JWT's `projectId` with the expected project ID from your console, without logging the token. Decoding claims alone does not verify a JWT signature.

To require a real paid invoice:

```ts
const session = await liveauth.start({ forceLightning: true });
console.log(session.invoice?.bolt11);

// Poll this after the invoice is paid.
const token = await liveauth.confirmLightning(session);
```

### Server Gate Helper

```ts
import { createMcpGate } from '@liveauth-labs/mcp-server';

const gate = createMcpGate({
  publicKey: 'la_pk_xxx',
  baseUrl: 'https://api.liveauth.app',
});

const result = await gate.invoke(
  jwtFromYourTransport,
  { message: 'hello' },
  async (input, context) => ({
    content: [{ type: 'text', text: input.message }],
    charge: context.liveAuth.charge,
  }),
  {}
);
```

`gate.invoke(...)` validates the JWT, charges the configured sats cost or the backend project default, and passes `context.liveAuth` into your handler. The older `gate.gateTool(...)` name is still supported.

### Paid Tool Attribution

If your MCP server has a registered LiveAuth tool ID, pass `toolId` when creating the gate. Charges then go to:

```text
POST /api/mcp/tools/{toolId}/charge
```

instead of the legacy generic endpoint:

```text
POST /api/mcp/charge
```

You can also pass a registered tool slug/name as `toolName`. In that mode charges go to the generic endpoint with tool identity in the body:

```text
POST /api/mcp/charge
```

Tool charges preserve the same session budget checks, but also record an immutable revenue event with gross sats, LiveAuth platform fee, developer net sats, tool method name, paying project/session/token, metadata, and idempotency key. When `costSats` is omitted, LiveAuthCore uses the registered tool's default price; without `toolId` or `toolName`, it falls back to the project's global MCP price.

Registered tools can also have a paid-call webhook URL. On every successful new paid call, LiveAuthCore queues a `liveauth.mcp.tool.paid_call` webhook with the tool identity, gross/platform/net sats, revenue event ID, metadata, and the signed receipt. If the tool webhook URL is blank, LiveAuthCore falls back to the project's webhook URL; idempotent retries do not enqueue duplicates.

```ts
import { createMcpGate } from '@liveauth-labs/mcp-server';

const gate = createMcpGate({
  publicKey: process.env.LIVEAUTH_PUBLIC_KEY!,
  baseUrl: process.env.LIVEAUTH_API_URL ?? 'https://api.liveauth.app',
  toolName: 'paid-research-tool',
});

const result = await gate.invoke(
  jwtFromYourTransport,
  { url: 'https://example.com' },
  async (input, context) => {
    const page = await fetch(input.url).then(r => r.text());

    return {
      text: page,
      revenueEventId: context.liveAuth.charge.revenueEventId,
      receipt: context.liveAuth.charge.receipt,
      netSats: context.liveAuth.charge.netSats,
    };
  },
  { requestId: 'req_123' },
  {
    toolMethodName: 'web_fetch',
    idempotencyKey: 'req_123',
    agentId: 'agent_abc',
    metadata: {
      urlHost: new URL('https://example.com').hostname,
    },
  }
);
```

When `toolId` or `toolName` is set, `GateToolOptions` supports:

| Option | Purpose |
|--------|---------|
| `costSats` | Optional sats to charge for this call. Omit to use registered tool pricing or the project global price. |
| `toolName` | Optional per-call tool slug/name override when using the generic endpoint. |
| `toolMethodName` | Method within the tool, such as `web_fetch` or `search`. |
| `idempotencyKey` | Retry-safe key. Reusing it for the same tool returns the original revenue event and signed receipt instead of double charging. |
| `agentId` | Optional caller/agent identifier for reporting. |
| `metadata` | Small JSON object for audit context. Do not store private tool output here. |

Tool charge responses include the normal budget counters plus revenue accounting:

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
      "idempotencyKey": "req_123"
    }
  }
}
```

The receipt is a signed per-call audit artifact returned by LiveAuthCore for paid tool charges. Store it with your tool result when you need proof of charge or later reconciliation.

If no `toolId` or `toolName` is configured, the SDK keeps using `/api/mcp/charge` for backward-compatible usage metering.

## Configuration

### Claude Desktop

Add to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "liveauth": {
      "command": "npx",
      "args": ["-y", "@liveauth-labs/mcp-server"],
      "env": {
        "LIVEAUTH_API_BASE": "https://api.liveauth.app",
        "LIVEAUTH_API_KEY": "la_pk_your_public_key"
      }
    }
  }
}
```

**Credential-free mode:** If you omit `LIVEAUTH_API_KEY`, the server calls the normal MCP endpoints without a project header. LiveAuth binds its configured anonymous demo project, returns a signed PoW challenge, and preserves normal verification, JWT, rate-limit, and metering boundaries. `LIVEAUTH_DEMO=true` remains an explicit opt-in to the older locally simulated Lightning preview.

**Other env vars:**

| Variable | Default | Purpose |
|---|---|---|
| `LIVEAUTH_API_KEY` | _(unset)_ | Your LiveAuth project public key (`la_pk_…`). |
| `LIVEAUTH_API_BASE` | `https://api.liveauth.app` | Override for self-hosted LiveAuth. |
| `LIVEAUTH_DEMO` | `false` | Explicitly use the legacy locally simulated Lightning demo. |

### Other MCP Clients

The server speaks stdio (JSON-RPC 2.0). Start it with:

```bash
liveauth-mcp
```

It also works with any MCP-compatible client: Cursor, VS Code, ChatGPT, Windsurf, Continue, Cline.

## Tool Reference

Full schemas for each MCP tool. Each tool is JSON-RPC 2.0 compatible and tested under `src/index.test.ts` and `src/cli.test.ts`.

### `liveauth_mcp_start`

Start a new LiveAuth MCP session. Returns a PoW challenge by default, or a Lightning invoice if `forceLightning=true`.

**Parameters:**
- `forceLightning` (boolean, optional): If true, request Lightning invoice instead of PoW challenge
- `forceL402` (boolean, optional): If true, start a session that should be confirmed with an L402 bundle macaroon

**Returns (PoW):**
```json
{
  "quoteId": "uuid-of-session",
  "powChallenge": {
    "projectId": "guid",
    "projectPublicKey": "la_pk_...",
    "challengeHex": "a1b2c3...",
    "targetHex": "0000ffff...",
    "difficultyBits": 18,
    "expiresAtUnix": 1234567890,
    "signature": "sig..."
  },
  "invoice": null
}
```

**Returns (Lightning):**
```json
{
  "quoteId": "uuid-of-session",
  "powChallenge": null,
  "invoice": {
    "bolt11": "lnbc...",
    "amountSats": 50,
    "expiresAtUnix": 1234567890,
    "paymentHash": "abc123..."
  },
  "lightning": {
    "invoice": "lnbc...",
    "lightningUri": "lightning:lnbc...",
    "amountSats": 50,
    "expiresAt": "2009-02-13T23:31:30.000Z",
    "expiresAtUnix": 1234567890,
    "status": "pending"
  }
}
```

**Returns (L402 bundle):**
```json
{
  "quoteId": "uuid-of-session",
  "powChallenge": null,
  "invoice": null,
  "authHint": "l402_bundle"
}
```

### `liveauth_mcp_confirm`

Submit a solved proof-of-work challenge, let the package solve its cached challenge, poll a Lightning payment, or present an L402 macaroon to receive a JWT authentication token.

**Parameters:**
- `quoteId` (string): The quoteId from the start response
- `challengeHex` (string, optional, PoW only): The challenge hex from the start response
- `nonce` (number, optional, PoW only): The nonce that solves the PoW challenge
- `hashHex` (string, optional, PoW only): The resulting hash (sha256 of `projectPublicKey:challengeHex:nonce`)
- `expiresAtUnix` (number, optional, PoW only): Expiration timestamp from the challenge
- `difficultyBits` (number, optional, PoW only): Difficulty bits from the challenge
- `signature` (string, optional, PoW only): Signature from the challenge
- `macaroon` (string, L402 only): Bundle macaroon returned from the L402 bundle claim flow

When the challenge came from this MCP server, calling confirm with `quoteId` alone reuses the package's existing PoW solver. Explicit solution fields remain supported for compatibility.

**Returns:**
```json
{
  "jwt": "eyJhbGc...",
  "expiresIn": 600,
  "remainingBudgetSats": 10000,
  "refreshToken": "abc123def456..."
}
```

**Note:** Store the `refreshToken` securely. It is returned in MCP tool data but never written to stderr or application logs. Use `liveauth_mcp_refresh` to get a new JWT without re-authenticating.

### `liveauth_mcp_charge`

Meter API usage after making an authenticated call. The bundled MCP server calls the generic `/api/mcp/charge` endpoint. Supplying `toolName` lets LiveAuth resolve a registered tool, apply its configured price, and create a paid-tool revenue event; omitting `toolName` keeps backward-compatible generic metering.

**Parameters:**
- `callCostSats` (number, optional): Cost of the API call in sats. Omit to use backend pricing.
- `toolName` (string, optional): Registered MCP tool slug/name for per-tool pricing and attribution.

**Returns:**
```json
{
  "status": "ok",
  "callsUsed": 5,
  "satsUsed": 15
}
```

If budget is exceeded:
```json
{
  "status": "deny",
  "callsUsed": 100,
  "satsUsed": 1000,
  "reason": "budget_exceeded"
}
```

### `liveauth_mcp_status`

Check the status of an MCP session. Use to poll for Lightning payment confirmation.

**Parameters:**
- `quoteId` (string): The quoteId from the start response

**Returns:**
```json
{
  "quoteId": "uuid-of-session",
  "status": "pending",
  "paymentStatus": "pending",
  "expiresAt": "2026-02-17T12:00:00Z"
}
```

When `paymentStatus` is "paid", the session is confirmed. Call `liveauth_mcp_confirm` again to get the JWT.

### `liveauth_mcp_lnurl`

Get the Lightning invoice for a session (lnget-compatible). Use this to retrieve the BOLT11 invoice for payment with any Lightning wallet.

**Parameters:**
- `quoteId` (string): The quoteId from the start response

**Returns:**
```json
{
  "pr": "lnbc2100n1...",
  "routes": []
}
```

**Note:** This is compatible with lnget and other Lightning payment tools. Use this to poll for the invoice when `liveauth_mcp_confirm` returns "payment pending".

### `liveauth_mcp_usage`

Query current usage and remaining budget without making a charge. Use this to check status before making API calls.

**Parameters:** (none required)

**Returns:**
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

### `liveauth_mcp_refresh`

Refresh the JWT token without re-authenticating. Use the refreshToken returned from confirm to get a new JWT when the current one expires.

**Parameters:**
- `refreshToken` (string): The refreshToken from the confirm response

**Returns:**
```json
{
  "jwt": "eyJhbGc...",
  "expiresIn": 600,
  "remainingBudgetSats": 9985
}
```

**Note:** Save the refreshToken securely. You'll need it to extend the session without solving a new PoW or making another Lightning payment.

## Usage Example

### PoW Authentication

1. Call `liveauth_mcp_start` to get a PoW challenge and quoteId
2. Call `liveauth_mcp_confirm` with the quoteId; the MCP server solves its cached challenge with the existing package solver
3. Advanced clients may still submit an explicit solution (`hash = sha256(projectPublicKey:challengeHex:nonce)` where `hash < targetHex`)
4. Use the JWT in `Authorization: Bearer <token>` header for API requests
5. After each generic API call, call `liveauth_mcp_charge` with a call cost, or omit it to use the project global MCP price
6. For monetized MCP tools, wrap handlers with `createMcpGate({ toolId })` or `createMcpGate({ toolName })` so each call creates a revenue event and signed receipt

### Lightning Authentication

1. Call `liveauth_mcp_start` with `forceLightning: true` to get a Lightning invoice
2. Use `liveauth_mcp_lnurl` (or poll `liveauth_mcp_status`) to get the BOLT11 invoice
3. Pay the invoice using your Lightning node/wallet
4. Poll `liveauth_mcp_status` with the quoteId until paymentStatus is "paid"
5. Call `liveauth_mcp_confirm` with just the quoteId to receive the JWT
6. Use the JWT with either generic `liveauth_mcp_charge` metering or SDK paid-tool attribution

## Authentication Flow

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  AI Agent       │────▶│  MCP Server     │────▶│  LiveAuth API   │
│                 │     │                 │     │                 │
│ 1. Start       │     │ /api/mcp/start  │     │ Returns PoW    │
│ 2. Solve PoW   │     │                 │     │ challenge       │
│ 3. Confirm     │     │ /api/mcp/confirm│     │ Returns JWT    │
│ 4. API calls   │     │                 │     │                 │
│ 5. Charge      │     │ /api/mcp/charge │     │ Meter usage    │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

Paid tool servers use the same JWT but charge through an attributed endpoint:

```text
Agent calls MCP tool
→ Tool server calls POST /api/mcp/tools/{toolId}/charge
  or POST /api/mcp/charge with toolName
→ LiveAuth validates JWT and budget
→ LiveAuth records gross / platform fee / net revenue and returns a signed receipt
→ Tool handler runs and returns the result
```

## L402 Bundle Flow

LiveAuthCore supports Lightning-backed L402 bundles for prepaid MCP access. Buy a bundle, claim the macaroon after payment, then start an MCP session in L402 mode and confirm it with that macaroon.

```bash
# 1. Create a bundle invoice.
curl -X POST https://api.liveauth.app/api/public/l402/bundle/invoice \
  -H "Content-Type: application/json" \
  -d '{"publicKey":"la_pk_xxx","tier":"starter","agentId":"agent_abc"}'

# 2. After the invoice is paid, claim a macaroon.
curl -X POST https://api.liveauth.app/api/public/l402/bundle/claim \
  -H "Content-Type: application/json" \
  -d '{"publicKey":"la_pk_xxx","paymentHash":"payment_hash_from_step_1"}'

# 3. Start and confirm an MCP session with the macaroon.
curl -X POST https://api.liveauth.app/api/mcp/start \
  -H "X-LW-Public: la_pk_xxx" \
  -H "Content-Type: application/json" \
  -d '{"forceL402":true}'

curl -X POST https://api.liveauth.app/api/mcp/confirm \
  -H "X-LW-Public: la_pk_xxx" \
  -H "Content-Type: application/json" \
  -d '{"quoteId":"quote_id_from_step_3","macaroon":"macaroon_from_step_2"}'
```

## Development

```bash
# Install dependencies
npm install

# Build
npm run build

# Run locally
node dist/cli.js
```

## Resources

- [LiveAuth Demo & Docs](https://liveauth.app)
- [MCP Protocol Spec](https://modelcontextprotocol.io)
- [GitHub Repository](https://github.com/dulzuradev/liveauth-mcp)

## License

MIT

---

**Categories:** `authentication` · `payments` · `lightning` · `l402` · `bitcoin` · `pay-per-call` · `metering` · `agent-tools` · `anti-abuse` · `mcp-server` · `typescript`

## Paid execution and diagnostics contract (SDK 1.2.0)

The gate validates the session, records the charge, then invokes the handler.
**Authorization plus an accepted execution attempt is billable**, including a handler
exception, timeout, or cancellation after charging. There is no automatic refund.
Input rejection before the gate and charge denials do not consume usage. A revenue
event with status `Charged` proves billing, not successful tool execution.

Register the tool and move its lifecycle from `Draft` to `Active` before serving paid
calls. Draft means unpublished (`tool_unpublished`); Paused or other non-active states
return `tool_inactive`. Public discovery also requires `Visibility=Public`, but
visibility is separate from lifecycle: active private/internal tools can be charged.
There is no separate publication flag or new visibility restriction in this change.
Unknown or removed tools return HTTP 404 with JSON `status=deny`,
`reason=tool_not_found`, and the supplied tool identity. Registered-tool lifecycle
and budget denials retain HTTP 200 with `status=deny`.

| Reason | Meaning |
| --- | --- |
| `tool_unpublished` | Tool is Draft. |
| `tool_inactive` | Tool is Paused or otherwise non-active. |
| `tool_not_found` | No matching non-removed tool. |
| `budget_exceeded` | Existing budget policy rejected the charge. |
| `rate_limited` | SDK-supported structured rate denial; the current MCP charge controller does not emit this reason or enforce its per-minute setting. |
| `denied` | SDK fallback when the denial has no reason. Unknown future reason codes remain available on the SDK error. |

`gate.charge()` returns structured denials with `ok=false`, including JSON HTTP
error responses with `status=deny`. `gate.invoke()` and `gate.gateTool()` throw
`ChargeDeniedError` with `reason`, `code`, `toolName`, and `toolId`. For compatibility
it extends `BudgetExceededError` (and `LiveAuthMcpError`); new handlers must inspect
`reason` rather than assume every instance means budget exhaustion. Unrelated HTTP
authentication, transport, and validation failures keep their existing error path.
Older backends may still return plain-text unknown-tool errors until upgraded.

On handler failure the gate throws `ToolExecutionError` with `charge`,
`idempotencyKey`, and a non-enumerable `cause`. Its public message is generic.
Expose an allowlist of charge fields: `grossSats`, `revenueEventId`, signed `receipt`,
and the idempotency key. Keep `isError=true` in the MCP response. Do not serialize
or log the error cause, JWT-bearing handler context, or arbitrary metadata.
Receipt payload/signature are existing public response artifacts and can be returned.
A successful charge can have no receipt; preserve this distinction rather than
inventing one. Billing metadata does not imply successful execution.

```ts
import { ChargeDeniedError, ToolExecutionError } from '@liveauth-labs/mcp-server';

try {
  return await gate.invoke(jwt, input, handler, {}, { idempotencyKey });
} catch (error) {
  if (error instanceof ToolExecutionError) {
    return {
      isError: true,
      content: [{ type: 'text', text: 'Tool execution failed after authorization' }],
      _meta: { liveauth: {
        billed: true,
        grossSats: error.charge.grossSats,
        revenueEventId: error.charge.revenueEventId,
        receipt: error.charge.receipt,
        idempotencyKey: error.idempotencyKey,
      } },
    };
  }
  if (error instanceof ChargeDeniedError) {
    // Map known reasons to a public response. Do not serialize error.details wholesale.
    throw error;
  }
  throw error;
}
```

### Three distinct identifiers

- Receipt `body.requestId`: LiveAuth's server HTTP request/correlation identifier
  for the original recorded charge. A retry returns that original receipt.
- Receipt `body.idempotencyKey`: caller-controlled stable retry key. Deduplication
  is scoped to the paying project and registered tool, not the server request ID.
- InvokeWorks `_meta.requestId`: MCP/client correlation ID, taken from `X-Request-Id`
  or generated by InvokeWorks. InvokeWorks intentionally also uses it as the
  LiveAuth idempotency key.

For example, `_meta.requestId="client-123"`, receipt
`body.idempotencyKey="client-123"`, and receipt `body.requestId="server-456"`
are valid together. The SDK accepts `idempotencyKey`; it does not send a separate
client request-ID option. Caller context `{ requestId }` is local handler context.
Use a new key for a new logical call and reuse a key only for the same intended
operation. Deduplicated charging does not cache handler results: retries can execute
the handler again. Tool-state and price checks still precede deduplication.
