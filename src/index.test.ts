import { describe, it, expect, vi, beforeEach } from 'vitest';
import fetch from 'node-fetch';
import {
  BudgetExceededError,
  LiveAuthMcpClient,
  LiveAuthMcpServerGate,
  createMcpClient,
  createMcpGate,
  solvePow
} from './index.js';

// Mock node-fetch
vi.mock('node-fetch');
const mockedFetch = vi.mocked(fetch);

// Test constants
const API_BASE = process.env.LIVEAUTH_API_BASE || 'https://api.liveauth.app';
const API_KEY = process.env.LIVEAUTH_API_KEY || '';

function jsonResponse(body: unknown, init: ResponseInit = {}): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
    ...init,
  });
}

describe('LiveAuth MCP SDK helpers', () => {
  it('exports a client that sends the public key header and remembers confirmed tokens', async () => {
    const calls: Array<{ url: string; init?: RequestInit }> = [];
    const fakeFetch = vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = String(input);
      calls.push({ url, init });

      if (url.endsWith('/api/mcp/start')) {
        return jsonResponse({
          quoteId: 'quote-1',
          powChallenge: {
            projectPublicKey: 'la_pk_test',
            challengeHex: 'abc123',
            targetHex: 'ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff',
            difficultyBits: 0,
            expiresAtUnix: 9999999999,
            signature: 'sig-test',
          },
          invoice: null,
        });
      }

      return jsonResponse({
        jwt: 'jwt-test',
        expiresIn: 600,
        remainingBudgetSats: 1000,
        refreshToken: 'refresh-test',
      });
    });

    const client = new LiveAuthMcpClient({
      publicKey: 'la_pk_test',
      baseUrl: API_BASE,
      fetch: fakeFetch,
    });

    const session = await client.start();
    const token = await client.confirm(session);

    expect(session.method).toBe('pow');
    expect(token.jwt).toBe('jwt-test');
    expect(client.token).toBe('jwt-test');
    expect(calls[0]?.init?.headers).toMatchObject({ 'X-LW-Public': 'la_pk_test' });
    expect(calls[1]?.init?.headers).toMatchObject({ 'X-LW-Public': 'la_pk_test' });
    client.destroy();
  });

  it('auto-refreshes confirmed tokens before expiry', async () => {
    vi.useFakeTimers();

    const fakeFetch = vi.fn(async () =>
      jsonResponse({
        jwt: 'jwt-refreshed',
        expiresIn: 60,
        remainingBudgetSats: 900,
      })
    );

    const client = new LiveAuthMcpClient({
      publicKey: 'la_pk_test',
      baseUrl: API_BASE,
      fetch: fakeFetch,
      refreshBufferMs: 500,
    });

    try {
      client.setToken('jwt-old', 'refresh-test', 1);
      await vi.advanceTimersByTimeAsync(500);

      expect(fakeFetch).toHaveBeenCalledWith(
        `${API_BASE}/api/mcp/refresh`,
        expect.objectContaining({
          method: 'POST',
          headers: expect.objectContaining({ 'X-LW-Public': 'la_pk_test' }),
          body: JSON.stringify({ refreshToken: 'refresh-test' }),
        })
      );
      expect(client.token).toBe('jwt-refreshed');
    } finally {
      client.destroy();
      vi.useRealTimers();
    }
  });

  it('starts and confirms Lightning sessions with invoice callbacks', async () => {
    const onInvoice = vi.fn();
    const calls: Array<{ url: string; init?: RequestInit }> = [];
    const invoice = {
      bolt11: 'lnbc1lightning',
      amountSats: 50,
      expiresAtUnix: 1_800_000_000,
      paymentHash: 'payment-hash',
    };

    const fakeFetch = vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = String(input);
      calls.push({ url, init });

      if (url.endsWith('/api/mcp/start')) {
        return jsonResponse({
          quoteId: 'quote-lightning',
          powChallenge: null,
          invoice,
        });
      }

      return jsonResponse({
        jwt: 'jwt-lightning',
        expiresIn: 600,
        remainingBudgetSats: 50,
        paymentStatus: 'paid',
        refreshToken: 'refresh-lightning',
      });
    });

    const client = new LiveAuthMcpClient({
      publicKey: 'la_pk_test',
      baseUrl: API_BASE,
      authMethod: 'lightning',
      fetch: fakeFetch,
      onInvoice,
    });

    const session = await client.start();
    const token = await client.confirmLightning(session);

    expect(session.method).toBe('lightning');
    expect(onInvoice).toHaveBeenCalledWith(invoice);
    expect(token.jwt).toBe('jwt-lightning');
    expect(JSON.parse(String(calls[0]?.init?.body))).toMatchObject({ forceLightning: true });
    expect(JSON.parse(String(calls[1]?.init?.body))).toEqual({ quoteId: 'quote-lightning' });
    client.destroy();
  });

  it('starts and confirms L402 sessions with a macaroon', async () => {
    const calls: Array<{ url: string; init?: RequestInit }> = [];
    const fakeFetch = vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = String(input);
      calls.push({ url, init });

      if (url.endsWith('/api/mcp/start')) {
        return jsonResponse({
          quoteId: 'quote-l402',
          powChallenge: null,
          invoice: null,
          authHint: 'l402_bundle',
        });
      }

      return jsonResponse({
        jwt: 'jwt-l402',
        expiresIn: 600,
        remainingBudgetSats: 99,
        paymentStatus: 'l402_paid',
      });
    });

    const client = new LiveAuthMcpClient({
      publicKey: 'la_pk_test',
      baseUrl: API_BASE,
      authMethod: 'l402',
      fetch: fakeFetch,
    });

    const session = await client.start();
    const token = await client.confirmL402('macaroon-test', session);

    expect(session.method).toBe('l402');
    expect(token.jwt).toBe('jwt-l402');
    expect(JSON.parse(String(calls[0]?.init?.body))).toMatchObject({ forceL402: true });
    expect(JSON.parse(String(calls[1]?.init?.body))).toEqual({
      quoteId: 'quote-l402',
      macaroon: 'macaroon-test',
    });
    client.destroy();
  });

  it('exports factory helpers and an invoke alias for gated calls', async () => {
    const client = createMcpClient({
      publicKey: 'la_pk_test',
      baseUrl: API_BASE,
      fetch: vi.fn(),
    });

    const fakeFetch = vi.fn(async () =>
      jsonResponse({
        status: 'ok',
        callsUsed: 1,
        satsUsed: 1,
      })
    );

    const gate = createMcpGate({
      publicKey: 'la_pk_test',
      baseUrl: API_BASE,
      fetch: fakeFetch,
    });

    const result = await gate.invoke(
      'jwt-test',
      { message: 'hello' },
      async (input, context) => ({
        text: input.message,
        satsUsed: context.liveAuth.charge.satsUsed,
      }),
      {},
      { validateFirst: false }
    );

    expect(client).toBeInstanceOf(LiveAuthMcpClient);
    expect(gate).toBeInstanceOf(LiveAuthMcpServerGate);
    expect(result).toEqual({ text: 'hello', satsUsed: 1 });
  });

  it('routes server gate charges to the tool charge endpoint when toolId is configured', async () => {
    const receipt = {
      version: 'mcp-call-receipt-v1',
      payload: 'payload-test',
      signature: 'signature-test',
      signatureAlgorithm: 'HMAC-SHA256',
      keyId: 'liveauth-mcp-receipt-v1',
      body: {
        receiptId: 'mcp_receipt_event1',
        revenueEventId: 'event-1',
        mcpToolId: 'tool-123',
        toolSlug: 'web-fetch',
        toolMethodName: 'web_fetch',
        mcpGateTokenId: 'token-123',
        mcpGateSessionId: 'session-123',
        payingProjectId: 'project-123',
        agentId: 'agent-123',
        grossSats: 5,
        platformFeeSats: 1,
        netSats: 4,
        feeBasisPoints: 500,
        status: 'Charged',
        idempotencyKey: 'call-123',
        requestId: 'request-123',
        createdAt: '2026-06-11T12:00:00.0000000Z',
      },
    };

    const fakeFetch = vi.fn(async () =>
      jsonResponse({
        status: 'ok',
        callsUsed: 2,
        satsUsed: 10,
        grossSats: 5,
        platformFeeSats: 1,
        netSats: 4,
        feeBasisPoints: 500,
        revenueEventId: 'event-1',
        receipt,
      })
    );

    const gate = createMcpGate({
      publicKey: 'la_pk_test',
      baseUrl: API_BASE,
      toolId: 'tool-123',
      defaultCostSats: 5,
      fetch: fakeFetch,
    });

    const result = await gate.invoke(
      'jwt-test',
      { url: 'https://example.com' },
      async (_input, context) => ({
        charge: context.liveAuth.charge,
        receiptId: context.liveAuth.charge.receipt?.body.receiptId,
      }),
      {},
      {
        validateFirst: false,
        costSats: 5,
        toolMethodName: 'web_fetch',
        idempotencyKey: 'call-123',
        agentId: 'agent-123',
        metadata: { urlHost: 'example.com' },
      }
    );

    expect(fakeFetch).toHaveBeenCalledWith(
      `${API_BASE}/api/mcp/tools/tool-123/charge`,
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({ 'X-LW-Public': 'la_pk_test' }),
        body: JSON.stringify({
          callCostSats: 5,
          toolMethodName: 'web_fetch',
          idempotencyKey: 'call-123',
          agentId: 'agent-123',
          metadata: { urlHost: 'example.com' },
        }),
      })
    );
    expect(result.charge).toMatchObject({
      ok: true,
      revenueEventId: 'event-1',
      platformFeeSats: 1,
      netSats: 4,
      receipt,
    });
    expect(result.receiptId).toBe('mcp_receipt_event1');
  });

  it('solves PoW with the backend publicKey:challengeHex:nonce payload', async () => {
    const solution = await solvePow({
      projectPublicKey: 'la_pk_test',
      challengeHex: 'abc123',
      targetHex: 'ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff',
      difficultyBits: 0,
      expiresAtUnix: 9999999999,
      signature: 'sig-test',
    });

    expect(solution.nonce).toBe(0);
    expect(solution.sig).toBe('sig-test');
    expect(solution.hashHex).toHaveLength(64);
  });

  it('throws BudgetExceededError when the server gate receives deny', async () => {
    const fakeFetch = vi.fn(async (input: string | URL | Request) => {
      const url = String(input);
      if (url.endsWith('/api/mcp/usage')) {
        return jsonResponse({
          status: 'active',
          callsUsed: 10,
          satsUsed: 100,
          maxSatsPerDay: 100,
          remainingBudgetSats: 0,
          maxCallsPerMinute: 60,
          expiresAt: new Date().toISOString(),
          dayWindowStart: null,
        });
      }

      return jsonResponse({
        status: 'deny',
        callsUsed: 10,
        satsUsed: 100,
      });
    });

    const gate = new LiveAuthMcpServerGate({
      publicKey: 'la_pk_test',
      baseUrl: API_BASE,
      fetch: fakeFetch,
    });

    await expect(gate.gateTool('jwt-test', {}, async () => 'never', {})).rejects.toBeInstanceOf(
      BudgetExceededError
    );
  });
});

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
