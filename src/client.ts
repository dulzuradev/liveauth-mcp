import { LiveAuthMcpError } from './errors.js';
import { cleanBaseUrl, projectHeaders, requestJson, requireFetch } from './http.js';
import { solvePow } from './pow.js';
import type {
  AuthSession,
  LiveAuthMcpClientConfig,
  McpChargeResult,
  McpChargeResponse,
  McpConfirmOptions,
  McpConfirmResponse,
  McpRefreshResponse,
  McpStartOptions,
  McpStartResponse,
  McpStatusResponse,
  McpUsageResponse,
  PowSolution,
  ResolvedAuthMethod
} from './types.js';

export class LiveAuthMcpClient {
  readonly publicKey: string;
  readonly baseUrl: string;

  private readonly fetchImpl: NonNullable<LiveAuthMcpClientConfig['fetch']>;
  private readonly authMethod: NonNullable<LiveAuthMcpClientConfig['authMethod']>;
  private readonly autoRefresh: boolean;
  private readonly refreshBufferMs: number;
  private readonly onInvoice: NonNullable<LiveAuthMcpClientConfig['onInvoice']> | undefined;
  private readonly onBudgetExceeded: NonNullable<LiveAuthMcpClientConfig['onBudgetExceeded']> | undefined;
  private readonly onRefreshError: NonNullable<LiveAuthMcpClientConfig['onRefreshError']> | undefined;

  private session?: AuthSession;
  private jwt?: string;
  private refreshToken: string | undefined;
  private jwtExpiresAtMs: number | undefined;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  constructor(config: LiveAuthMcpClientConfig) {
    if (!config.publicKey) {
      throw new LiveAuthMcpError('LiveAuthMcpClient requires config.publicKey');
    }

    this.publicKey = config.publicKey;
    this.baseUrl = cleanBaseUrl(config.baseUrl);
    this.fetchImpl = requireFetch(config.fetch);
    this.authMethod = config.authMethod ?? 'auto';
    this.autoRefresh = config.autoRefresh ?? true;
    this.refreshBufferMs = config.refreshBufferMs ?? 30_000;
    this.onInvoice = config.onInvoice;
    this.onBudgetExceeded = config.onBudgetExceeded;
    this.onRefreshError = config.onRefreshError;
  }

  get currentSession(): AuthSession | undefined {
    return this.session;
  }

  get token(): string | undefined {
    return this.jwt;
  }

  get tokenExpiresAt(): Date | undefined {
    return this.jwtExpiresAtMs ? new Date(this.jwtExpiresAtMs) : undefined;
  }

  setToken(jwt: string, refreshToken?: string, expiresIn?: number): void {
    this.clearRefreshTimer();
    this.jwt = jwt;
    this.refreshToken = refreshToken ?? this.refreshToken;
    this.jwtExpiresAtMs = expiresIn && expiresIn > 0 ? Date.now() + expiresIn * 1_000 : undefined;
    this.scheduleRefresh();
  }

  destroy(): void {
    this.clearRefreshTimer();
    this.session = undefined;
    this.jwt = undefined;
    this.refreshToken = undefined;
    this.jwtExpiresAtMs = undefined;
  }

  async start(options: McpStartOptions = {}): Promise<AuthSession> {
    const body = {
      forceLightning: options.forceLightning ?? this.authMethod === 'lightning',
      forceL402: options.forceL402 ?? this.authMethod === 'l402'
    };

    const response = await requestJson<McpStartResponse>(this.fetchImpl, `${this.baseUrl}/api/mcp/start`, {
      method: 'POST',
      headers: projectHeaders(this.publicKey),
      body: JSON.stringify(body)
    });

    const session: AuthSession = {
      ...response,
      method: resolveMethod(response)
    };

    this.session = session;

    if (session.invoice) {
      this.onInvoice?.(session.invoice);
    }

    return session;
  }

  async solvePow(session = this.requireSession()): Promise<PowSolution> {
    if (!session.powChallenge) {
      throw new LiveAuthMcpError('This LiveAuth MCP session does not include a PoW challenge');
    }

    // API public keys can alias the same project; PoW uses its canonical key.
    return solvePow(session.powChallenge);
  }

