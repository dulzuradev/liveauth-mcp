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
  constructor(message = 'LiveAuth MCP budget was exceeded', details?: unknown) {
    super(message, { status: 402, code: 'budget_exceeded', details });
    this.name = 'BudgetExceededError';
  }
}
