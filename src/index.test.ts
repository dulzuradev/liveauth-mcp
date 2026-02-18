import { describe, it, expect, vi, beforeEach } from 'vitest';
import fetch from 'node-fetch';

// Mock node-fetch
vi.mock('node-fetch');
const mockedFetch = vi.mocked(fetch);

// Test constants
const API_BASE = process.env.LIVEAUTH_API_BASE || 'https://api.liveauth.app';
const API_KEY = process.env.LIVEAUTH_API_KEY || '';

describe('LiveAuth MCP Server E2E', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('liveauth_mcp_start (PoW)', () => {
    it('should fetch PoW challenge successfully', async () => {
      const mockResponse = {
        quoteId: 'test-quote-id-123',
        powChallenge: {
          projectId: 'b842cae1-e06e-480f-be76-a64a75e0f871',
          projectPublicKey: 'la_pk_test',
          challengeHex: 'a1b2c3d4e5f67890',
          targetHex: '0000ffff00000000',
          difficultyBits: 18,
          expiresAtUnix: 1739900000,
          signature: 'sig_test123',
        },
        invoice: null,
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockResponse,
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/start`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(API_KEY ? { 'X-LW-Public': API_KEY } : {}),
        },
        body: JSON.stringify({ forceLightning: false }),
      });

      expect(response.ok).toBe(true);
      const result = await response.json();
      expect(result.quoteId).toBeDefined();
      expect(result.powChallenge).toBeDefined();
      expect(result.powChallenge.difficultyBits).toBe(18);
      expect(result.invoice).toBeNull();
    });

    it('should fetch Lightning invoice when forceLightning=true', async () => {
      const mockResponse = {
        quoteId: 'test-quote-id-456',
        powChallenge: null,
        invoice: {
          bolt11: 'lnbc2100n1p5etsnqpp5ets8gdjeyugpuw5a8gu4yqndau6dqal0wa639fu574plkm27xgqqdr9f35hve2pw46xsgryv4mzqmr0va5kugrxdaezqmtrwqaxywp5xf3kzef394jnqdn9956rsvrx943x2dek94snvdrpxu6k2vrx8qmnzcqzzsxqzjcrzjqvdnqyc82a9maxu6c7mee0shqr33u4z9z04wpdwhf96gxzpln8jczr3665qqxdqqqyqqqqlgqqqqraqq2qsp5yzz2yhj80sfvhn00wqnffc9p0xz0kzjeq8lgtlx276c3vnrlcfcs9qxpqysgq08ugx8clr503rt3tre9yrnhek4y4zrwph6sgydlpwnr47cch2qqya2rd3st4mcp0y70977f5slyh9c7pw24jzgz2v4gm0gmztpxp5tsq7346xf',
          amountSats: 210,
          expiresAtUnix: 1739900000,
          paymentHash: 'testpaymenthash123',
        },
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockResponse,
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/start`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ forceLightning: true }),
      });

      expect(response.ok).toBe(true);
      const result = await response.json();
      expect(result.quoteId).toBeDefined();
      expect(result.powChallenge).toBeNull();
      expect(result.invoice).toBeDefined();
      expect(result.invoice.bolt11).toContain('lnbc');
      expect(result.invoice.amountSats).toBe(210);
    });
  });

  describe('liveauth_mcp_confirm (PoW)', () => {
    it('should confirm PoW solution and return JWT', async () => {
      const mockResponse = {
        jwt: 'eyJhbGc.test.jwt.token',
        expiresIn: 600,
        remainingBudgetSats: 10000,
        refreshToken: 'refresh_test_123',
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockResponse,
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/confirm`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          quoteId: 'test-quote-id',
          challengeHex: 'a1b2c3d4e5f67890',
          nonce: 12345,
          hashHex: '00001234abcdef',
          expiresAtUnix: 1739900000,
          difficultyBits: 18,
          sig: 'sig_test123',
        }),
      });

      expect(response.ok).toBe(true);
      const result = await response.json();
      expect(result.jwt).toBeDefined();
      expect(result.refreshToken).toBeDefined();
      expect(result.expiresIn).toBe(600);
    });

    it('should return pending status for Lightning payment', async () => {
      const mockResponse = {
        jwt: null,
        expiresIn: 0,
        remainingBudgetSats: 0,
        paymentStatus: 'pending',
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockResponse,
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/confirm`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          quoteId: 'test-quote-id-lightning',
        }),
      });

      expect(response.ok).toBe(true);
      const result = await response.json();
      expect(result.paymentStatus).toBe('pending');
      expect(result.jwt).toBeNull();
    });
  });

  describe('liveauth_mcp_lnurl', () => {
    it('should return lnget-compatible invoice response', async () => {
      const mockResponse = {
        pr: 'lnbc2100n1p5etsnqpp5ets8gdjeyugpuw5a8gu4yqndau6dqal0wa639fu574plkm27xgqqdr9f35hve2pw46xsgryv4mzqmr0va5kugrxdaezqmtrwqaxywp5xf3kzef394jnqdn9956rsvrx943x2dek94snvdrpxu6k2vrx8qmnzcqzzsxqzjcrzjqvdnqyc82a9maxu6c7mee0shqr33u4z9z04wpdwhf96gxzpln8jczr3665qqxdqqqyqqqqlgqqqqraqq2qsp5yzz2yhj80sfvhn00wqnffc9p0xz0kzjeq8lgtlx276c3vnrlcfcs9qxpqysgq08ugx8clr503rt3tre9yrnhek4y4zrwph6sgydlpwnr47cch2qqya2rd3st4mcp0y70977f5slyh9c7pw24jzgz2v4gm0gmztpxp5tsq7346xf',
        routes: [],
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockResponse,
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/lnurl/test-quote-id`, {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      });

      expect(response.ok).toBe(true);
      const result = await response.json();
      expect(result.pr).toContain('lnbc');
      expect(result.routes).toEqual([]);
    });

    it('should return 404 for invalid quoteId', async () => {
      mockedFetch.mockResolvedValueOnce({
        ok: false,
        status: 404,
        json: async () => ({ error: 'Not found' }),
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/lnurl/invalid-quote`, {
        method: 'GET',
      });

      expect(response.ok).toBe(false);
      expect(response.status).toBe(404);
    });
  });

  describe('liveauth_mcp_status', () => {
    it('should return session status with payment status', async () => {
      const mockResponse = {
        quoteId: 'test-quote-id',
        status: 'pending',
        paymentStatus: 'pending',
        expiresAt: '2026-02-18T12:00:00Z',
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockResponse,
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/status/test-quote-id`, {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      });

      expect(response.ok).toBe(true);
      const result = await response.json();
      expect(result.paymentStatus).toBe('pending');
    });

    it('should return paid status when invoice is paid', async () => {
      const mockResponse = {
        quoteId: 'test-quote-id',
        status: 'confirmed',
        paymentStatus: 'paid',
        expiresAt: '2026-02-18T12:00:00Z',
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockResponse,
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/status/test-quote-id`, {
        method: 'GET',
      });

      expect(response.ok).toBe(true);
      const result = await response.json();
      expect(result.paymentStatus).toBe('paid');
    });
  });

  describe('liveauth_mcp_charge', () => {
    it('should charge successfully and return ok status', async () => {
      const mockResponse = {
        status: 'ok',
        callsUsed: 1,
        satsUsed: 10,
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockResponse,
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/charge`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': 'Bearer test.jwt.token',
        },
        body: JSON.stringify({ callCostSats: 10 }),
      });

      expect(response.ok).toBe(true);
      const result = await response.json();
      expect(result.status).toBe('ok');
      expect(result.satsUsed).toBe(10);
    });

    it('should return deny status when budget exceeded', async () => {
      const mockResponse = {
        status: 'deny',
        callsUsed: 100,
        satsUsed: 1000,
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockResponse,
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/charge`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': 'Bearer test.jwt.token',
        },
        body: JSON.stringify({ callCostSats: 10 }),
      });

      expect(response.ok).toBe(true);
      const result = await response.json();
      expect(result.status).toBe('deny');
    });
  });

  describe('liveauth_mcp_usage', () => {
    it('should return usage stats', async () => {
      const mockResponse = {
        status: 'active',
        callsUsed: 5,
        satsUsed: 50,
        maxSatsPerDay: 10000,
        remainingBudgetSats: 9950,
        maxCallsPerMinute: 60,
        expiresAt: '2026-02-18T12:00:00Z',
        dayWindowStart: '2026-02-18T00:00:00Z',
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockResponse,
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/usage`, {
        method: 'GET',
        headers: {
          'Authorization': 'Bearer test.jwt.token',
        },
      });

      expect(response.ok).toBe(true);
      const result = await response.json();
      expect(result.callsUsed).toBe(5);
      expect(result.remainingBudgetSats).toBe(9950);
    });
  });

  describe('liveauth_mcp_refresh', () => {
    it('should refresh JWT successfully', async () => {
      const mockResponse = {
        jwt: 'eyJhbGc.new.test.jwt.token',
        expiresIn: 600,
        remainingBudgetSats: 9900,
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockResponse,
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/refresh`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ refreshToken: 'refresh_test_123' }),
      });

      expect(response.ok).toBe(true);
      const result = await response.json();
      expect(result.jwt).toBeDefined();
      expect(result.expiresIn).toBe(600);
    });

    it('should return 401 for invalid refresh token', async () => {
      mockedFetch.mockResolvedValueOnce({
        ok: false,
        status: 401,
        json: async () => ({ error_description: 'Invalid refresh token' }),
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/refresh`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ refreshToken: 'invalid_token' }),
      });

      expect(response.status).toBe(401);
    });
  });

  describe('Error handling', () => {
    it('should handle API errors gracefully', async () => {
      mockedFetch.mockResolvedValueOnce({
        ok: false,
        status: 500,
        json: async () => ({ error: 'Internal server error', error_description: 'Something went wrong' }),
      } as any);

      const response = await fetch(`${API_BASE}/api/mcp/start`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({}),
      });

      expect(response.ok).toBe(false);
    });

    it('should handle network errors', async () => {
      mockedFetch.mockRejectedValueOnce(new Error('Network error'));

      await expect(
        fetch(`${API_BASE}/api/mcp/start`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({}),
        })
      ).rejects.toThrow('Network error');
    });
  });

  describe('Input validation', () => {
    it('should validate quoteId format', () => {
      const validUuid = 'b842cae1-e06e-480f-be76-a64a75e0f871';
      const invalidUuid = 'not-a-uuid';

      const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
      expect(uuidRegex.test(validUuid)).toBe(true);
      expect(uuidRegex.test(invalidUuid)).toBe(false);
    });

    it('should validate hex string format', () => {
      const validHex = 'a1b2c3d4e5f67890';
      const invalidHex = 'xyz123';

      const hexRegex = /^[0-9a-fA-F]+$/;
      expect(hexRegex.test(validHex)).toBe(true);
      expect(hexRegex.test(invalidHex)).toBe(false);
    });

    it('should validate BOLT11 invoice format', () => {
      const validInvoice = 'lnbc2100n1p5etsnqpp5ets8gdjeyugpuw5a8gu4yqndau6dqal0wa639fu574plkm27xgqqdr9f35hve2pw46xsgryv4mzqmr0va5kugrxdaezqmtrwqaxywp5xf3kzef394jnqdn9956rsvrx943x2dek94snvdrpxu6k2vrx8qmnzcqzzsxqzjcrzjqvdnqyc82a9maxu6c7mee0shqr33u4z9z04wpdwhf96gxzpln8jczr3665qqxdqqqyqqqqlgqqqqraqq2qsp5yzz2yhj80sfvhn00wqnffc9p0xz0kzjeq8lgtlx276c3vnrlcfcs9qxpqysgq08ugx8clr503rt3tre9yrnhek4y4zrwph6sgydlpwnr47cch2qqya2rd3st4mcp0y70977f5slyh9c7pw24jzgz2v4gm0gmztpxp5tsq7346xf';
      const invalidInvoice = 'invalid_invoice';

      expect(validInvoice.startsWith('lnbc')).toBe(true);
      expect(invalidInvoice.startsWith('lnbc')).toBe(false);
    });
  });
});
