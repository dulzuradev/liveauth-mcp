#!/usr/bin/env node

import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ListResourcesRequestSchema,
  ListToolsRequestSchema,
  ReadResourceRequestSchema,
  type CallToolResult,
  type Tool,
} from '@modelcontextprotocol/sdk/types.js';
import nodeFetch from 'node-fetch';
import { runGooseSetup } from './goose.js';
import { getLightningAppHtml, LIGHTNING_APP_MIME_TYPE, LIGHTNING_APP_URI } from './lightning-app.js';
import {
  type LightningPaymentDetails,
  toToolResult,
  withLightningDetails,
} from './lightning.js';
import { solvePow } from './pow.js';
import type { PowChallenge } from './types.js';

const PACKAGE_VERSION = '1.1.0';

interface DemoSession {
  quoteId: string;
  invoice: string;
  amountSats: number;
  expiresAtUnix: number;
  paymentHash: string;
  paid: boolean;
  createdAt: number;
}

export interface LiveAuthMcpServerConfig {
  apiBase?: string;
  apiKey?: string;
  demo?: boolean;
  fetch?: typeof fetch;
  now?: () => number;
  random?: () => number;
}

interface McpStartResponse extends Record<string, unknown> {
  quoteId: string;
  powChallenge: PowChallenge | null;
  invoice: {
    bolt11: string;
    amountSats: number;
    expiresAtUnix: number;
    paymentHash: string;
  } | null;
  authHint?: string | null;
}

interface McpConfirmResponse extends Record<string, unknown> {
  jwt: string | null;
  expiresIn: number;
  remainingBudgetSats: number;
  paymentStatus?: string;
  refreshToken?: string;
}

interface McpRefreshResponse extends Record<string, unknown> {
  jwt: string;
  expiresIn: number;
  remainingBudgetSats: number;
}

interface McpStatusResponse extends Record<string, unknown> {
  quoteId: string;
  status: string;
  paymentStatus: string | null;
  expiresAt: string;
}

interface McpChargeResponse extends Record<string, unknown> {
  status: 'ok' | 'deny';
  callsUsed: number;
  satsUsed: number;
}

interface McpUsageResponse extends Record<string, unknown> {
  status: string;
  callsUsed: number;
  satsUsed: number;
  maxSatsPerDay: number;
  remainingBudgetSats: number;
  maxCallsPerMinute: number;
  expiresAt: string;
  dayWindowStart: string | null;
}

interface McpErrorResponse {
  error?: string;
  error_description?: string;
}

const lightningAppMeta = {
  ui: {
    resourceUri: LIGHTNING_APP_URI,
    visibility: ['model', 'app'],
  },
};

const appCallableMeta = {
  ui: {
    visibility: ['model', 'app'],
  },
};

