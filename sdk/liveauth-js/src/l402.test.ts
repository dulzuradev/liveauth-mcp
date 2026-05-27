import { describe, it, expect, vi, beforeEach } from 'vitest';
import { L402Client, L402Bundle, BundleTiers, parseWwwAuthenticate, isL402Challenge, extractInvoiceFrom402 } from '../src/l402.js';

// ─── Helpers ─────────────────────────────────────────────────────────────────

function mockResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    headers: new Headers(),
  } as unknown as Response;
}

// ─── parseWwwAuthenticate ──────────────────────────────────────────────────

describe('parseWwwAuthenticate', () => {
  it('parses L402 scheme', () => {
    const result = parseWwwAuthenticate('L402 macaroon="abc123", invoice="https://..."');
    expect(result).not.toBeNull();
    expect(result!.schemes).toContain('l402');
    expect(result!.params.macaroon).toBe('abc123');
  });

  it('parses x402 scheme', () => {
    const result = parseWwwAuthenticate('x402 maxAmount="1000"');
    expect(result).not.toBeNull();
    expect(result!.schemes).toContain('x402');
  });

  it('returns null for empty', () => {
    expect(parseWwwAuthenticate('')).toBeNull();
  });

  it('detects plain Bearer as scheme', () => {
    const result = parseWwwAuthenticate('Bearer');
    expect(result).not.toBeNull();
    expect(result!.schemes).toContain('bearer');
  });
});

// ─── isL402Challenge ───────────────────────────────────────────────────────

describe('isL402Challenge', () => {
  it('detects L402', () => expect(isL402Challenge('L402 macaroon="abc"')).toBe(true));
  it('detects x402', () => expect(isL402Challenge('x402 maxAmount="500"')).toBe(true));
  it('ignores Bearer', () => expect(isL402Challenge('Bearer token="xyz"')).toBe(false));
  it('handles null', () => expect(isL402Challenge(null)).toBe(false));
});

// ─── extractInvoiceFrom402 ─────────────────────────────────────────────────

describe('extractInvoiceFrom402', () => {
  it('extracts payment hash and amount', () => {
    const result = extractInvoiceFrom402({
      paymentHash: 'abc123',
      amountSats: 5,
      endpoints: { invoice: '/api/l402/invoice', validate: '/api/l402/validate' }
    });
    expect(result!.paymentHash).toBe('abc123');
    expect(result!.amountSats).toBe(5);
    expect(result!.endpoints!.invoice).toBe('/api/l402/invoice');
  });

  it('returns null for non-object', () => {
    expect(extractInvoiceFrom402(null)).toBeNull();
    expect(extractInvoiceFrom402('not an object')).toBeNull();
  });
});

// ─── L402Client ─────────────────────────────────────────────────────────────

describe('L402Client', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('requires publicKey and apiKey', () => {
    expect(() => new L402Client({ publicKey: '', apiKey: 'sk_xxx' })).toThrow('L402: publicKey is required');
    expect(() => new L402Client({ publicKey: 'pk_xxx', apiKey: '' })).toThrow('L402: apiKey is required');
  });

  describe('createInvoice()', () => {
    it('creates invoice via API', async () => {
      const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(mockResponse({
        paymentHash: 'hash_abc123',
        bolt11: 'lnbc50...',
        amountSats: 1,
        expiresAtUnix: 1715000000,
        tokenScope: 'time_scoped_bearer',
        tokenScopeDescription: ' bearer',
        instructions: 'Pay this invoice...',
      }));
      vi.stubGlobal('fetch', fetchMock);

      const client = new L402Client({ publicKey: 'la_pk_xxx', apiKey: 'la_sk_xxx' });
      const invoice = await client.createInvoice('my-agent', 1);

      expect(invoice.paymentHash).toBe('hash_abc123');
      expect(invoice.bolt11).toBe('lnbc50...');
      expect(invoice.amountSats).toBe(1);
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/public/l402/invoice'),
        expect.objectContaining({ method: 'POST' })
      );
    });
  });

  describe('validatePayment()', () => {
    it('stores token and returns result', async () => {
      const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(mockResponse({
        token: 'preimage_token_xyz',
        tokenType: 'L402',
        expiresInSeconds: 3600,
        tokenScope: 'time_scoped_bearer',
        tokenScopeDescription: '',
      }));
      vi.stubGlobal('fetch', fetchMock);

      const client = new L402Client({ publicKey: 'la_pk_xxx', apiKey: 'la_sk_xxx' });
      const result = await client.validatePayment('hash_abc123');

      expect(result.token).toBe('preimage_token_xyz');
      expect(result.tokenType).toBe('L402');
      expect(result.expiresInSeconds).toBe(3600);
      expect(client.hasValidToken()).toBe(true);
    });
  });

  describe('hasValidToken()', () => {
    it('returns false when no token', () => {
      const client = new L402Client({ publicKey: 'la_pk_xxx', apiKey: 'la_sk_xxx' });
      expect(client.hasValidToken()).toBe(false);
    });

    it('returns true after token set', () => {
      const client = new L402Client({ publicKey: 'la_pk_xxx', apiKey: 'la_sk_xxx' });
      client.setToken('my_token', Math.floor(Date.now() / 1000) + 3600);
      expect(client.hasValidToken()).toBe(true);
    });

    it('returns false for expired token', () => {
      const client = new L402Client({ publicKey: 'la_pk_xxx', apiKey: 'la_sk_xxx' });
      client.setToken('my_token', Math.floor(Date.now() / 1000) - 1);
      expect(client.hasValidToken()).toBe(false);
    });
  });

  describe('token management', () => {
    it('setToken / getToken / clearToken', () => {
      const client = new L402Client({ publicKey: 'la_pk_xxx', apiKey: 'la_sk_xxx' });
      expect(client.getToken()).toBeNull();

      client.setToken('my_token', Math.floor(Date.now() / 1000) + 3600);
      expect(client.getToken()).toBe('my_token');
      expect(client.hasValidToken()).toBe(true);

      client.clearToken();
      expect(client.getToken()).toBeNull();
      expect(client.hasValidToken()).toBe(false);
    });
  });
});

