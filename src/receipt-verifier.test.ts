import { createSign, generateKeyPairSync } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { verifyReceiptV2 } from './receipt-verifier.js';
import type { McpSignedReceipt, ReceiptKeySet } from './types.js';

function fixture(keyId = 'key-a') {
  const pair = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  const payloadObject = {
    version: 'receipt-v2', algorithm: 'ECDSA-P256-SHA256', signingKeyId: keyId,
    issuer: 'LiveAuth Test', environment: 'TEST', amountDebitedSats: 5,
    toolSlug: 'inspect', requestHash: 'a'.repeat(64), resultHash: 'b'.repeat(64)
  };
  const payload = Buffer.from(JSON.stringify(payloadObject)).toString('base64url');
  const signer = createSign('SHA256');
  signer.update(payload, 'utf8');
  signer.end();
  const signature = signer.sign({ key: pair.privateKey, dsaEncoding: 'ieee-p1363' }).toString('base64url');
  const receipt = {
    version: 'receipt-v2', payload, signature, signatureAlgorithm: 'ECDSA-P256-SHA256',
    keyId, body: {} as never, presentationDataAuthenticated: false
  } satisfies McpSignedReceipt;
  const keySet = {
    version: 'receipt-key-set-v1',
    keys: [{ keyId, algorithm: 'ECDSA-P256-SHA256',
      publicKey: pair.publicKey.export({ format: 'der', type: 'spki' }).toString('base64'), status: 'active' }]
  } satisfies ReceiptKeySet;
  return { receipt, keySet, payloadObject };
}

describe('receipt-v2 verification', () => {
  it('verifies before returning the parsed payload and validates expectations', () => {
    const { receipt, keySet } = fixture();
    expect(verifyReceiptV2(receipt, keySet, { issuer: 'LiveAuth Test', environment: 'TEST' }).payload)
      .toMatchObject({ amountDebitedSats: 5, toolSlug: 'inspect' });
  });

  it.each([
    ['one-byte payload mutation', (r: McpSignedReceipt) => ({ ...r, payload: `${r.payload.slice(0, -1)}A` })],
    ['altered amount', (r: McpSignedReceipt) => replacePayload(r, { amountDebitedSats: 6 })],
    ['altered tool', (r: McpSignedReceipt) => replacePayload(r, { toolSlug: 'other' })],
    ['altered argument hash', (r: McpSignedReceipt) => replacePayload(r, { requestHash: 'c'.repeat(64) })],
    ['altered result hash', (r: McpSignedReceipt) => replacePayload(r, { resultHash: 'd'.repeat(64) })],
  ])('rejects %s', (_name, mutate) => {
    const { receipt, keySet } = fixture();
    expect(() => verifyReceiptV2(mutate(receipt), keySet)).toThrow(/invalid_receipt_signature/);
  });

  it('rejects wrong keys, unknown key IDs, unsupported algorithms, and malformed signatures', () => {
    const { receipt, keySet } = fixture();
    const wrong = fixture('key-a').keySet;
    expect(() => verifyReceiptV2(receipt, wrong)).toThrow(/invalid_receipt_signature/);
    expect(() => verifyReceiptV2({ ...receipt, keyId: 'unknown' }, keySet)).toThrow(/unknown_receipt_key/);
    expect(() => verifyReceiptV2({ ...receipt, signatureAlgorithm: 'none' }, keySet)).toThrow(/unsupported_receipt_algorithm/);
    expect(() => verifyReceiptV2({ ...receipt, signature: '*' }, keySet)).toThrow(/malformed_receipt_signature/);
  });

  it('verifies old receipts after rotation while accepting the new active key', () => {
    const old = fixture('old');
    const current = fixture('current');
    const rotated = { version: 'receipt-key-set-v1', keys: [
      { ...old.keySet.keys[0]!, status: 'retired' }, current.keySet.keys[0]!
    ] } satisfies ReceiptKeySet;
    expect(verifyReceiptV2(old.receipt, rotated).key.status).toBe('retired');
    expect(verifyReceiptV2(current.receipt, rotated).key.status).toBe('active');
  });
});

function replacePayload(receipt: McpSignedReceipt, updates: Record<string, unknown>): McpSignedReceipt {
  const parsed = JSON.parse(Buffer.from(receipt.payload, 'base64url').toString('utf8'));
  return { ...receipt, payload: Buffer.from(JSON.stringify({ ...parsed, ...updates })).toString('base64url') };
}
