# LiveAuth Paid Web Fetch MCP

First-party paid MCP server for proving LiveAuth tool monetization.

It exposes:

| Tool | Cost | Purpose |
|------|------|---------|
| `web_fetch` | 5 sats | Fetch a public URL and return cleaned text plus metadata. |
| `web_fetch_metadata` | 1 sat | Fetch title, description, status, and content type only. |

Every tool call is charged through:

```text
POST /api/mcp/tools/{toolId}/charge
```

That means LiveAuth records a `McpToolRevenueEvent` with gross sats, platform fee, net sats, method name, paying project/session/token, metadata, and idempotency key.

## Install

```bash
cd /Users/scott/Repos/LiveAuth/examples/paid-web-fetch-mcp
npm install
```

The package points `@liveauth-labs/mcp-server` at the sibling local repo:

```json
"@liveauth-labs/mcp-server": "file:../../../liveauth-mcp"
```

## Configure

Copy `.env.example` to `.env` and set:

```text
LIVEAUTH_API_URL=http://127.0.0.1:5089
LIVEAUTH_PUBLIC_KEY=la_pk_demo
LIVEAUTH_TOOL_ID=00000000-0000-0000-0000-000000000005
```

`LIVEAUTH_TOOL_ID` defaults to the first-party seed created by the Phase 1 backend work.

You can either set `LIVEAUTH_JWT` or pass `liveauthJwt` in tool input. The smoke script can obtain a PoW-backed JWT automatically if the local LiveAuth API is running.

## Run

```bash
npm start
```

Claude Desktop example:

```json
{
  "mcpServers": {
    "liveauth-web-fetch": {
      "command": "node",
      "args": ["/Users/scott/Repos/LiveAuth/examples/paid-web-fetch-mcp/server.mjs"],
      "env": {
        "LIVEAUTH_API_URL": "https://api.liveauth.app",
        "LIVEAUTH_PUBLIC_KEY": "la_pk_xxx",
        "LIVEAUTH_TOOL_ID": "tool-guid",
        "LIVEAUTH_JWT": "eyJhbG..."
      }
    }
  }
}
```

## Tool Input

`web_fetch`:

```json
{
  "url": "https://example.com",
  "maxBytes": 200000,
  "includeHtml": false,
  "idempotencyKey": "request-or-call-id",
  "liveauthJwt": "optional-if-env-not-set"
}
```

`web_fetch_metadata`:

```json
{
  "url": "https://example.com",
  "idempotencyKey": "request-or-call-id",
  "liveauthJwt": "optional-if-env-not-set"
}
```

## Safety Rules

The fetcher blocks:

- non-http/non-https schemes
- `localhost` and `*.localhost`
- `127.0.0.0/8`
- `10.0.0.0/8`
- `172.16.0.0/12`
- `192.168.0.0/16`
- `169.254.0.0/16`
- IPv6 loopback, unique-local, and link-local ranges
- hostnames that resolve to blocked addresses

Request limits:

| Setting | Default |
|---------|---------|
| Default max bytes | 200 KB |
| Hard max bytes | 500 KB |
| Timeout | 10 seconds |
| Redirects | 3 |
| User-Agent | `LiveAuth-WebFetch-MCP/1.0` |

## Local Smoke Test

Start the LiveAuth API locally, then run:

```bash
LIVEAUTH_API_URL=http://127.0.0.1:5089 \
LIVEAUTH_PUBLIC_KEY=la_pk_demo \
LIVEAUTH_TOOL_ID=00000000-0000-0000-0000-000000000005 \
npm run smoke
```

The smoke script:

1. Starts an MCP session.
2. Solves the PoW challenge.
3. Confirms the session to get a JWT.
4. Starts this MCP server over stdio.
5. Calls `web_fetch_metadata`.
6. Prints the charge result, including `revenueEventId`.

## Tests

```bash
npm test
```

The test suite covers URL blocking, IP range checks, and HTML title/text extraction.
