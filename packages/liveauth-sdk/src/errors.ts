import type { CostShieldApiErrorBody } from './types.js';

export class LiveAuthError extends Error {
  readonly code: string;
  readonly status: number | undefined;
  readonly retryAfterSeconds: number | undefined;
  readonly retryable: boolean;
  readonly details: unknown;

  constructor(
    message: string,
    options: {
      code: string;
      status?: number;
      retryAfterSeconds?: number;
      retryable?: boolean;
      details?: unknown;
      cause?: unknown;
    }
  ) {
    super(message, { cause: options.cause });
    this.name = 'LiveAuthError';
    this.code = options.code;
    this.status = options.status;
    this.retryAfterSeconds = options.retryAfterSeconds;
    this.retryable = options.retryable ?? false;
    this.details = options.details;
  }
}

export async function responseError(
  response: Response
): Promise<LiveAuthError> {
  let body: CostShieldApiErrorBody = {};
  try {
    body = await response.json() as CostShieldApiErrorBody;
  } catch {
    // Preserve the HTTP status when the response is not JSON.
  }

  const code = typeof body.error === 'string'
    ? body.error
    : `http_${response.status}`;
  const message =
    typeof body.error_description === 'string'
      ? body.error_description
      : typeof body.message === 'string'
        ? body.message
        : `LiveAuth request failed with status ${response.status}.`;
  const retryAfterRaw = response.headers.get('Retry-After');
  const retryAfter = retryAfterRaw == null
    ? undefined
    : Number.parseInt(retryAfterRaw, 10);

  return new LiveAuthError(message, {
    code,
    status: response.status,
    ...(typeof retryAfter === 'number' && Number.isFinite(retryAfter)
      ? { retryAfterSeconds: retryAfter }
      : {}),
    retryable:
      response.status === 408 ||
      response.status === 429 ||
      response.status >= 500 ||
      code === 'challenge_expired',
    details: body
  });
}

export function configurationError(
  code: string,
  message: string
): LiveAuthError {
  return new LiveAuthError(message, { code });
}
