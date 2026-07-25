import { createHash } from 'node:crypto';

import { LiveAuth } from '../dist/index.js';
import { CostShieldVerifier } from '../dist/server.js';

const required = [
  'LIVEAUTH_PUBLIC_KEY',
  'LIVEAUTH_SECRET_KEY',
  'LIVEAUTH_PROJECT_ID'
];
const missing = required.filter(name => !process.env[name]);
if (missing.length > 0) {
  console.error(`Missing environment variables: ${missing.join(', ')}`);
  process.exit(2);
}

const apiUrl =
  process.env.LIVEAUTH_API_URL ?? 'http://127.0.0.1:5167';
const environment = process.env.LIVEAUTH_ENVIRONMENT ?? 'TEST';
const action =
  process.env.LIVEAUTH_ACTION ?? 'ai.generate_image';
const origin =
  process.env.LIVEAUTH_ORIGIN ?? 'http://localhost:4200';

const client = new LiveAuth({
  apiUrl,
  publicKey: process.env.LIVEAUTH_PUBLIC_KEY,
  environment,
  origin,
  powSolver: solveInNode
});
const verifier = new CostShieldVerifier({
  apiUrl,
  projectId: process.env.LIVEAUTH_PROJECT_ID,
  environment,
  secretKey: process.env.LIVEAUTH_SECRET_KEY
});

const authorization = await client.protect({
  action,
  onProgress: progress => {
    process.stdout.write(
      `\rSolved ${progress.attempts.toLocaleString()} candidates`
    );
  }
});
process.stdout.write('\n');

const verified = await verifier.authorize(authorization.token, {
  action,
  origin
});
console.log(JSON.stringify({
  action: verified.claims.action,
  environment: verified.claims.environment,
  verificationMethod: verified.claims.verificationMethod,
  singleUse: verified.claims.singleUse,
  consumed: verified.remote?.consumed ?? false,
  solveMilliseconds: authorization.solveMilliseconds
}, null, 2));

async function solveInNode({ challenge, signal, onProgress }) {
  const startedAt = performance.now();
  const target = BigInt(`0x${challenge.targetHex}`);
  for (let nonce = 0; Number.isSafeInteger(nonce); nonce++) {
    if (signal?.aborted)
      throw signal.reason ?? new Error('Aborted');
    const hashHex = createHash('sha256')
      .update(
        `${challenge.projectPublicKey}:${
          challenge.challengeId
        }:${nonce}`
      )
      .digest('hex');
    if (BigInt(`0x${hashHex}`) <= target) {
      return {
        nonce,
        hashHex,
        attempts: nonce + 1,
        elapsedMilliseconds: Math.round(
          performance.now() - startedAt
        )
      };
    }
    if (nonce > 0 && nonce % 50_000 === 0) {
      onProgress?.({
        attempts: nonce,
        nonce,
        elapsedMilliseconds: Math.round(
          performance.now() - startedAt
        ),
        difficultyBits: challenge.difficultyBits
      });
      await new Promise(resolve => setImmediate(resolve));
    }
  }
  throw new Error('Unable to solve challenge.');
}
