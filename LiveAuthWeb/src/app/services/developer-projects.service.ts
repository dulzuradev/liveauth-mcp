import { Injectable } from '@angular/core';
import {HttpClient, HttpParams} from '@angular/common/http';
import { DevAuthService } from './dev-auth.service';
import { Observable } from 'rxjs';
import { BASE_API_URL } from '../config';

export interface CreateProjectRequest { name: string; }
export interface CreateProjectResponse { projectId: string; publicKey: string; secretKey: string; }

export interface ProjectDto {
  monthlyAuthCount: number;
  monthlyAuthPeriodStart: Date;
  proPaidUntil: Date;
  projectId: string;
  name: string;
  publicKey: string;
  plan: string;
  monthlyQuota: number;
  monthlyUsed: number;
  createdAt: string;
  environment: 'TEST' | 'LIVE';
  active: boolean;
  satsPerLogin: number;
}
export interface ListProjectsResponse { projects: ProjectDto[]; }

export interface ProjectUsageResponse {
  plan: string;
  isPro: boolean;
  proExpiresAt: string | null;
  monthlyLimit: number;
  monthlyUsed: number;
  monthlyRemaining: number;
  monthlyUsagePercent: number;
  periodStart: string;
  periodEnd: string;
  totalSatsCharged: number;
  totalVerifications: number;
}

export interface RotateSecretResponse {
  projectId: string;
  publicKey: string;
  secretKey: string;
  rotatedAt: string;
}

export interface ProjectSettingsResponse {
  allowedDomains: string[];     // store as array in backend
  webhookUrl: string | null;
  satsPerLogin: number;
  maxAuthsPerIpPerHour: number;
}

export interface UpdateProjectSettingsRequest extends ProjectSettingsResponse {}

// Analytics + logs

export interface AnalyticsSummary {
  totalAuths24h: number;
  success24h: number;
  failed24h: number;
  satsPaid24h: number;
  rateLimitHits24h: number;
}

export interface LogEntry {
  timestamp: string;   // ISO string from backend
  ipMasked: string;
  sats: number;
  status: string;      // "SUCCESS" | "FAILED" | "RATE_LIMIT" | etc.
  reason: string;
}

// api key models
export interface ProjectApiKeyDto {
  id: string;
  label: string;
  publicKey: string;
  createdAt: string;
  lastUsedAt?: string | null;
  isActive: boolean;
}

export interface ListProjectApiKeysResponse {
  keys: ProjectApiKeyDto[];
}

export interface CreateApiKeyRequest {
  label: string;
}

export interface CreateApiKeyResponse {
  id: string;
  label: string;
  publicKey: string;
  secretKey: string; // shown once
}

export type WebhookEventStatus = 'Pending' | 'Delivering' | 'Delivered' | 'Dead';

export interface WebhookEventDto {
  id: string;
  eventType: string;
  createdAt: string;
  lastAttemptAt?: string;
  attemptCount: number;
  status: WebhookEventStatus;
  lastStatusCode?: number;
  lastError?: string;
}

export interface ListWebhookEventsResponse {
  events: WebhookEventDto[];
}

export interface ConfirmSubscriptionResponse {
  paid: boolean;
  proPaidUntil?: string;
}

export interface CreateSubscriptionInvoiceRequest {
  projectId: string;
  plan: 'pro';           // future-proof for more plans
}

export interface CreateSubscriptionInvoiceResponse {
  sessionId: string;
  invoice: string;       // BOLT11
  amountSats: number;
  expiresAtUnix: number;
}

@Injectable({ providedIn: 'root' })
export class DeveloperProjectsService {
    private baseUrl = BASE_API_URL;

  constructor(private http: HttpClient, private devAuth: DevAuthService) {}

  createProject(req: CreateProjectRequest): Observable<CreateProjectResponse> {
    return this.http.post<CreateProjectResponse>(
      `${this.baseUrl}/api/dev/projects`,
      req,
      this.devAuth.authHeaders()
    );
  }

  listProjects(): Observable<ListProjectsResponse> {
    return this.http.get<ListProjectsResponse>(
      `${this.baseUrl}/api/dev/projects`,
      this.devAuth.authHeaders()
    );
  }

  rotateSecret(projectId: string): Observable<RotateSecretResponse> {
    return this.http.post<RotateSecretResponse>(
      `${this.baseUrl}/api/dev/projects/${projectId}/rotate-secret`,
      {},
      this.devAuth.authHeaders()
    );
  }

  // Project status: Active / Paused
  updateProjectStatus(projectId: string, active: boolean): Observable<void> {
    return this.http.patch<void>(
      `${this.baseUrl}/api/dev/projects/${projectId}/status`,
      { active },
      this.devAuth.authHeaders()
    );
  }

