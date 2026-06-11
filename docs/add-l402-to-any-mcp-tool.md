# Add LiveAuth to Any MCP Tool

Monetize AI agent tool calls with LiveAuth MCP sessions. Agents can authenticate with proof-of-work, a Lightning-backed session, or an L402 bundle. Your tool server charges each call through LiveAuth and, when configured with a registered tool ID, LiveAuth records a revenue event with gross sats, platform fee, and net sats.

---

## How It Works

```
Agent                    LiveAuth                    Your MCP Tool
  │                           │                            │
  │  1. start/confirm session ─►  (PoW, Lightning, or L402)  │
  │  ◄─ MCP JWT                 │                            │
  │  2. invoke MCP tool ────────────────────────────────────► │
  │                           ◄── POST /api/mcp/tools/{id}/charge │
  │                           ──► ok + revenueEventId + receipt │
  │  ◄──────────────────────────  tool result                 │
```

---

## Step 1: Obtain an MCP JWT

The simplest path is proof-of-work:

```bash
curl -X POST https://api.liveauth.app/api/mcp/start \
  -H "Content-Type: application/json" \
  -H "X-LW-Public: la_pk_your_public_key" \
  -d '{}'
```

The response includes a `quoteId` and `powChallenge`. Solve the challenge, then confirm:

```bash
curl -X POST https://api.liveauth.app/api/mcp/confirm \
  -H "Content-Type: application/json" \
  -H "X-LW-Public: la_pk_your_public_key" \
  -d '{
    "quoteId": "uuid",
    "challengeHex": "abc123",
    "nonce": 42,
    "hashHex": "0000...",
    "difficultyBits": 18,
    "expiresAtUnix": 1745032800,
    "sig": "signed-challenge"
  }'
```

Response:

```json
{
  "jwt": "eyJhbG...",
  "expiresIn": 600,
  "remainingBudgetSats": 10000,
  "refreshToken": "refresh-token"
}
```

You can also force a Lightning invoice:

```bash
curl -X POST https://api.liveauth.app/api/mcp/start \
  -H "Content-Type: application/json" \
  -H "X-LW-Public: la_pk_your_public_key" \
  -d '{"forceLightning": true}'
```

Or use L402 bundle mode as shown below.

---

## Optional: Purchase an L402 Call Bundle

```bash
# Choose a tier: starter (100 calls), growth (1k), scale (10k), enterprise (100k)
curl -X POST https://api.liveauth.app/api/public/l402/bundle/invoice \
  -H "Content-Type: application/json" \
  -d '{"tier": "starter"}'
```

**Response:**
```json
{
  "bundleId": "bundle_starter_a1b2c3d4e5f6",
  "invoice": "lnbc1p...",
  "paymentHash": "abc123...",
  "amountSats": 50,
  "expiresAtUnix": 1745032800,
  "tier": "starter",
  "totalCalls": 100
}
```

**Pay the invoice** (any Lightning wallet), then:

```bash
curl -X POST https://api.liveauth.app/api/public/l402/bundle/claim \
  -H "Content-Type: application/json" \
  -d '{"paymentHash": "abc123..."}'
```

**Response:**
```json
{
  "macaroon": "eyJraWQiOiJsYSIsImFpZCI6ImFnZW50Ii...",
  "bundleId": "bundle_starter_a1b2c3d4e5f6",
  "remainingCalls": 100,
  "expiresAtUnix": 1745623200,
  "scopes": ["mcp.verify", "auth.start"]
}
```

---

## Optional: Authenticate With an L402 Bundle

```bash
# Start MCP session with L402 bundle mode
curl -X POST https://api.liveauth.app/api/mcp/start \
  -H "Content-Type: application/json" \
  -H "X-LW-Public: la_pk_your_public_key" \
  -d '{"forceL402": true}'
```

**Response:**
```json
{
  "quoteId": "uuid",
  "authHint": "l402_bundle"
}
```

Now confirm with your macaroon:

```bash
curl -X POST https://api.liveauth.app/api/mcp/confirm \
  -H "Content-Type: application/json" \
  -H "X-LW-Public: la_pk_your_public_key" \
  -d '{
    "quoteId": "uuid",
    "macaroon": "eyJraWQiOiJsYSIsImFpZCI6ImFnZW50Ii..."
  }'
```

**Response:**
```json
{
  "jwt": "eyJhbG...",
  "expiresIn": 600,
  "remainingBudgetSats": 99,
  "paymentStatus": "l402_paid"
}
```

---

## Step 2: Register Your MCP Tool

In the developer dashboard, open **MCP Tool Revenue**, choose **Register MCP tool**, then set:

- Tool name and slug.
- Optional project association.
- Status: `Draft`, `Active`, or `Paused`.
- Visibility: `Private`, `Unlisted`, or `Public`.
- Minimum, default, and maximum sats per call.

The dashboard returns a tool ID and an integration snippet. Use that tool ID as `LIVEAUTH_TOOL_ID`.

You can also register through the developer API:

```bash
curl -X POST https://api.liveauth.app/api/dev/mcp-tools \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <developer-jwt>" \
  -d '{
    "projectId": "optional-project-guid",
    "name": "Paid Research Tool",
    "slug": "paid-research-tool",
    "description": "Searches a paid corpus for agent workflows.",
    "visibility": "Private",
    "status": "Draft",
    "defaultCostSats": 5,
    "minCostSats": 1,
    "maxCostSats": 25
  }'
```

Tool slugs are globally unique. Deleted tools are soft-deleted so historical revenue events remain auditable.

---

## Step 3: Charge a Paid MCP Tool Call

