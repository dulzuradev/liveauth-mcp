import crypto from 'node:crypto';
import { config } from 'dotenv';

config();

const apiUrl = (process.env.LIVEAUTH_API_URL || 'http://127.0.0.1:5166').replace(/\/$/, '');
const publicKey = process.env.LIVEAUTH_API_KEY || '';
if (!publicKey) throw new Error('Set LIVEAUTH_API_KEY to the local project public key.');

const headers = { 'content-type': 'application/json', 'x-lw-public': publicKey };
const startResponse = await fetch(`${apiUrl}/api/mcp/start`, { method: 'POST', headers, body: '{}' });
if (!startResponse.ok) throw new Error(`MCP start failed: ${startResponse.status} ${await startResponse.text()}`);
const start = await startResponse.json();
if (!start.powChallenge) throw new Error('The server did not offer TEST PoW authentication.');

const challenge = start.powChallenge;
const target = BigInt(`0x${challenge.targetHex}`);
let nonce = 0;
let hashHex;
for (;;) {
  hashHex = crypto.createHash('sha256')
    .update(`${challenge.projectPublicKey}:${challenge.challengeHex}:${nonce}`)
    .digest('hex');
  if (BigInt(`0x${hashHex}`) <= target) break;
  nonce++;
}

const confirmResponse = await fetch(`${apiUrl}/api/mcp/confirm`, {
  method: 'POST', headers,
  body: JSON.stringify({
    quoteId: start.quoteId,
    challengeHex: challenge.challengeHex,
    nonce,
    hashHex,
    difficultyBits: challenge.difficultyBits,
    expiresAtUnix: challenge.expiresAtUnix,
    sig: challenge.signature
  })
});
if (!confirmResponse.ok) throw new Error(`MCP confirm failed: ${confirmResponse.status} ${await confirmResponse.text()}`);
const confirmed = await confirmResponse.json();
console.log(JSON.stringify({
  jwt: confirmed.jwt,
  expiresIn: confirmed.expiresIn,
  remainingBudgetSats: confirmed.remainingBudgetSats,
  exportCommand: `export LIVEAUTH_JWT='${confirmed.jwt}'`
}, null, 2));
