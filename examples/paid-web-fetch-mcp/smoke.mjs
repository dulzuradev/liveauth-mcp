#!/usr/bin/env node

import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';
import { config } from 'dotenv';
import { obtainJwt } from './mcp-session.mjs';

config();

const api = process.env.LIVEAUTH_API_URL || 'http://127.0.0.1:5089';
const publicKey = process.env.LIVEAUTH_PUBLIC_KEY || process.env.LIVEAUTH_API_KEY || 'la_pk_demo';
const jwt = process.env.LIVEAUTH_JWT || await obtainJwt({ api, publicKey });

const transport = new StdioClientTransport({
  command: 'node',
  args: ['server.mjs'],
  cwd: process.cwd(),
  env: {
    ...process.env,
    LIVEAUTH_API_URL: api,
    LIVEAUTH_PUBLIC_KEY: publicKey,
    LIVEAUTH_JWT: jwt
  }
});

const client = new Client({ name: 'liveauth-paid-web-fetch-smoke', version: '1.0.0' }, { capabilities: {} });
await client.connect(transport);

try {
  const tools = await client.listTools();
  console.log('tools:', tools.tools.map(tool => tool.name).join(', '));

  const result = await client.callTool({
    name: 'web_fetch_metadata',
    arguments: {
      url: process.env.WEB_FETCH_SMOKE_URL || 'https://example.com',
      idempotencyKey: `smoke-${Date.now()}`
    }
  });

  console.log(result.content?.[0]?.text);
} finally {
  await client.close();
}
