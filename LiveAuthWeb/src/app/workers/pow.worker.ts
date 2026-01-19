/// <reference lib="webworker" />

/* ================================================================
 * Utilities
 * ================================================================ */

function hexToBytes(hex: string): Uint8Array {
  const clean = hex.trim().toLowerCase();
  if (clean.length % 2 !== 0) {
    throw new Error('Invalid hex string');
  }

  const out = new Uint8Array(clean.length / 2);
  for (let i = 0; i < out.length; i++) {
    out[i] = parseInt(clean.substr(i * 2, 2), 16);
  }
  return out;
}

async function sha256Hex(input: string): Promise<string> {
  const data = new TextEncoder().encode(input);
  const hash = await crypto.subtle.digest('SHA-256', data);
  return Array.from(new Uint8Array(hash))
    .map(b => b.toString(16).padStart(2, '0'))
    .join('');
}

function isLessThan(hash: Uint8Array, target: Uint8Array): boolean {
  for (let i = 0; i < hash.length; i++) {
    if (hash[i] < target[i]) return true;
    if (hash[i] > target[i]) return false;
  }
  return true;
}

/* ================================================================
 * Worker entrypoint
 * ================================================================ */

addEventListener('message', async (event: MessageEvent) => {
  try {
    const {
      projectPublicKey,
      challengeHex,
      targetHex
    } = event.data ?? {};

    if (!projectPublicKey || !challengeHex || !targetHex) {
      throw new Error('Invalid PoW worker payload');
    }

    const target = hexToBytes(targetHex);
    let nonce = 0;

    while (true) {
      const input = `${projectPublicKey}:${challengeHex}:${nonce}`;
      const hashHex = await sha256Hex(input);
      const hashBytes = hexToBytes(hashHex);

      if (isLessThan(hashBytes, target)) {
        // ✅ EXACT shape expected by client
        postMessage({
          nonce,
          hashHex
        });
        return;
      }

      nonce++;
    }
  } catch (err: any) {
    postMessage({
      error: err?.message ?? 'PoW worker error'
    });
  }
});
