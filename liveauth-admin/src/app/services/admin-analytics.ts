import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import {
  AdminAnalyticsOverviewResponse,
  AdminAuthEventDto,
  AdminProjectUsageDto,
  AdminSubscriptionDto,
  AdminTransactionsResponse,
  TransactionDetailDto,
  AdminUsersListResponse,
  AdminUserDetailResponse
} from '../admin-analytics.models';
import { AdminAuthService } from './admin-auth';

@Injectable({ providedIn: 'root' })
export class AdminAnalyticsService {
  private baseUrl = 'https://api.liveauth.app';

  constructor(
    private http: HttpClient,
    private auth: AdminAuthService
  ) {}

  private getAuthHeaders(): Record<string, string> | undefined {
    const token = this.auth.getToken();
    return token ? { Authorization: `Bearer ${token}` } : undefined;
  }

  getOverview(windowHours = 24): Observable<AdminAnalyticsOverviewResponse> {
    return this.http
      .get<any>(`${this.baseUrl}/api/admin/analytics/overview`, {
        params: { windowHours },
        headers: this.getAuthHeaders()
      })
      .pipe(
        map(raw => ({
          windowHours,

          // Projects
          totalProjects: raw.totalProjects ?? 0,
          activeProjects: raw.activeProjects ?? 0,
          proProjects: raw.proProjects ?? 0,
          proExpired: raw.proExpired ?? 0,
          freeProjects: raw.freeProjects ?? 0,
          projectsInGracePeriod: raw.projectsInGracePeriod ?? 0,
          activeAuthSessions: raw.activeAuthSessions ?? 0,
          pendingInvoices: raw.pendingInvoices ?? 0,

          // Auth Metrics
          totalAuths: raw.authRequests ?? 0,
          successfulAuths: raw.authSuccesses ?? 0,
          failedAuths: raw.authFailures ?? 0,
          rateLimitHits: raw.rateLimitHits ?? 0,

          // Revenue
          totalSatsPaid: raw.satsPaid ?? 0,
          paidAuths: raw.paidAuths ?? 0,

          // MCP
          mcpSessionsTotal: raw.mcpSessionsTotal ?? 0,
          mcpSessionsActive: raw.mcpSessionsActive ?? 0,
          mcpTokensIssued: raw.mcpTokensIssued ?? 0,
          mcpSatsEarned: raw.mcpSatsEarned ?? 0,
          mcpSatsEarnedUsd: raw.mcpSatsEarnedUsd ?? null,

          // L402
          l402InvoicesCreated: raw.l402InvoicesCreated ?? 0,
          l402PaymentsReceived: raw.l402PaymentsReceived ?? 0,
          l402SatsEarned: raw.l402SatsEarned ?? 0,
          l402SatsEarnedUsd: raw.l402SatsEarnedUsd ?? null,

          // Exchange Rate
          btcUsdRate: raw.btcUsdRate ?? null,
          totalSatsEarnedUsd: raw.totalSatsEarnedUsd ?? null,

          // Funnel
          funnel: raw.funnel ?? {
            challengesIssued: 0,
            authsStarted: 0,
            authsPaid: 0,
            authsVerified: 0,
            tokensUsed: 0,
            startToPaidRate: 0,
            paidToVerifiedRate: 0,
            verifiedToUsedRate: 0
          },

          generatedAtUtc: raw.windowEnd ? new Date(raw.windowEnd).toISOString() : new Date().toISOString(),

          authsOverTime: Array.isArray(raw.authsOverTime)
            ? raw.authsOverTime.map((x: any) => ({
              timestampUtc: x.timestampUtc,
              successful: x.successful ?? 0,
              failed: x.failed ?? 0
            }))
            : [],

          recentEvents: Array.isArray(raw.recentEvents)
            ? raw.recentEvents.map((e: any) => ({
              id: e.id ?? crypto.randomUUID(),
              timestamp: e.timestamp,
              projectId: e.projectId,
              projectName: e.projectName ?? '(unknown)',
              eventType: e.eventType,
              success: !!e.success,
              satsPaid: e.satsPaid ?? undefined,
              reason: e.reason ?? undefined,
              clientIpMasked: e.clientIpMasked ?? undefined
            }))
            : []
        }))
      );
  }

  getProjects(windowHours = 24): Observable<AdminProjectUsageDto[]> {
    return this.http.get<AdminProjectUsageDto[]>(
      `${this.baseUrl}/api/admin/analytics/projects`,
      { params: { windowHours }, headers: this.getAuthHeaders() }
    ).pipe(map(projects => projects ?? []));
  }

  getSubscriptions(): Observable<AdminSubscriptionDto[]> {
    return this.http.get<AdminSubscriptionDto[]>(
      `${this.baseUrl}/api/admin/analytics/subscriptions`,
      { headers: this.getAuthHeaders() }
    ).pipe(map(subscriptions => subscriptions ?? []));
  }

  getTransactions(search?: string, limit = 50, offset = 0, projectId?: string): Observable<AdminTransactionsResponse> {
    const params: any = { search: search || '', limit: limit.toString(), offset: offset.toString() };
    if (projectId) params['projectId'] = projectId;
    return this.http.get<AdminTransactionsResponse>(
      `${this.baseUrl}/api/admin/analytics/transactions`,
      { params, headers: this.getAuthHeaders() }
    );
  }

  getTransaction(id: string): Observable<TransactionDetailDto> {
    return this.http.get<TransactionDetailDto>(
      `${this.baseUrl}/api/admin/analytics/transactions/${id}`,
      { headers: this.getAuthHeaders() }
    );
  }

  getUsers(search?: string, limit = 50, offset = 0): Observable<AdminUsersListResponse> {
    return this.http.get<AdminUsersListResponse>(
      `${this.baseUrl}/api/admin/users`,
      { params: { search: search || '', limit: limit.toString(), offset: offset.toString() }, headers: this.getAuthHeaders() }
    );
  }

  getUser(id: string): Observable<AdminUserDetailResponse> {
    return this.http.get<AdminUserDetailResponse>(
      `${this.baseUrl}/api/admin/users/${id}`,
      { headers: this.getAuthHeaders() }
    );
  }
}
