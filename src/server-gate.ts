import { BudgetExceededError, LiveAuthMcpError, UnauthorizedError } from './errors.js';
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
  readonly baseUrl: string;
  readonly toolId?: string;
  readonly defaultCostSats: number;

  private readonly fetchImpl: NonNullable<LiveAuthMcpServerGateConfig['fetch']>;

  constructor(config: LiveAuthMcpServerGateConfig) {
    if (!config.publicKey) {
      throw new LiveAuthMcpError('LiveAuthMcpServerGate requires config.publicKey');
    }

    this.publicKey = config.publicKey;
    this.baseUrl = cleanBaseUrl(config.baseUrl);
    this.toolId = config.toolId;
    this.defaultCostSats = config.defaultCostSats ?? 1;
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

    const endpoint = this.toolId
      ? `${this.baseUrl}/api/mcp/tools/${encodeURIComponent(this.toolId)}/charge`
      : `${this.baseUrl}/api/mcp/charge`;

    const body = this.toolId
      ? {
          callCostSats,
          ...(options.toolMethodName ? { toolMethodName: options.toolMethodName } : {}),
          ...(options.idempotencyKey ? { idempotencyKey: options.idempotencyKey } : {}),
          ...(options.agentId ? { agentId: options.agentId } : {}),
          ...(options.metadata ? { metadata: options.metadata } : {}),
        }
      : { callCostSats };

    const response = await requestJson<McpChargeResponse>(this.fetchImpl, endpoint, {
      method: 'POST',
      headers: projectHeaders(this.publicKey, jwt),
      body: JSON.stringify(body)
    });

    return { ...response, ok: response.status === 'ok' };
  }

  async gateTool<TInput, TResult, TContext extends object = Record<string, never>>(
    jwt: string,
    input: TInput,
    handler: ToolHandler<TInput, TResult, TContext>,
    context: TContext,
    options: GateToolOptions = {}
  ): Promise<TResult> {
    const usage = options.validateFirst === false ? undefined : await this.validateSession(jwt);
    const charge = await this.charge(jwt, options.costSats ?? this.defaultCostSats, options);

    if (!charge.ok) {
      throw new BudgetExceededError('LiveAuth MCP budget denied this tool call', charge);
    }

    const liveAuth = {
      jwt,
      ...(usage ? { usage } : {}),
      charge
    };

    return handler(input, {
      ...context,
      liveAuth
    });
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
