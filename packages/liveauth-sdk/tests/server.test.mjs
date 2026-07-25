import assert from 'node:assert/strict';
import {
  generateKeyPairSync,
  sign
} from 'node:crypto';
import test from 'node:test';

import {
  CostShieldVerifier
} from '../dist/server.js';
import {
  LiveAuthError
} from '../dist/index.js';

const projectId = '11111111-1111-4111-8111-111111111111';
const protectedActionId =
  '22222222-2222-4222-8222-222222222222';
const origin = 'https://app.example.test';
const action = 'ai.generate_image';
const keyId = 'costshield-test-key';
const { privateKey, publicKey } = generateKeyPairSync('rsa', {
  modulusLength: 2048
});
const publicJwk = {
  ...publicKey.export({ format: 'jwk' }),
  kid: keyId,
  use: 'sig',
  alg: 'RS256'
};

function issueToken(overrides = {}) {
  const now = Math.floor(Date.now() / 1000);
  const header = {
    alg: 'RS256',
    kid: keyId,
    typ: 'costshield+jwt'
  };
  const payload = {
    iss: 'https://api.liveauth.app',
    aud: 'liveauth-costshield',
    exp: now + 120,
    nbf: now - 5,
    iat: now,
    jti: 'authorization-token-id',
    projectId,
    projectPublicKey: 'la_pk_sdk_test',
    environment: 'TEST',
    action,
    protectedActionId,
    origin,
    verificationMethod: 'pow',
    difficulty: 17,
    clientContextHash: 'context-hash',
    singleUse: true,
    configurationVersion: 2,
    ...overrides
  };
  const signingInput = [
    Buffer.from(JSON.stringify(header)).toString('base64url'),
    Buffer.from(JSON.stringify(payload)).toString('base64url')
  ].join('.');
  const signature = sign(
    'RSA-SHA256',
    Buffer.from(signingInput),
    privateKey
  ).toString('base64url');
  return `${signingInput}.${signature}`;
}

function jsonResponse(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}

function createVerifier(fetcher, options = {}) {
  return new CostShieldVerifier({
    projectId,
    environment: 'TEST',
    apiUrl: 'https://api.example.test',
    fetch: fetcher,
    ...options
  });
}

test('verifies RS256 signatures and action-bound claims locally', async () => {
  let jwksRequests = 0;
  const verifier = createVerifier(async url => {
    assert.match(String(url), /jwks\.json$/);
    jwksRequests++;
    return jsonResponse({ keys: [publicJwk] });
  });

  const first = await verifier.verify(issueToken(), {
    action,
    origin
  });
  const second = await verifier.verify(issueToken(), {
    action,
    origin
  });

  assert.equal(first.projectId, projectId);
  assert.equal(first.action, action);
  assert.equal(first.singleUse, true);
  assert.equal(second.protectedActionId, protectedActionId);
  assert.equal(jwksRequests, 1, 'JWKS should be cached');
});

test('rejects claim confusion and modified signatures', async () => {
  const verifier = createVerifier(async () =>
    jsonResponse({ keys: [publicJwk] })
  );

  await assert.rejects(
    verifier.verify(issueToken(), {
      action: 'ai.generate_text',
      origin
    }),
    error => {
      assert.ok(error instanceof LiveAuthError);
      assert.equal(error.code, 'action_mismatch');
      return true;
    }
  );

  const token = issueToken();
  const parts = token.split('.');
  parts[2] = `${
    parts[2].startsWith('a') ? 'b' : 'a'
  }${parts[2].slice(1)}`;
  const modified = parts.join('.');
  await assert.rejects(
    verifier.verify(modified, { action, origin }),
    error => {
      assert.ok(error instanceof LiveAuthError);
      assert.equal(error.code, 'invalid_token_signature');
      return true;
    }
  );
});

test('consumes single-use tokens remotely after local verification', async () => {
  const requests = [];
  const verifier = createVerifier(async (url, init) => {
    requests.push({ url: String(url), init });
    if (String(url).endsWith('jwks.json'))
      return jsonResponse({ keys: [publicJwk] });
    return jsonResponse({
      verified: true,
      consumed: true,
      authorizationId:
        '33333333-3333-4333-8333-333333333333',
      action,
      environment: 'TEST',
      origin,
      verificationMethod: 'pow',
      expiresAtUnix: Math.floor(Date.now() / 1000) + 120,
      requireSingleUse: true
    });
  }, {
    secretKey: 'la_sk_server_only'
  });

  const result = await verifier.authorize(issueToken(), {
    action,
    origin
  });

  assert.equal(result.remote.consumed, true);
  assert.equal(requests.length, 2);
  assert.match(requests[1].url, /authorizations\/consume$/);
  assert.equal(
    requests[1].init.headers.Authorization,
    'Bearer la_sk_server_only'
  );
});

test('Express middleware rejects missing tokens and attaches authorization', async () => {
  const verifier = createVerifier(async (url) => {
    if (String(url).endsWith('jwks.json'))
      return jsonResponse({ keys: [publicJwk] });
    return jsonResponse({
      verified: true,
      consumed: true,
      authorizationId:
        '33333333-3333-4333-8333-333333333333',
      action,
      environment: 'TEST',
      origin,
      verificationMethod: 'pow',
      expiresAtUnix: Math.floor(Date.now() / 1000) + 120,
      requireSingleUse: true
    });
  }, {
    secretKey: 'la_sk_server_only'
  });
  const middleware = verifier.protect(action, { origin });
  let status;
  let body;
  const response = {
    status(value) {
      status = value;
      return this;
    },
    json(value) {
      body = value;
    }
  };

  await middleware({ headers: {} }, response, () => {
    throw new Error('missing authorization must not call next');
  });
  assert.equal(status, 401);
  assert.equal(body.error, 'missing_authorization');

  const request = {
    headers: {
      authorization: `Bearer ${issueToken()}`
    }
  };
  let nextCalled = false;
  await middleware(request, response, () => {
    nextCalled = true;
  });
  assert.equal(nextCalled, true);
  assert.equal(request.costShield.claims.action, action);
  assert.equal(request.costShield.remote.consumed, true);
});
