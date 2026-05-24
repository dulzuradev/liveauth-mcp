import test from 'node:test';
import assert from 'node:assert/strict';
import { createHostedWebFetchServer } from '../hosted-service.mjs';
import { normalizeLimits } from '../web-fetch.mjs';

test('hosted service exposes health and tool metadata', async () => {
  const fixture = await startFixture();

  try {
    const health = await fetch(`${fixture.url}/healthz`);
    assert.equal(health.status, 200);
    assert.deepEqual((await health.json()).tools, ['web_fetch', 'web_fetch_metadata']);

    const tools = await fetch(`${fixture.url}/tools`);
    assert.equal(tools.status, 200);
    assert.equal((await tools.json()).tools.length, 2);
  } finally {
    await fixture.close();
  }
});

test('hosted metadata calls require a LiveAuth JWT', async () => {
  const fixture = await startFixture();

  try {
    const response = await fetch(`${fixture.url}/tools/web_fetch_metadata`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ url: 'https://example.com' })
    });

    assert.equal(response.status, 401);
    assert.equal((await response.json()).error, 'unauthorized');
  } finally {
    await fixture.close();
  }
});

test('hosted metadata calls charge through LiveAuth tool attribution', async () => {
  const calls = [];
  const fixture = await startFixture({ calls });

  try {
    const response = await fetch(`${fixture.url}/tools/web_fetch_metadata`, {
      method: 'POST',
      headers: {
        'Authorization': 'Bearer jwt-test',
        'Content-Type': 'application/json',
        'X-LiveAuth-Agent-Id': 'agent-test'
      },
      body: JSON.stringify({
        url: 'https://example.com/docs',
        idempotencyKey: 'call-test'
      })
    });

    assert.equal(response.status, 200);
    const body = await response.json();
    assert.equal(body.title, 'Example');
    assert.equal(body.charge.revenueEventId, 'event-test');
    assert.equal(calls[0].jwt, 'jwt-test');
    assert.equal(calls[0].options.toolMethodName, 'web_fetch_metadata');
    assert.equal(calls[0].options.idempotencyKey, 'call-test');
    assert.equal(calls[0].options.agentId, 'agent-test');
    assert.deepEqual(calls[0].options.metadata, { urlHost: 'example.com' });
  } finally {
    await fixture.close();
  }
});

async function startFixture({ calls = [] } = {}) {
  const gate = {
    async invoke(jwt, input, handler, context, options) {
      calls.push({ jwt, input, context, options });

      return handler(input, {
        liveAuth: {
          charge: {
            ok: true,
            status: 'ok',
            callsUsed: 1,
            satsUsed: options.costSats,
            grossSats: options.costSats,
            platformFeeSats: 1,
            netSats: Math.max(0, options.costSats - 1),
            revenueEventId: 'event-test'
          }
        }
      });
    }
  };

  const server = createHostedWebFetchServer({
    gate,
    limits: normalizeLimits(),
    costs: { webFetch: 5, metadata: 1 },
    toolId: 'tool-test',
    async fetchWebMetadataImpl(url) {
      return {
        url,
        status: 200,
        contentType: 'text/html',
        title: 'Example',
        description: 'Example page',
        fetchedAt: '2026-05-23T00:00:00.000Z'
      };
    }
  });

  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();

  return {
    url: `http://127.0.0.1:${address.port}`,
    close: () => new Promise(resolve => server.close(resolve))
  };
}
