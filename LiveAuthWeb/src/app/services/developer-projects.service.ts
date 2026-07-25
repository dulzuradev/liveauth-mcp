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
  l402BalanceSats: number;
  mcpSatsPerCall: number;
  mcpInvoiceCallCredits: number;
  mcpMaxSatsPerDay: number;
  mcpMaxCallsPerMinute: number;
  mcpSessionsTotal: number;
  mcpSessionsActive: number;
  mcpTokensIssued: number;
  mcpTokensActive: number;
  mcpCallsUsed: number;
  mcpSatsUsed: number;
  mcpActiveBudgetSats: number;
  mcpPaidToolCalls: number;
  mcpPaidToolSatsCharged: number;
  mcpPaidToolNetSats: number;
  mcpDeniedToolCharges: number;
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
  // Custom LND node config
  useCustomNode: boolean;
  lndBaseUrl: string | null;
  lndMacaroon: string | null;   // Masked in UI
  mcpSatsPerCall: number;
  mcpInvoiceCallCredits: number;
  mcpMaxSatsPerDay: number;
  mcpMaxCallsPerMinute: number;
}

export interface UpdateProjectSettingsRequest extends Omit<ProjectSettingsResponse,
  'mcpSatsPerCall' | 'mcpInvoiceCallCredits' | 'mcpMaxSatsPerDay' | 'mcpMaxCallsPerMinute'> {
  mcpSatsPerCall?: number;
  mcpInvoiceCallCredits?: number;
  mcpMaxSatsPerDay?: number;
  mcpMaxCallsPerMinute?: number;
}

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

export type CostShieldEnvironment = 'TEST' | 'LIVE';

export interface ProtectedActionDto {
  id: string;
  projectId: string;
  environment: CostShieldEnvironment;
  name: string;
  displayName: string;
  description: string;
  isEnabled: boolean;
  baseDifficulty: number;
  suspiciousDifficulty: number;
  maximumDifficulty: number;
  anonymousRequestLimit: number;
  anonymousLimitWindowSeconds: number;
  authenticatedRequestLimit: number | null;
  authenticatedLimitWindowSeconds: number | null;
  requireSingleUseToken: boolean;
  tokenLifetimeSeconds: number;
  allowedOrigins: string[];
  failureBehavior: 'Deny' | 'LightningFallback';
  allowLightningFallback: boolean;
  lightningPriceSats: number;
  lightningFallbackMode: 'RateLimitOnly' | 'Always';
  lightningBypassesProofOfWork: boolean;
  estimatedCostPerExecution: number;
  configurationVersion: number;
  createdAt: string;
  updatedAt: string;
}

export interface ProtectedActionListResponse {
  actions: ProtectedActionDto[];
}

export interface UpsertProtectedActionRequest {
  environment: CostShieldEnvironment;
  name: string;
  displayName: string;
  description: string;
  isEnabled: boolean;
  baseDifficulty: number;
  suspiciousDifficulty: number;
  maximumDifficulty: number;
  anonymousRequestLimit: number;
  anonymousLimitWindowSeconds: number;
  authenticatedRequestLimit: number | null;
  authenticatedLimitWindowSeconds: number | null;
  requireSingleUseToken: boolean;
  tokenLifetimeSeconds: number;
  allowedOrigins: string[];
  failureBehavior: 'Deny' | 'LightningFallback';
  allowLightningFallback: boolean;
  lightningPriceSats: number;
  lightningFallbackMode: 'RateLimitOnly' | 'Always';
  lightningBypassesProofOfWork: boolean;
  estimatedCostPerExecution: number;
}

export interface CostShieldActionUsageDto {
  protectedActionId: string;
  action: string;
  displayName: string;
  challengesIssued: number;
  authorizationsIssued: number;
  protectedRequests: number;
  requestsDenied: number;
  estimatedCostAvoided: number;
}

export interface CostShieldOverviewResponse {
  windowHours: number;
  windowStart: string;
  windowEnd: string;
  protectedActionCount: number;
  enabledActionCount: number;
  challengesIssued: number;
  challengesCompleted: number;
  authorizationsIssued: number;
  protectedRequests: number;
  requestsDenied: number;
  rateLimitedRequests: number;
  invalidAttempts: number;
  replayAttemptsBlocked: number;
  estimatedProviderCostAuthorized: number;
  estimatedCostAvoided: number;
  challengeSuccessRate: number;
  averageChallengeTimeMilliseconds: number | null;
  estimatedValues: boolean;
  topActions: CostShieldActionUsageDto[];
}

export interface CostShieldEventDto {
  id: string;
  protectedActionId: string | null;
  action: string | null;
  displayName: string | null;
  eventType: string;
  environment: string | null;
  verificationMethod: string | null;
  success: boolean;
  reason: string | null;
  source: string | null;
  durationMilliseconds: number | null;
  estimatedCostProtected: number | null;
  createdAt: string;
}