const TOOLS: Tool[] = [
  {
    name: 'liveauth_mcp_start',
    description: 'Start a LiveAuth MCP session. Returns a PoW challenge by default, a Lightning invoice, or an L402 bundle auth hint. No project key is required for the default PoW flow.',
    inputSchema: {
      type: 'object',
      properties: {
        forceLightning: {
          type: 'boolean',
          description: 'If true, request a Lightning invoice instead of a PoW challenge',
        },
        forceL402: {
          type: 'boolean',
          description: 'If true, request an L402 bundle auth session',
        },
      },
      required: [],
    },
    _meta: lightningAppMeta,
  },
  {
    name: 'liveauth_mcp_status',
    description: 'Check an MCP session. Use this to poll Lightning payment state; paid, pending, and expired states are returned as structured data.',
    inputSchema: {
      type: 'object',
      properties: {
        quoteId: { type: 'string', description: 'The quoteId from the start response' },
      },
      required: ['quoteId'],
    },
    _meta: appCallableMeta,
  },
  {
    name: 'liveauth_mcp_lnurl',
    description: 'Get the BOLT11 Lightning invoice for a session in lnget-compatible form plus portable structured payment data.',
    inputSchema: {
      type: 'object',
      properties: {
        quoteId: { type: 'string', description: 'The quoteId from the start response' },
      },
      required: ['quoteId'],
    },
    _meta: lightningAppMeta,
  },
  {
    name: 'liveauth_mcp_confirm',
    description: 'Confirm a LiveAuth session and receive a JWT. With only a PoW quoteId, the server reuses its existing PoW solver automatically. With Lightning, call with quoteId to poll settlement.',
    inputSchema: {
      type: 'object',
      properties: {
        quoteId: { type: 'string', description: 'The quoteId from the start response' },
        challengeHex: { type: 'string', description: 'Challenge hex (optional; PoW can be solved automatically)' },
        nonce: { type: 'number', description: 'Solved PoW nonce (optional)' },
        hashHex: { type: 'string', description: 'Solved PoW hash (optional)' },
        expiresAtUnix: { type: 'number', description: 'Challenge expiration (optional)' },
        difficultyBits: { type: 'number', description: 'Challenge difficulty (optional)' },
        signature: { type: 'string', description: 'Challenge signature (optional)' },
        macaroon: { type: 'string', description: 'L402 bundle macaroon (L402 only)' },
      },
      required: ['quoteId'],
    },
    _meta: lightningAppMeta,
  },
  {
    name: 'liveauth_mcp_charge',
    description: 'Meter usage after an authenticated call. A confirmed session JWT is required.',
    inputSchema: {
      type: 'object',
      properties: {
        callCostSats: { type: 'number', description: 'Optional call cost in sats; omit to use project or tool pricing' },
        toolName: { type: 'string', description: 'Optional registered MCP tool slug or name for pricing and attribution' },
      },
      required: [],
    },
  },
  {
    name: 'liveauth_mcp_usage',
    description: 'Query usage and remaining budget for the authenticated MCP session.',
    inputSchema: { type: 'object', properties: {}, required: [] },
  },
  {
    name: 'liveauth_mcp_refresh',
    description: 'Refresh the JWT without re-authenticating. The refresh token is returned only in tool data and is never logged.',
    inputSchema: {
      type: 'object',
      properties: {
        refreshToken: { type: 'string', description: 'The refreshToken from the confirm response' },
      },
      required: ['refreshToken'],
    },
  },
];

function getAuthHeaders(apiKey: string, demo: boolean): Record<string, string> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  if (apiKey && !demo) headers['X-LW-Public'] = apiKey;
  return headers;
}

function errorResult(message: string): CallToolResult {
  return { content: [{ type: 'text', text: message }], isError: true };
}

async function readApiError(response: Response, fallback: string): Promise<Error> {
  try {
    const error = await response.json() as McpErrorResponse;
    return new Error(error.error_description || error.error || fallback);
  } catch {
    return new Error(fallback);
  }
}

