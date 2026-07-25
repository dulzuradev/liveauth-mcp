export type CostShieldEnvironment = 'TEST' | 'LIVE';

export type FetchLike = (
  input: RequestInfo | URL,
  init?: RequestInit
) => Promise<Response>;

export interface PowProgress {
  attempts: number;
  nonce: number;
  elapsedMilliseconds: number;
  difficultyBits: number;
}

export interface CostShieldChallenge {
  challengeId: string;
  projectPublicKey: string;
  environment: CostShieldEnvironment;
  action: string;
  protectedActionId: string;
  targetHex: string;
  difficultyBits: number;
  difficultyReason: string;
  expiresAtUnix: number;
  configurationVersion: number;
  signature: string;
}

export interface PowSolution {
  nonce: number;
  hashHex: string;
  attempts: number;
  elapsedMilliseconds: number;
}

export interface PowSolverOptions {
  challenge: CostShieldChallenge;
  signal?: AbortSignal;
  onProgress?: (progress: PowProgress) => void;
}

export type PowSolver = (
  options: PowSolverOptions
) => Promise<PowSolution>;

export interface WorkerLike {
  onmessage: Worker['onmessage'];
  onerror: Worker['onerror'];
  postMessage: Worker['postMessage'];
  terminate: Worker['terminate'];
}

export type WorkerFactory = (workerUrl: URL) => WorkerLike;

export interface LiveAuthOptions {
  publicKey: string;
  environment: CostShieldEnvironment;
  apiUrl?: string;
  origin?: string;
  fetch?: FetchLike;
  powSolver?: PowSolver;
  workerFactory?: WorkerFactory;
  requestTimeoutMilliseconds?: number;
  challengeRetries?: number;
}

export interface ProtectOptions {
  action: string;
  origin?: string;
  subject?: string;
  riskHint?: string;
  clientMetadata?: Record<string, string>;
  signal?: AbortSignal;
  onProgress?: (progress: PowProgress) => void;
}

export interface CostShieldAuthorization {
  token: string;
  tokenType: string;
  expiresAtUnix: number;
  authorizationId: string;
  action: string;
  environment: CostShieldEnvironment;
  requireSingleUse: boolean;
}

export interface CostShieldAuthorizationResult
  extends CostShieldAuthorization {
  difficultyBits: number;
  difficultyReason: string;
  solveMilliseconds: number;
}

export interface CostShieldApiErrorBody {
  error?: string;
  error_description?: string;
  message?: string;
  [key: string]: unknown;
}
