import { LiveAuthMcpError } from './errors.js';
import type { PowChallenge, PowSolution, PowSolverOptions } from './types.js';

const DEFAULT_YIELD_EVERY = 2_000;

export async function solvePow(
  challenge: PowChallenge,
  publicKey = challenge.projectPublicKey,
  options: PowSolverOptions = {}
): Promise<PowSolution> {
  const target = BigInt(`0x${challenge.targetHex}`);
  const maxIterations = options.maxIterations ?? Number.MAX_SAFE_INTEGER;
  const yieldEvery = Math.max(1, options.yieldEvery ?? DEFAULT_YIELD_EVERY);

  for (let nonce = 0; nonce <= maxIterations; nonce += 1) {
    if (options.signal?.aborted) {
      throw new LiveAuthMcpError('PoW solving was aborted', { code: 'pow_aborted' });
    }

    const hashHex = await sha256Hex(`${publicKey}:${challenge.challengeHex}:${nonce}`);
    if (BigInt(`0x${hashHex}`) <= target) {
      return {
        challengeHex: challenge.challengeHex,
        nonce,
        hashHex,
        difficultyBits: challenge.difficultyBits,
        expiresAtUnix: challenge.expiresAtUnix,
        sig: challenge.signature
      };
    }

    if (nonce > 0 && nonce % yieldEvery === 0) {
      await new Promise((resolve) => setTimeout(resolve, 0));
    }
  }

  throw new LiveAuthMcpError('PoW solution was not found before maxIterations', {
    code: 'pow_max_iterations'
  });
}

export async function sha256Hex(input: string): Promise<string> {
  if (!globalThis.crypto?.subtle) {
    throw new LiveAuthMcpError('PoW solving requires Web Crypto. Use Node 18+ or a browser runtime.');
  }

  const bytes = new TextEncoder().encode(input);
  const digest = await globalThis.crypto.subtle.digest('SHA-256', bytes);
  return bytesToHex(new Uint8Array(digest));
}

function bytesToHex(bytes: Uint8Array): string {
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
}
