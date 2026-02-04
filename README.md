# LiveAuth MCP Server

Model Context Protocol (MCP) server for LiveAuth authentication. Enables AI agents to authenticate using proof-of-work or Lightning Network payments.

## What is This?

This MCP server allows AI agents (Claude, GPT, AutoGPT, etc.) to:
- Request proof-of-work challenges
- Solve challenges to prove computational work
- Fallback to Lightning Network payments when needed
- Receive JWT tokens for authenticated API access

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

### `liveauth_get_challenge`

Get a proof-of-work challenge for authentication.

**Parameters:**
- `projectPublicKey` (string): Your LiveAuth project public key (starts with `la_pk_`)

**Returns:** Challenge object with difficulty, target, expiration, and signature

**Example:**
```typescript
{
  projectPublicKey: "la_pk_abc123...",
  challengeHex: "a1b2c3...",
  targetHex: "0000ffff...",
  difficultyBits: 18,
  expiresAtUnix: 1234567890,
  sig: "signature..."
}
```

### `liveauth_verify_pow`

Verify a solved proof-of-work challenge and receive JWT token.

**Parameters:**
- `projectPublicKey` (string): Your project public key
- `challengeHex` (string): Challenge from get_challenge
- `nonce` (number): Solution nonce
- `hashHex` (string): Resulting hash
- `expiresAtUnix` (number): Expiration from challenge
- `difficultyBits` (number): Difficulty from challenge
- `sig` (string): Signature from challenge

**Returns:** JWT authentication token or fallback instruction

### `liveauth_start_lightning`

Start Lightning Network payment authentication as fallback.

**Parameters:**
- `projectPublicKey` (string): Your project public key

**Returns:** Lightning invoice and session details

## Usage Example

An AI agent authenticating to a LiveAuth-protected API would:

1. Call `liveauth_get_challenge` with the project's public key
2. Solve the PoW challenge (compute nonce that produces hash below target)
3. Call `liveauth_verify_pow` with the solution
4. Receive JWT token
5. Use token in `Authorization: Bearer <token>` header for API requests

If PoW fails or isn't feasible, the agent can:

1. Call `liveauth_start_lightning` to get a payment invoice
2. Pay the Lightning invoice
3. Poll for payment confirmation
4. Receive JWT token

## Why LiveAuth?

**For API Providers:**
- Protect endpoints from abuse without CAPTCHA
- Monetize AI agent access with micropayments
- No user friction (agents handle authentication)

**For AI Agents:**
- Permissionless access (no account signup)
- Cryptographically proven authentication
- Choose between compute (PoW) or payment (Lightning)

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
