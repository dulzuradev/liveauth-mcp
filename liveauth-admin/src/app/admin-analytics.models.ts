export interface AdminAuthEventDto {
  id: string;
  timestamp: string;
  projectId: string;
  projectName: string;
  eventType: string;
  success: boolean;
  satsPaid?: number;
  reason?: string;
  clientIpMasked?: string;
}

export interface FunnelMetrics {
  challengesIssued: number;
  authsStarted: number;
  authsPaid: number;
  authsVerified: number;
  tokensUsed: number;
  startToPaidRate: number;
  paidToVerifiedRate: number;
  verifiedToUsedRate: number;
}

export interface AdminAnalyticsOverviewResponse {
  windowHours: number;

  // Projects
  totalProjects: number;
  activeProjects: number;
  proProjects: number;
  proExpired: number;
  freeProjects: number;
  projectsInGracePeriod: number;
  activeAuthSessions: number;
  pendingInvoices: number;

  // Auth Metrics
  totalAuths: number;
  successfulAuths: number;
  failedAuths: number;
  rateLimitHits: number;

  // Revenue
  totalSatsPaid: number;
  paidAuths: number;

  // MCP Gate Metrics
  mcpSessionsTotal: number;
  mcpSessionsActive: number;
  mcpTokensIssued: number;
  mcpSatsEarned: number;
  mcpSatsEarnedUsd: number | null;
  mcpPaidToolCalls: number;
  mcpPaidToolSatsCharged: number;
  mcpDeniedToolCharges: number;

  // L402 Metrics
  l402InvoicesCreated: number;
  l402PaymentsReceived: number;
  l402SatsEarned: number;
  l402SatsEarnedUsd: number | null;

  // Exchange Rate
  btcUsdRate: number | null;
  totalSatsEarnedUsd: number | null;

  // Funnel
  funnel: FunnelMetrics;

  generatedAtUtc: string;

  authsOverTime: {
    timestampUtc: string;
    successful: number;
    failed: number;
  }[];

  recentEvents: AdminAuthEventDto[];
}

// Project usage leaderboard
export interface AdminProjectUsageDto {
  projectId: string;
  name: string;
  plan: 'free' | 'pro';

  auths: number;
  successes: number;
  failures: number;
  rateLimitHits: number;

  satsPaid: number;
}

// Subscription / revenue visibility
export interface AdminSubscriptionDto {
  subscriptionId?: string;
  projectId: string;
  projectName: string;

  plan: 'pro';
  amountSats: number;

  isPaid: boolean;

  createdAt: string;
  paidAt?: string | null;
  expiresAt: string;
}

// Transaction models

export interface TransactionDto {
  id: string;
  type: string;
  projectId: string;
  projectName?: string;
  projectPublicKey?: string;
  amountSats: number;
  paymentHash: string;
  invoice: string;
  status: string;
  createdAt: string;
  paidAt?: string;
  clientIp?: string;
  environment?: string;
}

export interface TransactionDetailDto extends TransactionDto {
  userHint?: string;
  payerLightningKey?: string;
  powChallenge?: string;
  powDifficultyBits?: number;
}

export interface AdminTransactionsResponse {
  transactions: TransactionDto[];
  total: number;
  totalSats: number;
}

// ── Users / Developer models ──────────────────────────────

export interface AdminUserDto {
  id: string;
  email: string;
  githubUsername?: string;
  createdAt: string;
  emailVerified: boolean;
  projectCount: number;
  proProjectCount: number;
  totalAuths: number;
  hasLightningKey: boolean;
}

export interface AdminUserProjectDto {
  id: string;
  name: string;
  publicKey: string;
  plan: string;
  createdAt: string;
  isActive: boolean;
  proPaidUntil?: string;
  totalAuths: number;
  totalSats: number;
  lastAuthAt?: string;
}

export interface AdminUserDetailResponse extends AdminUserDto {
  projects: AdminUserProjectDto[];
}

export interface AdminUsersListResponse {
  users: AdminUserDto[];
  total: number;
}
