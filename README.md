# LiveAuth MCP Server

Model Context Protocol (MCP) server for LiveAuth authentication. Enables AI agents to authenticate using proof-of-work or Lightning Network payments.

## ⚡ One-Liner Demo

```bash
npx @liveauth-labs/mcp-server
```

That's it! Runs in demo mode (3 sats per verification). No API key needed.

**Demo vs Production:**
- **Demo mode**: Returns real Lightning invoice (paid by user's wallet) but simulates confirmation for testing
- **Production**: Real payment required, real JWT issued

## ⚡ Quick Start (5 Minutes)

### Option 1: Demo Mode (No Config)

```bash
# Just run - no API key needed
npx @liveauth-labs/mcp-server
```

That's it! The server runs in demo mode with 3 sats per verification.

> **Note:** Demo mode returns a real Lightning invoice (so you can see the actual payment flow), but confirmation is simulated for testing. For production, set `LIVEAUTH_API_KEY`.

### Option 2: Production Mode

1. **Get API keys** at [liveauth.app](https://liveauth.app)
2. **Add to Claude Desktop** (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "liveauth": {
      "command": "npx",
      "args": ["-y", "@liveauth-labs/mcp-server"],
      "env": {
        "LIVEAUTH_API_KEY": "la_pk_xxx"
      }
    }
  }
}
```

3. **Restart Claude** - Done!

### Option 3: CLI (Programmatic)

```bash
# Production
export LIVEAUTH_API_KEY=la_pk_xxx
npx @liveauth-labs/mcp-server

# Demo
npx @liveauth-labs/mcp-server
```

---

## What is This?

This MCP server allows AI agents (Claude, GPT, AutoGPT, etc.) to:
- Start an MCP session and get a proof-of-work challenge
- Solve challenges to prove computational work
- Receive JWT tokens for authenticated API access
- Meter API usage with sats per call
- Wrap paid MCP tools so calls are attributed to a LiveAuth tool and recorded as revenue events

## Installation

```bash
npm install -g @liveauth-labs/mcp-server
```

Or use directly with npx:

```bash
npx @liveauth-labs/mcp-server
```

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
  defaultCostSats: 1,
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

`gate.invoke(...)` validates the JWT, charges the configured sats cost, and passes `context.liveAuth` into your handler. The older `gate.gateTool(...)` name is still supported.

### Paid Tool Attribution

If your MCP server has a registered LiveAuth tool ID, pass `toolId` when creating the gate. Charges then go to:

```text
POST /api/mcp/tools/{toolId}/charge
```

instead of the legacy generic endpoint:

```text
POST /api/mcp/charge
```

Tool charges preserve the same session budget checks, but also record an immutable revenue event with gross sats, LiveAuth platform fee, developer net sats, tool method name, paying project/session/token, metadata, and idempotency key.

```ts
import { createMcpGate } from '@liveauth-labs/mcp-server';

const gate = createMcpGate({
  publicKey: process.env.LIVEAUTH_PUBLIC_KEY!,
  baseUrl: process.env.LIVEAUTH_API_URL ?? 'https://api.liveauth.app',
  toolId: process.env.LIVEAUTH_TOOL_ID!,
  defaultCostSats: 5,
});

const result = await gate.invoke(
  jwtFromYourTransport,
  { url: 'https://example.com' },
  async (input, context) => {
    const page = await fetch(input.url).then(r => r.text());

    return {
      text: page,
      revenueEventId: context.liveAuth.charge.revenueEventId,
      netSats: context.liveAuth.charge.netSats,
    };
  },
  { requestId: 'req_123' },
  {
    costSats: 5,
    toolMethodName: 'web_fetch',
    idempotencyKey: 'req_123',
    agentId: 'agent_abc',
    metadata: {
      urlHost: new URL('https://example.com').hostname,
    },
  }
);
```

When `toolId` is set, `GateToolOptions` supports:

| Option | Purpose |
|--------|---------|
| `costSats` | Sats to charge for this call. |
| `toolMethodName` | Method within the tool, such as `web_fetch` or `search`. |
| `idempotencyKey` | Retry-safe key. Reusing it for the same tool returns the original revenue event instead of double charging. |
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
  "revenueEventId": "event-guid"
}
```

If no `toolId` is configured, the SDK keeps using `/api/mcp/charge` for backward-compatible usage metering.

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
        "LIVEAUTH_API_KEY": "your-project-public-key"
      }
    }
  }
}
```

**Demo Mode:** If you omit `LIVEAUTH_API_KEY` or set `LIVEAUTH_DEMO=true`, the server will use the free demo endpoint (3 sats per verification). This is useful for testing without an API key.

### Other MCP Clients

The server communicates over stdio. Start it with:

```bash
liveauth-mcp
```

## Available Tools

### `liveauth_mcp_start`

Start a new LiveAuth MCP session. Returns a PoW challenge by default, or a Lightning invoice if `forceLightning=true`.

**Parameters:**
- `forceLightning` (boolean, optional): If true, request Lightning invoice instead of PoW challenge

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
  }
}
```

