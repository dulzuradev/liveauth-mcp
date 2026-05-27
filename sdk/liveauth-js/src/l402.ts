/**
 * L402 Lightning Payment SDK for AI Agents
 * Pay-per-call and bundle purchase via Lightning Network
 */

// ─── Types ──────────────────────────────────────────────────────────────────

export interface L402ClientConfig {
  /** Project public key (la_pk_xxx) */
  publicKey: string;
  /** Project secret key (la_sk_xxx) */
  apiKey: string;
  /** Optional API base (default: https://api.liveauth.app) */
  baseUrl?: string;
  /** Optional custom amount in sats per call (default: server config) */
  amountSats?: number;
}

export interface InvoiceResult {
  /** Base64 payment hash (r_hash from LND) */
  paymentHash: string;
  /** Bolt11 invoice — pass to Lightning wallet / LNURL */
  bolt11: string;
  /** Sats being asked */
  amountSats: number;
  /** Unix timestamp when invoice expires */
  expiresAtUnix: number;
  /** Hint about token type */
  tokenScope: string;
  tokenScopeDescription: string;
  instructions: string;
}

export interface TokenResult {
  token: string;
  tokenType: 'L402';
  expiresInSeconds: number;
  tokenScope: string;
  tokenScopeDescription: string;
}

export interface BundleTier {
  name: 'starter' | 'growth' | 'scale' | 'enterprise';
  totalCalls: number;
  priceSats: number;
  effectiveRate: number;
  validDays: number;
}

export const BundleTiers: BundleTier[] = [
  { name: 'starter', totalCalls: 100, priceSats: 50, effectiveRate: 0.5, validDays: 90 },
  { name: 'growth', totalCalls: 1_000, priceSats: 400, effectiveRate: 0.4, validDays: 90 },
  { name: 'scale', totalCalls: 10_000, priceSats: 3_000, effectiveRate: 0.3, validDays: 90 },
  { name: 'enterprise', totalCalls: 100_000, priceSats: 20_000, effectiveRate: 0.2, validDays: 90 },
];

export interface BundleInvoiceResult {
  bundleId: string;
  bolt11: string;
  paymentHash: string;
  amountSats: number;
  expiresAtUnix: number;
  tier: string;
  totalCalls: number;
}

export interface BundleClaimResult {
  macaroon: string;
  bundleId: string;
  remainingCalls: number;
  expiresAtUnix: number;
  scopes: string[];
}

export interface BundleStatusResult {
  bundleId: string;
  tier: string;
  totalCalls: number;
  remainingCalls: number;
  usedCalls: number;
  expiresAtUnix: number;
  isExpired: boolean;
  isDepleted: boolean;
}

// ─── Helpers ────────────────────────────────────────────────────────────────

/**
 * Parse WWW-Authenticate header to detect L402 or x402 scheme.
 */
export function parseWwwAuthenticate(header: string): {
  schemes: string[];
  params: Record<string, string>;
} | null {
  if (!header) return null;

  const parts = header.trim().split(/\s+/);
  if (parts.length === 0) return null;

  const schemes: string[] = [];
  const params: Record<string, string> = {};

  for (const part of parts) {
    const colonIdx = part.indexOf('=');
    if (colonIdx === -1) {
      const scheme = part.replace(/,$/, '').toLowerCase();
      if (scheme) schemes.push(scheme);
    } else {
      const key = part.slice(0, colonIdx).toLowerCase();
      let val = part.slice(colonIdx + 1);
      // Strip trailing comma first (before quotes to handle ," properly)
      val = val.replace(/,$/, '');
      // Strip surrounding quotes
      val = val.replace(/^"/, '').replace(/"$/, '');
      params[key] = val;
      params[key] = val;
    }
  }

  return { schemes, params };
}

/**
 * Check if WWW-Authenticate header indicates L402 payment required.
 */
export function isL402Challenge(header: string | null): boolean {
  if (!header) return false;
  const parsed = parseWwwAuthenticate(header);
  return parsed !== null && (
    parsed.schemes.includes('l402') ||
    parsed.schemes.includes('x402')
  );
}

/**
 * Extract invoice details from a 402 response body.
 */
