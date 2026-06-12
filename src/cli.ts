#!/usr/bin/env node

import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
  Tool,
} from '@modelcontextprotocol/sdk/types.js';
import nodeFetch from 'node-fetch';

// Demo mode session tracking (in-memory)
interface DemoSession {
  quoteId: string;
  invoice: string;
  amountSats: number;
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

// Helper to build auth headers (optional API key)
function getAuthHeaders(apiKey: string, demo: boolean): Record<string, string> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  };
  if (apiKey && !demo) {
    headers['X-LW-Public'] = apiKey;
  }
  return headers;
}

// MCP API response types
interface McpStartResponse {
  quoteId: string;
  powChallenge: {
    projectId: string;
    projectPublicKey: string;
    challengeHex: string;
    targetHex: string;
    difficultyBits: number;
    expiresAtUnix: number;
    signature: string;
  } | null;
  invoice: {
    bolt11: string;
    amountSats: number;
    expiresAtUnix: number;
    paymentHash: string;
  } | null;
  authHint?: string | null;
}

interface McpConfirmResponse {
  jwt: string | null;
  expiresIn: number;
  remainingBudgetSats: number;
  paymentStatus?: 'pending' | 'paid';
  refreshToken?: string;
}

interface McpRefreshResponse {
  jwt: string;
  expiresIn: number;
  remainingBudgetSats: number;
}

interface McpStatusResponse {
  quoteId: string;
  status: string;
  paymentStatus: string | null;
  expiresAt: string;
}

interface McpChargeResponse {
  status: 'ok' | 'deny';
  callsUsed: number;
  satsUsed: number;
}

