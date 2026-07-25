import assert from 'node:assert/strict';
import test from 'node:test';

import {
  createWorkerPowSolver,
  LiveAuth,
  LiveAuthError
} from '../dist/index.js';
import {
  hashCandidate,
  hexToBytes,
  isAtOrBelowTarget
} from '../dist/pow.js';

const publicKey = 'la_pk_sdk_test';
const challengeId = '0123456789abcdef0123456789abcdef';

function challenge(overrides = {}) {
  return {
    challengeId,
    projectPublicKey: publicKey,
    environment: 'TEST',
    action: 'ai.generate_image',
    protectedActionId: '22222222-2222-4222-8222-222222222222',
    targetHex: 'f'.repeat(64),
    difficultyBits: 8,
    difficultyReason: 'base_policy',
    expiresAtUnix: Math.floor(Date.now() / 1000) + 120,
    configurationVersion: 3,
    signature: 'signed-challenge',
    ...overrides
  };
}

function jsonResponse(body, status = 200, headers = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      'Content-Type': 'application/json',
      ...headers
    }
  });
}

test('protect requests, solves, and completes an action challenge', async () => {
  const requests = [];
  const fetcher = async (url, init) => {
    requests.push({ url: String(url), init });
    if (requests.length === 1)
      return jsonResponse(challenge());
    return jsonResponse({
      token: 'costshield.jwt',
      tokenType: 'Bearer',
      expiresAtUnix: Math.floor(Date.now() / 1000) + 120,
      authorizationId: '33333333-3333-4333-8333-333333333333',
      action: 'ai.generate_image',
      environment: 'TEST',
      requireSingleUse: true
    });
  };
  const progress = [];
  const sdk = new LiveAuth({
    publicKey,
    environment: 'TEST',
    apiUrl: 'https://api.example.test/',
    origin: 'https://app.example.test',
    fetch: fetcher,
    powSolver: async options => {
      options.onProgress?.({
        attempts: 25,
        nonce: 24,
        elapsedMilliseconds: 2,
        difficultyBits: options.challenge.difficultyBits
      });
      return {
        nonce: 42,
        hashHex: '0'.repeat(64),
        attempts: 43,
        elapsedMilliseconds: 9
      };
    }
  });

  const result = await sdk.protect({
    action: 'ai.generate_image',
    subject: 'user-123',
    clientMetadata: { surface: 'editor' },
    onProgress: value => progress.push(value)
  });

  assert.equal(result.token, 'costshield.jwt');
  assert.equal(result.solveMilliseconds, 9);
  assert.equal(result.difficultyBits, 8);
  assert.equal(progress.length, 1);
  assert.equal(
    requests[0].url,
    'https://api.example.test/api/public/costshield/challenges'
  );
  assert.equal(
    requests[1].url,
    `https://api.example.test/api/public/costshield/challenges/${
      challengeId
    }/complete`
  );
  assert.equal(
    requests[0].init.headers['X-LW-Public'],
    publicKey
  );

  const createBody = JSON.parse(requests[0].init.body);
  assert.equal(createBody.origin, 'https://app.example.test');
  assert.equal(createBody.subject, 'user-123');
  const completeBody = JSON.parse(requests[1].init.body);
  assert.equal(completeBody.nonce, 42);
  assert.equal(completeBody.signature, 'signed-challenge');
  assert.equal(completeBody.subject, 'user-123');
});

test('protect exposes rate-limit details without retrying', async () => {
  const sdk = new LiveAuth({
    publicKey,
    environment: 'TEST',
    fetch: async () => jsonResponse(
      {
        error: 'action_rate_limit',
        error_description: 'The protected action rate limit was reached.'
      },
      429,
      { 'Retry-After': '17' }
    ),
    powSolver: async () => {
      throw new Error('solver should not run');
    }
  });

  await assert.rejects(
    sdk.protect({ action: 'ai.generate_image' }),
    error => {
      assert.ok(error instanceof LiveAuthError);
      assert.equal(error.code, 'action_rate_limit');
      assert.equal(error.status, 429);
      assert.equal(error.retryAfterSeconds, 17);
      assert.equal(error.retryable, true);
      return true;
    }
  );
});

test('protect replaces a stale challenge before solving', async () => {
  let challengeRequests = 0;
  let solveRequests = 0;
  const sdk = new LiveAuth({
    publicKey,
    environment: 'TEST',
    challengeRetries: 1,
    fetch: async url => {
      if (String(url).endsWith('/challenges')) {
        challengeRequests++;
        return jsonResponse(challenge({
          expiresAtUnix: challengeRequests === 1
            ? Math.floor(Date.now() / 1000) - 1
            : Math.floor(Date.now() / 1000) + 120
        }));
      }
      return jsonResponse({
        token: 'fresh.jwt',
        tokenType: 'Bearer',
        expiresAtUnix: Math.floor(Date.now() / 1000) + 120,
        authorizationId: '33333333-3333-4333-8333-333333333333',
        action: 'ai.generate_image',
        environment: 'TEST',
        requireSingleUse: false
      });
    },
    powSolver: async () => {
      solveRequests++;
      return {
        nonce: 1,
        hashHex: '0'.repeat(64),
        attempts: 2,
        elapsedMilliseconds: 1
      };
    }
  });

  const result = await sdk.protect({ action: 'ai.generate_image' });

  assert.equal(result.token, 'fresh.jwt');
  assert.equal(challengeRequests, 2);
  assert.equal(solveRequests, 1);
});

test('worker solver forwards progress and terminates after a solution', async () => {
  let workerUrl;
  let terminated = false;
  const worker = {
    onmessage: null,
    onerror: null,
    postMessage(message) {
      assert.equal(message.challengeId, challengeId);
      queueMicrotask(() => {
        this.onmessage({
          data: {
            type: 'progress',
            attempts: 4096,
            nonce: 4096,
            elapsedMilliseconds: 5
          }
        });
        this.onmessage({
          data: {
            type: 'solved',
            nonce: 5000,
            hashHex: '0'.repeat(64),
            attempts: 5001,
            elapsedMilliseconds: 8
          }
        });
      });
    },
    terminate() {
      terminated = true;
    }
  };
  const solver = createWorkerPowSolver(url => {
    workerUrl = url;
    return worker;
  });
  const progress = [];

  const result = await solver({
    challenge: challenge(),
    onProgress: value => progress.push(value)
  });

  assert.match(workerUrl.pathname, /pow-worker\.js$/);
  assert.equal(progress[0].difficultyBits, 8);
  assert.equal(result.nonce, 5000);
  assert.equal(terminated, true);
});

test('worker hashing matches the CostShield SHA-256 payload contract', async () => {
  const result = await hashCandidate(publicKey, challengeId, 42);

  assert.equal(
    result.hex,
    '18f3de6334a15472d0b5e07890fc709a' +
      'f7f1bb9cae0b77579b28f4e68f99aa5d'
  );
  assert.equal(
    isAtOrBelowTarget(
      result.bytes,
      hexToBytes(result.hex)
    ),
    true
  );
  assert.equal(
    isAtOrBelowTarget(
      result.bytes,
      hexToBytes('0'.repeat(64))
    ),
    false
  );
});
