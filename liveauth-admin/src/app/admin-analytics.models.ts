export interface AdminAnalyticsOverviewResponse {
  windowHours: number;

  totalAuths: number;
  successfulAuths: number;
  failedAuths: number;

  totalSatsPaid: number;
  totalInvoicesSettled: number;

  totalProjects: number;
  proProjects: number;
  freeProjects: number;

  rateLimitHits: number;

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

