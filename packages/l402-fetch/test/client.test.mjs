import test from 'node:test';
import assert from 'node:assert/strict';
import { liveAuthFetch, L402MaxSpendError, MockWalletAdapter } from '../dist/index.js';

test('refuses a challenge above maxSats without calling the wallet', async () => {
  const original = globalThis.fetch;
  let walletCalls = 0;
  globalThis.fetch = async () => new Response('{}', { status: 402, headers: {
    'WWW-Authenticate': 'L402 macaroon="signed", invoice="lntb500n1example"',
    'X-LiveAuth-Price-Sats': '500'
  }});
  try {
    await assert.rejects(() => liveAuthFetch('https://example.test/research', {
      maxSats: 499,
      wallet: { async payInvoice() { walletCalls++; return { preimage: '00'.repeat(32), amountPaidSats: 500 }; } }
    }), L402MaxSpendError);
    assert.equal(walletCalls, 0);
  } finally { globalThis.fetch = original; }
});

test('pays and retries exactly once with conventional Authorization header', async () => {
  const original = globalThis.fetch;
  const headers = [];
  globalThis.fetch = async request => {
    headers.push(request.headers.get('authorization'));
    if (headers.length === 1) return new Response('', { status: 402, headers: {
      'WWW-Authenticate': 'L402 macaroon="signed", invoice="lntb5n1example"',
      'X-LiveAuth-Price-Sats': '5'
    }});
    return new Response('ok', { status: 200, headers: { 'X-LiveAuth-Receipt-Id': 'receipt-1' } });
  };
  try {
    const response = await liveAuthFetch('https://example.test/weather', {
      maxSats: 5, wallet: new MockWalletAdapter({ preimage: 'ab'.repeat(32), amountPaidSats: 5 })
    });
    assert.equal(await response.text(), 'ok');
    assert.equal(headers[0], null);
    assert.equal(headers[1], `L402 signed:${'ab'.repeat(32)}`);
    assert.equal(response.liveAuthReceipt.id, 'receipt-1');
  } finally { globalThis.fetch = original; }
});
