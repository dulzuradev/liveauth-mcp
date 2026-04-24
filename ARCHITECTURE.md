# LiveAuth Architecture

## What It Is

**LiveAuth** is a non-custodial CAPTCHA replacement and AI agent authentication platform. It uses Proof-of-Work (PoW) and Lightning Network payments as the cost-of-attack mechanism instead of tracking users or serving puzzles.

Target: SaaS product, $10–20k/month revenue. Non-custodial Bitcoin/Lightning throughout.

---

## System Overview

```
Browser / AI Agent
       │
       ├─ PoW challenge (free, CPU-bound)
       ├─ Lightning invoice (paid sats)
       └─ L402 macaroon (prepaid bundle)
              │
              ▼
        api.liveauth.app
              │
       ┌──────┴────────────────────────────────┐
       │                                        │
   liveauth-api                      liveauth-caddy
   (.NET 8 / EF Core)               (reverse proxy + TLS)
       │
       ├─ SQLite (prod) ────────────────► /data/liveauth.db
       ├─ LND / Lightning Service
       └─ JWT signing (HMAC-SHA256)
```

**Public sites (Caddy, port 443):**
- `https://liveauth.app` — main marketing + auth demo
- `https://admin.liveauth.app` — developer dashboard
- `https://docs.liveauth.app` — public docs
- `https://api.liveauth.app` — REST API

---

## Core Concepts

### The Three Auth Flows

| Flow | Mechanism | Cost | Latency | Use case |
|------|-----------|------|---------|----------|
| **PoW** | Client solves SHA256 hash puzzle | Free (CPU) | Instant | Human browsers, low-value actions |
| **Lightning** | Pay per-session invoice | 21 sats/login | ~1 sec | Higher-value sessions, bots |
| **L402** | Prepaid macaroon token | Bundled sats | Instant | AI agents, high-frequency calls |

### PoW Parameters
- **Difficulty**: adaptive per-project, starts at ~20 bits
- **Challenge TTL**: 5 minutes
- **Algorithm**: `SHA256(projectPublicKey:challengeHex:nonce)` — hash must be below target
- **Signature**: HMAC-SHA256 of challenge payload by server-side secret

### Lightning
- **LND** manages invoices and HTLCs
- **Per-login invoice**: created on auth start, checked on confirm
- **Sats per login**: configurable per project (`SatsPerLogin`, default 21)

### L402 (Macaroon-based)
- **Bundle**: prepaid block of calls (e.g. $29 = 50k sats = ~50k MCP calls)
- **Macaroon**: HMAC-SHA256 credential scoped to a bundle + project + expiry
- **Format**: `base64(jti | bundleId | scopes | expiry | signature)`
- **x402 compatible**: aligns with Cloudflare/Coinbase L402 draft standard

---

## Key Entities

### Developer
Portal owner. Signs up via email/password, GitHub OAuth, or Lightning invoice.
```
Id, Email, PasswordHash?, PasswordSalt?, LightningAuthKey?,
GitHubId?, GitHubUsername?, EmailVerified, VerificationToken?, VerificationExpiresAt?
```

**Developer Login Flows:**
1. **Email/Password** — register → verify email → login. Verification link: `https://liveauth.app/dev/verify-email?token=...`
2. **GitHub OAuth** — redirect to GitHub → callback with JWT
3. **Lightning** — generate invoice → pay → JWT issued on payment confirm

### Project
An API key pair owned by a Developer. Each project has its own PoW difficulty and sats config.
```
Id, DeveloperId, Name, PublicKey, SecretKeyHash,
IsActive, Plan, SatsPerLogin, L402BalanceSats,
WebhookUrl?, LndMacaroon? (encrypted), AllowedDomains[]
```

**System project** (`00000000-0000-0000-0000-000000000001`): used for internal AuthEvent FK
**Demo project** (`00000000-0000-0000-0000-000000000002`): `la_pk_demo` — bypass auth for demo flow

### AuthEvent
Audit log for all auth attempts.
```
Id, ProjectId, ApiKeyId?, EventType, ClientIp, Success, SatsPaid?, Reason?
```

### L402Bundle
Prepaid call bundle purchased via Lightning.
```
BundleId, ProjectId, DeveloperId, Tier,
TotalCalls, RemainingCalls, ExpiresAtUnix,
PaymentHash, Bolt11, AmountSats, Status (pending→paid→active→expired|depleted)
```

