import type { McpChargeResult } from './types.js';

export class LiveAuthMcpError extends Error {
  readonly status: number | undefined;
  readonly code: string | undefined;
  readonly details: unknown;

  constructor(message: string, options: { status?: number; code?: string; details?: unknown } = {}) {
    super(message);
    this.name = 'LiveAuthMcpError';
    this.status = options.status;
    this.code = options.code;
    this.details = options.details;
  }
}

export class UnauthorizedError extends LiveAuthMcpError {
  constructor(message = 'LiveAuth MCP session is not authorized', details?: unknown) {
    super(message, { status: 401, code: 'unauthorized', details });
    this.name = 'UnauthorizedError';
  }
}

export class BudgetExceededError extends LiveAuthMcpError {
  constructor(message = 'LiveAuth MCP budget was exceeded', details?: unknown, options: { status?: number; code?: string } = {}) {
    super(message, { status: 402, code: 'budget_exceeded', ...options, details });
    this.name = 'BudgetExceededError';
  }
}

/** A charge denial. Extends BudgetExceededError to preserve existing catch handlers.
 * Use reason/code to distinguish tool availability, budget, rate, and other denials.
 */
export class ChargeDeniedError extends BudgetExceededError {
  readonly reason: string;
  readonly toolName?: string | null;
  readonly toolId?: string | null;

  constructor(charge: McpChargeResult) {
    const reason = charge.reason || 'denied';
    const messages: Record<string, string> = {
      tool_inactive: 'LiveAuth MCP tool is inactive',
      tool_unpublished: 'LiveAuth MCP tool is unpublished',
      tool_not_found: 'LiveAuth MCP tool was not found',
      budget_exceeded: 'LiveAuth MCP budget was exceeded',
      rate_limited: 'LiveAuth MCP rate limit was exceeded',
    };
    super(messages[reason] ?? 'LiveAuth MCP denied this tool call', charge, {
      code: reason,
      status: reason === 'budget_exceeded' ? 402 : reason === 'rate_limited' ? 429 : reason === 'tool_not_found' ? 404 : 403,
    });
    this.name = 'ChargeDeniedError';
    this.reason = reason;
    this.toolName = charge.toolName;
    this.toolId = charge.toolId;
  }
}

/** Authorization is billable even when execution fails. Never serialize cause. */
export class ToolExecutionError extends LiveAuthMcpError {
  readonly charge: McpChargeResult;
  readonly idempotencyKey?: string;
  declare readonly cause: unknown;

  constructor(cause: unknown, charge: McpChargeResult, idempotencyKey?: string) {
    super('Tool execution failed after LiveAuth authorization', { code: 'tool_execution_failed' });
    this.name = 'ToolExecutionError';
    this.charge = charge;
    this.idempotencyKey = idempotencyKey;
    Object.defineProperty(this, 'cause', { value: cause, enumerable: false });
  }
}
