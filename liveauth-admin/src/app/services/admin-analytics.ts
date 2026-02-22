import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import {
  AdminAnalyticsOverviewResponse,
  AdminAuthEventDto,
  AdminProjectUsageDto,
  AdminSubscriptionDto
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
        map(raw => {
          const res: AdminAnalyticsOverviewResponse = {
            windowHours,

            totalAuths: raw.authRequests ?? 0,
            successfulAuths: raw.authSuccesses ?? 0,
            failedAuths: raw.authFailures ?? 0,

            totalSatsPaid: raw.satsPaid ?? 0,
            totalInvoicesSettled: raw.paidAuths ?? 0,

            totalProjects: raw.totalProjects ?? 0,
            proProjects: raw.proProjects ?? 0,
            freeProjects:
              (raw.totalProjects ?? 0) - (raw.proProjects ?? 0),

            rateLimitHits: raw.rateLimitHits ?? 0,

            generatedAtUtc: new Date().toISOString(),

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
                clientIpMasked: e.clientIpMasked ?? undefined
              }))
              : []
          };

          return res;
        })
      );
  }

  getProjects(windowHours = 24): Observable<AdminProjectUsageDto[]> {
    return this.http.get<AdminProjectUsageDto[]>(
      `${this.baseUrl}/api/admin/analytics/projects`,
      { params: { windowHours }, headers: this.getAuthHeaders() }
    );
  }

  getSubscriptions(): Observable<AdminSubscriptionDto[]> {
    return this.http.get<AdminSubscriptionDto[]>(
      `${this.baseUrl}/api/admin/analytics/subscriptions`,
      { headers: this.getAuthHeaders() }
    );
  }
}

