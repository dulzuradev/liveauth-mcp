# Getting Started with LiveAuth

LiveAuth verifies humans economically instead of heuristically.

Instead of CAPTCHAs or tracking, LiveAuth asks the browser to perform a short cryptographic proof. If that fails or is skipped, it falls back to a small Bitcoin Lightning payment.

- No cookies
- No fingerprinting
- No behavioral profiling

---

## How It Works

When a user tries to log in or submit a form, LiveAuth presents a challenge. The user solves a lightweight cryptographic puzzle on their device (Proof-of-Work) or pays a tiny Lightning invoice (~1 satoshi). Success grants a short-lived JWT.

```
User → Login Form → LiveAuth SDK (PoW or Lightning challenge)
                          ↓
                   JWT issued on success
                          ↓
                   Your server validates JWT → allow access
```

---

## Add LiveAuth to Your Site

### 1. Get API Keys

Sign up at [liveauth.app](https://liveauth.app) and create a project. You'll get:
- **Public Key** (`la_pk_xxx`) — safe to expose in browser code
- **Secret Key** (`la_sk_xxx`) — keep this server-side

### 2. Install the SDK

```bash
npm install @liveauth-labs/sdk
```

### 3. Protect a Login Form

```javascript
import { LiveAuth } from '@liveauth-labs/sdk';

const liveauth = new LiveAuth({
  publicKey: 'la_pk_your_public_key',
  apiKey: 'la_sk_your_secret_key'  // enables Lightning fallback
});

// On form submit
const result = await liveauth.verify();

if (result.token) {
  // Send token to your backend for session creation
  await fetch('/api/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ liveauth_token: result.token })
  });
}
```

**Result types:**

```javascript
// PoW succeeded — send result.token to your server
{ method: 'pow', token: 'eyJhbGci...', solveMs: 412, difficultyBits: 18 }

// Lightning fallback — show invoice to user, poll for confirmation
{ method: 'lightning', lightning: { sessionId: 'sess_xxx', invoice: 'lnbc1p...', amountSats: 21 }, diagnostics: { reason: 'pow_unsupported' } }
```

---

## Validating the JWT

On your server, decode and verify the JWT using the secret key from your dashboard:

```javascript
const jwt = require('jsonwebtoken');

function verifyLiveAuthToken(token, secretKey) {
  try {
    const decoded = jwt.verify(token, secretKey, {
      issuer: 'LiveAuth',
      audience: 'LiveAuthUsers'  // fixed value — not configurable
    });

    return {
      valid: true,
      projectId: decoded.sub,        // project ID (string, e.g. "B842CAE1-E06E-480F-BE76-A64A75E0F871")
      agentId: decoded.aid,          // agent/human identifier
      authType: decoded.auth_type,   // 'pow', 'lightning', or 'l402'
      sessionId: decoded.jti          // unique token ID (use for deduplication)
    };
  } catch (err) {
    return { valid: false, reason: err.message };
  }
}
```

**JWT Claims:**

| Claim | Description | Example |
|-------|-------------|---------|
| `sub` | Project ID | `"B842CAE1-E06E-480F-BE76-A64A75E0F871"` |
| `aid` | Agent/user ID | `"agent_9x7k2"` |
| `auth_type` | Verification method | `"pow"`, `"lightning"`, `"l402"` |
| `iss` | Issuer | `"LiveAuth"` |
| `aud` | Audience | `"LiveAuthUsers"` |
| `exp` | Expiration | (Unix timestamp) |
| `iat` | Issued at | (Unix timestamp) |

---

## PoW vs Lightning

| Method | Cost | Speed | Best for |
|--------|------|-------|----------|
| **Proof-of-Work** | Free | Instant | Most logins, low-value actions |
| **Lightning** | ~1–21 sats | ~5 sec | Higher-security flows, bot prevention |

The SDK defaults to PoW first. If you configured an `apiKey`, Lightning appears as a fallback option users can choose.

---

## SDK Configuration

```javascript
const liveauth = new LiveAuth({
  publicKey: 'la_pk_xxx',        // Required: your project public key
  apiKey: 'la_sk_xxx',            // Optional: enables Lightning fallback
  baseUrl: 'https://api.liveauth.app',  // Optional: defaults to LiveAuth API
  forceLightning: false,          // Optional: skip PoW, go straight to Lightning
  powTimeoutMs: 30000,            // Optional: max PoW time before fallback (ms)
  maxPowIterations: 50000000,    // Optional: max iterations before fallback
  onProgress: (hashesPerSec, iterations) => {},  // Optional: PoW progress callback
  onVerified: (token) => {}      // Optional: called with JWT on success
});
```

---

## Try the Demo

See it in action at [docs.liveauth.app/demo.html](https://docs.liveauth.app/demo.html) — pick PoW (free) or Lightning tab to experience the full flow.

---

## MCP Tools for AI Agents

For AI agent tool calls, use LiveAuth MCP sessions. Agents can authenticate with proof-of-work, Lightning, or an L402 bundle, then your MCP server can meter generic usage with `/api/mcp/charge` or record paid tool revenue with `/api/mcp/tools/{toolId}/charge`. See **Add LiveAuth to MCP Tools** and **MCP Gate Design** in the sidebar.

---

## Need Help?

- **API Reference:** L402 Macaroon Spec, Add LiveAuth to MCP Tools, MCP Gate Design docs in the sidebar
- **SDK Source:** [github.com/dulzuradev/liveauth-js](https://github.com/dulzuradev/liveauth-js)