### L402Macaroon
HMAC credential issued against a bundle.
```
Jti, BundleId, ProjectId, AgentId?, ScopesJson,
ExpiresAtUnix, IssuedAt, IsRevoked, SignatureB64
```

### McpGateSession
Short-lived session for the MCP auth flow (10 min TTL).
```
Id, ProjectId, PowChallengeHex?, PowDifficultyBits?, PowSignature?,
LightningInvoice?, LightningPaymentHash?,
SatsPerCallAtStart, Status (pending|confirmed), ExpiresAt
```

### McpGateToken
JWT-issuing token for confirmed MCP sessions.
```
ProjectId, SessionId, JwtId, RefreshToken,
IssuedAt, ExpiresAt, CallsUsed, SatsUsed,
MaxCallsPerMinute, MaxSatsPerDay, DayWindowStart, Status
```

---

## SDKs

### `@liveauth-labs/sdk` (npm, v0.3.0)
Browser SDK for human verification. PoW + Lightning fallback.

```typescript
import { LiveAuth } from '@liveauth-labs/sdk';

const liveauth = new LiveAuth({
  publicKey: 'la_pk_xxx',
  apiKey: 'la_sk_xxx',    // for Lightning fallback
});

const result = await liveauth.verify();
// result.token → JWT for session
```

Also includes `BillingClient` for L402 balance top-ups via Lightning.

**Note**: The SDK's `AgentAuth` class (in `agent-auth.ts`) uses `X-LW-Public` header — not `X-LW-PublicKey`. This matches `PublicKeyAuthMiddleware` which reads `X-LW-Public`.

### `@liveauth-labs/mcp-server` (npm, v0.8.0)
CLI tool that wraps an MCP client and enforces LiveAuth auth before forwarding requests.

```bash
# Demo mode (no config needed)
npx @liveauth-labs/mcp-server

# Production
LIVEAUTH_API_KEY=la_sk_xxx npx @liveauth-labs/mcp-server
```

The MCP server exposes 5 tools: `liveauth_mcp_start`, `liveauth_mcp_confirm`, `liveauth_mcp_refresh`, `liveauth_mcp_usage`, `liveauth_mcp_status`.

---

## API Endpoints

### Public Auth
| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/public/auth/start` | — | Start PoW auth session |
| POST | `/api/public/auth/confirm` | — | Submit PoW solution |
| POST | `/api/public/auth/demo/start` | — | Demo mode (no PoW) |
| POST | `/api/public/auth/demo/confirm` | — | Confirm demo session |

### Developer Auth
| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/dev/auth/start` | — | Developer Lightning login start |
| POST | `/api/dev/auth/confirm` | — | Confirm Lightning developer login |
| POST | `/api/dev/auth/register` | — | Register new developer account |
| POST | `/api/dev/auth/verify-email` | — | Verify email via token (from link) |
| POST | `/api/dev/auth/login` | — | Email/password login |
| POST | `/api/dev/auth/forgot-password` | — | Request password reset email |
| POST | `/api/dev/auth/logout` | — | Logout (clears OAuth state cookie) |
| GET | `/api/dev/auth/github/status` | — | Check GitHub OAuth availability |
| GET | `/api/dev/auth/github/start` | — | Initiate GitHub OAuth flow |
| GET | `/api/dev/auth/github/callback` | — | GitHub OAuth callback |

### MCP Gate (`/api/mcp`)
| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/mcp/start` | API key | Start MCP auth (PoW or Lightning) |
| POST | `/api/mcp/confirm` | — | Confirm PoW or Lightning or L402 |
| POST | `/api/mcp/refresh` | JWT | Refresh MCP gate token |
| GET | `/api/mcp/status/{quoteId}` | API key | Check session/payment status |
| GET | `/api/mcp/lnurl/{quoteId}` | — | LNURL-compatible invoice lookup |
| POST | `/api/mcp/charge` | JWT | Deduct sats for an MCP call |
| GET | `/api/mcp/usage` | JWT | Get current session usage |

### L402 (`/api/public/l402`)
| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/public/l402/invoice` | — | Create L402 invoice |
| POST | `/api/public/l402/validate?paymentHash=` | — | Validate payment, get token |
| GET | `/api/public/l402/verify?token=` | — | Check token validity |

