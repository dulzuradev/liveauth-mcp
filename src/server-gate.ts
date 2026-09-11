import { createHash } from 'node:crypto';
import { PaidOperationReplayError, ChargeDeniedError, ToolExecutionError, LiveAuthMcpError, UnauthorizedError } from './errors.js';
import { cleanBaseUrl, projectHeaders, requestJson, requireFetch } from './http.js';
import type {
  GateToolOptions,
  LiveAuthMcpServerGateConfig,
  McpChargeResponse,
  McpChargeResult,
  McpUsageResponse,
  ToolHandler
} from './types.js';

export class LiveAuthMcpServerGate {
  readonly publicKey: string;
  readonly fundingMode?: LiveAuthMcpServerGateConfig['fundingMode'];
  private readonly providerSecret?: string;
  readonly baseUrl: string;
  readonly toolId?: string;
  readonly toolName?: string;
  readonly defaultCostSats?: number;

  private readonly fetchImpl: NonNullable<LiveAuthMcpServerGateConfig['fetch']>;

  constructor(config: LiveAuthMcpServerGateConfig) {
    if (!config.publicKey) {
      throw new LiveAuthMcpError('LiveAuthMcpServerGate requires config.publicKey');
    }

    this.publicKey = config.publicKey;
    this.fundingMode = config.fundingMode;
    this.providerSecret = config.providerSecret;
    this.baseUrl = cleanBaseUrl(config.baseUrl);
    this.toolId = config.toolId;
    this.toolName = config.toolName;
    this.defaultCostSats = config.defaultCostSats;
    this.fetchImpl = requireFetch(config.fetch);
  }

  async validateSession(jwt: string): Promise<McpUsageResponse> {
    if (!jwt) {
      throw new UnauthorizedError('Missing LiveAuth MCP JWT');
    }

    try {
      return await requestJson<McpUsageResponse>(this.fetchImpl, `${this.baseUrl}/api/mcp/usage`, {
        method: 'GET',
        headers: projectHeaders(this.publicKey, jwt)
      });
    } catch (error) {
      if (error instanceof LiveAuthMcpError && (error.status === 401 || error.status === 404)) {
        throw new UnauthorizedError('Invalid LiveAuth MCP JWT', error.details);
      }

      throw error;
    }
  }

  async charge(
    jwt: string,
    callCostSats = this.defaultCostSats,
    options: GateToolOptions = {}
  ): Promise<McpChargeResult> {
    if (!jwt) {
      throw new UnauthorizedError('Missing LiveAuth MCP JWT');
    }

    if (this.fundingMode === 'caller') {
      const capabilities = await requestJson<{ callerFunding?: boolean }>(this.fetchImpl, `${this.baseUrl}/api/mcp/capabilities`, {
        method: 'GET', headers: projectHeaders(this.publicKey, jwt)
      });
      if (capabilities.callerFunding !== true) throw new LiveAuthMcpError('Caller funding unavailable', { code: 'caller_funding_unavailable' });
    }
    const endpoint = this.toolId
      ? `${this.baseUrl}/api/mcp/tools/${encodeURIComponent(this.toolId)}/charge`
      : `${this.baseUrl}/api/mcp/charge`;

    const toolName = options.toolName ?? this.toolName;
    if (this.fundingMode === 'caller' && (!options.idempotencyKey || !options.requestHash)) {
      throw new LiveAuthMcpError('Caller-funded calls require an idempotency key and request binding', { code: 'request_binding_required' });
    }
    const body = {
      ...(this.fundingMode ? { fundingMode: this.fundingMode } : {}),
      ...(options.requestHash ? { requestHash: options.requestHash } : {}),
      ...(callCostSats === undefined ? {} : { callCostSats }),
      ...(!this.toolId && toolName ? { toolName } : {}),
      ...(options.toolMethodName ? { toolMethodName: options.toolMethodName } : {}),
      ...(options.idempotencyKey ? { idempotencyKey: options.idempotencyKey } : {}),
      ...(options.agentId ? { agentId: options.agentId } : {}),
      ...(options.metadata ? { metadata: options.metadata } : {}),
    };

    let response: McpChargeResponse;
    try {
      response = await requestJson<McpChargeResponse>(this.fetchImpl, endpoint, {
        method: 'POST',
        headers: { ...projectHeaders(this.publicKey, jwt),
          ...(this.providerSecret && this.fundingMode !== 'caller' ? { 'X-LW-Secret': this.providerSecret } : {}) },
        body: JSON.stringify(body)
      });
    } catch (error) {
      // Structured HTTP denials use the same result contract as HTTP 200 denials.
      if (error instanceof LiveAuthMcpError && error.details && typeof error.details === 'object' &&
          'status' in error.details && error.details.status === 'deny') {
        response = error.details as McpChargeResponse;
      } else {
        throw error;
      }
    }
    if (this.fundingMode === 'caller' && response.fundingMode !== 'caller') {
      throw new LiveAuthMcpError('Backend does not support caller funding; execution refused', { code: 'caller_funding_unavailable' });
    }
    return { ...response, ok: response.status === 'ok' };
  }

