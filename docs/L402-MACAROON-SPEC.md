# L402-Native Auth — Macaroon Spec & Endpoint Design

## Overview

Auth via Lightning invoice → macaroon credential. Agents pay upfront for bundles, use macaroon per-call.

---

## Macaroon Format

Macaroon = signed assertion containing claims. Format: `base64(cbor({claims})) + "." + base64(signature)`

### Claims

| Field | Type | Description |
|-------|------|-------------|
| `kid` | string | Key ID (matches API key prefix) |
| `aid` | string | Agent ID (e.g. `agent_xxx`) |
| `scopes` | string[] | Allowed endpoint scopes (e.g. `["mcp.verify", "auth.start"]`) |
| `bid` | string | Bundle ID (references purchased bundle) |
| `rate` | int | Calls remaining |
| `exp` | int | Unix timestamp — expiration |
| `iat` | int | Unix timestamp — issued at |
| `jti` | string | Unique token ID (for revocation) |

### Example

```json
{
  "kid": "la_pk_abc123",
  "aid": "agent_9x7k2",
  "scopes": ["mcp.verify", "auth.start"],
  "bid": "bundle_1k_001",
  "rate": 950,
  "exp": 1713300000,
  "iat": 1713296400,
  "jti": "tok_7f3a9b2c"
}
```

---

## Bundle Pricing Tiers

| Bundle | Calls | Price | Effective Rate |
|--------|-------|-------|----------------|
| Starter | 100 | 50 sats | 0.5 sat/call |
| Growth | 1,000 | 400 sats | 0.4 sat/call |
| Scale | 10,000 | 3,000 sats | 0.3 sat/call |
| Enterprise | 100,000 | 20,000 sats | 0.2 sat/call |

- Bundle credits decrement per call (not per verification attempt)
- Unused calls expire 90 days after purchase
- No refunds (Lightning is non-reversible)

---

## Endpoints

### 1. Create Bundle Purchase → Get Invoice

```
POST /api/public/l402/bundle/invoice
```

**Request:**
```json
{
  "tier": "growth"  // starter | growth | scale | enterprise
}
```

**Response:**
```json
{
  "bundleId": "bundle_growth_abc123",
  "invoice": "lnbc1p...",
  "amountSats": 400,
  "expiresAt": 1713297600
}
```

### 2. Webhook — Invoice Paid → Issue Macaroon

Internal only (not exposed to client). Listens for Lightning payment confirmation.

On success:
- Generate macaroon with `rate = bundle_calls_remaining`
- Store macaroon record (jti, bundle_id, agent_id, exp, scopes)
- Return macaroon to client (or hold for poll)

### 3. Poll / Fetch Macaroon

```
POST /api/public/l402/bundle/claim
```

**Request:**
```json
{
  "paymentHash": "abc123..."
}
```

**Response:**
```json
{
  "macaroon": "eyJraWQiOiJsYV9wayIsImFpZCI6ImFnZW50Xzl4N2syIi...",
  "expiresAt": 1713382800,
  "rate": 950,
  "scopes": ["mcp.verify", "auth.start"]
}
```

### 4. Validate Macaroon (Middleware)

```
Authorization: L402 <macaroon>
```

Middleware decodes macaroon, checks:
- Signature valid (HMAC-SHA256)
- Not expired (`exp`)
- Rate > 0
- JTI not revoked

On each call: decrement rate, update record.

### 5. Check Balance / Status

```
GET /api/public/l402/bundle/status?kid=la_pk_abc123
```

**Response:**
```json
{
  "bundleId": "bundle_growth_abc123",
  "rate": 872,
  "expiresAt": 1713382800,
  "tier": "growth"
}
```

---

## Rate Limiting & Abuse

- **Per-bundle rate limit:** Max 10 calls/minute per macaroon (configurable)
- **Per-IP rate limit:** 100 invoices/hour (prevent spam)
- **First/last seen tracking** — flag suspicious burst patterns
- **Bundle pause:** If rate hits 0, reject calls with `402 Payment Required`

---

## Recharge Flow

When rate runs low (<10% remaining), agent can purchase a new bundle:
```
POST /api/public/l402/bundle/invoice
```

New bundle gets a new macaroon. Old macaroon stays valid until expiry.

---

## Notes

- Use **HMAC-SHA256** for macaroon signature
- Macaroon itself is opaque to client — just a bearer token
- `scope` granularity can be added later (e.g. `["mcp.verify"]` only = cheaper bundle)