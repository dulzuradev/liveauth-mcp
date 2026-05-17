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
  readonly defaultCostSats: number;

  private readonly fetchImpl: NonNullable<LiveAuthMcpServerGateConfig['fetch']>;

  constructor(config: LiveAuthMcpServerGateConfig) {
    if (!config.publicKey) {
      throw new LiveAuthMcpError('LiveAuthMcpServerGate requires config.publicKey');
    }

    this.publicKey = config.publicKey;
    this.baseUrl = cleanBaseUrl(config.baseUrl);
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

  async charge(jwt: string, callCostSats = this.defaultCostSats): Promise<McpChargeResult> {
    if (!jwt) {
      throw new UnauthorizedError('Missing LiveAuth MCP JWT');
    }

    const response = await requestJson<McpChargeResponse>(this.fetchImpl, `${this.baseUrl}/api/mcp/charge`, {
      method: 'POST',
      headers: projectHeaders(this.publicKey, jwt),
      body: JSON.stringify({ callCostSats })
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
    const charge = await this.charge(jwt, options.costSats ?? this.defaultCostSats);

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
