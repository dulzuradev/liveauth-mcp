import { createHash } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { createMcpClient, solvePow } from './index.js';
import type { PowChallenge } from './types.js';

const canonicalKey = 'la_pk_project_primary';
const apiKey = 'la_pk_project_api_key';
const challenge: PowChallenge = {
  projectId: '11111111-1111-1111-1111-111111111111',
  projectPublicKey: canonicalKey,
  challengeHex: '0123456789abcdef',
  targetHex: 'f'.repeat(64),
  difficultyBits: 0,
  expiresAtUnix: 9999999999,
  signature: 'test-signature',
};

describe('MCP PoW project API key aliases', () => {
  it.each([canonicalKey, apiKey])('confirms using the canonical challenge key with header %s', async (publicKey) => {
    // Compute the expected preimage independently of the SDK solver.
    const expectedHash = createHash('sha256')
      .update(`${canonicalKey}:${challenge.challengeHex}:0`).digest('hex');
    const client = createMcpClient({
      publicKey, autoRefresh: false,
      fetch: async (input, init) => {
        expect(new Headers(init?.headers).get('X-LW-Public')).toBe(publicKey);
        if (String(input).endsWith('/start')) {
          return Response.json({ quoteId: 'quote-1', powChallenge: challenge });
        }
        const body = JSON.parse(String(init?.body));
        expect(body).toMatchObject({ quoteId: 'quote-1', nonce: 0, hashHex: expectedHash,
          challengeHex: challenge.challengeHex, sig: challenge.signature });
        return Response.json({ jwt: 'test-token', expiresIn: 600, remainingBudgetSats: 100 });
      },
    });
    try {
      await client.start();
      expect((await client.confirm()).jwt).toBe('test-token');
    } finally { client.destroy(); }
  });

  it('passes an explicit invalid solution through and propagates server rejection without storing a token', async () => {
    const client = createMcpClient({
      publicKey: apiKey, autoRefresh: false,
      fetch: async (input, init) => {
        expect(new Headers(init?.headers).get('X-LW-Public')).toBe(apiKey);
        if (String(input).endsWith('/start')) {
          return Response.json({ quoteId: 'quote-1', powChallenge: challenge });
        }
        expect(JSON.parse(String(init?.body)).hashHex).toBe('0'.repeat(64));
        return Response.json({ error: 'invalid_pow' }, { status: 401 });
      },
    });
    try {
      const session = await client.start();
      const solution = await solvePow(challenge);
      await expect(client.confirm(session, { powSolution: { ...solution, hashHex: '0'.repeat(64) } }))
        .rejects.toMatchObject({ status: 401, message: 'invalid_pow' });
      expect(client.token).toBeUndefined();
    } finally { client.destroy(); }
  });
});
