import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { InMemoryTransport } from '@modelcontextprotocol/sdk/inMemory.js';
import { describe, expect, it, vi } from 'vitest';
import { createLiveAuthMcpServer } from './cli.js';
import type { LiveAuthMcpServerConfig } from './cli.js';

const API_BASE = 'https://api.test.liveauth.local';

function jsonResponse(body: unknown, init: ResponseInit = {}): Response {
  return new Response(JSON.stringify(body), {
    status: init.status ?? 200,
    headers: { 'Content-Type': 'application/json' },
    ...init,
  });
}

function parseToolJson(result: Awaited<ReturnType<Client['callTool']>>): any {
  const text = result.content?.[0]?.text;
  expect(typeof text).toBe('string');
  return JSON.parse(text as string);
}

async function withMcpClient<T>(
  config: LiveAuthMcpServerConfig,
  run: (client: Client) => Promise<T>
): Promise<T> {
  const server = createLiveAuthMcpServer(config);
  const client = new Client({ name: 'liveauth-mcp-test', version: '1.0.0' }, { capabilities: {} });
  const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair();

  await server.connect(serverTransport);
  await client.connect(clientTransport);

  try {
    return await run(client);
  } finally {
    await client.close();
  }
}

describe('LiveAuth stdio MCP server tools', () => {
  it('lists every README-advertised MCP tool', async () => {
    await withMcpClient({ apiBase: API_BASE, apiKey: 'la_pk_test', fetch: vi.fn() }, async (client) => {
      const result = await client.listTools();

      expect(result.tools.map((tool) => tool.name).sort()).toEqual([
        'liveauth_mcp_charge',
        'liveauth_mcp_confirm',
        'liveauth_mcp_lnurl',
        'liveauth_mcp_refresh',
        'liveauth_mcp_start',
        'liveauth_mcp_status',
        'liveauth_mcp_usage',
      ]);

      const confirm = result.tools.find((tool) => tool.name === 'liveauth_mcp_confirm');
      expect(confirm?.inputSchema.properties).toHaveProperty('macaroon');
    });
  });

  it('runs the no-config demo flow across start, lnurl, status, confirm, charge, usage, and refresh', async () => {
    const fetchImpl = vi.fn(async (input: string | URL | Request) => {
      const url = String(input);
      expect(url).toBe(`${API_BASE}/api/public/auth/demo/start`);
      return jsonResponse({
        sessionId: 'demo-session-1',
        invoice: {
          bolt11: 'lnbc1demo',
          amountSats: 3,
          expiresAtUnix: 1_800_000_000,
          paymentHash: 'demo-hash',
        },
      });
    });

    await withMcpClient(
      {
        apiBase: API_BASE,
        apiKey: '',
        demo: true,
        fetch: fetchImpl,
        now: () => 1_800_000_000_000,
        random: () => 0.123456789,
      },
      async (client) => {
        const start = parseToolJson(await client.callTool({ name: 'liveauth_mcp_start', arguments: {} }));
        expect(start).toMatchObject({
          quoteId: 'demo-session-1',
          _demo: true,
          invoice: {
            bolt11: 'lnbc1demo',
            amountSats: 3,
            paymentHash: 'demo-hash',
          },
        });

        const lnurl = parseToolJson(
          await client.callTool({ name: 'liveauth_mcp_lnurl', arguments: { quoteId: start.quoteId } })
        );
        expect(lnurl).toMatchObject({ pr: 'lnbc1demo', routes: [], _demo: true });

        const statusBeforeConfirm = parseToolJson(
          await client.callTool({ name: 'liveauth_mcp_status', arguments: { quoteId: start.quoteId } })
        );
        expect(statusBeforeConfirm).toMatchObject({
          status: 'pending',
          paymentStatus: 'pending',
          _demo: true,
        });

        const chargeBeforeConfirm = await client.callTool({
          name: 'liveauth_mcp_charge',
          arguments: { callCostSats: 1 },
        });
        expect(chargeBeforeConfirm.isError).toBe(true);
        expect(chargeBeforeConfirm.content?.[0]?.text).toContain('not confirmed');

        const confirm = parseToolJson(
          await client.callTool({ name: 'liveauth_mcp_confirm', arguments: { quoteId: start.quoteId } })
        );
        expect(confirm).toMatchObject({
          expiresIn: 3600,
          remainingBudgetSats: 1000,
          paymentStatus: 'paid',
          refreshToken: 'demo_refresh_demo-session-1',
          _demo: true,
        });
        expect(confirm.jwt).toMatch(/^demo_jwt_/);

        const charge = parseToolJson(
          await client.callTool({ name: 'liveauth_mcp_charge', arguments: { callCostSats: 7 } })
        );
        expect(charge).toMatchObject({ status: 'ok', callsUsed: 1, satsUsed: 7, _demo: true });

        const usage = parseToolJson(await client.callTool({ name: 'liveauth_mcp_usage', arguments: {} }));
        expect(usage).toMatchObject({
          status: 'active',
          callsUsed: 1,
          satsUsed: 7,
          remainingBudgetSats: 993,
          _demo: true,
        });

        const refresh = parseToolJson(
          await client.callTool({
            name: 'liveauth_mcp_refresh',
            arguments: { refreshToken: confirm.refreshToken },
          })
        );
        expect(refresh).toMatchObject({ expiresIn: 3600, remainingBudgetSats: 993, _demo: true });
        expect(refresh.jwt).toMatch(/^demo_jwt_/);
      }
    );

    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });

  it('forwards production MCP tool calls with project headers, cached JWT, and L402 macaroon', async () => {
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    const fetchImpl = vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = String(input);
      requests.push({ url, init });

      if (url.endsWith('/api/mcp/start')) {
        return jsonResponse({
          quoteId: 'quote-l402',
          powChallenge: null,
          invoice: null,
          authHint: 'l402_bundle',
        });
      }

      if (url.endsWith('/api/mcp/confirm')) {
        return jsonResponse({
          jwt: 'jwt-from-l402',
          expiresIn: 600,
          remainingBudgetSats: 42,
          paymentStatus: 'l402_paid',
          refreshToken: 'refresh-l402',
        });
      }

      if (url.endsWith('/api/mcp/charge')) {
        return jsonResponse({ status: 'ok', callsUsed: 1, satsUsed: 5 });
      }

      if (url.endsWith('/api/mcp/usage')) {
        return jsonResponse({
          status: 'active',
          callsUsed: 1,
          satsUsed: 5,
          maxSatsPerDay: 42,
          remainingBudgetSats: 37,
          maxCallsPerMinute: 60,
          expiresAt: '2026-06-11T12:00:00Z',
          dayWindowStart: '2026-06-11T00:00:00Z',
        });
      }

      if (url.endsWith('/api/mcp/status/quote-l402')) {
        return jsonResponse({
          quoteId: 'quote-l402',
          status: 'confirmed',
          paymentStatus: 'paid',
          expiresAt: '2026-06-11T12:00:00Z',
        });
      }

      if (url.endsWith('/api/mcp/lnurl/quote-l402')) {
        return jsonResponse({ pr: 'lnbc1production', routes: [] });
      }

      if (url.endsWith('/api/mcp/refresh')) {
        return jsonResponse({
          jwt: 'jwt-refreshed',
          expiresIn: 600,
          remainingBudgetSats: 36,
        });
      }

      return jsonResponse({ error_description: `Unexpected URL ${url}` }, { status: 500 });
    });

    await withMcpClient(
      { apiBase: API_BASE, apiKey: 'la_pk_test', demo: false, fetch: fetchImpl },
      async (client) => {
        const start = parseToolJson(
          await client.callTool({ name: 'liveauth_mcp_start', arguments: { forceL402: true } })
        );
        expect(start).toMatchObject({ quoteId: 'quote-l402', authHint: 'l402_bundle' });

        const confirm = parseToolJson(
          await client.callTool({
            name: 'liveauth_mcp_confirm',
            arguments: { quoteId: start.quoteId, macaroon: 'macaroon-test' },
          })
        );
        expect(confirm).toMatchObject({ jwt: 'jwt-from-l402', paymentStatus: 'l402_paid' });

        expect(parseToolJson(await client.callTool({ name: 'liveauth_mcp_charge', arguments: { callCostSats: 5 } })))
          .toMatchObject({ status: 'ok', callsUsed: 1, satsUsed: 5 });
        expect(parseToolJson(await client.callTool({ name: 'liveauth_mcp_usage', arguments: {} })))
          .toMatchObject({ remainingBudgetSats: 37 });
        expect(parseToolJson(await client.callTool({ name: 'liveauth_mcp_status', arguments: { quoteId: start.quoteId } })))
          .toMatchObject({ paymentStatus: 'paid' });
        expect(parseToolJson(await client.callTool({ name: 'liveauth_mcp_lnurl', arguments: { quoteId: start.quoteId } })))
          .toMatchObject({ pr: 'lnbc1production', routes: [] });
        expect(parseToolJson(await client.callTool({
          name: 'liveauth_mcp_refresh',
          arguments: { refreshToken: 'refresh-l402' },
        }))).toMatchObject({ jwt: 'jwt-refreshed', remainingBudgetSats: 36 });
      }
    );

    expect(requests.map((request) => request.url)).toEqual([
      `${API_BASE}/api/mcp/start`,
      `${API_BASE}/api/mcp/confirm`,
      `${API_BASE}/api/mcp/charge`,
      `${API_BASE}/api/mcp/usage`,
      `${API_BASE}/api/mcp/status/quote-l402`,
      `${API_BASE}/api/mcp/lnurl/quote-l402`,
      `${API_BASE}/api/mcp/refresh`,
    ]);

    expect(requests[0]?.init?.headers).toMatchObject({ 'X-LW-Public': 'la_pk_test' });
    expect(JSON.parse(String(requests[0]?.init?.body))).toMatchObject({ forceL402: true });
    expect(JSON.parse(String(requests[1]?.init?.body))).toMatchObject({
      quoteId: 'quote-l402',
      macaroon: 'macaroon-test',
    });
    expect(requests[2]?.init?.headers).toMatchObject({
      'X-LW-Public': 'la_pk_test',
      Authorization: 'Bearer jwt-from-l402',
    });
    expect(requests[3]?.init?.headers).toMatchObject({ Authorization: 'Bearer jwt-from-l402' });
  });
});
