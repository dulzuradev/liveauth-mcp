#!/usr/bin/env node

import { config } from 'dotenv';
import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ListToolsRequestSchema
} from '@modelcontextprotocol/sdk/types.js';
import {
  createLiveAuthGate,
  loadWebFetchConfig
} from './runtime-config.mjs';
import {
  callHostedTool,
  cleanBaseUrl
} from './hosted-client.mjs';
import { createWebFetchToolDefinitions } from './tool-definitions.mjs';
import {
  WebFetchError
} from './web-fetch.mjs';
import {
  resolveJwtFromArgs,
  runWebFetch,
  runWebFetchMetadata
} from './web-fetch-runner.mjs';

config();

const webFetchConfig = loadWebFetchConfig();
const hostedUrl = process.env.WEB_FETCH_HOSTED_URL
  ? cleanBaseUrl(process.env.WEB_FETCH_HOSTED_URL)
  : '';
const gate = hostedUrl ? null : createLiveAuthGate(webFetchConfig);
const TOOLS = createWebFetchToolDefinitions(webFetchConfig.limits);

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
  if (hostedUrl) {
    return jsonToolResult(await callHostedTool({
      baseUrl: hostedUrl,
      toolName: 'web_fetch',
      args,
      jwt: resolveJwtFromArgs(args),
      agentId: process.env.WEB_FETCH_AGENT_ID
    }));
  }

  const result = await runWebFetch(args, {
    gate: requireGate(),
    jwt: resolveJwtFromArgs(args),
    limits: webFetchConfig.limits,
    costs: webFetchConfig.costs
  });

  return jsonToolResult(result);
}

async function handleWebFetchMetadata(args) {
  if (hostedUrl) {
    return jsonToolResult(await callHostedTool({
      baseUrl: hostedUrl,
      toolName: 'web_fetch_metadata',
      args,
      jwt: resolveJwtFromArgs(args),
      agentId: process.env.WEB_FETCH_AGENT_ID
    }));
  }

  const result = await runWebFetchMetadata(args, {
    gate: requireGate(),
    jwt: resolveJwtFromArgs(args),
    limits: webFetchConfig.limits,
    costs: webFetchConfig.costs
  });

  return jsonToolResult(result);
}

function requireGate() {
  if (!gate) {
    throw new Error('LiveAuth gate is not configured');
  }

  return gate;
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

const transport = new StdioServerTransport();
await server.connect(transport);
