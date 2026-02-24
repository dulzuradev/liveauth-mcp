export interface AdminAnalyticsOverviewResponse {
  windowHours: number;

  // Basic Auth Metrics
  totalAuths: number;
  successfulAuths: number;
  failedAuths: number;
  rateLimitHits: number;

  // Revenue
  totalSatsPaid: number;
  totalInvoicesSettled: number;

  // Projects
  totalProjects: number;
  proProjects: number;
  freeProjects: number;

  // MCP Gate Metrics
  mcpSessionsTotal: number;
  mcpSessionsActive: number;
  mcpTokensIssued: number;
  mcpSatsEarned: number;

  // L402 Metrics
  l402InvoicesCreated: number;
  l402PaymentsReceived: number;
  l402SatsEarned: number;

  // Funnel Metrics
  funnel: FunnelMetrics;

  generatedAtUtc: string;

  authsOverTime: {
    timestampUtc: string;
    successful: number;
    failed: number;
  }[];

  recentEvents: AdminAuthEventDto[];
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
  projectId: string;
  projectName: string;

  plan: 'pro';
  amountSats: number;

  isPaid: boolean;

  createdAt: string;   // ISO
  expiresAt: string;   // ISO
}

// Auth event log
export interface AdminAuthEventDto {
  id: string;

  timestamp: string;   // ISO
  projectId: string;
  projectName: string;

  eventType: string;   // AuthGranted, RateLimitHit, etc.
  success: boolean;

  satsPaid?: number;
  clientIpMasked?: string;
}