export function extractInvoiceFrom402(body: unknown): {
  endpoints?: { invoice?: string; validate?: string };
  paymentHash?: string;
  amountSats?: number;
} | null {
  if (!body || typeof body !== 'object') return null;
  const obj = body as Record<string, unknown>;

  return {
    endpoints: obj.endpoints as { invoice?: string; validate?: string } | undefined,
    paymentHash: obj.paymentHash as string | undefined,
    amountSats: obj.amountSats as number | undefined,
  };
}

/**
 * Retry a fetch request with an L402 token injected in Authorization header.
 */
export async function retryWithToken(
  url: string,
  init: RequestInit,
  token: string
): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set('Authorization', `L402 ${token}`);

  return fetch(url, {
    ...init,
    headers,
  });
}

// ─── L402Client ─────────────────────────────────────────────────────────────

export class L402Client {
  private readonly baseUrl: string;
  private readonly publicKey: string;
  private readonly apiKey: string;
  private readonly amountSats: number | undefined;
  private token: string | null = null;
  private tokenExpiry: number = 0;

  constructor(config: L402ClientConfig) {
    if (!config.publicKey) throw new Error('L402: publicKey is required');
    if (!config.apiKey) throw new Error('L402: apiKey is required');

    this.publicKey = config.publicKey;
    this.apiKey = config.apiKey;
    this.baseUrl = config.baseUrl ?? 'https://api.liveauth.app';
    this.amountSats = config.amountSats;
  }

  // ─── High-level API ───────────────────────────────────────────────────────

  /**
   * Make a capped HTTP request with automatic L402 payment.
   * If the server returns 402, we pay the invoice, get a token,
   * and retry the original request with the token attached.
   * Capped at one payment per call to prevent infinite loops.
   *
   * @example
   * ```ts
   * const l402 = new L402Client({ publicKey, apiKey });
   * const res = await l402.request('https://api.liveauth.app/api/mcp?q=test', {
   *   method: 'POST',
   *   headers: { 'Content-Type': 'application/json' },
   *   body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/list', params: {} }),
   * });
   * const data = await res.json();
   * ```
   */
  async request(url: string, init: RequestInit = {}): Promise<Response> {
    // Try with existing valid token first
    if (this.hasValidToken()) {
      const res = await this.authorizedFetch(url, init);
      if (res.status !== 402) return res;
      // Token was rejected — clear and retry payment flow
      this.clearToken();
    }

    // Check if we need a token via 402 challenge
    const probeRes = await fetch(url, {
      ...init,
      headers: this._authHeaders(init.headers),
    });

    if (probeRes.status !== 402) return probeRes;

    // Server wants payment — create invoice and pay
    const invoice = await this.createInvoice(url, this.amountSats);

    // In production: show bolt11 as QR code, wait for payment
    // For automated agents: validate immediately (if payment is instant via internal LN)
    const tokenResult = await this.validatePayment(invoice.paymentHash);

    // Retry with new token
    return this.authorizedFetch(url, init, tokenResult.token);
  }

  // ─── Mid-level API ───────────────────────────────────────────────────────