export function createLiveAuthMcpServer(config: LiveAuthMcpServerConfig = {}): Server {
  const apiBase = config.apiBase ?? process.env.LIVEAUTH_API_BASE ?? 'https://api.liveauth.app';
  const apiKey = config.apiKey ?? process.env.LIVEAUTH_API_KEY ?? '';
  // LIVEAUTH_DEMO preserves the legacy locally simulated demo. The normal no-key
  // path now uses the real anonymous demo project and its PoW boundary.
  const demo = config.demo ?? process.env.LIVEAUTH_DEMO === 'true';
  const fetchImpl = config.fetch ?? nodeFetch;
  const now = config.now ?? (() => Date.now());
  const random = config.random ?? (() => Math.random());

  let cachedJwt: string | null = null;
  let demoCallsUsed = 0;
  let demoSatsUsed = 0;
  const demoSessions = new Map<string, DemoSession>();
  const lightningByQuote = new Map<string, LightningPaymentDetails>();
  const powByQuote = new Map<string, PowChallenge>();

  const rememberLightning = (quoteId: string, value: Record<string, unknown>): Record<string, unknown> => {
    const enriched = withLightningDetails(value, lightningByQuote.get(quoteId), now());
    if (enriched.lightning) lightningByQuote.set(quoteId, enriched.lightning);
    return enriched;
  };

  const server = new Server(
    { name: 'liveauth-mcp', version: PACKAGE_VERSION },
    { capabilities: { tools: {}, resources: {} } }
  );

  server.setRequestHandler(ListToolsRequestSchema, async () => ({ tools: TOOLS }));

  server.setRequestHandler(ListResourcesRequestSchema, async () => ({
    resources: [
      {
        uri: LIGHTNING_APP_URI,
        name: 'LiveAuth Lightning Payment',
        title: 'LiveAuth Lightning Payment',
        description: 'Reusable MCP App for paying and polling a LiveAuth Lightning invoice.',
        mimeType: LIGHTNING_APP_MIME_TYPE,
      },
    ],
  }));

  server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
    if (request.params.uri !== LIGHTNING_APP_URI) {
      throw new Error(`Unknown resource: ${request.params.uri}`);
    }
    return {
      contents: [
        {
          uri: LIGHTNING_APP_URI,
          mimeType: LIGHTNING_APP_MIME_TYPE,
          text: getLightningAppHtml(),
          _meta: { ui: { prefersBorder: true } },
        },
      ],
    };
  });

  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: rawArgs } = request.params;
    const args = rawArgs ?? {};

    try {
      switch (name) {
        case 'liveauth_mcp_start': {
          const { forceLightning, forceL402 } = args as { forceLightning?: boolean; forceL402?: boolean };
          const endpoint = demo ? `${apiBase}/api/public/auth/demo/start` : `${apiBase}/api/mcp/start`;
          const response = await fetchImpl(endpoint, {
            method: 'POST',
            headers: getAuthHeaders(apiKey, demo),
            body: JSON.stringify({ forceLightning: forceLightning ?? false, forceL402: forceL402 ?? false }),
          });
          if (!response.ok) throw await readApiError(response as Response, `Start failed: ${response.statusText}`);

          const result = await response.json() as McpStartResponse;
          if (demo) {
            const demoResult = result as McpStartResponse & { sessionId?: string; amountSats?: number; expiresAtUnix?: number };
            const quoteId = result.quoteId || demoResult.sessionId;
            if (!quoteId) throw new Error('Demo start did not return a session identifier');
            if (demoResult.invoice) {
              demoSessions.set(quoteId, {
                quoteId,
                invoice: demoResult.invoice.bolt11,
                amountSats: demoResult.invoice.amountSats || demoResult.amountSats || 0,
                expiresAtUnix: demoResult.invoice.expiresAtUnix || demoResult.expiresAtUnix || Math.floor(now() / 1000) + 300,
                paymentHash: demoResult.invoice.paymentHash || '',
                paid: false,
                createdAt: now(),
              });
            }
            const demoPayload = rememberLightning(quoteId, {
              quoteId,
              powChallenge: null,
              invoice: demoResult.invoice,
              _demo: true,
              _instructions: 'Legacy demo mode simulates settlement. Call liveauth_mcp_confirm with quoteId.',
            });
            return toToolResult(demoPayload);
          }

          if (result.powChallenge) powByQuote.set(result.quoteId, result.powChallenge);
          const payload = rememberLightning(result.quoteId, {
            ...result,
            ...(result.powChallenge
              ? { _instructions: 'Call liveauth_mcp_confirm with quoteId only; the MCP server will solve this PoW challenge locally.' }
              : result.invoice
                ? { _instructions: 'Pay the Lightning invoice, then call liveauth_mcp_confirm or poll liveauth_mcp_status.' }
                : {}),
          });
          return toToolResult(payload);
        }

        case 'liveauth_mcp_confirm': {
          const {
            quoteId,
            challengeHex,
            nonce,
            hashHex,
            expiresAtUnix,
            difficultyBits,
            signature,
            macaroon,
          } = args as {
            quoteId: string;
            challengeHex?: string;
            nonce?: number;
            hashHex?: string;
            expiresAtUnix?: number;
            difficultyBits?: number;
            signature?: string;
            macaroon?: string;
          };

          if (demo && demoSessions.has(quoteId)) {
            const session = demoSessions.get(quoteId)!;
            session.paid = true;
            const demoJwt = `demo_jwt_${now()}_${random().toString(36).slice(2, 11)}`;
            cachedJwt = demoJwt;
            const payload = rememberLightning(quoteId, {
              quoteId,
              jwt: demoJwt,
              expiresIn: 3600,
              remainingBudgetSats: 1000,
              paymentStatus: 'paid',
              refreshToken: `demo_refresh_${quoteId}`,
              _demo: true,
            });
            return toToolResult(payload);
          }

          const body: Record<string, unknown> = { quoteId };
          if (challengeHex) body.challengeHex = challengeHex;
          if (nonce !== undefined) body.nonce = nonce;
          if (hashHex) body.hashHex = hashHex;
          if (expiresAtUnix !== undefined) body.expiresAtUnix = expiresAtUnix;
          if (difficultyBits !== undefined) body.difficultyBits = difficultyBits;
          if (signature) body.sig = signature;
          if (macaroon) body.macaroon = macaroon;

          if (!challengeHex && !macaroon && powByQuote.has(quoteId)) {
            const solution = await solvePow(powByQuote.get(quoteId)!);
            Object.assign(body, solution);
          }

          const response = await fetchImpl(`${apiBase}/api/mcp/confirm`, {
            method: 'POST',
            headers: getAuthHeaders(apiKey, demo),
            body: JSON.stringify(body),
          });
          if (!response.ok) throw await readApiError(response as Response, `Confirm failed: ${response.statusText}`);

          const result = await response.json() as McpConfirmResponse;
          if (result.jwt) cachedJwt = result.jwt;
          const payload = rememberLightning(quoteId, {
            quoteId,
            ...result,
            ...(result.paymentStatus === 'pending'
              ? { status: 'pending', _instructions: `Payment is pending. Poll liveauth_mcp_status with quoteId ${quoteId}.` }
              : {}),
          });
          return toToolResult(payload);
        }

        case 'liveauth_mcp_charge': {
          const { callCostSats, toolName } = args as { callCostSats?: number; toolName?: string };
          const demoCostSats = callCostSats ?? 1;
          if (demo) {
            if (!cachedJwt) return errorResult('Demo session is not confirmed. Call liveauth_mcp_confirm before charging.');
            if (demoSatsUsed + demoCostSats > 1000) {
              return errorResult(`Budget exceeded! Calls used: ${demoCallsUsed}, sats used: ${demoSatsUsed}.`);
            }
            demoCallsUsed += 1;
            demoSatsUsed += demoCostSats;
            return toToolResult({ status: 'ok', callsUsed: demoCallsUsed, satsUsed: demoSatsUsed, _demo: true });
          }

          const headers = getAuthHeaders(apiKey, demo);
          if (cachedJwt) headers.Authorization = `Bearer ${cachedJwt}`;
          const response = await fetchImpl(`${apiBase}/api/mcp/charge`, {
            method: 'POST',
            headers,
            body: JSON.stringify({
              ...(callCostSats === undefined ? {} : { callCostSats }),
              ...(toolName ? { toolName } : {}),
            }),
          });
          if (!response.ok) throw await readApiError(response as Response, `Charge failed: ${response.statusText}`);
          const result = await response.json() as McpChargeResponse;
          if (result.status === 'deny') {
            return errorResult(`Budget exceeded! Calls used: ${result.callsUsed}, sats used: ${result.satsUsed}.`);
          }
          return toToolResult(result);
        }

        case 'liveauth_mcp_usage': {
          if (demo) {
            return toToolResult({
              status: 'active',
              callsUsed: demoCallsUsed,
              satsUsed: demoSatsUsed,
              maxSatsPerDay: 1000,
              remainingBudgetSats: Math.max(0, 1000 - demoSatsUsed),
              maxCallsPerMinute: 60,
              _demo: true,
            });
          }
          const headers = getAuthHeaders(apiKey, demo);
          if (cachedJwt) headers.Authorization = `Bearer ${cachedJwt}`;
          const response = await fetchImpl(`${apiBase}/api/mcp/usage`, { method: 'GET', headers });
          if (!response.ok) throw await readApiError(response as Response, `Usage query failed: ${response.statusText}`);
          return toToolResult(await response.json() as McpUsageResponse);
        }

        case 'liveauth_mcp_status': {
          const { quoteId } = args as { quoteId: string };
          if (demo && demoSessions.has(quoteId)) {
            const session = demoSessions.get(quoteId)!;
            if (!session.paid && now() - session.createdAt > 2000) session.paid = true;
            const payload = rememberLightning(quoteId, {
              quoteId,
              status: session.paid ? 'confirmed' : 'pending',
              paymentStatus: session.paid ? 'paid' : 'pending',
              expiresAt: new Date(session.expiresAtUnix * 1000).toISOString(),
              _demo: true,
            });
            return toToolResult(payload);
          }
          const response = await fetchImpl(`${apiBase}/api/mcp/status/${encodeURIComponent(quoteId)}`, {
            method: 'GET',
            headers: getAuthHeaders(apiKey, demo),
          });
          if (!response.ok) throw await readApiError(response as Response, `Status check failed: ${response.statusText}`);
          const result = await response.json() as McpStatusResponse;
          return toToolResult(rememberLightning(quoteId, result));
        }

        case 'liveauth_mcp_lnurl': {
          const { quoteId } = args as { quoteId: string };
          if (demo && demoSessions.has(quoteId)) {
            const session = demoSessions.get(quoteId)!;
            return toToolResult(rememberLightning(quoteId, {
              quoteId,
              pr: session.invoice,
              routes: [],
              paymentStatus: session.paid ? 'paid' : 'pending',
              amountSats: session.amountSats,
              expiresAtUnix: session.expiresAtUnix,
              _demo: true,
            }));
          }
          const response = await fetchImpl(`${apiBase}/api/mcp/lnurl/${encodeURIComponent(quoteId)}`, {
            method: 'GET',
            headers: getAuthHeaders(apiKey, demo),
          });
          if (!response.ok) throw await readApiError(response as Response, `Lnurl fetch failed: ${response.statusText}`);
          const result = await response.json() as Record<string, unknown>;
          return toToolResult(rememberLightning(quoteId, { quoteId, ...result }));
        }

        case 'liveauth_mcp_refresh': {
          const { refreshToken } = args as { refreshToken: string };
          if (demo && refreshToken.startsWith('demo_refresh_')) {
            const demoJwt = `demo_jwt_${now()}_${random().toString(36).slice(2, 11)}`;
            cachedJwt = demoJwt;
            return toToolResult({
              jwt: demoJwt,
              expiresIn: 3600,
              remainingBudgetSats: Math.max(0, 1000 - demoSatsUsed),
              _demo: true,
            });
          }
          const response = await fetchImpl(`${apiBase}/api/mcp/refresh`, {
            method: 'POST',
            headers: getAuthHeaders(apiKey, demo),
            body: JSON.stringify({ refreshToken }),
          });
          if (!response.ok) throw await readApiError(response as Response, `Refresh failed: ${response.statusText}`);
          const result = await response.json() as McpRefreshResponse;
          cachedJwt = result.jwt;
          return toToolResult(result);
        }

        default:
          return errorResult(`Unknown tool: ${name}`);
      }
    } catch (error) {
      return errorResult(`Error: ${error instanceof Error ? error.message : String(error)}`);
    }
  });

  return server;
}