### Agent Auth (`/api/agent/auth`)
| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/agent/auth/start` | `X-LW-Public` | Start agent PoW auth |
| POST | `/api/agent/auth/verify` | `X-LW-Public` | Submit PoW solution |

### Billing
| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/billing/purchase` | Dev JWT | Purchase L402 credits |
| GET | `/api/billing/purchase/{id}` | Dev JWT | Check purchase status |

---

## Auth Middleware Pipeline

Request flow for API endpoints:

```
Request → Caddy (TLS termination)
        → liveauth-api (ASP.NET Core)
        → [Static files, docs] OR
        → PublicKeyAuthMiddleware (X-LW-Public header → Project lookup)
        → JwtAuthMiddleware (Bearer token → ClaimsPrincipal)
        → Controller
```

**Headers used:**
- `X-LW-Public` — project public key (matches `Project.PublicKey`)
- `X-LW-Secret` — project secret key hash (not currently enforced in middleware)
- `Authorization: Bearer <jwt>` — JWT from PoW/Lightning/L402 auth

---

## Pricing

| Plan | Price | Quota |
|------|-------|-------|
| Free | $0 | 500/month, no Lightning |
| Pro | ~$29/mo | 50k sats balance, custom LND, webhooks |

Bundle pricing: $29 = 50,000 sats ≈ 1 sat/call for MCP.

---

## Gotchas & Decision Patterns

### Schema Migrations
`EnsureCreatedAsync()` creates a baseline schema but **does not track column additions** from later `AddColumnIfMissingAsync` calls. Existing DBs won't get new columns automatically. Fix: direct `ALTER TABLE` on the running database.

### Docker Named Volumes
`docker cp` does NOT work with named volumes. Use `docker run --rm -v liveauth_sqlite_data:/data alpine` to write to them.

### Deploy Flatten Step
Angular builds into `dist/browser/` subdirectory. Caddy serves from `/srv/` (flat). **Always** run `flatten_dir` (copies `browser/*` → root) before deploying, or use `deploy.sh` which handles it. The `flatten_dir` also copies `browser/media/` → `media/`, `browser/docs/` → `docs/`, and `browser/liveauth-admin/` → `liveauth-admin/`.

### SDK Header Mismatch
`PublicKeyAuthMiddleware` reads `X-LW-Public`, NOT `X-LW-PublicKey`. The MCP server (`dulzuradev/liveauth-mcp`) was incorrectly sending `X-LW-PublicKey` — fixed in `8e427cb`.

### PoW Signing
PoW challenges are signed with HMAC-SHA256 by `PowChallengeSigner` using `PowHmacSecret` from config. The signature proves the challenge came from the server (prevents custom PoW attacks).

### MCP JWT
MCP gate JWTs are short-lived (10 min) and scoped to `projectId + sessionId`. They carry `authType` claim: `mcp_pow`, `mcp_lightning`, or `mcp_l402`.

### L402 vs Lightning
L402 is for **prepaid** bulk access (AI agents buying bundles). Lightning is for **per-session** payment (one-off logins). L402 macaroons survive beyond the MCP session lifecycle; Lightning invoices are single-use.

### Email / Resend
Transaction emails (verification, password reset) sent via **Resend API** (HTTP, not SMTP). Configured via `Resend__ApiKey`, `Resend__FromEmail`, `Resend__FromName` in docker-compose.yml. Verification tokens expire after 24 hours.

---

## File Locations

| Item | Location |
|------|----------|
| Server repo | `liveauth@64.225.32.102:/opt/liveauth/` |
| Docker compose | `/opt/liveauth/docker-compose.yml` |
| Caddyfile | `/opt/liveauth/Caddyfile` (mounted into `liveauth-caddy`) |
| SQLite DB | Named volume `liveauth_sqlite_data` → `/data/liveauth.db` |
| Web dist | `/opt/liveauth/LiveAuthWeb/dist/` → Caddy `/srv/` |
| MCP repo | `dulzuradev/liveauth-mcp` (GitHub) |
| SDK source | `LiveAuth/sdk/liveauth-js/` |
| Deploy script | `LiveAuth/scripts/deploy.sh` |
| Docs | `LiveAuth/docs/` (served at `docs.liveauth.app`) |

---

## Active npm Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `@liveauth-labs/sdk` | 0.3.0 | Browser PoW + Lightning SDK |
| `@liveauth-labs/mcp-server` | 0.8.0 | MCP server CLI |