For usage metering only, call the generic endpoint:

```bash
curl -X POST https://api.liveauth.app/api/mcp/charge \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer eyJhbG..." \
  -H "X-LW-Public: la_pk_your_public_key" \
  -d '{"callCostSats": 1}'
```

For monetized tools, use the tool-attributed endpoint:

```bash
curl -X POST https://api.liveauth.app/api/mcp/tools/<tool-guid>/charge \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer eyJhbG..." \
  -H "X-LW-Public: la_pk_your_public_key" \
  -d '{
    "toolMethodName": "web_fetch",
    "callCostSats": 5,
    "idempotencyKey": "request-or-call-id",
    "agentId": "optional-agent-id",
    "metadata": {
      "urlHost": "example.com"
    }
  }'
```

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

The signed receipt is derived from the persisted revenue event, so it is stable for idempotent retries and can be stored alongside your tool result for audit. If the same `idempotencyKey` is retried for the same tool, LiveAuth returns the original charge and receipt instead of double charging. If the session budget is exhausted, the response is:

```json
{
  "status": "deny",
  "callsUsed": 3,
  "satsUsed": 15,
  "reason": "budget_exceeded"
}
```

The v1 platform fee is 500 basis points (5%), with a 1 sat minimum fee whenever gross sats are positive.

---

## Step 4: Wrap a Tool Handler With the SDK

```ts
import { createMcpGate } from '@liveauth-labs/mcp-server';

const gate = createMcpGate({
  publicKey: process.env.LIVEAUTH_PUBLIC_KEY!,
  baseUrl: process.env.LIVEAUTH_API_URL ?? 'https://api.liveauth.app',
  toolId: process.env.LIVEAUTH_TOOL_ID!,
  defaultCostSats: 5,
});

const output = await gate.invoke(
  jwtFromMcpRequest,
  { url: 'https://example.com' },
  async (input, context) => {
    const html = await fetch(input.url).then(r => r.text());
    return {
      html,
      charge: context.liveAuth.charge
    };
  },
  { requestId: 'req_123' },
  {
    costSats: 5,
    toolMethodName: 'web_fetch',
    idempotencyKey: 'req_123',
    metadata: { urlHost: 'example.com' }
  }
);
```

When `toolId` is omitted, the SDK uses `/api/mcp/charge` for backward-compatible metering. When `toolId` is present, it uses `/api/mcp/tools/{toolId}/charge` and records revenue attribution.

---

## View Tool Revenue

Use the dashboard **MCP Tool Revenue** section or call:

```http
GET /api/dev/mcp-tools/{toolId}/revenue?windowHours=24
GET /api/dev/mcp-tools/{toolId}/revenue/events?limit=50
```

The revenue view shows call count, gross sats, LiveAuth platform fee, net sats, and recent event metadata. Keep metadata small and audit-oriented; do not store fetched content, prompts, completions, credentials, or private tool output there.

---

## Check Bundle Status Anytime

```bash
curl "https://api.liveauth.app/api/public/l402/bundle/status?bundleId=bundle_starter_a1b2c3d4e5f6"
```

**Response:**
```json
{
  "bundleId": "bundle_starter_a1b2c3d4e5f6",
  "tier": "starter",
  "totalCalls": 100,
  "remainingCalls": 87,
  "usedCalls": 13,
  "expiresAtUnix": 1745623200,
  "isExpired": false,
  "isDepleted": false
}
```

---

## Bundle Tiers

| Tier | Calls | Price | Rate |
|------|-------|-------|------|
| Starter | 100 | 50 sats | 0.5 sat/call |
| Growth | 1,000 | 400 sats | 0.4 sat/call |
| Scale | 10,000 | 3,000 sats | 0.3 sat/call |
| Enterprise | 100,000 | 20,000 sats | 0.2 sat/call |

---

## Macaroon Format

The macaroon is a signed bearer token. Don't parse it — just present it on confirm.

**Format:** `base64(JSON_claims).base64(HMAC-SHA256_signature)`

**Claims include:**
- `kid` — project/key ID
- `aid` — agent identifier
- `scopes` — allowed operations
- `bid` — bundle ID
- `rate` — remaining calls
- `exp` — expiry timestamp
- `jti` — unique token ID (for revocation)

---

## Node.js / TypeScript Integration

```javascript
import { Macaroon } from '@liveauth-labs/mcp-server';

const bundle = await fetch('https://api.liveauth.app/api/public/l402/bundle/invoice', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ tier: 'growth' })
}).then(r => r.json());

// Pay the bundle.invoice with your Lightning wallet, then:
const claim = await fetch('https://api.liveauth.app/api/public/l402/bundle/claim', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ paymentHash: bundle.paymentHash })
}).then(r => r.json());

console.log('Macaroon:', claim.macaroon);
// Store macaroon for session auth
```

---

## Troubleshooting

**"Payment not yet received" on /claim**
→ The Lightning invoice hasn't been paid yet. Pay the invoice first, then retry.

**"Macaroon expired"**
→ Bundles are valid 90 days from activation. Purchase a new bundle if yours expired.

**"Bundle depleted"**
→ All calls used. Purchase a higher tier bundle.

**"Invalid macaroon signature"**
→ The macaroon was corrupted or modified. Re-fetch from /claim if the bundle is still active.

---

## See Also

- [L402 Macaroon Spec](L402-MACAROON-SPEC.md) — detailed macaroon format
- [MCP LiveAuth Gate](mcp-liveauth-gate.md) — full MCP gate design
- [LiveAuth Dashboard](https://liveauth.app) — get API keys, view usage