  async confirm(session = this.requireSession(), options: McpConfirmOptions = {}): Promise<McpConfirmResponse> {
    const powSolution = await this.resolvePowSolution(session, options);

    const response = await requestJson<McpConfirmResponse>(this.fetchImpl, `${this.baseUrl}/api/mcp/confirm`, {
      method: 'POST',
      headers: projectHeaders(this.publicKey),
      body: JSON.stringify({
        quoteId: session.quoteId,
        ...(powSolution
          ? {
              challengeHex: powSolution.challengeHex,
              nonce: powSolution.nonce,
              hashHex: powSolution.hashHex,
              difficultyBits: powSolution.difficultyBits,
              expiresAtUnix: powSolution.expiresAtUnix,
              sig: powSolution.sig
            }
          : {}),
        ...(options.paymentHash ? { paymentHash: options.paymentHash } : {}),
        ...(options.macaroon ? { macaroon: options.macaroon } : {})
      })
    });

    this.rememberTokens(response);
    return response;
  }

  async confirmWithPow(session = this.requireSession()): Promise<McpConfirmResponse> {
    return this.confirm(session, { solvePow: true });
  }

  async confirmLightning(session = this.requireSession()): Promise<McpConfirmResponse> {
    return this.confirm(session);
  }

  async confirmL402(macaroon: string, session = this.requireSession()): Promise<McpConfirmResponse> {
    return this.confirm(session, { macaroon });
  }

  async refresh(refreshToken = this.refreshToken): Promise<McpRefreshResponse> {
    if (!refreshToken) {
      throw new LiveAuthMcpError('LiveAuth MCP refresh requires a refresh token');
    }

    const response = await requestJson<McpRefreshResponse>(this.fetchImpl, `${this.baseUrl}/api/mcp/refresh`, {
      method: 'POST',
      headers: projectHeaders(this.publicKey),
      body: JSON.stringify({ refreshToken })
    });

    this.setToken(response.jwt, refreshToken, response.expiresIn);
    return response;
  }

  async getUsage(jwt = this.requireJwt()): Promise<McpUsageResponse> {
    return requestJson<McpUsageResponse>(this.fetchImpl, `${this.baseUrl}/api/mcp/usage`, {
      method: 'GET',
      headers: projectHeaders(this.publicKey, jwt)
    });
  }

  async charge(callCostSats?: number, jwt = this.requireJwt()): Promise<McpChargeResult> {
    const response = await requestJson<McpChargeResponse>(this.fetchImpl, `${this.baseUrl}/api/mcp/charge`, {
      method: 'POST',
      headers: projectHeaders(this.publicKey, jwt),
      body: JSON.stringify(callCostSats === undefined ? {} : { callCostSats })
    });

    const result = { ...response, ok: response.status === 'ok' };
    if (!result.ok) {
      this.onBudgetExceeded?.(result);
    }

    return result;
  }

  async getStatus(quoteId = this.requireSession().quoteId): Promise<McpStatusResponse> {
    return requestJson<McpStatusResponse>(this.fetchImpl, `${this.baseUrl}/api/mcp/status/${quoteId}`, {
      method: 'GET',
      headers: projectHeaders(this.publicKey)
    });
  }

  private async resolvePowSolution(
    session: AuthSession,
    options: McpConfirmOptions
  ): Promise<PowSolution | undefined> {
    if (options.powSolution) return options.powSolution;
    if (options.solvePow || (session.method === 'pow' && session.powChallenge)) {
      return this.solvePow(session);
    }

    return undefined;
  }

  private rememberTokens(response: McpConfirmResponse): void {
    if (response.jwt) {
      this.setToken(response.jwt, response.refreshToken ?? undefined, response.expiresIn);
    }
  }

  private scheduleRefresh(): void {
    if (!this.autoRefresh || !this.refreshToken || !this.jwtExpiresAtMs) {
      return;
    }

    const delayMs = Math.max(0, this.jwtExpiresAtMs - Date.now() - this.refreshBufferMs);
    this.refreshTimer = setTimeout(() => {
      this.refresh().catch((error: unknown) => {
        this.clearRefreshTimer();
        this.onRefreshError?.(error);
      });
    }, delayMs);

    const timer = this.refreshTimer as { unref?: () => void };
    timer.unref?.();
  }

  private clearRefreshTimer(): void {
    if (!this.refreshTimer) {
      return;
    }

    clearTimeout(this.refreshTimer);
    this.refreshTimer = undefined;
  }

  private requireSession(): AuthSession {
    if (!this.session) {
      throw new LiveAuthMcpError('LiveAuth MCP session has not been started');
    }

    return this.session;
  }

  private requireJwt(): string {
    if (!this.jwt) {
      throw new LiveAuthMcpError('LiveAuth MCP JWT is not available');
    }

    return this.jwt;
  }
}

function resolveMethod(response: McpStartResponse): ResolvedAuthMethod {
  if (response.powChallenge) return 'pow';
  if (response.invoice) return 'lightning';
  if (response.authHint === 'l402_bundle') return 'l402';
  return 'unknown';
}
