import {
  configurationError,
  LiveAuthError,
  responseError
} from './errors.js';
import type {
  CostShieldAuthorization,
  CostShieldAuthorizationResult,
  CostShieldChallenge,
  FetchLike,
  LiveAuthOptions,
  PowSolution,
  PowSolver,
  PowSolverOptions,
  ProtectOptions,
  WorkerFactory,
  WorkerLike
} from './types.js';

const DEFAULT_API_URL = 'https://api.liveauth.app';
const DEFAULT_TIMEOUT_MILLISECONDS = 20_000;

interface WorkerProgressMessage {
  type: 'progress';
  attempts: number;
  nonce: number;
  elapsedMilliseconds: number;
}

interface WorkerSolvedMessage {
  type: 'solved';
  nonce: number;
  hashHex: string;
  attempts: number;
  elapsedMilliseconds: number;
}

interface WorkerErrorMessage {
  type: 'error';
  error: string;
}

type WorkerMessage =
  | WorkerProgressMessage
  | WorkerSolvedMessage
  | WorkerErrorMessage;

export class LiveAuth {
  private readonly publicKey: string;
  private readonly environment: 'TEST' | 'LIVE';
  private readonly apiUrl: string;
  private readonly configuredOrigin: string | undefined;
  private readonly fetcher: FetchLike;
  private readonly powSolver: PowSolver;
  private readonly timeoutMilliseconds: number;
  private readonly challengeRetries: number;

  constructor(options: LiveAuthOptions) {
    if (!options?.publicKey?.trim()) {
      throw configurationError(
        'missing_public_key',
        'A LiveAuth project public key is required.'
      );
    }
    if (options.environment !== 'TEST' && options.environment !== 'LIVE') {
      throw configurationError(
        'invalid_environment',
        'environment must be TEST or LIVE.'
      );
    }

    const fetcher = options.fetch ?? globalThis.fetch?.bind(globalThis);
    if (!fetcher) {
      throw configurationError(
        'fetch_unavailable',
        'A Fetch API implementation is required.'
      );
    }

    this.publicKey = options.publicKey.trim();
    this.environment = options.environment;
    this.apiUrl = normalizeApiUrl(options.apiUrl ?? DEFAULT_API_URL);
    this.configuredOrigin = options.origin == null
      ? undefined
      : normalizeOrigin(options.origin);
    this.fetcher = fetcher;
    this.timeoutMilliseconds = boundedInteger(
      options.requestTimeoutMilliseconds ??
        DEFAULT_TIMEOUT_MILLISECONDS,
      1_000,
      120_000,
      'requestTimeoutMilliseconds'
    );
    this.challengeRetries = boundedInteger(
      options.challengeRetries ?? 1,
      0,
      3,
      'challengeRetries'
    );
    this.powSolver = options.powSolver ??
      createWorkerPowSolver(options.workerFactory);
  }

  async protect(
    options: ProtectOptions
  ): Promise<CostShieldAuthorizationResult> {
    const action = options?.action?.trim();
    if (!action || action.length > 100) {
      throw configurationError(
        'invalid_action',
        'action is required and must be 100 characters or less.'
      );
    }

    const origin = resolveOrigin(
      options.origin,
      this.configuredOrigin
    );
    let lastExpirationError: LiveAuthError | undefined;

    for (
      let attempt = 0;
      attempt <= this.challengeRetries;
      attempt++
    ) {
      const challenge = await this.createChallenge(
        action,
        origin,
        options
      );

      if (challenge.expiresAtUnix <= currentUnixSeconds()) {
        lastExpirationError = new LiveAuthError(
          'The CostShield challenge expired before it could be solved.',
          {
            code: 'challenge_expired',
            retryable: true
          }
        );
        continue;
      }

      const solution = await this.powSolver({
        challenge,
        ...(options.signal == null
          ? {}
          : { signal: options.signal }),
        ...(options.onProgress == null
          ? {}
          : { onProgress: options.onProgress })
      });

      if (challenge.expiresAtUnix <= currentUnixSeconds()) {
        lastExpirationError = new LiveAuthError(
          'The CostShield challenge expired while it was being solved.',
          {
            code: 'challenge_expired',
            retryable: true
          }
        );
        continue;
      }

      try {
        const authorization = await this.completeChallenge(
          challenge,
          solution,
          origin,
          options.subject,
          options.signal
        );
        return {
          ...authorization,
          difficultyBits: challenge.difficultyBits,
          difficultyReason: challenge.difficultyReason,
          solveMilliseconds: solution.elapsedMilliseconds
        };
      } catch (error) {
        if (
          error instanceof LiveAuthError &&
          error.code === 'challenge_expired' &&
          attempt < this.challengeRetries
        ) {
          lastExpirationError = error;
          continue;
        }
        throw error;
      }
    }

    throw lastExpirationError ?? new LiveAuthError(
      'Unable to obtain a current CostShield challenge.',
      {
        code: 'challenge_expired',
        retryable: true
      }
    );
  }