function printHelp(): void {
  process.stdout.write(`LiveAuth MCP server ${PACKAGE_VERSION}\n\n`);
  process.stdout.write('Usage:\n');
  process.stdout.write('  liveauth-mcp                 Start the stdio MCP server\n');
  process.stdout.write('  liveauth-mcp setup goose     Print the official Goose install link and fallbacks\n');
  process.stdout.write('  liveauth-mcp --help          Show this help\n');
}

export async function runCli(args = process.argv.slice(2)): Promise<number> {
  if (args.length === 0) {
    const server = createLiveAuthMcpServer();
    await server.connect(new StdioServerTransport());
    console.error('LiveAuth MCP server running on stdio');
    return 0;
  }

  if (args.length === 1 && (args[0] === '--help' || args[0] === '-h')) {
    printHelp();
    return 0;
  }

  if (args.length === 2 && args[0] === 'setup' && args[1] === 'goose') {
    return runGooseSetup();
  }

  console.error(`Unknown arguments: ${args.join(' ')}`);
  console.error('Run liveauth-mcp --help for supported commands.');
  return 2;
}

if (import.meta.url === `file://${process.argv[1]}`) {
  runCli().then((code) => {
    process.exitCode = code;
  }).catch((error) => {
    console.error('Fatal error:', error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
