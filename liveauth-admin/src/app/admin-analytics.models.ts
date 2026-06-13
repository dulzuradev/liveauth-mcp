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

export interface AdminCommandCenterResponse {
  windowHours: number;
  windowStart: string;
  windowEnd: string;
  generatedAtUtc: string;
  btcUsdRate: number | null;
  revenue: AdminCommandCenterRevenue;
  auth: AdminCommandCenterAuth;
  mcp: AdminCommandCenterMcp;
  l402: AdminCommandCenterL402;
  webhooks: AdminCommandCenterWebhooks;
  fees: AdminCommandCenterFees;
  attention: AdminCommandCenterAlert[];
  topMcpTools: AdminCommandCenterMcpTool[];
  webhookFailures: AdminCommandCenterWebhookItem[];
  recentAuthEvents: AdminAuthEventDto[];
}

export interface AdminCommandCenterRevenue {
  totalSats: number;
  totalUsd: number | null;
  projectedMonthlyUsd: number | null;
  targetMinProgressPercent: number | null;
  targetMaxProgressPercent: number | null;
  targetMinMonthlyUsd: number;
  targetMaxMonthlyUsd: number;
  lightningAuthGrossSats: number;
  lightningAuthFeeSats: number;
  l402InvoiceGrossSats: number;
  l402InvoiceFeeSats: number;
  l402BundleGrossSats: number;
  l402BundleMarkupSats: number;
  mcpPaidToolGrossSats: number;
  mcpPaidToolPlatformFeeSats: number;
  mcpPaidToolNetSats: number;
}

export interface AdminCommandCenterAuth {
  totalProjects: number;
  activeProjects: number;
  proProjects: number;
  freeProjects: number;
  activeAuthSessions: number;
  pendingInvoices: number;
  authRequests: number;
  authSuccesses: number;
  authFailures: number;
  paidAuths: number;
  rateLimitHits: number;
  successRate: number;
  failureRate: number;
  rateLimitRate: number;
  funnel: FunnelMetrics;
  authsOverTime: {
    timestampUtc: string;
    successful: number;
    failed: number;
  }[];
}

export interface AdminCommandCenterMcp {
  sessionsTotal: number;
  sessionsActive: number;
  tokensIssued: number;
  tokensActive: number;
  callsUsed: number;
  satsUsed: number;
  paidToolCalls: number;
  paidToolGrossSats: number;
  paidToolPlatformFeeSats: number;
  paidToolNetSats: number;
  deniedCharges: number;
  inactiveToolDenials: number;
  activeTools: number;
  nonActiveTools: number;
}

export interface AdminCommandCenterL402 {
  purchasesPending: number;
  purchasesSettling: number;
  purchasesSettled: number;
  purchasesExpired: number;
  purchaseTotalChargedSats: number;
  purchaseInvoiceFeeSats: number;
  bundlesPending: number;
  bundlesActive: number;
  bundlesExpired: number;
  bundlesDepleted: number;
  bundleTotalChargedSats: number;
  bundleMarkupSats: number;
  bundleCallsRemaining: number;
  macaroonsIssued: number;
  macaroonsActive: number;
  macaroonsRevoked: number;
}

export interface AdminCommandCenterWebhooks {
  pending: number;
  inProgress: number;
  delivered: number;
  failed: number;
  dead: number;
  dueNow: number;
  oldestPendingAt?: string | null;
  oldestNextAttemptAt?: string | null;
}

export interface AdminCommandCenterFees {
  invoiceFeeBasisPoints: number;
  invoiceMinimumFeeSats: number;
  bundleMarkupBasisPoints: number;
  bundleMarkupMinimumFeeSats: number;
  mcpPaidToolFeeBasisPoints: number;
  mcpPaidToolMinimumFeeSats: number;
  updatedAt?: string | null;
}

export interface AdminCommandCenterAlert {
  severity: 'danger' | 'warn' | 'info' | string;
  kind: string;
  title: string;
  detail: string;
  count: number;
}

export interface AdminCommandCenterMcpTool {
  toolId: string;
  toolName: string;
  toolSlug: string;
  toolStatus: string;
  calls: number;
  grossSats: number;
  platformFeeSats: number;
  netSats: number;
  deniedCharges: number;
  averageGrossSatsPerCall: number;
}

export interface AdminCommandCenterWebhookItem {
  id: string;
  projectId: string;
  projectName: string;
  eventType: string;
  status: string;
  attemptCount: number;
  createdAt: string;
  nextAttemptAt: string;
  lastAttemptAt?: string | null;
  lastStatusCode?: number | null;
  lastError?: string | null;
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
