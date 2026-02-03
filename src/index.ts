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

interface PowChallengeResponse {
  projectPublicKey: string;
  challengeHex: string;
  targetHex: string;
  difficultyBits: number;
  expiresAtUnix: number;
  sig: string;
}

interface PowVerifyRequest {
  challengeHex: string;
  nonce: number;
  hashHex: string;
  expiresAtUnix: number;
  difficultyBits: number;
  sig: string;
}

interface PowVerifyResponse {
  verified: boolean;
  token?: string;
  fallback?: 'lightning';
}

interface LightningStartResponse {
  sessionId: string;
  invoice: string | null;
  amountSats: number;
  expiresAtUnix: number;
  mode: 'TEST' | 'LIVE';
}

// Define our MCP tools
const TOOLS: Tool[] = [
  {
    name: 'liveauth_get_challenge',
    description: 'Get a proof-of-work challenge from LiveAuth for authentication. The agent must solve this challenge to prove computational work.',
    inputSchema: {
      type: 'object',
      properties: {
        projectPublicKey: {
          type: 'string',
          description: 'The LiveAuth project public key (starts with la_pk_)',
        },
      },
      required: ['projectPublicKey'],
    },
  },
  {
    name: 'liveauth_verify_pow',
    description: 'Verify a solved proof-of-work challenge and receive a JWT authentication token',
    inputSchema: {
      type: 'object',
      properties: {
        projectPublicKey: {
          type: 'string',
          description: 'The LiveAuth project public key',
        },
        challengeHex: {
          type: 'string',
          description: 'The challenge hex from get_challenge',
        },
        nonce: {
          type: 'number',
          description: 'The nonce that solves the challenge',
        },
        hashHex: {
          type: 'string',
          description: 'The resulting hash hex',
        },
        expiresAtUnix: {
          type: 'number',
          description: 'Expiration timestamp from challenge',
        },
        difficultyBits: {
          type: 'number',
          description: 'Difficulty bits from challenge',
        },
        sig: {
          type: 'string',
          description: 'Signature from challenge',
        },
      },
      required: ['projectPublicKey', 'challengeHex', 'nonce', 'hashHex', 'expiresAtUnix', 'difficultyBits', 'sig'],
    },
  },
  {
    name: 'liveauth_start_lightning',
    description: 'Start Lightning Network payment authentication as fallback when PoW is not feasible',
    inputSchema: {
      type: 'object',
      properties: {
        projectPublicKey: {
          type: 'string',
          description: 'The LiveAuth project public key',
        },
      },
      required: ['projectPublicKey'],
    },
  },
];

// Create MCP server
const server = new Server(
  {
    name: 'liveauth-mcp',
    version: '0.1.0',
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
      case 'liveauth_get_challenge': {
        const { projectPublicKey } = args as { projectPublicKey: string };
        
        const response = await fetch(`${LIVEAUTH_API_BASE}/api/public/pow/challenge`, {
          headers: {
            'X-LW-Public': projectPublicKey,
            'Content-Type': 'application/json',
          },
        });

        if (!response.ok) {
          throw new Error(`Challenge request failed: ${response.statusText}`);
        }

        const challenge: PowChallengeResponse = await response.json() as PowChallengeResponse;

        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify(challenge, null, 2),
            },
          ],
        };
      }

      case 'liveauth_verify_pow': {
        const verifyArgs = args as PowVerifyRequest & { projectPublicKey: string };
        
        const response = await fetch(`${LIVEAUTH_API_BASE}/api/public/pow/verify`, {
          method: 'POST',
          headers: {
            'X-LW-Public': verifyArgs.projectPublicKey,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            challengeHex: verifyArgs.challengeHex,
            nonce: verifyArgs.nonce,
            hashHex: verifyArgs.hashHex,
            expiresAtUnix: verifyArgs.expiresAtUnix,
            difficultyBits: verifyArgs.difficultyBits,
            sig: verifyArgs.sig,
          }),
        });

        if (!response.ok) {
          throw new Error(`Verification failed: ${response.statusText}`);
        }

        const result: PowVerifyResponse = await response.json() as PowVerifyResponse;

        if (result.verified && result.token) {
          return {
            content: [
              {
                type: 'text',
                text: `Authentication successful! Token: ${result.token}`,
              },
            ],
          };
        } else if (result.fallback === 'lightning') {
          return {
            content: [
              {
                type: 'text',
                text: 'PoW verification failed. Please use liveauth_start_lightning for payment-based authentication.',
              },
            ],
          };
        } else {
          throw new Error('Verification failed');
        }
      }

      case 'liveauth_start_lightning': {
        const { projectPublicKey } = args as { projectPublicKey: string };
        
        const response = await fetch(`${LIVEAUTH_API_BASE}/api/public/auth/start`, {
          method: 'POST',
          headers: {
            'X-LW-Public': projectPublicKey,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({ userHint: 'agent' }),
        });

        if (!response.ok) {
          throw new Error(`Lightning start failed: ${response.statusText}`);
        }

        const lightning: LightningStartResponse = await response.json() as LightningStartResponse;

        return {
          content: [
            {
              type: 'text',
              text: `Lightning payment required:\nInvoice: ${lightning.invoice}\nAmount: ${lightning.amountSats} sats\nSession ID: ${lightning.sessionId}\n\nUse this sessionId to poll for payment confirmation.`,
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