  private async createChallenge(
    action: string,
    origin: string | undefined,
    options: ProtectOptions
  ): Promise<CostShieldChallenge> {
    const challenge = await this.postJson<CostShieldChallenge>(
      '/api/public/costshield/challenges',
      {
        projectPublicKey: this.publicKey,
        environment: this.environment,
        action,
        origin,
        subject: options.subject,
        riskHint: options.riskHint,
        clientMetadata: options.clientMetadata
      },
      options.signal
    );

    validateChallenge(
      challenge,
      this.publicKey,
      this.environment,
      action
    );
    return challenge;
  }

  private async completeChallenge(
    challenge: CostShieldChallenge,
    solution: PowSolution,
    origin: string | undefined,
    subject: string | undefined,
    signal: AbortSignal | undefined
  ): Promise<CostShieldAuthorization> {
    const authorization = await this.postJson<CostShieldAuthorization>(
      `/api/public/costshield/challenges/${
        encodeURIComponent(challenge.challengeId)
      }/complete`,
      {
        projectPublicKey: this.publicKey,
        environment: challenge.environment,
        action: challenge.action,
        origin,
        subject,
        nonce: solution.nonce,
        difficultyBits: challenge.difficultyBits,
        expiresAtUnix: challenge.expiresAtUnix,
        configurationVersion: challenge.configurationVersion,
        signature: challenge.signature
      },
      signal
    );

    if (
      !authorization?.token ||
      authorization.action !== challenge.action ||
      authorization.environment !== challenge.environment
    ) {
      throw new LiveAuthError(
        'LiveAuth returned an invalid authorization response.',
        { code: 'invalid_authorization_response' }
      );
    }
    return authorization;
  }

  private async postJson<T>(
    path: string,
    body: unknown,
    signal: AbortSignal | undefined
  ): Promise<T> {
    const requestAbort = createRequestAbort(
      signal,
      this.timeoutMilliseconds
    );
    try {
      const response = await this.fetcher(`${this.apiUrl}${path}`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-LW-Public': this.publicKey
        },
        body: JSON.stringify(body),
        signal: requestAbort.signal
      });

      if (!response.ok)
        throw await responseError(response);
      return await response.json() as T;
    } catch (error) {
      if (error instanceof LiveAuthError)
        throw error;
      if (requestAbort.signal.aborted) {
        const externallyAborted = signal?.aborted === true;
        throw new LiveAuthError(
          externallyAborted
            ? 'The CostShield request was cancelled.'
            : 'The CostShield request timed out.',
          {
            code: externallyAborted ? 'aborted' : 'request_timeout',
            retryable: !externallyAborted,
            cause: error
          }
        );
      }
      throw new LiveAuthError(
        'Unable to reach the LiveAuth API.',
        {
          code: 'network_error',
          retryable: true,
          cause: error
        }
      );
    } finally {
      requestAbort.cleanup();
    }
  }
}

export function createWorkerPowSolver(
  workerFactory?: WorkerFactory
): PowSolver {
  return (options: PowSolverOptions) => new Promise(
    (resolve, reject) => {
      const workerUrl = new URL('./pow-worker.js', import.meta.url);
      let worker: WorkerLike;
      try {
        worker = workerFactory
          ? workerFactory(workerUrl)
          : defaultWorkerFactory(workerUrl);
      } catch (error) {
        reject(error);
        return;
      }

      const abort = () => {
        cleanup();
        reject(new LiveAuthError(
          'The proof-of-work operation was cancelled.',
          { code: 'aborted' }
        ));
      };
      const cleanup = () => {
        options.signal?.removeEventListener('abort', abort);
        worker.terminate();
      };

      worker.onmessage = ({ data }) => {
        if (!isWorkerMessage(data))
          return;

        if (data.type === 'progress') {
          options.onProgress?.({
            attempts: data.attempts,
            nonce: data.nonce,
            elapsedMilliseconds: data.elapsedMilliseconds,
            difficultyBits: options.challenge.difficultyBits
          });
          return;
        }

        cleanup();
        if (data.type === 'error') {
          reject(new LiveAuthError(data.error, {
            code: 'pow_worker_error'
          }));
          return;
        }

        resolve({
          nonce: data.nonce,
          hashHex: data.hashHex,
          attempts: data.attempts,
          elapsedMilliseconds: data.elapsedMilliseconds
        });
      };
      worker.onerror = event => {
        cleanup();
        reject(new LiveAuthError(
          event.message ?? 'The proof-of-work worker failed.',
          { code: 'pow_worker_error' }
        ));
      };

      if (options.signal?.aborted) {
        abort();
        return;
      }
      options.signal?.addEventListener('abort', abort, { once: true });
      worker.postMessage({
        type: 'solve',
        projectPublicKey: options.challenge.projectPublicKey,
        challengeId: options.challenge.challengeId,
        targetHex: options.challenge.targetHex
      });
    }
  );
}

