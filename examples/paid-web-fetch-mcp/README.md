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

## Hosted HTTP Service

Run the same paid Web Fetch tool as a hosted service:

```bash
npm run start:hosted
```

The hosted service exposes:

| Endpoint | Purpose |
|----------|---------|
| `GET /healthz` | Readiness check with the configured tool ID. |
| `GET /tools` | Tool definitions for `web_fetch` and `web_fetch_metadata`. |
| `POST /tools/web_fetch` | Paid full-page fetch. |
| `POST /tools/web_fetch_metadata` | Paid metadata-only fetch. |

Hosted calls require a LiveAuth MCP JWT:

```bash
curl -X POST http://127.0.0.1:8787/tools/web_fetch_metadata \
  -H "Authorization: Bearer $LIVEAUTH_JWT" \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://example.com",
    "idempotencyKey": "request-or-call-id"
  }'
```

Each hosted call uses the same `/api/mcp/tools/{toolId}/charge` revenue attribution as the stdio MCP server. The response includes the fetched result and the LiveAuth charge object, including `revenueEventId` and the signed receipt when returned by LiveAuthCore.

For MCP clients that still need stdio, run `server.mjs` as a thin adapter by setting `WEB_FETCH_HOSTED_URL`. In that mode the local MCP process forwards `web_fetch` and `web_fetch_metadata` calls to the hosted service instead of fetching directly:

```json
{
  "mcpServers": {
    "liveauth-web-fetch": {
      "command": "node",
      "args": ["/Users/scott/Repos/LiveAuth/examples/paid-web-fetch-mcp/server.mjs"],
      "env": {
        "WEB_FETCH_HOSTED_URL": "https://fetch.liveauth.app",
        "LIVEAUTH_JWT": "eyJhbG..."
      }
    }
  }
}
```

For production/container deploys, build from the directory that contains both `LiveAuth` and `liveauth-mcp`:

```bash
cd /Users/scott/Repos
docker build -f LiveAuth/examples/paid-web-fetch-mcp/Dockerfile \
  -t liveauth-web-fetch-mcp .

docker run --rm -p 8787:8787 \
  -e LIVEAUTH_API_URL=https://api.liveauth.app \
  -e LIVEAUTH_PUBLIC_KEY=la_pk_xxx \
  -e LIVEAUTH_TOOL_ID=00000000-0000-0000-0000-000000000005 \
  liveauth-web-fetch-mcp
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

For hosted mode, start the service in one terminal:

```bash
npm run start:hosted
```

Then run:

```bash
LIVEAUTH_API_URL=http://127.0.0.1:5089 \
LIVEAUTH_PUBLIC_KEY=la_pk_demo \
WEB_FETCH_HOSTED_URL=http://127.0.0.1:8787 \
npm run smoke:hosted
```

The smoke script:

1. Starts an MCP session.
2. Solves the PoW challenge.
3. Confirms the session to get a JWT.
4. Starts this MCP server over stdio.
5. Calls `web_fetch_metadata`.
6. Prints the charge result, including `revenueEventId` and the signed receipt when returned by LiveAuthCore.

The hosted smoke follows the same auth flow, then calls `POST /tools/web_fetch_metadata`.

## Tests

```bash
npm test
```

The test suite covers URL blocking, IP range checks, and HTML title/text extraction.