  /**
   * Create a Lightning invoice for current payment cycle.
   *
   * @param destination  Optional identifier shown in invoice memo
   * @param amountSats  Optional sats amount (default: server config)
   * @returns Invoice with bolt11 and payment hash
   *
   * @example
   * ```ts
   * const invoice = await l402.createInvoice('my-agent');
   * // Show invoice.bolt11 as QR code for human to pay
   * ```
   */
  async createInvoice(destination?: string, amountSats?: number): Promise<InvoiceResult> {
    const params = new URLSearchParams();
    if (destination) params.set('destination', destination);
    if (amountSats) params.set('amountSats', String(amountSats));
    params.set('publicKey', this.publicKey);

    const res = await fetch(`${this.baseUrl}/api/public/l402/invoice?${params}`, {
      method: 'POST',
      headers: this._baseHeaders(),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: 'Unknown error' }));
      throw new Error(err.error ?? err.message ?? `HTTP ${res.status}`);
    }

    const json = await res.json();
    return {
      paymentHash: json.paymentHash,
      bolt11: json.bolt11,
      amountSats: json.amountSats,
      expiresAtUnix: json.expiresAtUnix,
      tokenScope: json.tokenScope ?? 'time_scoped_bearer',
      tokenScopeDescription: json.tokenScopeDescription ?? '',
      instructions: json.instructions ?? '',
    };
  }

  /**
   * Validate payment and store resulting L402 token.
   *
   * @param paymentHash  From createInvoice result
   * @returns Token result with expiry info
   */
  async validatePayment(paymentHash: string): Promise<TokenResult> {
    const params = new URLSearchParams({ paymentHash, publicKey: this.publicKey });

    const res = await fetch(`${this.baseUrl}/api/public/l402/validate?${params}`, {
      method: 'POST',
      headers: this._baseHeaders(),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: 'Unknown error' }));
      throw new Error(err.error ?? err.message ?? `HTTP ${res.status}`);
    }

    const json = await res.json();
    const expiresInSeconds = json.expiresInSeconds ?? 3600;
    this.token = json.token;
    this.tokenExpiry = Math.floor(Date.now() / 1000) + expiresInSeconds;

    return {
      token: json.token,
      tokenType: json.tokenType ?? 'L402',
      expiresInSeconds,
      tokenScope: json.tokenScope ?? 'time_scoped_bearer',
      tokenScopeDescription: json.tokenScopeDescription ?? '',
    };
  }

  // ─── Low-level API ───────────────────────────────────────────────────────

  /** Check if we have a non-expired token cached */
  hasValidToken(): boolean {
    if (!this.token) return false;
    return Math.floor(Date.now() / 1000) < this.tokenExpiry;
  }

  /** Get cached token (or null) */
  getToken(): string | null {
    return this.token;
  }

  /** Set token manually */
  setToken(token: string, expiresAtUnix?: number): void {
    this.token = token;
    this.tokenExpiry = expiresAtUnix ?? Math.floor(Date.now() / 1000) + 3600;
  }

  /** Clear cached token */
  clearToken(): void {
    this.token = null;
    this.tokenExpiry = 0;
  }

  // ─── Internal ────────────────────────────────────────────────────────────

  private _baseHeaders(): Headers {
    return new Headers({
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    });
  }

  private _authHeaders(existing?: HeadersInit): Headers {
    const headers = new Headers(existing);
    headers.set('X-LW-Public', this.publicKey);
    if (this.hasValidToken()) {
      headers.set('Authorization', `L402 ${this.token}`);
    }
    return headers;
  }

  private async authorizedFetch(url: string, init: RequestInit, token?: string): Promise<Response> {
    const headers = new Headers(init.headers);
    headers.set('X-LW-Public', this.publicKey);
    headers.set('Authorization', `L402 ${token ?? this.token}`);

    return fetch(url, {
      ...init,
      headers,
    });
  }
}

// ─── L402Bundle ───────────────────────────────────────────────────────────────

export interface L402BundleConfig {
  publicKey: string;
  apiKey: string;
  baseUrl?: string;
}

export class L402Bundle {
  private readonly baseUrl: string;
  private readonly publicKey: string;
  private readonly apiKey: string;
  private macaroon: string | null = null;
  private bundleExpiry: number = 0;
  private remainingCalls: number = 0;

  constructor(config: L402BundleConfig) {
    if (!config.publicKey) throw new Error('L402Bundle: publicKey is required');
    if (!config.apiKey) throw new Error('L402Bundle: apiKey is required');

    this.publicKey = config.publicKey;
    this.apiKey = config.apiKey;
    this.baseUrl = config.baseUrl ?? 'https://api.liveauth.app';
  }

  // ─── Purchase flow ────────────────────────────────────────────────────────

