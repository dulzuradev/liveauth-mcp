import {
  configurationError,
  LiveAuthError,
  responseError
} from './errors.js';
import type {
  CostShieldEnvironment,
  FetchLike
} from './types.js';

const DEFAULT_API_URL = 'https://api.liveauth.app';
const DEFAULT_ISSUER = 'https://api.liveauth.app';
const DEFAULT_AUDIENCE = 'liveauth-costshield';
const MAX_TOKEN_LENGTH = 8 * 1024;

interface JwtHeader {
  alg?: unknown;
  kid?: unknown;
  typ?: unknown;
}

interface JwtPayload {
  iss?: unknown;
  aud?: unknown;
  exp?: unknown;
  nbf?: unknown;
  iat?: unknown;
  jti?: unknown;
  projectId?: unknown;
  projectPublicKey?: unknown;
  protectedActionId?: unknown;
  environment?: unknown;
  action?: unknown;
  origin?: unknown;
  verificationMethod?: unknown;
  difficulty?: unknown;
  clientContextHash?: unknown;
  singleUse?: unknown;
  configurationVersion?: unknown;
  clientSubject?: unknown;
}

interface CostShieldJwk extends JsonWebKey {
  kid?: string;
  use?: string;
  alg?: string;
}

interface CostShieldJwks {
  keys?: CostShieldJwk[];
}

export interface CostShieldVerifierOptions {
  projectId: string;
  environment: CostShieldEnvironment;
  secretKey?: string;
  apiUrl?: string;
  issuer?: string;
  audience?: string;
  fetch?: FetchLike;
  clockToleranceSeconds?: number;
  jwksCacheMilliseconds?: number;
}

export interface CostShieldExpectations {
  action: string;
  origin?: string;
}

export interface CostShieldClaims {
  tokenId: string;
  projectId: string;
  projectPublicKey: string;
  protectedActionId: string;
  environment: CostShieldEnvironment;
  action: string;
  origin?: string;
  verificationMethod: string;
  difficulty: number;
  clientContextHash: string;
  singleUse: boolean;
  configurationVersion: number;
  clientSubject?: string;
  issuedAtUnix: number;
  expiresAtUnix: number;
}

export interface RemoteAuthorizationResult {
  verified: boolean;
  consumed: boolean;
  authorizationId: string;
  action: string;
  environment: CostShieldEnvironment;
  origin: string | null;
  verificationMethod: string;
  expiresAtUnix: number;
  requireSingleUse: boolean;
}

export interface AuthorizedCostShieldRequest {
  claims: CostShieldClaims;
  remote: RemoteAuthorizationResult | null;
}

export interface AuthorizeOptions extends CostShieldExpectations {
  consume?: 'auto' | 'always' | 'never';
  signal?: AbortSignal;
}

export interface ExpressLikeRequest {
  headers: Record<string, string | string[] | undefined>;
  costShield?: AuthorizedCostShieldRequest;
}

export interface ExpressLikeResponse {
  status(code: number): ExpressLikeResponse;
  json(body: unknown): unknown;
}

export type ExpressLikeNext = (error?: unknown) => void;

export interface ProtectMiddlewareOptions {
  origin?:
    | string
    | ((request: ExpressLikeRequest) => string | undefined);
  consume?: 'auto' | 'always' | 'never';
  token?: (request: ExpressLikeRequest) => string | undefined;
}

export class CostShieldVerifier {
  private readonly projectId: string;
  private readonly environment: CostShieldEnvironment;
  private readonly secretKey: string | undefined;
  private readonly apiUrl: string;
  private readonly issuer: string;
  private readonly audience: string;
  private readonly fetcher: FetchLike;
  private readonly clockToleranceSeconds: number;
  private readonly jwksCacheMilliseconds: number;
  private jwksCache:
    | { expiresAt: number; keys: CostShieldJwk[] }
    | undefined;

