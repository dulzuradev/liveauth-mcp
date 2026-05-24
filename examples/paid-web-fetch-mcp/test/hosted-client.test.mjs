import test from 'node:test';
import assert from 'node:assert/strict';
import { callHostedTool } from '../hosted-client.mjs';

test('hosted client forwards MCP adapter calls without putting JWT in the body', async () => {
  const calls = [];
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async (url, init) => {
    calls.push({ url, init });
    return new Response(JSON.stringify({ ok: true }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' }
    });
  };

  try {
    const result = await callHostedTool({
      baseUrl: 'https://fetch.liveauth.app/',
      toolName: 'web_fetch_metadata',
      jwt: 'jwt-test',
      agentId: 'agent-test',
      args: {
        url: 'https://example.com',
        liveauthJwt: 'jwt-test',
        idempotencyKey: 'call-test'
      }
    });

    assert.deepEqual(result, { ok: true });
    assert.equal(calls[0].url, 'https://fetch.liveauth.app/tools/web_fetch_metadata');
    assert.equal(calls[0].init.headers.Authorization, 'Bearer jwt-test');
    assert.equal(calls[0].init.headers['X-LiveAuth-Agent-Id'], 'agent-test');
    assert.deepEqual(JSON.parse(calls[0].init.body), {
      url: 'https://example.com',
      idempotencyKey: 'call-test'
    });
  } finally {
    globalThis.fetch = originalFetch;
  }
});
