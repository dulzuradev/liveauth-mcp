#!/usr/bin/env node

import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
  Tool,
} from '@modelcontextprotocol/sdk/types.js';
import fetch from 'node-fetch';

const LIVEAUTH_API_BASE = process.env.LIVEAUTH_API_BASE || 'https://api.liveauth.app';

// Store JWT after confirm (in-memory for the session)
let cachedJwt: string | null = null;

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
    description: 'Start a new LiveAuth MCP session. Returns a PoW challenge (default) or Lightning invoice (if forceLightning=true).',
    inputSchema: {
      type: 'object',
      properties: {
        forceLightning: {
          type: 'boolean',
          description: 'If true, request Lightning invoice instead of PoW challenge',
        },
      },
      required: [],
    },
  },
  {
    name: 'liveauth_mcp_status',
    description: 'Check the status of an MCP session. Use to poll for Lightning payment confirmation.',
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
          description: 'Cost of the API call in sats',
        },
      },
      required: ['callCostSats'],
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

// Create MCP server
const server = new Server(
  {
    name: 'liveauth-mcp',
    version: '0.2.0',
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

// Helper to check for errors in response
function isError(response: unknown): response is McpErrorResponse {
  return typeof response === 'object' && response !== null && 'error' in response;
}

// Handle tool calls
server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const { name, arguments: args } = request.params;

  try {
    switch (name) {
      case 'liveauth_mcp_start': {
        const { forceLightning } = args as { forceLightning?: boolean };

        const response = await fetch(`${LIVEAUTH_API_BASE}/api/mcp/start`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            forceLightning: forceLightning ?? false,
          }),
        });

        if (!response.ok) {
          const error = await response.json() as McpErrorResponse;
          throw new Error(error.error_description || `Start failed: ${response.statusText}`);
        }

        const result = await response.json() as McpStartResponse;

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
        const { quoteId, challengeHex, nonce, hashHex, expiresAtUnix, difficultyBits, signature } = args as {
          quoteId: string;
          challengeHex?: string;
          nonce?: number;
          hashHex?: string;
          expiresAtUnix?: number;
          difficultyBits?: number;
          signature?: string;
        };

        const body: Record<string, unknown> = { quoteId };
        
        // Add PoW fields if provided
        if (challengeHex) body.challengeHex = challengeHex;
        if (nonce !== undefined) body.nonce = nonce;
        if (hashHex) body.hashHex = hashHex;
        if (expiresAtUnix) body.expiresAtUnix = expiresAtUnix;
        if (difficultyBits) body.difficultyBits = difficultyBits;
        if (signature) body.sig = signature;

        const response = await fetch(`${LIVEAUTH_API_BASE}/api/mcp/confirm`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
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
        const { callCostSats } = args as { callCostSats: number };

        const authHeaders: Record<string, string> = {
          'Content-Type': 'application/json',
        };
        if (cachedJwt) {
          authHeaders['Authorization'] = `Bearer ${cachedJwt}`;
        }

        const response = await fetch(`${LIVEAUTH_API_BASE}/api/mcp/charge`, {
          method: 'POST',
          headers: authHeaders,
          body: JSON.stringify({
            callCostSats,
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
        const authHeaders: Record<string, string> = {
          'Content-Type': 'application/json',
        };
        if (cachedJwt) {
          authHeaders['Authorization'] = `Bearer ${cachedJwt}`;
        }

        const response = await fetch(`${LIVEAUTH_API_BASE}/api/mcp/usage`, {
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

        const response = await fetch(`${LIVEAUTH_API_BASE}/api/mcp/status/${quoteId}`, {
          method: 'GET',
          headers: {
            'Content-Type': 'application/json',
          },
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

      case 'liveauth_mcp_refresh': {
        const { refreshToken } = args as { refreshToken: string };

        const response = await fetch(`${LIVEAUTH_API_BASE}/api/mcp/refresh`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
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

// Start server
async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error('LiveAuth MCP server running on stdio');
}

main().catch((error) => {
  console.error('Fatal error:', error);
  process.exit(1);
});
