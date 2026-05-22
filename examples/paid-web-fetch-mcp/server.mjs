#!/usr/bin/env node

import { randomUUID } from 'node:crypto';
import { config } from 'dotenv';
import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ListToolsRequestSchema
} from '@modelcontextprotocol/sdk/types.js';
import { createMcpGate } from '@liveauth-labs/mcp-server';
import {
  fetchWebMetadata,
  fetchWebPage,
  normalizeLimits,
  validateHttpUrl,
  WebFetchError
} from './web-fetch.mjs';

config();

const LIVEAUTH_API_URL = process.env.LIVEAUTH_API_URL || 'https://api.liveauth.app';
const LIVEAUTH_PUBLIC_KEY = process.env.LIVEAUTH_PUBLIC_KEY || process.env.LIVEAUTH_API_KEY || '';
const LIVEAUTH_TOOL_ID = process.env.LIVEAUTH_TOOL_ID || '00000000-0000-0000-0000-000000000005';
const WEB_FETCH_DEFAULT_COST_SATS = numberFromEnv('WEB_FETCH_DEFAULT_COST_SATS', 5);
const WEB_FETCH_METADATA_COST_SATS = numberFromEnv('WEB_FETCH_METADATA_COST_SATS', 1);

const limits = normalizeLimits({
  defaultMaxBytes: numberFromEnv('WEB_FETCH_DEFAULT_MAX_BYTES', 200_000),
  maxBytes: numberFromEnv('WEB_FETCH_MAX_BYTES', 500_000),
  timeoutMs: numberFromEnv('WEB_FETCH_TIMEOUT_MS', 10_000),
  maxRedirects: numberFromEnv('WEB_FETCH_MAX_REDIRECTS', 3),
  userAgent: process.env.WEB_FETCH_USER_AGENT || undefined
});

if (!LIVEAUTH_PUBLIC_KEY) {
  throw new Error('LIVEAUTH_PUBLIC_KEY is required');
}

const gate = createMcpGate({
  publicKey: LIVEAUTH_PUBLIC_KEY,
  baseUrl: LIVEAUTH_API_URL,
  toolId: LIVEAUTH_TOOL_ID,
  defaultCostSats: WEB_FETCH_DEFAULT_COST_SATS
});

const TOOLS = [
  {
    name: 'web_fetch',
    description: 'Fetch a public http/https URL and return cleaned text plus metadata. Blocks private/local network targets.',
    inputSchema: {
      type: 'object',
      properties: {
        url: { type: 'string', description: 'Public http/https URL to fetch' },
        maxBytes: {
          type: 'number',
          description: `Maximum response bytes to read, capped at ${limits.maxBytes}`
        },
        includeHtml: {
          type: 'boolean',
          description: 'Include raw HTML in addition to cleaned text'
        },
        liveauthJwt: {
          type: 'string',
          description: 'Optional LiveAuth MCP JWT. Defaults to LIVEAUTH_JWT env.'
        },
        idempotencyKey: {
          type: 'string',
          description: 'Optional retry-safe call ID. Defaults to a generated UUID.'
        }
      },
      required: ['url']
    }
  },
  {
    name: 'web_fetch_metadata',
    description: 'Fetch low-cost page metadata for a public http/https URL. Blocks private/local network targets.',
    inputSchema: {
      type: 'object',
      properties: {
        url: { type: 'string', description: 'Public http/https URL to inspect' },
        liveauthJwt: {
          type: 'string',
          description: 'Optional LiveAuth MCP JWT. Defaults to LIVEAUTH_JWT env.'
        },
        idempotencyKey: {
          type: 'string',
          description: 'Optional retry-safe call ID. Defaults to a generated UUID.'
        }
      },
      required: ['url']
    }
  }
];

const server = new Server(
  {
    name: 'liveauth-paid-web-fetch-mcp',
    version: '0.1.0'
  },
  {
    capabilities: {
      tools: {}
    }
  }
);

server.setRequestHandler(ListToolsRequestSchema, async () => ({ tools: TOOLS }));

server.setRequestHandler(CallToolRequestSchema, async request => {
  const args = request.params.arguments ?? {};

  try {
    switch (request.params.name) {
      case 'web_fetch':
        return await handleWebFetch(args);
      case 'web_fetch_metadata':
        return await handleWebFetchMetadata(args);
      default:
        throw new Error(`Unknown tool: ${request.params.name}`);
    }
  } catch (error) {
    const code = error instanceof WebFetchError ? error.code : 'tool_error';
    return {
      content: [
        {
          type: 'text',
          text: JSON.stringify({ error: code, message: error.message }, null, 2)
        }
      ],
      isError: true
    };
  }
});

async function handleWebFetch(args) {
  const url = requireString(args.url, 'url');
  const parsed = validateHttpUrl(url);
  const jwt = resolveJwt(args);
  const idempotencyKey = String(args.idempotencyKey || randomUUID());
  const maxBytes = args.maxBytes ?? limits.defaultMaxBytes;

  const result = await gate.invoke(
    jwt,
    { url, maxBytes, includeHtml: args.includeHtml === true },
    async (input, context) => ({
      ...await fetchWebPage(input.url, {
        maxBytes: input.maxBytes,
        includeHtml: input.includeHtml,
        limits
      }),
      charge: context.liveAuth.charge
    }),
    {},
    {
      costSats: WEB_FETCH_DEFAULT_COST_SATS,
      validateFirst: true,
      toolMethodName: 'web_fetch',
      idempotencyKey,
      metadata: {
        urlHost: parsed.hostname
      }
    }
  );

  return jsonToolResult(result);
}

async function handleWebFetchMetadata(args) {
  const url = requireString(args.url, 'url');
  const parsed = validateHttpUrl(url);
  const jwt = resolveJwt(args);
  const idempotencyKey = String(args.idempotencyKey || randomUUID());

  const result = await gate.invoke(
    jwt,
    { url },
    async (input, context) => ({
      ...await fetchWebMetadata(input.url, { limits }),
      charge: context.liveAuth.charge
    }),
    {},
    {
      costSats: WEB_FETCH_METADATA_COST_SATS,
      validateFirst: true,
      toolMethodName: 'web_fetch_metadata',
      idempotencyKey,
      metadata: {
        urlHost: parsed.hostname
      }
    }
  );

  return jsonToolResult(result);
}

function resolveJwt(args) {
  const jwt = args.liveauthJwt || process.env.LIVEAUTH_JWT;
  if (!jwt || typeof jwt !== 'string') {
    throw new Error('Missing LiveAuth MCP JWT. Set LIVEAUTH_JWT or pass liveauthJwt.');
  }

  return jwt;
}

function requireString(value, name) {
  if (typeof value !== 'string' || !value.trim()) {
    throw new Error(`${name} is required`);
  }

  return value.trim();
}

function jsonToolResult(value) {
  return {
    content: [
      {
        type: 'text',
        text: JSON.stringify(value, null, 2)
      }
    ]
  };
}

function numberFromEnv(name, fallback) {
  const raw = process.env[name];
  if (!raw) return fallback;
  const value = Number(raw);
  return Number.isFinite(value) ? value : fallback;
}

const transport = new StdioServerTransport();
await server.connect(transport);