  // Settings
  getProjectSettings(projectId: string): Observable<ProjectSettingsResponse> {
    return this.http.get<ProjectSettingsResponse>(
      `${this.baseUrl}/api/dev/projects/${projectId}/settings`,
      this.devAuth.authHeaders()
    );
  }

  getProjectUsage(projectId: string): Observable<ProjectUsageResponse> {
    return this.http.get<ProjectUsageResponse>(
      `${this.baseUrl}/api/dev/projects/${projectId}/usage`,
      this.devAuth.authHeaders()
    );
  }

  updateProjectSettings(projectId: string, body: UpdateProjectSettingsRequest): Observable<void> {
    return this.http.put<void>(
      `${this.baseUrl}/api/dev/projects/${projectId}/settings`,
      body,
      this.devAuth.authHeaders()
    );
  }

  getProjectAnalytics(
    projectId: string,
    range: '1h' | '24h' | '7d'
  ) {
    const windowHours = this.mapRangeToWindowHours(range);

    const params = new HttpParams().set('windowHours', windowHours.toString());

    return this.http.get<AnalyticsSummary>(
      `${this.baseUrl}/api/dev/projects/${projectId}/analytics`,
      {
        params,
        ...this.devAuth.authHeaders()
      }
    );
  }

  getProjectLogs(
    projectId: string,
    range: '1h' | '24h' | '7d',
    limit: number = 50
  ) {
    const windowHours = this.mapRangeToWindowHours(range);

    let params = new HttpParams()
      .set('windowHours', windowHours.toString())
      .set('limit', limit.toString());

    return this.http.get<LogEntry[]>(
      `${this.baseUrl}/api/dev/projects/${projectId}/logs`,
      {
        params,
        ...this.devAuth.authHeaders()
      }
    );
  }

  /** Map '1h' | '24h' | '7d' into windowHours expected by the API */
  private mapRangeToWindowHours(range: '1h' | '24h' | '7d'): number {
    switch (range) {
      case '1h':
        return 1;
      case '7d':
        return 24 * 7;
      case '24h':
      default:
        return 24;
    }
  }

  // Test webhook
  testProjectWebhook(projectId: string): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/dev/projects/${projectId}/test-webhook`,
      {},
      this.devAuth.authHeaders()
    );
  }

  listProjectApiKeys(projectId: string): Observable<ListProjectApiKeysResponse> {
    return this.http.get<ListProjectApiKeysResponse>(
      `${this.baseUrl}/api/dev/projects/${projectId}/keys`,
      this.devAuth.authHeaders()
    );
  }

  createProjectApiKey(projectId: string, body: CreateApiKeyRequest): Observable<CreateApiKeyResponse> {
    return this.http.post<CreateApiKeyResponse>(
      `${this.baseUrl}/api/dev/projects/${projectId}/keys`,
      body,
      this.devAuth.authHeaders()
    );
  }

  revokeProjectApiKey(projectId: string, keyId: string): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/dev/projects/${projectId}/keys/${keyId}/revoke`,
      {},
      this.devAuth.authHeaders()
    );
  }

  renameProjectApiKey(projectId: string, keyId: string, label: string): Observable<void> {
    return this.http.patch<void>(
      `${this.baseUrl}/api/dev/projects/${projectId}/keys/${keyId}`,
      { label },
      this.devAuth.authHeaders()
    );
  }

  getProjectWebhooks(projectId: string, limit = 50): Observable<ListWebhookEventsResponse> {
    return this.http.get<ListWebhookEventsResponse>(
      `${this.baseUrl}/api/dev/projects/${projectId}/webhooks?limit=${limit}`,
      this.devAuth.authHeaders()
    );
  }

  replayProjectWebhook(projectId: string, eventId: string): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/dev/projects/${projectId}/webhooks/${eventId}/replay`,
      {},
      this.devAuth.authHeaders()
    );
  }

  updateProjectEnvironment(projectId: string, environment: 'TEST' | 'LIVE') {
    return this.http.patch<void>(
      `${this.baseUrl}/api/dev/projects/${projectId}/environment`,
      { environment },
      this.devAuth.authHeaders()
    );
  }

  confirmSubscription(sessionId: string): Observable<ConfirmSubscriptionResponse> {
    return this.http.post<ConfirmSubscriptionResponse>(
      `${this.baseUrl}/api/dev/billing/confirm`,
      { sessionId },
      this.devAuth.authHeaders()
    );
  }

  createSubscriptionInvoice(
    req: CreateSubscriptionInvoiceRequest
  ): Observable<CreateSubscriptionInvoiceResponse> {
    return this.http.post<CreateSubscriptionInvoiceResponse>(
      `${this.baseUrl}/api/dev/billing/subscribe`,
      req,
      this.devAuth.authHeaders()
    );
  }
}