export interface CostShieldEventListResponse {
  total: number;
  limit: number;
  offset: number;
  events: CostShieldEventDto[];
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

export type WebhookEventStatus = 'Pending' | 'InProgress' | 'Delivered' | 'Failed' | 'Dead';

export interface WebhookEventDto {
  id: string;
  eventType: string;
  createdAt: string;
  lastAttemptAt?: string;
  attemptCount: number;
  status: WebhookEventStatus;
  lastStatusCode?: number;
  lastError?: string;
  destinationUrl?: string;
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

export interface McpToolDto {
  id: string;
  developerId?: string | null;
  projectId?: string | null;
  name: string;
  slug: string;
  description: string;
  category?: string | null;
  status: string;
  visibility: string;
  defaultCostSats: number;
  minCostSats: number;
  maxCostSats: number;
  websiteUrl?: string | null;
  docsUrl?: string | null;
  webhookUrl?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface McpToolListResponse {
  tools: McpToolDto[];
}

export interface McpToolRevenueSummaryResponse {
  toolId: string;
  toolName: string;
  toolStatus: string;
  windowHours: number;
  calls: number;
  grossSats: number;
  platformFeeSats: number;
  netSats: number;
  averageGrossSatsPerCall: number;
}

export interface McpToolRevenueTopToolDto {
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

export interface McpToolRevenueOverviewResponse {
  windowHours: number;
  paidCalls: number;
  grossSats: number;
  platformFeeSats: number;
  netSats: number;
  deniedCharges: number;
  topTools: McpToolRevenueTopToolDto[];
}

export interface McpToolRevenueEventDto {
  id: string;
  mcpToolId: string;
  mcpGateTokenId?: string | null;
  mcpGateSessionId?: string | null;
  payingProjectId?: string | null;
  agentId?: string | null;
  toolMethodName: string;
  grossSats: number;
  platformFeeSats: number;
  netSats: number;
  feeBasisPoints: number;
  status: string;
  idempotencyKey?: string | null;
  requestId?: string | null;
  metadataJson?: string | null;
  createdAt: string;
  reversalOfEventId?: string | null;
}

export interface McpToolRevenueEventsResponse {
  toolId: string;
  limit: number;
  events: McpToolRevenueEventDto[];
}

export interface LightningFeeSettingsResponse {
  invoiceFeeBasisPoints: number;
  invoiceMinimumFeeSats: number;
  bundleMarkupBasisPoints: number;
  bundleMarkupMinimumFeeSats: number;
  mcpPaidToolFeeBasisPoints: number;
  mcpPaidToolMinimumFeeSats: number;
  updatedAt?: string | null;
}

export interface McpChargeResponse {
  status: string;
  callsUsed: number;
  satsUsed: number;
  grossSats?: number | null;
  platformFeeSats?: number | null;
  netSats?: number | null;
  feeBasisPoints?: number | null;
  revenueEventId?: string | null;
  reason?: string | null;
  receipt?: any;
  toolId?: string | null;
  toolName?: string | null;
  toolSlug?: string | null;
}

export interface TestMcpToolChargeRequest {
  projectId?: string | null;
  callCostSats?: number | null;
  toolMethodName?: string | null;
  agentId?: string | null;
  metadata?: any;
}

export interface TestMcpToolChargeResponse {
  charge: McpChargeResponse;
  webhookQueued: boolean;
  webhookEventId?: string | null;
  webhookEventType?: string | null;
  webhookDestinationUrl?: string | null;
  webhookStatus?: string | null;
  message: string;
}

export interface CreateMcpToolRequest {
  projectId?: string | null;
  clearProject?: boolean | null;
  name: string;
  slug?: string | null;
  description?: string | null;
  category?: string | null;
  visibility?: string | null;
  status?: string | null;
  defaultCostSats: number;
  minCostSats: number;
  maxCostSats: number;
  websiteUrl?: string | null;
  docsUrl?: string | null;
  webhookUrl?: string | null;
}

export interface UpdateMcpToolRequest extends Partial<CreateMcpToolRequest> {
  clearProject?: boolean | null;
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

