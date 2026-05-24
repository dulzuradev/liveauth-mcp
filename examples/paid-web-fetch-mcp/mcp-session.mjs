import crypto from 'node:crypto';

export async function obtainJwt({ api, publicKey }) {
  const headers = {
    'Content-Type': 'application/json',
    'X-LW-Public': publicKey
  };

  const start = await fetch(`${api}/api/mcp/start`, {
    method: 'POST',
    headers,
    body: '{}'
  });

  if (!start.ok) {
    throw new Error(`MCP start failed: ${start.status} ${await start.text()}`);
  }

  const session = await start.json();
  const challenge = session.powChallenge;
  if (!challenge) {
    throw new Error('MCP start did not return a PoW challenge. Set LIVEAUTH_JWT for non-PoW flows.');
  }

  let nonce = 0;
  let hashHex = '';
  const target = BigInt(`0x${challenge.targetHex}`);

  while (true) {
    hashHex = crypto
      .createHash('sha256')
      .update(`${challenge.projectPublicKey}:${challenge.challengeHex}:${nonce}`)
      .digest('hex');

    if (BigInt(`0x${hashHex}`) <= target) break;
    nonce += 1;
  }

  const confirm = await fetch(`${api}/api/mcp/confirm`, {
    method: 'POST',
    headers,
    body: JSON.stringify({
      quoteId: session.quoteId,
      challengeHex: challenge.challengeHex,
      nonce,
      hashHex,
      difficultyBits: challenge.difficultyBits,
      expiresAtUnix: challenge.expiresAtUnix,
      sig: challenge.signature
    })
  });

  if (!confirm.ok) {
    throw new Error(`MCP confirm failed: ${confirm.status} ${await confirm.text()}`);
  }

  const confirmed = await confirm.json();
  if (!confirmed.jwt) {
    throw new Error(`MCP confirm did not return jwt: ${JSON.stringify(confirmed)}`);
  }

  return confirmed.jwt;
}