interface McpUsageResponse {
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

// Define MCP tools
const TOOLS: Tool[] = [
  {
    name: 'liveauth_mcp_start',
    description: 'Start a new LiveAuth MCP session. Returns a PoW challenge (default), Lightning invoice, or L402 bundle auth hint.',
    inputSchema: {
      type: 'object',
      properties: {
        forceLightning: {
          type: 'boolean',
          description: 'If true, request Lightning invoice instead of PoW challenge',
        },
        forceL402: {
          type: 'boolean',
          description: 'If true, request an L402 bundle auth session',
        },
      },
      required: [],
    },
  },
  {
    name: 'liveauth_mcp_status',
    description: 'Check the status of an MCP session. Use to poll for Lightning payment confirmation. Also returns the invoice via lnurl compatibility.',
    inputSchema: {
      type: 'object',
      properties: {
        quoteId: {
          type: 'string',
          description: 'The quoteId from the start response',
        },
      },
      required: ['quoteId'],
    },
  },
  {
    name: 'liveauth_mcp_lnurl',
    description: 'Get the Lightning invoice for a session (lnget-compatible). Use this to retrieve the BOLT11 invoice for payment.',
    inputSchema: {
      type: 'object',
      properties: {
        quoteId: {
          type: 'string',
          description: 'The quoteId from the start response',
        },
      },
      required: ['quoteId'],
    },
  },
  {
    name: 'liveauth_mcp_confirm',
    description: 'Submit the solved proof-of-work challenge (or poll for Lightning payment) to receive a JWT. For Lightning, call with just quoteId to check/poll payment status.',
    inputSchema: {
      type: 'object',
      properties: {
        quoteId: {
          type: 'string',
          description: 'The quoteId from the start response',
        },
        challengeHex: {
          type: 'string',
          description: 'The challenge hex from the start response (PoW only)',
        },
        nonce: {
          type: 'number',
          description: 'The nonce that solves the PoW challenge (PoW only)',
        },
        hashHex: {
          type: 'string',
          description: 'The resulting hash hex (PoW only)',
        },
        expiresAtUnix: {
          type: 'number',
          description: 'Expiration timestamp from the challenge (PoW only)',
        },
        difficultyBits: {
          type: 'number',
          description: 'Difficulty bits from the challenge (PoW only)',
        },
        signature: {
          type: 'string',
          description: 'Signature from the challenge (PoW only)',
        },
        macaroon: {
          type: 'string',
          description: 'L402 bundle macaroon (L402 only)',
        },
      },
      required: ['quoteId'],
    },
  },
  {
    name: 'liveauth_mcp_charge',
    description: 'Meter API usage after making an authenticated call. Call this with the cost in sats for each API request made using the JWT.',
    inputSchema: {
      type: 'object',
      properties: {
        callCostSats: {
          type: 'number',
          description: 'Optional cost of the API call in sats. Omit to use LiveAuth project or tool pricing.',
        },
        toolName: {
          type: 'string',
          description: 'Optional registered MCP tool slug or name for per-tool pricing and revenue attribution.',
        },
      },
      required: [],
    },
  },
  {
    name: 'liveauth_mcp_usage',
    description: 'Query current usage and remaining budget for the MCP session. Use this to check how many sats and calls have been used without making a charge.',
    inputSchema: {
      type: 'object',
      properties: {},
      required: [],
    },
  },
  {
    name: 'liveauth_mcp_refresh',
    description: 'Refresh the JWT token without re-authenticating. Use the refreshToken returned from confirm to get a new JWT.',
    inputSchema: {
      type: 'object',
      properties: {
        refreshToken: {
          type: 'string',
          description: 'The refreshToken from the confirm response',
        },
      },
      required: ['refreshToken'],
    },
  },
];

export function createLiveAuthMcpServer(config: LiveAuthMcpServerConfig = {}): Server {
  const LIVEAUTH_API_BASE = config.apiBase ?? process.env.LIVEAUTH_API_BASE ?? 'https://api.liveauth.app';
  const LIVEAUTH_API_KEY = config.apiKey ?? process.env.LIVEAUTH_API_KEY ?? '';
  const LIVEAUTH_DEMO = config.demo ?? (process.env.LIVEAUTH_DEMO === 'true' || !LIVEAUTH_API_KEY);
  const fetchImpl = config.fetch ?? nodeFetch;
  const now = config.now ?? (() => Date.now());
  const random = config.random ?? (() => Math.random());

  // Store JWT after confirm (in-memory for the session)
  let cachedJwt: string | null = null;
  let demoCallsUsed = 0;
  let demoSatsUsed = 0;
  const demoSessions = new Map<string, DemoSession>();

  // Create MCP server
  const server = new Server(
  {
    name: 'liveauth-mcp',
    version: '1.0.3',
  },
  {
    capabilities: {
      tools: {},
    },
  }
);

// Handle tool listing
server.setRequestHandler(ListToolsRequestSchema, async () => {
  return { tools: TOOLS };
});

// Handle tool calls
server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const { name, arguments: args } = request.params;

  try {
    switch (name) {
      case 'liveauth_mcp_start': {
        const { forceLightning, forceL402 } = args as { forceLightning?: boolean; forceL402?: boolean };

        // Use demo endpoint if no API key or DEMO mode enabled
        const endpoint = LIVEAUTH_DEMO 
          ? `${LIVEAUTH_API_BASE}/api/public/auth/demo/start`
          : `${LIVEAUTH_API_BASE}/api/mcp/start`;
        
        const response = await fetchImpl(endpoint, {
          method: 'POST',
          headers: getAuthHeaders(LIVEAUTH_API_KEY, LIVEAUTH_DEMO),
          body: JSON.stringify({
            forceLightning: forceLightning ?? false,
            forceL402: forceL402 ?? false,
          }),
        });

        if (!response.ok) {
          const error = await response.json() as McpErrorResponse;
          throw new Error(error.error_description || `Start failed: ${response.statusText}`);
        }

        const result = await response.json() as McpStartResponse;

        // Transform demo response to MCP format
        if (LIVEAUTH_DEMO && 'invoice' in result) {
          const demoResult = result as any;
          const sessionId = result.quoteId || demoResult.sessionId;
          
          // Store demo session for status tracking
          if (demoResult.invoice) {
            demoSessions.set(sessionId, {
              quoteId: sessionId,
              invoice: demoResult.invoice.bolt11 || demoResult.invoice,
              amountSats: demoResult.invoice.amountSats || demoResult.amountSats || 0,
              paymentHash: demoResult.invoice.paymentHash || '',
              paid: false,
              createdAt: now(),
            });
          }
          
          return {
            content: [
              {
                type: 'text',
                text: JSON.stringify({
                  quoteId: sessionId,
                  powChallenge: null,
                  invoice: demoResult.invoice ? {
                    bolt11: demoResult.invoice.bolt11 || demoResult.invoice,
                    amountSats: demoResult.invoice.amountSats || demoResult.amountSats || 0,
                    expiresAtUnix: demoResult.invoice.expiresAtUnix || demoResult.expiresAtUnix,
                    paymentHash: demoResult.invoice.paymentHash || '',
                  } : null,
                  _demo: true,
                  _instructions: 'Demo mode: Payment simulated. Use liveauth_mcp_confirm to complete.',
                }, null, 2),
              },
            ],
          };
        }

        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify(result, null, 2),
            },
          ],
        };
      }