function defaultWorkerFactory(workerUrl: URL): WorkerLike {
  if (typeof Worker === 'undefined') {
    throw configurationError(
      'worker_unavailable',
      'Web Workers are unavailable. Provide a powSolver for this runtime.'
    );
  }
  return new Worker(workerUrl, {
    type: 'module',
    name: 'liveauth-costshield-pow'
  });
}

function validateChallenge(
  challenge: CostShieldChallenge,
  publicKey: string,
  environment: string,
  action: string
): void {
  const valid =
    challenge != null &&
    challenge.projectPublicKey === publicKey &&
    challenge.environment === environment &&
    challenge.action === action &&
    /^[a-f0-9]{32}$/.test(challenge.challengeId) &&
    /^[a-f0-9]{64}$/.test(challenge.targetHex) &&
    Number.isInteger(challenge.difficultyBits) &&
    Number.isInteger(challenge.expiresAtUnix) &&
    Number.isInteger(challenge.configurationVersion) &&
    typeof challenge.signature === 'string' &&
    challenge.signature.length > 0;

  if (!valid) {
    throw new LiveAuthError(
      'LiveAuth returned an invalid challenge response.',
      { code: 'invalid_challenge_response' }
    );
  }
}

function createRequestAbort(
  externalSignal: AbortSignal | undefined,
  timeoutMilliseconds: number
): {
  signal: AbortSignal;
  cleanup: () => void;
} {
  const controller = new AbortController();
  const abortFromExternal = () =>
    controller.abort(externalSignal?.reason);
  if (externalSignal?.aborted)
    abortFromExternal();
  else
    externalSignal?.addEventListener(
      'abort',
      abortFromExternal,
      { once: true }
    );

  const timeout = setTimeout(
    () => controller.abort('timeout'),
    timeoutMilliseconds
  );
  return {
    signal: controller.signal,
    cleanup: () => {
      clearTimeout(timeout);
      externalSignal?.removeEventListener(
        'abort',
        abortFromExternal
      );
    }
  };
}

function resolveOrigin(
  requested: string | undefined,
  configured: string | undefined
): string | undefined {
  if (requested != null)
    return normalizeOrigin(requested);
  if (configured != null)
    return configured;
  if (typeof globalThis.location?.origin === 'string')
    return normalizeOrigin(globalThis.location.origin);
  return undefined;
}

function normalizeOrigin(value: string): string {
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw configurationError(
      'invalid_origin',
      'origin must be an absolute HTTP or HTTPS origin.'
    );
  }
  if (
    (url.protocol !== 'http:' && url.protocol !== 'https:') ||
    url.username ||
    url.password ||
    url.pathname !== '/' ||
    url.search ||
    url.hash
  ) {
    throw configurationError(
      'invalid_origin',
      'origin must be an absolute HTTP or HTTPS origin.'
    );
  }
  return url.origin;
}

function normalizeApiUrl(value: string): string {
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw configurationError(
      'invalid_api_url',
      'apiUrl must be an absolute HTTP or HTTPS URL.'
    );
  }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw configurationError(
      'invalid_api_url',
      'apiUrl must be an absolute HTTP or HTTPS URL.'
    );
  }
  return url.toString().replace(/\/+$/, '');
}

function boundedInteger(
  value: number,
  minimum: number,
  maximum: number,
  name: string
): number {
  if (
    !Number.isInteger(value) ||
    value < minimum ||
    value > maximum
  ) {
    throw configurationError(
      `invalid_${name}`,
      `${name} must be an integer from ${minimum} to ${maximum}.`
    );
  }
  return value;
}

function currentUnixSeconds(): number {
  return Math.floor(Date.now() / 1000);
}

function isWorkerMessage(value: unknown): value is WorkerMessage {
  if (value == null || typeof value !== 'object')
    return false;
  const type = (value as { type?: unknown }).type;
  return type === 'progress' || type === 'solved' || type === 'error';
}
