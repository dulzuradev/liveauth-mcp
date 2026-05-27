# @liveauth-labs/l402-sdk

L402 Lightning payment SDK for AI agents. Pay-per-call and bundle purchase via the Lightning Network — no browser, no credit card.

## Installation

```bash
npm install @liveauth-labs/l402-sdk
```

## What is L402?

[L402](https://l402.com) is the Lightning-native evolution of HTTP 402 *Payment Required*. Instead of credit cards or SaaS billing, AI agents pay per API call in sats via Lightning.

**How it works:**
1. Client calls a metered API endpoint → server responds `402 Payment Required` with a Lightning invoice
2. Client pays the invoice via Lightning
3. Client validates payment → receives an L402 bearer token
4. Client uses the token for gated API calls

## Pay-Per-Call (L402Client)

Best for metered APIs where each call costs 1–5 sats. The SDK handles the 402 flow automatically.

```typescript
import { L402Client } from '@liveauth-labs/l402-sdk';

const l402 = new L402Client({
  publicKey: process.env.LIVEAUTH_PUBLIC_KEY!,   // la_pk_xxx
  apiKey: process.env.LIVEAUTH_SECRET_KEY!,       // la_sk_xxx
});

// Auto-paying: SDK intercepts 402, pays invoice, retries — all in one call
const res = await l402.request('https://api.liveauth.app/api/mcp', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/list', params: {} }),
});

const data = await res.json();
```

### Manual flow (mid-level API)

```typescript
// 1. Create invoice
const invoice = await l402.createInvoice('my-agent', /* amountSats */ 1);
console.log('Pay this bolt11:', invoice.bolt11);

// 2. Show QR code (in a UI), or for automated: validate immediately
//    In production: show invoice.bolt11 to user, wait for payment via LN wallet

// 3. Validate payment and get L402 token
const token = await l402.validatePayment(invoice.paymentHash);
console.log('Token expires in:', token.expiresInSeconds, 'seconds');

// 4. Use token for subsequent calls
const res = await fetch('https://api.liveauth.app/api/mcp', {
  headers: { 'Authorization': `L402 ${token.token}` }
});
```

### Token reuse

Tokens are valid for 1 hour (server-configurable). The SDK caches them automatically:

```typescript
const l402 = new L402Client({ publicKey, apiKey });

// First call: triggers 402 → pays → gets token
// Second call: uses cached token (no payment needed)
const res1 = await l402.request(url, init);
const res2 = await l402.request(url, init); // Same token, no re-pay
```

You can also manage tokens manually:

```typescript
l402.setToken(token, expiresAtUnix);  // Restore from storage
l402.getToken();                        // Get current token
l402.hasValidToken();                   // Check expiry
l402.clearToken();                      // Force re-auth
```

---

## Bundle Purchase (L402Bundle)

For agents that make many calls, bundles offer better rates than pay-per-call:

| Tier       | Calls   | Sats  | Effective rate |
|------------|---------|-------|----------------|
| starter    | 100     | 50    | 0.5 sat/call   |
| growth     | 1,000   | 400   | 0.4 sat/call   |
| scale      | 10,000  | 3,000 | 0.3 sat/call   |
| enterprise | 100,000 | 20,000| 0.2 sat/call   |

Validity: **90 days** from activation.

```typescript
import { L402Bundle, BundleTiers } from '@liveauth-labs/l402-sdk';

const bundle = new L402Bundle({ publicKey, apiKey });

// Show tier options
console.log('Tiers:', BundleTiers.map(t => `${t.name}: ${t.priceSats} sats for ${t.totalCalls} calls`));

// 1. Create invoice for the tier you want
const inv = await bundle.createInvoice('growth', 'my-agent');
console.log(`Pay ${inv.amountSats} sats → ${inv.totalCalls} calls`);
console.log('Bolt11:', inv.bolt11);

// 2. Show bolt11 as QR code (use bolt11-qr library or LNURL QR)
//    User pays via their Lightning wallet (Alby, phoenix, etc.)

// 3. Poll until payment confirms and bundle activates
const claim = await bundle.claim(inv.paymentHash);
console.log('Macaroon:', claim.macaroon);
console.log('Calls remaining:', claim.remainingCalls);

// 4. Make authenticated requests
const res = await bundle.request('https://api.liveauth.app/api/mcp', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/list', params: {} }),
});
```

### Check bundle status

```typescript
// At any time
const status = await bundle.getStatus(claim.bundleId);
console.log(`Remaining: ${status.remainingCalls}/${status.totalCalls}`);
console.log(`Expired: ${status.isExpired}, Depleted: ${status.isDepleted}`);
```

---

## Helpers

```typescript
import { 
  parseWwwAuthenticate,
  isL402Challenge,
  extractInvoiceFrom402,
  retryWithToken 
} from '@liveauth-labs/l402-sdk';

// Parse WWW-Authenticate header to detect payment challenge
const wwwAuth = response.headers.get('WWW-Authenticate');
if (wwwAuth && isL402Challenge(wwwAuth)) {
  // Server wants L402 payment
  const info = extractInvoiceFrom402(await response.json());
  console.log('Amount:', info.amountSats, 'sats');
}

// Manual retry with token
const res = await retryWithToken(url, init, l402.getToken()!);
```

---

## API Reference

### `new L402Client(config)`

| Parameter   | Required | Description                              |
|-------------|----------|------------------------------------------|
| `publicKey` | Yes      | Project public key (`la_pk_xxx`)          |
| `apiKey`    | Yes      | Project secret key (`la_sk_xxx`)          |
| `baseUrl`   | No       | API base URL (default: `api.liveauth.app`) |
| `amountSats`| No       | Custom sats per call (default: server config) |

### `L402Client` methods

| Method | Returns | Description |
|--------|---------|-------------|
| `request(url, init)` | `Promise<Response>` | Auto-paying HTTP request |
| `createInvoice(destination?, amountSats?)` | `InvoiceResult` | Create Lightning invoice |
| `validatePayment(paymentHash)` | `TokenResult` | Validate invoice, get token |
| `hasValidToken()` | `boolean` | Check if cached token is valid |
| `getToken()` | `string \| null` | Get current token |
| `setToken(token, expiresAtUnix?)` | `void` | Set token manually |
| `clearToken()` | `void` | Clear cached token |

### `new L402Bundle(config)`

| Parameter   | Required | Description |
|-------------|----------|-------------|
| `publicKey` | Yes      | Project public key |
| `apiKey`    | Yes      | Project secret key |
| `baseUrl`   | No       | API base URL |

### `L402Bundle` methods

| Method | Returns | Description |
|--------|---------|-------------|
| `createInvoice(tier, agentId?)` | `BundleInvoiceResult` | Create bundle purchase invoice |
| `claim(paymentHash, opts?)` | `BundleClaimResult` | Poll until bundle activates |
| `getStatus(bundleId)` | `BundleStatusResult` | Check remaining calls / expiry |
| `request(url, init)` | `Promise<Response>` | Authenticated request via macaroon |

### `BundleTiers`

Static array of all tier configs:

```typescript
BundleTiers.map(t => `${t.name}: ${t.priceSats}sats / ${t.totalCalls} calls @ ${t.effectiveRate} sat/call`)
```

---

## Requirements

- **Node.js 18+** or any modern browser with `fetch` + `crypto.subtle` built in
- No runtime dependencies — pure TypeScript

---

## Related

- [LiveAuth Docs](https://docs.liveauth.app) — Full API reference
- [@liveauth-labs/sdk](https://www.npmjs.com/package/@liveauth-labs/sdk) — PoW + Lightning auth for AI agents
- [@liveauth-labs/mcp-server](https://www.npmjs.com/package/@liveauth-labs/mcp-server) — MCP server drop-in auth
- [LiveAuth Dashboard](https://liveauth.app) — Get your API keys
