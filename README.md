# LiveAuth MCP Server

Model Context Protocol (MCP) server for LiveAuth authentication. Enables AI agents to authenticate using proof-of-work or Lightning Network payments.

## What is This?

This MCP server allows AI agents (Claude, GPT, AutoGPT, etc.) to:
- Start an MCP session and get a proof-of-work challenge
- Solve challenges to prove computational work
- Receive JWT tokens for authenticated API access
- Meter API usage with sats per call

## Installation

```bash
npm install -g @liveauth-labs/mcp-server
```

Or use directly with npx:

```bash
npx @liveauth-labs/mcp-server
```

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
        "LIVEAUTH_API_BASE": "https://api.liveauth.app"
      }
    }
  }
}
```

### Other MCP Clients

The server communicates over stdio. Start it with:

```bash
liveauth-mcp
```

## Available Tools

### `liveauth_mcp_start`

Start a new LiveAuth MCP session and get a proof-of-work challenge.

**Parameters:**
- `forceLightning` (boolean, optional): Request Lightning payment instead of PoW (not yet implemented)

**Returns:**
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
  "remainingBudgetSats": 10000
}
```

### `liveauth_mcp_charge`

Meter API usage after making an authenticated call. Call this with the cost in sats for each API request.

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
  "satsUsed": 1000
}
```

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

## Usage Example

An AI agent authenticating to a LiveAuth-protected API would:

1. Call `liveauth_mcp_start` to get a PoW challenge and quoteId
2. Solve the PoW challenge:
   - Compute `hash = sha256(projectPublicKey:challengeHex:nonce)`
   - Find a nonce where hash < targetHex
3. Call `liveauth_mcp_confirm` with the solution to receive a JWT
4. Use the JWT in `Authorization: Bearer <token>` header for API requests
5. After each API call, call `liveauth_mcp_charge` with the call cost in sats

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