  // Delete project (soft delete)
  deleteProject(projectId: string): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/api/dev/projects/${projectId}`,
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

  updateProjectSettings(projectId: string, body: UpdateProjectSettingsRequest): Observable<ProjectSettingsResponse | null> {
    return this.http.put<ProjectSettingsResponse | null>(
      `${this.baseUrl}/api/dev/projects/${projectId}/settings`,
      body,
      this.devAuth.authHeaders()
    );
  }

  testLndConnection(projectId: string, baseUrl: string, macaroon: string | null): Observable<any> {
    return this.http.post(
      `${this.baseUrl}/api/dev/projects/${projectId}/test-lnd`,
      { baseUrl, macaroon },
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

  listProtectedActions(
    projectId: string,
    environment?: CostShieldEnvironment
  ): Observable<ProtectedActionListResponse> {
    const options = environment
      ? {
          params: new HttpParams().set('environment', environment),
          ...this.devAuth.authHeaders()
        }
      : this.devAuth.authHeaders();

    return this.http.get<ProtectedActionListResponse>(
      `${this.baseUrl}/api/dev/projects/${projectId}/costshield/actions`,
      options
    );
  }

  createProtectedAction(
    projectId: string,
    request: UpsertProtectedActionRequest
  ): Observable<ProtectedActionDto> {
    return this.http.post<ProtectedActionDto>(
      `${this.baseUrl}/api/dev/projects/${projectId}/costshield/actions`,
      request,
      this.devAuth.authHeaders()
    );
  }

  updateProtectedAction(
    projectId: string,
    actionId: string,
    request: UpsertProtectedActionRequest
  ): Observable<ProtectedActionDto> {
    return this.http.put<ProtectedActionDto>(
      `${this.baseUrl}/api/dev/projects/${projectId}/costshield/actions/${actionId}`,
      request,
      this.devAuth.authHeaders()
    );
  }

  deleteProtectedAction(
    projectId: string,
    actionId: string
  ): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/api/dev/projects/${projectId}/costshield/actions/${actionId}`,
      this.devAuth.authHeaders()
    );
  }

  getCostShieldOverview(
    projectId: string,
    windowHours: number = 24
  ): Observable<CostShieldOverviewResponse> {
    return this.http.get<CostShieldOverviewResponse>(
      `${this.baseUrl}/api/dev/projects/${projectId}/costshield/overview`,
      {
        params: new HttpParams().set('windowHours', windowHours.toString()),
        ...this.devAuth.authHeaders()
      }
    );
  }

  getCostShieldEvents(
    projectId: string,
    limit: number = 50,
    offset: number = 0
  ): Observable<CostShieldEventListResponse> {
    return this.http.get<CostShieldEventListResponse>(
      `${this.baseUrl}/api/dev/projects/${projectId}/costshield/events`,
      {
        params: new HttpParams()
          .set('limit', limit.toString())
          .set('offset', offset.toString()),
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

  listMcpTools(): Observable<McpToolListResponse> {
    return this.http.get<McpToolListResponse>(
      `${this.baseUrl}/api/dev/mcp-tools`,
      this.devAuth.authHeaders()
    );
  }

  getLightningFeeSettings(): Observable<LightningFeeSettingsResponse> {
    return this.http.get<LightningFeeSettingsResponse>(
      `${this.baseUrl}/api/dev/settings/lightning-fees`,
      this.devAuth.authHeaders()
    );
  }

  createMcpTool(req: CreateMcpToolRequest): Observable<McpToolDto> {
    return this.http.post<McpToolDto>(
      `${this.baseUrl}/api/dev/mcp-tools`,
      req,
      this.devAuth.authHeaders()
    );
  }

  updateMcpTool(toolId: string, req: UpdateMcpToolRequest): Observable<McpToolDto> {
    return this.http.patch<McpToolDto>(
      `${this.baseUrl}/api/dev/mcp-tools/${toolId}`,
      req,
      this.devAuth.authHeaders()
    );
  }

  deleteMcpTool(toolId: string): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/api/dev/mcp-tools/${toolId}`,
      this.devAuth.authHeaders()
    );
  }

  getMcpToolsRevenueOverview(
    range: '1h' | '24h' | '7d',
    limit: number = 10
  ): Observable<McpToolRevenueOverviewResponse> {
    const params = new HttpParams()
      .set('windowHours', this.mapRangeToWindowHours(range).toString())
      .set('limit', limit.toString());

    return this.http.get<McpToolRevenueOverviewResponse>(
      `${this.baseUrl}/api/dev/mcp-tools/revenue`,
      {
        params,
        ...this.devAuth.authHeaders()
      }
    );
  }

  getMcpToolRevenue(
    toolId: string,
    range: '1h' | '24h' | '7d'
  ): Observable<McpToolRevenueSummaryResponse> {
    const params = new HttpParams().set(
      'windowHours',
      this.mapRangeToWindowHours(range).toString()
    );

    return this.http.get<McpToolRevenueSummaryResponse>(
      `${this.baseUrl}/api/dev/mcp-tools/${toolId}/revenue`,
      {
        params,
        ...this.devAuth.authHeaders()
      }
    );
  }

  getMcpToolRevenueEvents(
    toolId: string,
    limit: number = 50
  ): Observable<McpToolRevenueEventsResponse> {
    const params = new HttpParams().set('limit', limit.toString());

    return this.http.get<McpToolRevenueEventsResponse>(
      `${this.baseUrl}/api/dev/mcp-tools/${toolId}/revenue/events`,
      {
        params,
        ...this.devAuth.authHeaders()
      }
    );
  }

  testMcpToolCharge(
    toolId: string,
    req: TestMcpToolChargeRequest
  ): Observable<TestMcpToolChargeResponse> {
    return this.http.post<TestMcpToolChargeResponse>(
      `${this.baseUrl}/api/dev/mcp-tools/${toolId}/test-charge`,
      req,
      this.devAuth.authHeaders()
    );
  }
}