      case 'liveauth_mcp_confirm': {
        const { quoteId, challengeHex, nonce, hashHex, expiresAtUnix, difficultyBits, signature, macaroon } = args as {
          quoteId: string;
          challengeHex?: string;
          nonce?: number;
          hashHex?: string;
          expiresAtUnix?: number;
          difficultyBits?: number;
          signature?: string;
          macaroon?: string;
        };

        // Handle demo mode confirm
        if (LIVEAUTH_DEMO && demoSessions.has(quoteId)) {
          const session = demoSessions.get(quoteId)!;
          session.paid = true;
          
          // Generate a demo JWT (in production, this would come from the API)
          const demoJwt = `demo_jwt_${now()}_${random().toString(36).substr(2, 9)}`;
          cachedJwt = demoJwt;
          
          return {
            content: [
              {
                type: 'text',
                text: JSON.stringify({
                  jwt: demoJwt,
                  expiresIn: 3600,
                  remainingBudgetSats: 1000,
                  paymentStatus: 'paid',
                  refreshToken: `demo_refresh_${quoteId}`,
                  _demo: true,
                  _note: 'Demo mode - this is a simulated JWT',
                }, null, 2),
              },
            ],
          };
        }

        const body: Record<string, unknown> = { quoteId };
        
        // Add PoW fields if provided
        if (challengeHex) body.challengeHex = challengeHex;
        if (nonce !== undefined) body.nonce = nonce;
        if (hashHex) body.hashHex = hashHex;
        if (expiresAtUnix) body.expiresAtUnix = expiresAtUnix;
        if (difficultyBits) body.difficultyBits = difficultyBits;
        if (signature) body.sig = signature;
        if (macaroon) body.macaroon = macaroon;

        const response = await fetchImpl(`${LIVEAUTH_API_BASE}/api/mcp/confirm`, {
          method: 'POST',
          headers: getAuthHeaders(LIVEAUTH_API_KEY, LIVEAUTH_DEMO),
          body: JSON.stringify(body),
        });

        if (!response.ok) {
          const error = await response.json() as McpErrorResponse;
          throw new Error(error.error_description || `Confirm failed: ${response.statusText}`);
        }

        const result = await response.json() as McpConfirmResponse;

        // Handle Lightning pending status
        if (result.paymentStatus === 'pending') {
          return {
            content: [
              {
                type: 'text',
                text: `Lightning payment pending. Poll with liveauth_mcp_status using quoteId: ${quoteId}`,
              },
            ],
          };
        }

        // Cache JWT if we got one
        if (result.jwt) {
          cachedJwt = result.jwt;
        }

        // Log refresh token for user (but don't cache it)
        if (result.refreshToken) {
          console.error(`Refresh token: ${result.refreshToken} (save this to refresh without re-auth)`);
        }

        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify(result, null, 2),
            },
          ],
        };
      }

      case 'liveauth_mcp_charge': {
        const { callCostSats, toolName } = args as { callCostSats?: number; toolName?: string };
        const demoCostSats = callCostSats ?? 1;

        if (LIVEAUTH_DEMO) {
          if (!cachedJwt) {
            return {
              content: [
                {
                  type: 'text',
                  text: 'Demo session is not confirmed. Call liveauth_mcp_confirm before charging.',
                },
              ],
              isError: true,
            };
          }

          if (demoSatsUsed + demoCostSats > 1000) {
            return {
              content: [
                {
                  type: 'text',
                  text: `Budget exceeded! Calls used: ${demoCallsUsed}, Sats used: ${demoSatsUsed}. Stop making API calls.`,
                },
              ],
              isError: true,
            };
          }

          demoCallsUsed += 1;
          demoSatsUsed += demoCostSats;

          return {
            content: [
              {
                type: 'text',
                text: JSON.stringify({
                  status: 'ok',
                  callsUsed: demoCallsUsed,
                  satsUsed: demoSatsUsed,
                  _demo: true,
                }, null, 2),
              },
            ],
          };
        }

        const authHeaders = getAuthHeaders(LIVEAUTH_API_KEY, LIVEAUTH_DEMO);
        if (cachedJwt) {
          authHeaders['Authorization'] = `Bearer ${cachedJwt}`;
        }

        const response = await fetchImpl(`${LIVEAUTH_API_BASE}/api/mcp/charge`, {
          method: 'POST',
          headers: authHeaders,
          body: JSON.stringify({
            ...(callCostSats === undefined ? {} : { callCostSats }),
            ...(toolName ? { toolName } : {}),
          }),
        });

        if (!response.ok) {
          const error = await response.json() as McpErrorResponse;
          throw new Error(error.error_description || `Charge failed: ${response.statusText}`);
        }

        const result = await response.json() as McpChargeResponse;

        // If status is 'deny', the agent has exceeded its budget
        if (result.status === 'deny') {
          return {
            content: [
              {
                type: 'text',
                text: `Budget exceeded! Calls used: ${result.callsUsed}, Sats used: ${result.satsUsed}. Stop making API calls.`,
              },
            ],
            isError: true,
          };
        }

        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify(result, null, 2),
            },
          ],
        };
      }

      case 'liveauth_mcp_usage': {
        // Return demo usage in demo mode
        if (LIVEAUTH_DEMO) {
          return {
            content: [
              {
                type: 'text',
                text: JSON.stringify({
                  status: 'active',
                  callsUsed: demoCallsUsed,
                  satsUsed: demoSatsUsed,
                  maxSatsPerDay: 1000,
                  remainingBudgetSats: Math.max(0, 1000 - demoSatsUsed),
                  maxCallsPerMinute: 60,
                  _demo: true,
                }, null, 2),
              },
            ],
          };
        }

        const authHeaders = getAuthHeaders(LIVEAUTH_API_KEY, LIVEAUTH_DEMO);
        if (cachedJwt) {
          authHeaders['Authorization'] = `Bearer ${cachedJwt}`;
        }

        const response = await fetchImpl(`${LIVEAUTH_API_BASE}/api/mcp/usage`, {
          method: 'GET',
          headers: authHeaders,
        });

        if (!response.ok) {
          const error = await response.json() as McpErrorResponse;
          throw new Error(error.error_description || `Usage query failed: ${response.statusText}`);
        }

        const result = await response.json() as McpUsageResponse;

        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify(result, null, 2),
            },
          ],
        };
      }

      case 'liveauth_mcp_status': {
        const { quoteId } = args as { quoteId: string };

        // Check demo session first
        if (LIVEAUTH_DEMO && demoSessions.has(quoteId)) {
          const session = demoSessions.get(quoteId)!;
          // In demo mode, auto-mark as paid after 2 seconds
          if (!session.paid && now() - session.createdAt > 2000) {
            session.paid = true;
          }
          return {
            content: [
              {
                type: 'text',
                text: JSON.stringify({
                  quoteId: session.quoteId,
                  status: session.paid ? 'confirmed' : 'pending',
                  paymentStatus: session.paid ? 'paid' : 'pending',
                  expiresAt: new Date(now() + 300000).toISOString(),
                  _demo: true,
                  _instructions: session.paid 
                    ? 'Payment confirmed in demo mode. Use liveauth_mcp_confirm to get JWT.'
                    : 'Demo mode: Invoice generated but not actually paid. Use liveauth_mcp_confirm to simulate completion.',
                }, null, 2),
              },
            ],
          };
        }

        const response = await fetchImpl(`${LIVEAUTH_API_BASE}/api/mcp/status/${quoteId}`, {
          method: 'GET',
          headers: getAuthHeaders(LIVEAUTH_API_KEY, LIVEAUTH_DEMO),
        });

        if (!response.ok) {
          const error = await response.json() as McpErrorResponse;
          throw new Error(error.error_description || `Status check failed: ${response.statusText}`);
        }

        const result = await response.json() as McpStatusResponse;

        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify(result, null, 2),
            },
          ],
        };
      }

      case 'liveauth_mcp_lnurl': {
        const { quoteId } = args as { quoteId: string };

        if (LIVEAUTH_DEMO && demoSessions.has(quoteId)) {
          const session = demoSessions.get(quoteId)!;
          return {
            content: [
              {
                type: 'text',
                text: JSON.stringify({
                  pr: session.invoice,
                  routes: [],
                  _demo: true,
                }, null, 2),
              },
            ],
          };
        }

        // Use the new lnget-compatible endpoint
        const response = await fetchImpl(`${LIVEAUTH_API_BASE}/api/mcp/lnurl/${quoteId}`, {
          method: 'GET',
          headers: getAuthHeaders(LIVEAUTH_API_KEY, LIVEAUTH_DEMO),
        });

        if (!response.ok) {
          const error = await response.json() as McpErrorResponse;
          throw new Error(error.error_description || `Lnurl fetch failed: ${response.statusText}`);
        }

        const result = await response.json() as { pr: string; routes: string[] };

        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify(result, null, 2),
            },
          ],
        };
      }

      case 'liveauth_mcp_refresh': {
        const { refreshToken } = args as { refreshToken: string };

        if (LIVEAUTH_DEMO && refreshToken.startsWith('demo_refresh_')) {
          const demoJwt = `demo_jwt_${now()}_${random().toString(36).substr(2, 9)}`;
          cachedJwt = demoJwt;

          return {
            content: [
              {
                type: 'text',
                text: JSON.stringify({
                  jwt: demoJwt,
                  expiresIn: 3600,
                  remainingBudgetSats: Math.max(0, 1000 - demoSatsUsed),
                  _demo: true,
                }, null, 2),
              },
            ],
          };
        }

        const response = await fetchImpl(`${LIVEAUTH_API_BASE}/api/mcp/refresh`, {
          method: 'POST',
          headers: getAuthHeaders(LIVEAUTH_API_KEY, LIVEAUTH_DEMO),
          body: JSON.stringify({
            refreshToken,
          }),
        });

        if (!response.ok) {
          const error = await response.json() as McpErrorResponse;
          throw new Error(error.error_description || `Refresh failed: ${response.statusText}`);
        }

        const result = await response.json() as McpRefreshResponse;

        // Cache the new JWT
        cachedJwt = result.jwt;

        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify(result, null, 2),
            },
          ],
        };
      }

      default:
        throw new Error(`Unknown tool: ${name}`);
    }
  } catch (error) {
    return {
      content: [
        {
          type: 'text',
          text: `Error: ${error instanceof Error ? error.message : String(error)}`,
        },
      ],
      isError: true,
    };
  }
});

  return server;
}

// Start server
async function main() {
  const server = createLiveAuthMcpServer();
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error('LiveAuth MCP server running on stdio');
}

if (import.meta.url === `file://${process.argv[1]}`) {
  main().catch((error) => {
    console.error('Fatal error:', error);
    process.exit(1);
  });
}