  async gateTool<TInput, TResult, TContext extends object = Record<string, never>>(
    jwt: string,
    input: TInput,
    handler: ToolHandler<TInput, TResult, TContext>,
    context: TContext,
    options: GateToolOptions = {}
  ): Promise<TResult> {
    options = { ...options, requestHash: requestHash(input) };
    const usage = options.validateFirst === false ? undefined : await this.validateSession(jwt);
    const charge = await this.charge(jwt, options.costSats ?? this.defaultCostSats, options);

    if (!charge.ok) {
      throw new ChargeDeniedError(charge);
    }

    if (charge.duplicate) throw new PaidOperationReplayError(charge);

    const liveAuth = {
      jwt,
      ...(usage ? { usage } : {}),
      charge
    };

    try {
      return await handler(input, { ...context, liveAuth });
    } catch (cause) {
      throw new ToolExecutionError(cause, charge, options.idempotencyKey);
    }
  }

  async invoke<TInput, TResult, TContext extends object = Record<string, never>>(
    jwt: string,
    input: TInput,
    handler: ToolHandler<TInput, TResult, TContext>,
    context: TContext,
    options: GateToolOptions = {}
  ): Promise<TResult> {
    return this.gateTool(jwt, input, handler, context, options);
  }
}

export function withLiveAuthToolGate<TInput, TResult, TContext extends object = Record<string, never>>(
  gate: LiveAuthMcpServerGate,
  handler: ToolHandler<TInput, TResult, TContext>,
  options: GateToolOptions & {
    getJwt: (input: TInput, context: TContext) => string | undefined;
  }
): (input: TInput, context: TContext) => Promise<TResult> {
  return async (input, context) => {
    const jwt = options.getJwt(input, context);
    if (!jwt) {
      throw new UnauthorizedError('Missing LiveAuth MCP JWT');
    }

    return gate.gateTool(jwt, input, handler, context, options);
  };
}

/** Canonical JSON binds the paid operation to its validated arguments. */
export function requestHash(value: unknown): string {
  const canonical = (item: unknown): string => {
    if (item === null || typeof item !== 'object') return JSON.stringify(item) ?? 'null';
    if (Array.isArray(item)) return '[' + item.map(canonical).join(',') + ']';
    return '{' + Object.keys(item).filter(key => (item as Record<string, unknown>)[key] !== undefined)
      .sort().map(key => JSON.stringify(key) + ':' + canonical((item as Record<string, unknown>)[key])).join(',') + '}';
  };
  return createHash('sha256').update(canonical(value)).digest('hex');
}

/** Preserve JSON-RPC identity on transport retries; explicit keys also span new RPC IDs. */
export function mcpIdempotencyKey(explicit: string | null | undefined, rpcId: string | number): string {
  if (explicit !== undefined && explicit !== null) {
    if (!/^[a-zA-Z0-9._-]{1,128}$/.test(explicit)) throw new LiveAuthMcpError('Invalid x-request-id', { code: 'invalid_idempotency_key' });
    return explicit;
  }
  return 'mcp-' + requestHash(rpcId);
}
