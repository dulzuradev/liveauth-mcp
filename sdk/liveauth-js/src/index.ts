/**
 * @liveauth-labs/l402-sdk
 * L402 Lightning payment SDK for AI agents
 *
 * @example
 * ```ts
 * import { L402Client, L402Bundle, BundleTiers } from '@liveauth-labs/l402-sdk';
 *
 * // Pay-per-call
 * const l402 = new L402Client({ publicKey, apiKey });
 * const res = await l402.request('https://api.liveauth.app/api/mcp', {
 *   method: 'POST',
 *   headers: { 'Content-Type': 'application/json' },
 *   body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/list', params: {} }),
 * });
 *
 * // Bundle purchase
 * const bundle = new L402Bundle({ publicKey, apiKey });
 * const inv = await bundle.createInvoice('growth', 'my-agent');
 * // Show inv.bolt11 as QR code...
 * const claim = await bundle.claim(inv.paymentHash);
 * const res = await bundle.request(url, init);
 * ```
 */

// Re-export billing (credits/purchase flow)
export {
  BillingClient,
  type BillingClientConfig,
  type PurchaseResult,
  type PurchaseStatus,
} from './billing.js';

// Re-export L402 (pay-per-call + bundles)
export {
  L402Client,
  L402Bundle,
  BundleTiers,
  parseWwwAuthenticate,
  isL402Challenge,
  extractInvoiceFrom402,
  retryWithToken,
  type L402ClientConfig,
  type InvoiceResult,
  type TokenResult,
  type BundleTier,
  type BundleInvoiceResult,
  type BundleClaimResult,
  type BundleStatusResult,
  type L402BundleConfig,
} from './l402.js';
