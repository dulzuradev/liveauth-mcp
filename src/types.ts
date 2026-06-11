export type AuthMethod = 'auto' | 'pow' | 'lightning' | 'l402';

export type ResolvedAuthMethod = 'pow' | 'lightning' | 'l402' | 'unknown';

export type FetchLike = (input: string | URL | Request, init?: RequestInit) => Promise<Response>;

export interface LiveAuthMcpClientConfig {
  publicKey: string;
  baseUrl?: string;
  authMethod?: AuthMethod;
  fetch?: FetchLike;
  autoRefresh?: boolean;
  refreshBufferMs?: number;
  onInvoice?: (invoice: McpInvoice) => void;
  onBudgetExceeded?: (result: McpChargeResult) => void;
  onRefreshError?: (error: unknown) => void;
}

export interface LiveAuthMcpServerGateConfig {
  publicKey: string;
  baseUrl?: string;
  toolId?: string;
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
  grossSats?: number | null;
  platformFeeSats?: number | null;
  netSats?: number | null;
  feeBasisPoints?: number | null;
  revenueEventId?: string | null;
  receipt?: McpSignedReceipt | null;
  reason?: string | null;
}

export interface McpChargeResult extends McpChargeResponse {
  ok: boolean;
}

export interface McpSignedReceipt {
  version: string;
  payload: string;
  signature: string;
  signatureAlgorithm: 'HMAC-SHA256' | string;
  keyId: string;
  body: McpCallReceipt;
}

export interface McpCallReceipt {
  receiptId: string;
  revenueEventId: string;
  mcpToolId: string;
  toolSlug: string;
  toolMethodName: string;
  mcpGateTokenId?: string | null;
  mcpGateSessionId?: string | null;
  payingProjectId?: string | null;
  agentId?: string | null;
  grossSats: number;
  platformFeeSats: number;
  netSats: number;
  feeBasisPoints: number;
  status: string;
  idempotencyKey?: string | null;
  requestId?: string | null;
  createdAt: string;
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
  toolMethodName?: string;
  idempotencyKey?: string;
  agentId?: string;
  metadata?: Record<string, unknown>;
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
