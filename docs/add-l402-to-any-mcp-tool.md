# Add L402 to Any MCP Tool in 5 Minutes

Monetize AI agent tool calls with Lightning payments. Agents purchase call bundles upfront, then invoke your tools at 0.2–0.5 sat/call.

---

## How It Works

```
Agent                    LiveAuth                    Your MCP Tool
  │                           │                            │
  │  1. POST /bundle/invoice ──►  (creates Lightning invoice)  │
  │  ◄─ bolt11                 │                            │
  │  2. pays invoice           │                            │
  │  3. POST /bundle/claim ──►  │                            │
  │  ◄─ macaroon               │                            │
  │  4. POST /mcp/start ────────►  (presents macaroon)       │
  │  ◄─ JWT + remaining        │                            │
  │  5. invoke tools ──────────►  (JWT auth, no extra payment) │
```

---

## Step 1: Purchase a Call Bundle

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

## Step 2: Authenticate Your MCP Session

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
  "quoteId": "session-uuid-here",
  "authHint": "l402_bundle"
}
```

Now confirm with your macaroon:

```bash
curl -X POST https://api.liveauth.app/api/mcp/confirm \
  -H "Content-Type: application/json" \
  -H "X-LW-Public: la_pk_your_public_key" \
  -d '{
    "quoteId": "session-uuid-here",
    "macaroon": "eyJraWQiOiJsYSIsImFpZCI6ImFnZW50Ii..."
  }'
```

**Response:**
```json
{
  "jwt": "eyJhbG...",
  "expiresIn": 600,
  "remainingBudgetSats": 99,
  "paymentStatus": "l402_paid",
  "refreshToken": "ref_xxx"
}
```

---

## Step 3: Use the JWT for MCP Tool Calls

```bash
# Use the JWT to call MCP endpoints
curl -H "Authorization: Bearer eyJhbG..." \
     https://api.liveauth.app/api/mcp/charge \
  -d '{"tool": "my-tool", "costSats": 1}'
```

Each MCP tool invocation costs 1 sat (or as configured per project). The bundle tracks remaining calls automatically.

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

- [L402 Macaroon Spec](../L402-MACAROON-SPEC.md) — detailed macaroon format
- [MCP LiveAuth Gate](../mcp-liveauth-gate.md) — full MCP gate design
- [LiveAuth Dashboard](https://liveauth.app) — get API keys, view usage