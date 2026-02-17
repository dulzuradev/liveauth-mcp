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
  };
  invoice: null;
}

interface McpConfirmResponse {
  jwt: string;
  expiresIn: number;
  remainingBudgetSats: number;
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
    description: 'Start a new LiveAuth MCP session and get a proof-of-work challenge. Use this to begin the authentication flow.',
    inputSchema: {
      type: 'object',
      properties: {
        forceLightning: {
          type: 'boolean',
          description: 'If true, request Lightning payment instead of PoW (not yet implemented)',
        },
      },
      required: [],
    },
  },
  {
    name: 'liveauth_mcp_confirm',
    description: 'Submit the solved proof-of-work challenge to receive a JWT authentication token. The JWT is needed for subsequent API calls.',
    inputSchema: {
      type: 'object',
      properties: {
        quoteId: {
          type: 'string',
          description: 'The quoteId from the start response',
        },
        challengeHex: {
          type: 'string',
          description: 'The challenge hex from the start response',
        },
        nonce: {
          type: 'number',
          description: 'The nonce that solves the PoW challenge',
        },
        hashHex: {
          type: 'string',
          description: 'The resulting hash hex (sha256 of projectPublicKey:challengeHex:nonce)',
        },
        expiresAtUnix: {
          type: 'number',
          description: 'Expiration timestamp from the challenge',
        },
        difficultyBits: {
          type: 'number',
          description: 'Difficulty bits from the challenge',
        },
        signature: {
          type: 'string',
          description: 'Signature from the challenge',
        },
      },
      required: ['quoteId', 'challengeHex', 'nonce', 'hashHex', 'expiresAtUnix', 'difficultyBits', 'signature'],
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
          challengeHex: string;
          nonce: number;
          hashHex: string;
          expiresAtUnix: number;
          difficultyBits: number;
          signature: string;
        };

        const response = await fetch(`${LIVEAUTH_API_BASE}/api/mcp/confirm`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            quoteId,
            challengeHex,
            nonce,
            hashHex,
            expiresAtUnix,
            difficultyBits,
            sig: signature, // API uses 'sig' not 'signature'
          }),
        });

        if (!response.ok) {
          const error = await response.json() as McpErrorResponse;
          throw new Error(error.error_description || `Confirm failed: ${response.statusText}`);
        }

        const result = await response.json() as McpConfirmResponse;

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
