export interface WalletPayment {
  preimage: string;
  amountPaidSats: number;
}

export interface L402WalletAdapter {
  payInvoice(invoice: string, options: { maxSats: number; signal?: AbortSignal }): Promise<WalletPayment>;
}

export interface LiveAuthFetchOptions extends RequestInit {
  maxSats: number;
  wallet: L402WalletAdapter;
  credentialCache?: L402CredentialCache;
}

export interface LiveAuthReceiptMetadata {
  id: string;
  signature?: string;
  requestId?: string;
}

export type LiveAuthResponse = Response & { readonly liveAuthReceipt?: LiveAuthReceiptMetadata };

export interface CachedL402Credential {
  macaroon: string;
  preimage: string;
}

export interface L402CredentialCache {
  get(key: string): CachedL402Credential | undefined;
  set(key: string, credential: CachedL402Credential): void;
  delete(key: string): void;
}

export class MemoryCredentialCache implements L402CredentialCache {
  private readonly values = new Map<string, CachedL402Credential>();
  get(key: string) { return this.values.get(key); }
  set(key: string, credential: CachedL402Credential) { this.values.set(key, credential); }
  delete(key: string) { this.values.delete(key); }
}

export class L402ChallengeError extends Error { constructor(message: string) { super(message); this.name = 'L402ChallengeError'; } }
export class L402MaxSpendError extends Error {
  constructor(public readonly invoiceAmountSats: number, public readonly maxSats: number) {
    super(`L402 invoice requests ${invoiceAmountSats} sats, above maxSats ${maxSats}.`);
    this.name = 'L402MaxSpendError';
  }
}
export class L402PaymentError extends Error { constructor(message: string, options?: ErrorOptions) { super(message, options); this.name = 'L402PaymentError'; } }
export class L402RetryError extends Error { constructor(message: string) { super(message); this.name = 'L402RetryError'; } }

const defaultCache = new MemoryCredentialCache();

export async function liveAuthFetch(input: string | URL, options: LiveAuthFetchOptions): Promise<LiveAuthResponse> {
  const { maxSats, wallet, credentialCache = defaultCache, ...requestInit } = options;
  if (!Number.isSafeInteger(maxSats) || maxSats < 0) throw new TypeError('maxSats must be a non-negative safe integer.');
  if (!wallet || typeof wallet.payInvoice !== 'function') throw new TypeError('A wallet adapter is required.');

  const url = new URL(input.toString());
  const method = (requestInit.method ?? 'GET').toUpperCase();
  const cacheKey = `${method} ${url.href}`;
  const cached = credentialCache.get(cacheKey);
  let response = await send(url, requestInit, cached);
  if (cached && response.status === 401) {
    credentialCache.delete(cacheKey);
    response = await send(url, requestInit);
  }
  if (response.status !== 402) return attachReceipt(response);
  if (cached) credentialCache.delete(cacheKey);

  const challenge = parseChallenge(response.headers.get('www-authenticate'));
  const amount = parseAmount(response.headers.get('x-liveauth-price-sats'), challenge.invoice);
  if (amount > maxSats) throw new L402MaxSpendError(amount, maxSats);

  let payment: WalletPayment;
  try { payment = await wallet.payInvoice(challenge.invoice, { maxSats, signal: requestInit.signal ?? undefined }); }
  catch (error) { throw new L402PaymentError('Wallet failed to pay the L402 invoice.', { cause: error }); }
  if (!payment.preimage || !Number.isSafeInteger(payment.amountPaidSats) || payment.amountPaidSats > maxSats)
    throw new L402PaymentError('Wallet returned an invalid or over-limit payment result.');

  const credential = { macaroon: challenge.macaroon, preimage: payment.preimage };
  response = await send(url, requestInit, credential);
  if (response.status === 402) throw new L402RetryError('Gateway still requires payment after one paid retry.');
  if (response.status !== 401) credentialCache.set(cacheKey, credential);
  return attachReceipt(response);
}

function send(url: URL, init: RequestInit, credential?: CachedL402Credential): Promise<Response> {
  const headers = new Headers(init.headers);
  if (credential) headers.set('Authorization', `L402 ${credential.macaroon}:${credential.preimage}`);
  // A fresh Request preserves retryable string/Buffer/Blob bodies. Callers using a
  // one-shot ReadableStream should materialize it before calling liveAuthFetch.
  return fetch(new Request(url, { ...init, headers }));
}

function parseChallenge(value: string | null): { macaroon: string; invoice: string } {
  if (!value || !/^L402\s/i.test(value)) throw new L402ChallengeError('402 response did not include an L402 challenge.');
  const macaroon = /macaroon="([^"]+)"/i.exec(value)?.[1];
  const invoice = /invoice="([^"]+)"/i.exec(value)?.[1];
  if (!macaroon || !invoice) throw new L402ChallengeError('L402 challenge is missing macaroon or invoice.');
  return { macaroon, invoice };
}

function parseAmount(header: string | null, invoice: string): number {
  if (header && /^\d+$/.test(header)) return Number(header);
  const match = /^ln(?:bc|tb|bcrt)(\d+)([munp]?)/i.exec(invoice);
  if (!match) throw new L402ChallengeError('Cannot determine invoice amount; refusing to pay.');
  const value = BigInt(match[1]);
  const divisor = ({ '': 1n, m: 1_000n, u: 1_000_000n, n: 1_000_000_000n, p: 1_000_000_000_000n } as const)[match[2].toLowerCase() as '' | 'm' | 'u' | 'n' | 'p'];
  const millisats = value * 100_000_000_000n / divisor;
  return Number((millisats + 999n) / 1000n);
}

function attachReceipt(response: Response): LiveAuthResponse {
  const id = response.headers.get('x-liveauth-receipt-id');
  if (!id) return response as LiveAuthResponse;
  Object.defineProperty(response, 'liveAuthReceipt', { enumerable: true, value: {
    id, signature: response.headers.get('x-liveauth-receipt-signature') ?? undefined,
    requestId: response.headers.get('x-liveauth-request-id') ?? undefined
  }});
  return response as LiveAuthResponse;
}

export class MockWalletAdapter implements L402WalletAdapter {
  constructor(private readonly payment: WalletPayment | ((invoice: string) => WalletPayment | Promise<WalletPayment>)) {}
  async payInvoice(invoice: string, options: { maxSats: number }): Promise<WalletPayment> {
    const result = typeof this.payment === 'function' ? await this.payment(invoice) : this.payment;
    if (result.amountPaidSats > options.maxSats) throw new L402MaxSpendError(result.amountPaidSats, options.maxSats);
    return result;
  }
}
