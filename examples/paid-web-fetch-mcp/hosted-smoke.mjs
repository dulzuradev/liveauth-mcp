#!/usr/bin/env node

import { config } from 'dotenv';
import { obtainJwt } from './mcp-session.mjs';

config();

const api = process.env.LIVEAUTH_API_URL || 'http://127.0.0.1:5089';
const publicKey = process.env.LIVEAUTH_PUBLIC_KEY || process.env.LIVEAUTH_API_KEY || 'la_pk_demo';
const hostedUrl = cleanBaseUrl(process.env.WEB_FETCH_HOSTED_URL || 'http://127.0.0.1:8787');
const jwt = process.env.LIVEAUTH_JWT || await obtainJwt({ api, publicKey });

const response = await fetch(`${hostedUrl}/tools/web_fetch_metadata`, {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${jwt}`,
    'Content-Type': 'application/json',
    'X-LiveAuth-Agent-Id': process.env.WEB_FETCH_AGENT_ID || 'hosted-smoke'
  },
  body: JSON.stringify({
    url: process.env.WEB_FETCH_SMOKE_URL || 'https://example.com',
    idempotencyKey: `hosted-smoke-${Date.now()}`
  })
});

const text = await response.text();
console.log(text);

if (!response.ok) {
  process.exitCode = 1;
}

function cleanBaseUrl(value) {
  return String(value || '').replace(/\/+$/, '');
}