### `liveauth_mcp_confirm`

Submit the solved proof-of-work challenge to receive a JWT authentication token.

**Parameters:**
- `quoteId` (string): The quoteId from the start response
- `challengeHex` (string): The challenge hex from the start response
- `nonce` (number): The nonce that solves the PoW challenge
- `hashHex` (string): The resulting hash (sha256 of `projectPublicKey:challengeHex:nonce`)
- `expiresAtUnix` (number): Expiration timestamp from the challenge
- `difficultyBits` (number): Difficulty bits from the challenge
- `signature` (string): Signature from the challenge

**Returns:**
```json
{
  "jwt": "eyJhbGc...",
  "expiresIn": 600,
  "remainingBudgetSats": 10000,
  "refreshToken": "abc123def456..."
}
```

**Note:** Save the `refreshToken`! Use `liveauth_mcp_refresh` to get a new JWT without re-authenticating.

### `liveauth_mcp_charge`

Meter API usage after making an authenticated call. The bundled MCP server calls the generic `/api/mcp/charge` endpoint; this updates the session budget counters but does not create a tool revenue event. For paid MCP tool revenue attribution, use the SDK `createMcpGate({ toolId })` flow above.

**Parameters:**
- `callCostSats` (number): Cost of the API call in sats

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
2. Solve the PoW challenge:
   - Compute `hash = sha256(projectPublicKey:challengeHex:nonce)`
   - Find a nonce where hash < targetHex
3. Call `liveauth_mcp_confirm` with the solution to receive a JWT
4. Use the JWT in `Authorization: Bearer <token>` header for API requests
5. After each generic API call, call `liveauth_mcp_charge` with the call cost in sats
6. For monetized MCP tools, wrap handlers with `createMcpGate({ toolId })` so each call creates a revenue event

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

Paid tool servers use the same JWT but charge through the attributed endpoint:

```text
Agent calls MCP tool
→ Tool server calls POST /api/mcp/tools/{toolId}/charge
→ LiveAuth validates JWT and budget
→ LiveAuth records gross / platform fee / net revenue
→ Tool handler runs and returns the result
```

## x402 Compatibility

LiveAuth supports the x402 standard (Cloudflare/Coinbase). Use either format:

```bash
# L402 (LiveAuth native)
curl -H "Authorization: L402 l402_xxx" https://api.liveauth.app/api/mcp/start

# x402 (Cloudflare/Coinbase compatible)
curl -H "Authorization: x402 preimage_xxx" https://api.liveauth.app/api/mcp/start
```

The API accepts both and returns `WWW-Authenticate: x402` in 402 responses.

## Why LiveAuth?

**For API Providers:**
- Protect endpoints from abuse without CAPTCHA
- Monetize AI agent access with micropayments
- No user friction (agents handle authentication)

**For AI Agents:**
- Permissionless access (no account signup)
- Cryptographically proven authentication
- Pay with compute (PoW) or sats

## Development

```bash
# Install dependencies
npm install

# Build
npm run build

# Run locally
node dist/index.js
```

## Resources

- [LiveAuth Demo & Docs](https://liveauth.app)
- [MCP Protocol Spec](https://modelcontextprotocol.io)
- [GitHub Repository](https://github.com/dulzuradev/liveauth-mcp)

## License

MIT
