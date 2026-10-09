import { createPublicKey, createVerify } from 'node:crypto';
import { LiveAuthMcpError } from './errors.js';
import type { McpSignedReceipt, ReceiptKeySet, VerifiedReceipt } from './types.js';

const RECEIPT_VERSION = 'receipt-v2';
const RECEIPT_ALGORITHM = 'ECDSA-P256-SHA256';

export function verifyReceiptV2(
  receipt: McpSignedReceipt,
  keySet: ReceiptKeySet,
  expectations: { issuer?: string; environment?: string } = {}
): VerifiedReceipt {
  if (receipt.version !== RECEIPT_VERSION) throw invalid('unsupported_receipt_version');
  if (receipt.signatureAlgorithm !== RECEIPT_ALGORITHM) throw invalid('unsupported_receipt_algorithm');
  const publicKey = keySet.keys.find((key) => key.keyId === receipt.keyId);
  if (!publicKey) throw invalid('unknown_receipt_key');
  if (publicKey.algorithm !== RECEIPT_ALGORITHM) throw invalid('receipt_key_algorithm_mismatch');

  let signature: Buffer;
  try { signature = Buffer.from(base64UrlDecode(receipt.signature)); }
  catch { throw invalid('malformed_receipt_signature'); }

  let key;
  try {
    key = createPublicKey({ key: Buffer.from(publicKey.publicKey, 'base64'), format: 'der', type: 'spki' });
  } catch {
    throw invalid('malformed_receipt_public_key');
  }
  const verifier = createVerify('SHA256');
  verifier.update(receipt.payload, 'utf8');
  verifier.end();
  if (!verifier.verify({ key, dsaEncoding: 'ieee-p1363' }, signature))
    throw invalid('invalid_receipt_signature');

  let payload: Record<string, unknown>;
  try {
    payload = JSON.parse(Buffer.from(base64UrlDecode(receipt.payload)).toString('utf8')) as Record<string, unknown>;
  } catch {
    throw invalid('malformed_receipt_payload');
  }
  if (payload.version !== RECEIPT_VERSION || payload.algorithm !== RECEIPT_ALGORITHM ||
      payload.signingKeyId !== receipt.keyId)
    throw invalid('receipt_envelope_payload_mismatch');
  if (expectations.issuer !== undefined && payload.issuer !== expectations.issuer)
    throw invalid('receipt_issuer_mismatch');
  if (expectations.environment !== undefined && payload.environment !== expectations.environment)
    throw invalid('receipt_environment_mismatch');
  return { payload, key: publicKey };
}

export async function fetchReceiptKeys(baseUrl: string, fetchImpl: typeof fetch = fetch): Promise<ReceiptKeySet> {
  const response = await fetchImpl(`${baseUrl.replace(/\/+$/, '')}/.well-known/liveauth-receipt-keys.json`);
  if (!response.ok) throw new LiveAuthMcpError('Unable to resolve LiveAuth receipt keys', { status: response.status });
  return await response.json() as ReceiptKeySet;
}

function base64UrlDecode(value: string): Uint8Array {
  if (!/^[A-Za-z0-9_-]+$/.test(value)) throw new Error('invalid base64url');
  const normalized = value.replace(/-/g, '+').replace(/_/g, '/');
  return Buffer.from(normalized + '='.repeat((4 - normalized.length % 4) % 4), 'base64');
}

function invalid(code: string): LiveAuthMcpError {
  return new LiveAuthMcpError(`Invalid LiveAuth receipt: ${code}`, { code });
}
