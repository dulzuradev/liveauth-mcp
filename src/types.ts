export type AuthMethod = 'auto' | 'pow' | 'lightning' | 'l402';

export type ResolvedAuthMethod = 'pow' | 'lightning' | 'l402' | 'unknown';

export type FetchLike = (input: string | URL | Request, init?: RequestInit) => Promise<Response>;

export interface LiveAuthMcpClientConfig {
  publicKey: string;
  baseUrl?: string;
  authMethod?: AuthMethod;
  fetch?: FetchLike;
  onInvoice?: (invoice: McpInvoice) => void;
  onBudgetExceeded?: (result: McpChargeResult) => void;
}

export interface LiveAuthMcpServerGateConfig {
  publicKey: string;
  baseUrl?: string;
  defaultCostSats?: number;
  fetch?: FetchLike;
}

export interface McpStartOptions {
  forceLightning?: boolean;
  forceL402?: boolean;
}

export interface PowChallenge {
  projectId?: string;
  projectPublicKey: string;
  challengeHex: string;
  targetHex: string;
  difficultyBits: number;
  expiresAtUnix: number;
  signature: string;
}

export interface PowSolution {
  challengeHex: string;
  nonce: number;
  hashHex: string;
  difficultyBits: number;
  expiresAtUnix: number;
  sig: string;
}

export interface PowSolverOptions {
  signal?: AbortSignal;
  maxIterations?: number;
  yieldEvery?: number;
}

export interface McpInvoice {
  bolt11?: string | null;
  amountSats: number;
  expiresAtUnix: number;
  paymentHash?: string | null;
}

export interface McpStartResponse {
  quoteId: string;
  powChallenge?: PowChallenge | null;
  invoice?: McpInvoice | null;
  authHint?: string | null;
}

export interface AuthSession extends McpStartResponse {
  method: ResolvedAuthMethod;
}

export interface McpConfirmOptions {
  powSolution?: PowSolution;
  paymentHash?: string;
  macaroon?: string;
  solvePow?: boolean;
}

export interface McpConfirmResponse {
  jwt?: string | null;
  expiresIn: number;
  remainingBudgetSats: number;
  paymentStatus?: string | null;
  refreshToken?: string | null;
}

export interface McpRefreshResponse {
  jwt: string;
  expiresIn: number;
  remainingBudgetSats: number;
}

export interface McpUsageResponse {
  status: string;
  callsUsed: number;
  satsUsed: number;
  maxSatsPerDay: number;
  remainingBudgetSats: number;
  maxCallsPerMinute: number;
  expiresAt: string;
  dayWindowStart?: string | null;
}

export interface McpChargeResponse {
  status: 'ok' | 'deny' | 'error' | string;
  callsUsed: number;
  satsUsed: number;
}

export interface McpChargeResult extends McpChargeResponse {
  ok: boolean;
}

export interface McpStatusResponse {
  quoteId: string;
  status: string;
  paymentStatus?: string | null;
  expiresAt: string;
}

export interface LnurlInvoiceResponse {
  pr: string;
  routes: string[];
}

export interface GateToolOptions {
  costSats?: number;
  validateFirst?: boolean;
}

export interface LiveAuthToolContext {
  liveAuth: {
    jwt: string;
    usage?: McpUsageResponse;
    charge: McpChargeResult;
  };
}

export type ToolHandler<TInput, TResult, TContext> = (
  input: TInput,
  context: TContext & LiveAuthToolContext
) => TResult | Promise<TResult>;