  /**
   * Create a Lightning invoice for a bundle purchase.
   *
   * @param tier  Tier name: starter, growth, scale, enterprise
   * @param agentId  Optional agent ID for tracking
   * @returns Bundle invoice with bolt11 QR code data
   *
   * @example
   * ```ts
   * const bundle = new L402Bundle({ publicKey, apiKey });
   * const inv = await route.createInvoice('growth', 'my-agent');
   * // Show inv.bolt11 as QR code
   * ```
   */
  async createInvoice(tier: string, agentId?: string): Promise<BundleInvoiceResult> {
    const res = await fetch(`${this.baseUrl}/api/public/l402/bundle/invoice`, {
      method: 'POST',
      headers: this._headers(),
      body: JSON.stringify({ tier, agentId, publicKey: this.publicKey }),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: 'Unknown error' }));
      throw new Error(err.error ?? err.message ?? `HTTP ${res.status}`);
    }

    const json = await res.json();
    return {
      bundleId: json.bundleId,
      bolt11: json.bolt11 ?? json.invoice,
      paymentHash: json.paymentHash,
      amountSats: json.amountSats,
      expiresAtUnix: json.expiresAtUnix,
      tier: json.tier,
      totalCalls: json.totalCalls,
    };
  }

  /**
   * Poll until bundle is activated after payment.
   * Waits for blockchain confirmation + activation (usually < 1 min).
   *
   * @param paymentHash  From createInvoice
   * @param opts.pollIntervalMs  Default 2000ms
   * @param opts.timeoutMs       Default 10 min
   * @returns Macaroon + bundle details
   */
  async claim(
    paymentHash: string,
    opts: { pollIntervalMs?: number; timeoutMs?: number } = {}
  ): Promise<BundleClaimResult> {
    const { pollIntervalMs = 2000, timeoutMs = 600_000 } = opts;
    const deadline = Date.now() + timeoutMs;

    while (Date.now() < deadline) {
      const res = await fetch(`${this.baseUrl}/api/public/l402/bundle/claim`, {
        method: 'POST',
        headers: this._headers(),
        body: JSON.stringify({
          paymentHash,
          publicKey: this.publicKey,
        }),
      });

      if (res.ok) {
        const json = await res.json();
        this.macaroon = json.macaroon;
        this.bundleExpiry = json.expiresAtUnix;
        this.remainingCalls = json.remainingCalls;

        return {
          macaroon: json.macaroon,
          bundleId: json.bundleId,
          remainingCalls: json.remainingCalls,
          expiresAtUnix: json.expiresAtUnix,
          scopes: json.scopes ?? [],
        };
      }

      if (res.status === 402 || res.status === 409) {
        // Payment not confirmed yet — wait and retry
        await sleep(pollIntervalMs);
        continue;
      }

      const err = await res.json().catch(() => ({ error: 'Unknown error' }));
      throw new Error(err.error ?? err.message ?? `HTTP ${res.status}`);
    }

    throw new Error('Bundle claim timed out — payment may still be pending');
  }

  /**
   * Get bundle status (remaining calls, expiry).
   *
   * @param bundleId  Bundle ID (from createInvoice or claim result)
   */
  async getStatus(bundleId: string): Promise<BundleStatusResult> {
    const res = await fetch(
      `${this.baseUrl}/api/public/l402/bundle/status?bundleId=${bundleId}&publicKey=${this.publicKey}`,
      { headers: this._headers() }
    );

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: 'Unknown error' }));
      throw new Error(err.error ?? err.message ?? `HTTP ${res.status}`);
    }

    const json = await res.json();
    return {
      bundleId: json.bundleId,
      tier: json.tier,
      totalCalls: json.totalCalls,
      remainingCalls: json.remainingCalls,
      usedCalls: json.usedCalls,
      expiresAtUnix: json.expiresAtUnix,
      isExpired: json.isExpired,
      isDepleted: json.isDepleted,
    };
  }

  /**
   * Make an authenticated request using the macaroon from a claimed bundle.
   */
  async request(url: string, init: RequestInit = {}): Promise<Response> {
    if (!this.macaroon) {
      throw new Error('L402Bundle: must call claim() before making requests');
    }

    const headers = new Headers(init.headers);
    headers.set('Authorization', `macaroon ${this.macaroon}`);
    headers.set('X-LW-Public', this.publicKey);

    return fetch(url, {
      ...init,
      headers,
    });
  }

  // ─── Bundle state ────────────────────────────────────────────────────────

  hasValidMacaroon(): boolean {
    if (!this.macaroon) return false;
    return Math.floor(Date.now() / 1000) < this.bundleExpiry;
  }

  getRemainingCalls(): number {
    return this.remainingCalls;
  }

  // ─── Internal ────────────────────────────────────────────────────────────

  private _headers(): Headers {
    return new Headers({
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-LW-Public': this.publicKey,
    });
  }
}

// ─── Internal ─────────────────────────────────────────────────────────────────

const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));