  constructor(options: CostShieldVerifierOptions) {
    if (!isGuid(options?.projectId)) {
      throw configurationError(
        'invalid_project_id',
        'projectId must be a valid LiveAuth project UUID.'
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

    this.projectId = options.projectId.toLowerCase();
    this.environment = options.environment;
    this.secretKey = options.secretKey?.trim() || undefined;
    this.apiUrl = normalizeApiUrl(options.apiUrl ?? DEFAULT_API_URL);
    this.issuer = options.issuer ?? DEFAULT_ISSUER;
    this.audience = options.audience ?? DEFAULT_AUDIENCE;
    this.fetcher = fetcher;
    this.clockToleranceSeconds = boundedInteger(
      options.clockToleranceSeconds ?? 30,
      0,
      300,
      'clockToleranceSeconds'
    );
    this.jwksCacheMilliseconds = boundedInteger(
      options.jwksCacheMilliseconds ?? 300_000,
      1_000,
      3_600_000,
      'jwksCacheMilliseconds'
    );
  }

  async verify(
    token: string,
    expectations: CostShieldExpectations,
    signal?: AbortSignal
  ): Promise<CostShieldClaims> {
    validateTokenInput(token);
    const action = validateAction(expectations?.action);
    const expectedOrigin = expectations.origin == null
      ? undefined
      : normalizeOrigin(expectations.origin);
    const parsed = parseJwt(token);

    if (
      parsed.header.alg !== 'RS256' ||
      parsed.header.typ !== 'costshield+jwt' ||
      typeof parsed.header.kid !== 'string' ||
      parsed.header.kid.length === 0
    ) {
      throw authorizationError(
        'invalid_token_header',
        'The CostShield token header is invalid.'
      );
    }

    let key = await this.findKey(parsed.header.kid, false, signal);
    if (!key)
      key = await this.findKey(parsed.header.kid, true, signal);
    if (!key) {
      throw authorizationError(
        'unknown_signing_key',
        'The CostShield token signing key is unknown.'
      );
    }

    const cryptoKey = await crypto.subtle.importKey(
      'jwk',
      key,
      {
        name: 'RSASSA-PKCS1-v1_5',
        hash: 'SHA-256'
      },
      false,
      ['verify']
    );
    const signatureValid = await crypto.subtle.verify(
      'RSASSA-PKCS1-v1_5',
      cryptoKey,
      parsed.signature,
      new TextEncoder().encode(parsed.signingInput)
    );
    if (!signatureValid) {
      throw authorizationError(
        'invalid_token_signature',
        'The CostShield token signature is invalid.'
      );
    }

    return validateClaims(
      parsed.payload,
      {
        issuer: this.issuer,
        audience: this.audience,
        projectId: this.projectId,
        environment: this.environment,
        action,
        origin: expectedOrigin,
        clockToleranceSeconds: this.clockToleranceSeconds
      }
    );
  }

  async authorize(
    token: string,
    options: AuthorizeOptions
  ): Promise<AuthorizedCostShieldRequest> {
    const claims = await this.verify(token, options, options.signal);
    const mode = options.consume ?? 'auto';
    const shouldConsume =
      mode === 'always' ||
      (mode === 'auto' && claims.singleUse);

    if (mode === 'never' && claims.singleUse) {
      throw configurationError(
        'single_use_requires_consumption',
        'Single-use CostShield tokens must be consumed remotely.'
      );
    }
    if (!shouldConsume)
      return { claims, remote: null };
    if (!this.secretKey) {
      throw configurationError(
        'missing_secret_key',
        'secretKey is required to consume CostShield tokens.'
      );
    }

    const remote = await this.remoteAuthorization(
      token,
      options,
      options.signal
    );
    return { claims, remote };
  }

  protect(
    action: string,
    options: ProtectMiddlewareOptions = {}
  ): (
    request: ExpressLikeRequest,
    response: ExpressLikeResponse,
    next: ExpressLikeNext
  ) => Promise<void> {
    const expectedAction = validateAction(action);
    return async (request, response, next) => {
      const token = options.token?.(request) ??
        bearerToken(request.headers.authorization);
      if (!token) {
        response.status(401).json({
          error: 'missing_authorization',
          error_description:
            'Provide a CostShield bearer token in Authorization.'
        });
        return;
      }

      try {
        const origin = typeof options.origin === 'function'
          ? options.origin(request)
          : options.origin;
        request.costShield = await this.authorize(token, {
          action: expectedAction,
          ...(origin == null ? {} : { origin }),
          ...(options.consume == null
            ? {}
            : { consume: options.consume })
        });
        next();
      } catch (error) {
        if (!(error instanceof LiveAuthError)) {
          next(error);
          return;
        }
        response.status(statusForError(error)).json({
          error: error.code,
          error_description: error.message
        });
      }
    };
  }

  async authorizeRequest(
    request: Request,
    options: AuthorizeOptions
  ): Promise<AuthorizedCostShieldRequest> {
    const token = bearerToken(request.headers.get('Authorization'));
    if (!token) {
      throw new LiveAuthError(
        'Provide a CostShield bearer token in Authorization.',
        {
          code: 'missing_authorization',
          status: 401
        }
      );
    }
    return this.authorize(token, options);
  }

  private async remoteAuthorization(
    token: string,
    expectations: CostShieldExpectations,
    signal: AbortSignal | undefined
  ): Promise<RemoteAuthorizationResult> {
    let response: Response;
    try {
      response = await this.fetcher(
        `${this.apiUrl}/api/costshield/authorizations/consume`,
        {
          method: 'POST',
          headers: {
            Authorization: `Bearer ${this.secretKey}`,
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({
            token,
            action: expectations.action,
            environment: this.environment,
            origin: expectations.origin
          }),
          ...(signal == null ? {} : { signal })
        }
      );
    } catch (error) {
      throw new LiveAuthError(
        'Unable to reach LiveAuth for token consumption.',
        {
          code: 'network_error',
          retryable: true,
          cause: error
        }
      );
    }

    if (!response.ok)
      throw await responseError(response);
    const result = await response.json() as RemoteAuthorizationResult;
    if (
      result?.verified !== true ||
      result.action !== expectations.action ||
      result.environment !== this.environment
    ) {
      throw authorizationError(
        'invalid_verification_response',
        'LiveAuth returned an invalid verification response.'
      );
    }
    return result;
  }

  private async findKey(
    keyId: string,
    forceRefresh: boolean,
    signal: AbortSignal | undefined
  ): Promise<CostShieldJwk | undefined> {
    const keys = await this.getJwks(forceRefresh, signal);
    return keys.find(key =>
      key.kid === keyId &&
      key.kty === 'RSA' &&
      key.use === 'sig' &&
      key.alg === 'RS256'
    );
  }

  private async getJwks(
    forceRefresh: boolean,
    signal: AbortSignal | undefined
  ): Promise<CostShieldJwk[]> {
    if (
      !forceRefresh &&
      this.jwksCache &&
      this.jwksCache.expiresAt > Date.now()
    ) {
      return this.jwksCache.keys;
    }

    let response: Response;
    try {
      response = await this.fetcher(
        `${this.apiUrl}/api/public/costshield/.well-known/jwks.json`,
        {
          headers: { Accept: 'application/json' },
          ...(signal == null ? {} : { signal })
        }
      );
    } catch (error) {
      throw new LiveAuthError(
        'Unable to load LiveAuth signing keys.',
        {
          code: 'jwks_unavailable',
          retryable: true,
          cause: error
        }
      );
    }
    if (!response.ok)
      throw await responseError(response);

    const body = await response.json() as CostShieldJwks;
    if (!Array.isArray(body.keys) || body.keys.length === 0) {
      throw new LiveAuthError(
        'LiveAuth returned an invalid signing-key response.',
        { code: 'invalid_jwks' }
      );
    }
    this.jwksCache = {
      keys: body.keys,
      expiresAt: Date.now() + this.jwksCacheMilliseconds
    };
    return body.keys;
  }
}

function validateClaims(
  payload: JwtPayload,
  expected: {
    issuer: string;
    audience: string;
    projectId: string;
    environment: CostShieldEnvironment;
    action: string;
    origin: string | undefined;
    clockToleranceSeconds: number;
  }
): CostShieldClaims {
  const now = Math.floor(Date.now() / 1000);
  const expiresAt = requiredInteger(payload.exp, 'exp');
  const issuedAt = requiredInteger(payload.iat, 'iat');
  const notBefore = payload.nbf == null
    ? undefined
    : requiredInteger(payload.nbf, 'nbf');

  if (expiresAt < now - expected.clockToleranceSeconds) {
    throw authorizationError(
      'token_expired',
      'The CostShield token has expired.'
    );
  }
  if (
    notBefore != null &&
    notBefore > now + expected.clockToleranceSeconds
  ) {
    throw authorizationError(
      'token_not_active',
      'The CostShield token is not active yet.'
    );
  }
  if (payload.iss !== expected.issuer) {
    throw authorizationError(
      'issuer_mismatch',
      'The CostShield token issuer is invalid.'
    );
  }
  const audiences = typeof payload.aud === 'string'
    ? [payload.aud]
    : Array.isArray(payload.aud)
      ? payload.aud
      : [];
  if (!audiences.includes(expected.audience)) {
    throw authorizationError(
      'audience_mismatch',
      'The CostShield token audience is invalid.'
    );
  }

  const projectId = requiredString(payload.projectId, 'projectId')
    .toLowerCase();
  const environment = requiredString(
    payload.environment,
    'environment'
  );
  const action = requiredString(payload.action, 'action');
  const origin = payload.origin == null
    ? undefined
    : normalizeOrigin(requiredString(payload.origin, 'origin'));

  if (projectId !== expected.projectId) {
    throw authorizationError(
      'project_mismatch',
      'The token is not valid for this LiveAuth project.'
    );
  }
  if (environment !== expected.environment) {
    throw authorizationError(
      'environment_mismatch',
      'The token is not valid for this environment.'
    );
  }
  if (action !== expected.action) {
    throw authorizationError(
      'action_mismatch',
      'The token is not valid for this protected action.'
    );
  }
  if (expected.origin != null && origin !== expected.origin) {
    throw authorizationError(
      'origin_mismatch',
      'The token is not valid for the expected origin.'
    );
  }

  const subject = payload.clientSubject == null
    ? undefined
    : requiredString(payload.clientSubject, 'clientSubject');
  return {
    tokenId: requiredString(payload.jti, 'jti'),
    projectId,
    projectPublicKey: requiredString(
      payload.projectPublicKey,
      'projectPublicKey'
    ),
    protectedActionId: requiredString(
      payload.protectedActionId,
      'protectedActionId'
    ),
    environment: environment as CostShieldEnvironment,
    action,
    ...(origin == null ? {} : { origin }),
    verificationMethod: requiredString(
      payload.verificationMethod,
      'verificationMethod'
    ),
    difficulty: requiredInteger(payload.difficulty, 'difficulty'),
    clientContextHash: requiredString(
      payload.clientContextHash,
      'clientContextHash'
    ),
    singleUse: requiredBoolean(payload.singleUse, 'singleUse'),
    configurationVersion: requiredInteger(
      payload.configurationVersion,
      'configurationVersion'
    ),
    ...(subject == null ? {} : { clientSubject: subject }),
    issuedAtUnix: issuedAt,
    expiresAtUnix: expiresAt
  };
}

function parseJwt(token: string): {
  header: JwtHeader;
  payload: JwtPayload;
  signingInput: string;
  signature: Uint8Array<ArrayBuffer>;
} {
  const parts = token.split('.');
  if (parts.length !== 3 || parts.some(part => part.length === 0)) {
    throw authorizationError(
      'invalid_token',
      'The CostShield token is malformed.'
    );
  }
  try {
    const headerPart = parts[0]!;
    const payloadPart = parts[1]!;
    return {
      header: JSON.parse(
        new TextDecoder().decode(base64UrlDecode(headerPart))
      ) as JwtHeader,
      payload: JSON.parse(
        new TextDecoder().decode(base64UrlDecode(payloadPart))
      ) as JwtPayload,
      signingInput: `${headerPart}.${payloadPart}`,
      signature: base64UrlDecode(parts[2]!)
    };
  } catch (error) {
    throw new LiveAuthError(
      'The CostShield token is malformed.',
      {
        code: 'invalid_token',
        status: 401,
        cause: error
      }
    );
  }
}

function base64UrlDecode(value: string): Uint8Array<ArrayBuffer> {
  if (!/^[A-Za-z0-9_-]+$/.test(value))
    throw new Error('Invalid base64url value.');
  const base64 = value
    .replace(/-/g, '+')
    .replace(/_/g, '/')
    .padEnd(Math.ceil(value.length / 4) * 4, '=');
  const decoded = atob(base64);
  const output = new Uint8Array(new ArrayBuffer(decoded.length));
  for (let index = 0; index < decoded.length; index++)
    output[index] = decoded.charCodeAt(index);
  return output;
}

function bearerToken(
  header: string | string[] | null | undefined
): string | undefined {
  const value = Array.isArray(header) ? header[0] : header;
  if (typeof value !== 'string')
    return undefined;
  const match = /^Bearer\s+(\S+)$/i.exec(value.trim());
  return match?.[1];
}

function validateTokenInput(token: string): void {
  if (
    typeof token !== 'string' ||
    token.length === 0 ||
    token.length > MAX_TOKEN_LENGTH
  ) {
    throw authorizationError(
      'invalid_token',
      'The CostShield token is missing or too large.'
    );
  }
}

function validateAction(value: string): string {
  const action = value?.trim();
  if (!action || action.length > 100) {
    throw configurationError(
      'invalid_action',
      'action is required and must be 100 characters or less.'
    );
  }
  return action;
}

function requiredString(value: unknown, claim: string): string {
  if (typeof value !== 'string' || value.length === 0) {
    throw authorizationError(
      'invalid_token_claims',
      `The CostShield token is missing the ${claim} claim.`
    );
  }
  return value;
}

function requiredInteger(value: unknown, claim: string): number {
  const number = typeof value === 'string' && value.length > 0
    ? Number(value)
    : value;
  if (typeof number !== 'number' || !Number.isInteger(number)) {
    throw authorizationError(
      'invalid_token_claims',
      `The CostShield token has an invalid ${claim} claim.`
    );
  }
  return number;
}

function requiredBoolean(value: unknown, claim: string): boolean {
  if (value === true || value === 'true')
    return true;
  if (value === false || value === 'false')
    return false;
  throw authorizationError(
    'invalid_token_claims',
    `The CostShield token has an invalid ${claim} claim.`
  );
}

function authorizationError(
  code: string,
  message: string
): LiveAuthError {
  return new LiveAuthError(message, {
    code,
    status: code.endsWith('_mismatch') ? 403 : 401
  });
}

function statusForError(error: LiveAuthError): number {
  if (error.status != null)
    return error.status;
  if (
    error.code === 'network_error' ||
    error.code === 'jwks_unavailable'
  ) {
    return 503;
  }
  if (
    error.code === 'missing_secret_key' ||
    error.code === 'single_use_requires_consumption'
  ) {
    return 500;
  }
  return 401;
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

function isGuid(value: unknown): value is string {
  return typeof value === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
      .test(value);
}
