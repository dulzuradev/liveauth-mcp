#!/usr/bin/env node

import crypto from 'node:crypto';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';
import { config } from 'dotenv';

config();

const api = process.env.LIVEAUTH_API_URL || 'http://127.0.0.1:5089';
const publicKey = process.env.LIVEAUTH_PUBLIC_KEY || process.env.LIVEAUTH_API_KEY || 'la_pk_demo';
const headers = {
  'Content-Type': 'application/json',
  'X-LW-Public': publicKey
};

const jwt = process.env.LIVEAUTH_JWT || await obtainJwt();

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

async function obtainJwt() {
  const start = await fetch(`${api}/api/mcp/start`, {
    method: 'POST',
    headers,
    body: '{}'
  });

  if (!start.ok) {
    throw new Error(`MCP start failed: ${start.status} ${await start.text()}`);
  }

  const session = await start.json();
  const challenge = session.powChallenge;
  if (!challenge) {
    throw new Error('MCP start did not return a PoW challenge. Set LIVEAUTH_JWT for non-PoW flows.');
  }

  let nonce = 0;
  let hashHex = '';
  const target = BigInt(`0x${challenge.targetHex}`);

  while (true) {
    hashHex = crypto
      .createHash('sha256')
      .update(`${challenge.projectPublicKey}:${challenge.challengeHex}:${nonce}`)
      .digest('hex');

    if (BigInt(`0x${hashHex}`) <= target) break;
    nonce += 1;
  }

  const confirm = await fetch(`${api}/api/mcp/confirm`, {
    method: 'POST',
    headers,
    body: JSON.stringify({
      quoteId: session.quoteId,
      challengeHex: challenge.challengeHex,
      nonce,
      hashHex,
      difficultyBits: challenge.difficultyBits,
      expiresAtUnix: challenge.expiresAtUnix,
      sig: challenge.signature
    })
  });

  if (!confirm.ok) {
    throw new Error(`MCP confirm failed: ${confirm.status} ${await confirm.text()}`);
  }

  const confirmed = await confirm.json();
  if (!confirmed.jwt) {
    throw new Error(`MCP confirm did not return jwt: ${JSON.stringify(confirmed)}`);
  }

  return confirmed.jwt;
}