// ─── L402Bundle ─────────────────────────────────────────────────────────────

describe('L402Bundle', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('requires publicKey and apiKey', () => {
    expect(() => new L402Bundle({ publicKey: '', apiKey: 'sk_xxx' })).toThrow('L402Bundle: publicKey is required');
    expect(() => new L402Bundle({ publicKey: 'pk_xxx', apiKey: '' })).toThrow('L402Bundle: apiKey is required');
  });

  describe('createInvoice()', () => {
    it('creates bundle invoice for tier', async () => {
      const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(mockResponse({
        bundleId: 'bundle_xyz',
        invoice: 'bolt11_string',
        bolt11: 'bolt11_string',
        paymentHash: 'payment_hash_123',
        amountSats: 400,
        expiresAtUnix: 1715000000,
        tier: 'growth',
        totalCalls: 1_000,
      }));
      vi.stubGlobal('fetch', fetchMock);

      const bundle = new L402Bundle({ publicKey: 'la_pk_xxx', apiKey: 'la_sk_xxx' });
      const result = await bundle.createInvoice('growth', 'agent-1');

      expect(result.bundleId).toBe('bundle_xyz');
      expect(result.tier).toBe('growth');
      expect(result.totalCalls).toBe(1_000);
      expect(result.bolt11).toBe('bolt11_string');
    });
  });

  describe('getStatus()', () => {
    it('returns bundle status', async () => {
      const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(mockResponse({
        bundleId: 'bundle_xyz',
        tier: 'growth',
        totalCalls: 1000,
        remainingCalls: 850,
        usedCalls: 150,
        expiresAtUnix: 1720000000,
        isExpired: false,
        isDepleted: false,
      }));
      vi.stubGlobal('fetch', fetchMock);

      const bundle = new L402Bundle({ publicKey: 'la_pk_xxx', apiKey: 'la_sk_xxx' });
      const status = await bundle.getStatus('bundle_xyz');

      expect(status.remainingCalls).toBe(850);
      expect(status.isDepleted).toBe(false);
      expect(status.isExpired).toBe(false);
    });
  });

  describe('request()', () => {
    it('throws if macaroon not set', async () => {
      const bundle = new L402Bundle({ publicKey: 'la_pk_xxx', apiKey: 'la_sk_xxx' });
      await expect(bundle.request('https://api.liveauth.app/api/test')).rejects.toThrow('must call claim()');
    });
  });
});

// ─── BundleTiers ───────────────────────────────────────────────────────────

describe('BundleTiers', () => {
  it('has all four tiers', () => {
    expect(BundleTiers).toHaveLength(4);
    expect(BundleTiers.find(t => t.name === 'starter')).toMatchObject({ totalCalls: 100, priceSats: 50 });
    expect(BundleTiers.find(t => t.name === 'growth')).toMatchObject({ totalCalls: 1_000, priceSats: 400 });
    expect(BundleTiers.find(t => t.name === 'scale')).toMatchObject({ totalCalls: 10_000, priceSats: 3_000 });
    expect(BundleTiers.find(t => t.name === 'enterprise')).toMatchObject({ totalCalls: 100_000, priceSats: 20_000 });
  });

  it('effective rates decrease with tier', () => {
    for (let i = 1; i < BundleTiers.length; i++) {
      expect(BundleTiers[i].effectiveRate).toBeLessThan(BundleTiers[i - 1].effectiveRate);
    }
  });
});
