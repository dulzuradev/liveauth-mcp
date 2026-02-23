import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

// PrimeNG
import { InputTextModule } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { TooltipModule } from 'primeng/tooltip';
import { Tag } from 'primeng/tag';

// QR
import { QrcodeComponent } from 'qrcode-angular';

import {
  CreateProjectRequest,
  CreateProjectResponse,
  DeveloperProjectsService,
  ProjectDto,
  RotateSecretResponse,
  ProjectSettingsResponse,
  AnalyticsSummary,
  LogEntry,
  ProjectApiKeyDto,
  CreateApiKeyResponse,
  WebhookEventDto,
  ProjectUsageResponse
} from '../../../services/developer-projects.service';

import {
  DevAuthService,
  DevStartLoginResponse,
  DevConfirmLoginResponse
} from '../../../services/dev-auth.service';
import { Tab, TabList, TabPanel, TabPanels, Tabs } from 'primeng/tabs';
import {LocalTimePipe} from '../../../directives/local-time.pipe';


//
// UI-only types
//

// Extend API DTO with UI fields used by the template
export type UiProject = ProjectDto;

export interface ProjectSettingsForm {
  allowedDomains: string;
  webhookUrl: string;
  satsPerLogin: number | null;
  maxAuthsPerIpPerHour: number | null;
}

@Component({
  selector: 'app-developer-projects',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    InputTextModule,
    ButtonModule,
    TableModule,
    DialogModule,
    MessageModule,
    TooltipModule,
    QrcodeComponent,
    Tag,
    TabPanels,
    Tabs,
    TabList,
    Tab,
    TabPanel,
    LocalTimePipe
  ],
  templateUrl: './developer-projects-redesign.html',
  styleUrls: ['./dashboard-redesign.css']
})
export class DeveloperProjectsComponent implements OnInit, OnDestroy {

  // 🎯 Onboarding State
  onboardingStep = 1;
  hasCompletedOnboarding = false;
  selectedUseCase: 'agent-auth' | 'micropayment' | 'bot-mgmt' | 'custom' | '' = '';

  copyToClipboard(text: string) {
    if (!text) return;
    navigator.clipboard.writeText(text);
  }

  // 🔐 Dev Login State
  developerEmail = '';
  loginSession?: DevStartLoginResponse;
  loginDialogVisible = false;
  loginInProgress = false;
  polling = false;
  loggedIn = false;
  copiedLoginInvoice = false;

  get lightningQrValue(): string {
    if (!this.loginSession?.invoice) return '';
    return `LIGHTNING:${this.loginSession?.invoice.trim()}`;
  }

  // Dashboard stats helpers
  get activeProjectsCount(): number {
    return this.projects.filter(p => p.active).length;
  }

  get pausedProjectsCount(): number {
    return this.projects.filter(p => !p.active).length;
  }

  // countdown
  remainingSeconds = 0;
  private countdownTimerId?: any;
  private pollTimerId?: any;

  // Projects UI
  projectName = '';
  projects: UiProject[] = [];

  // Secret dialogs
  showSecretDialog = false;
  lastSecret?: CreateProjectResponse;
  copiedSecret = false;
  copiedRotatedSecret = false;
  copiedProjectId = false;
  copiedPublicKey = false;

  showRotateDialog = false;
  lastRotatedSecret?: RotateSecretResponse;

  loading = false;
  error?: string;

  // PROJECT DETAILS DIALOG STATE
  showProjectDialog = false;
  selectedProject: UiProject | null = null;

  projectForm: ProjectSettingsForm = {
    allowedDomains: '',
    webhookUrl: '',
    satsPerLogin: null,
    maxAuthsPerIpPerHour: null
  };

  savingSettings = false;

  // Webhooks
  webhooks: WebhookEventDto[] | null = null;
  loadingWebhooks = false;

  // Tabs
  _projectDialogTab: 'overview' | 'analytics' | 'logs' | 'keys' | 'webhooks' = 'overview';
  timeRange: '1h' | '24h' | '7d' = '24h';

  private get windowHours(): number {
    switch (this.timeRange) {
      case '1h':  return 1;
      case '7d':  return 24 * 7;
      default:    return 24;
    }
  }

  // Called by (valueChange) on <p-tabs>
  setProjectDialogTab(tab: 'overview' | 'analytics' | 'usage' | 'logs' | 'keys' | 'webhooks' | any) {
    this._projectDialogTab = tab;

    if (!this.selectedProject) return;

    switch (tab) {
      case 'analytics':
        this.loadAnalytics(this.selectedProject.projectId);
        break;
      case 'usage':
        this.loadAnalytics(this.selectedProject.projectId); // Loads both analytics and usage
        break;
      case 'logs':
        this.loadLogs(this.selectedProject.projectId);
        break;
      case 'keys':
        this.loadApiKeys(this.selectedProject.projectId);
        break;
      case 'webhooks':
        this.loadWebhooks(this.selectedProject.projectId);
        break;
      case 'overview':
      default:
        // overview has no extra loading
        break;
    }
  }

  onTimeRangeChange(range: '1h' | '24h' | '7d') {
    this.timeRange = range;
console.log('onTimeRangeChange', range);
    if (!this.selectedProject) return;

    // Reload whichever tab is active
    if (this._projectDialogTab === 'analytics') {
      this.loadAnalytics(this.selectedProject.projectId);
    } else if (this._projectDialogTab === 'logs') {
      this.loadLogs(this.selectedProject.projectId);
    }
  }

  // ANALYTICS + LOGS
  analytics: AnalyticsSummary | null = null;
  usage: ProjectUsageResponse | null = null;
  logs: LogEntry[] | null = null;

  // API keys state
  apiKeys: ProjectApiKeyDto[] | null = null;
  loadingKeys = false;

  showNewKeyDialog = false;
  newKeyLabel = '';
  creatingKey = false;

  showNewKeySecretDialog = false;
  lastCreatedKey: CreateApiKeyResponse | null = null;
  copiedNewKeySecret = false;

  planLimits = {
    freeMonthlyAuthLimit: 1000
  };

  startSubscriptionUpgrade(project: UiProject) {
    this.selectedProject = project;
    this.showBillingDialog = true;
    this.resetBillingState();
  }

  resetBillingState() {
    this.billingSession = undefined;
    this.billingLoading = false;
    this.billingPolling = false;
    this.billingExpired = false;
    this.billingCopied = false;
    this.billingRemainingLabel = '';
    clearInterval(this.billingPollTimer);
    clearInterval(this.billingCountdownTimer);
  }

  showBillingDialog = false;
  billingLoading = false;
  billingPolling = false;
  billingCopied = false;
  billingExpired = false;

  billingSession?: {
    sessionId: string;
    invoice: string;
    amountSats: number;
    expiresAtUnix: number;
  };

  private billingPollTimer?: any;
  private billingCountdownTimer?: any;
  billingRemainingLabel = '';



  constructor(
    private devAuth: DevAuthService,
    private devService: DeveloperProjectsService
  ) {}

  // ---------------------------------------------------------------------------
  // LIFECYCLE
  // ---------------------------------------------------------------------------

  ngOnInit() {
    const jwt = this.devAuth.getToken();
    if (jwt) {
      this.loggedIn = true;
      this.loadProjects();
    }
  }

  ngOnDestroy() {
    this.stopPolling();
    this.stopCountdown();
  }

  // ---------------------------------------------------------------------------
  // LOGIN FLOW
  // ---------------------------------------------------------------------------

  openLoginDialog() {
    this.error = undefined;
    this.loginDialogVisible = true;
  }

  startLogin() {
    this.error = undefined;

    const email = this.developerEmail.trim();
    if (!email) {
      this.error = 'Enter your developer email.';
      return;
    }

    this.loginInProgress = true;

    this.devAuth.startLogin({ developerEmail: email }).subscribe({
      next: (res) => {
        this.loginSession = res;
        this.loginInProgress = false;

        // start countdown
        this.startCountdown(res.expiresAtUnix);

        // begin polling
        this.pollForPayment();
      },
      error: (err) => {
        this.loginInProgress = false;
        this.error = this.extractErrorMessage(err) || 'Failed to start login.';
      }
    });
  }

  private pollForPayment() {
    if (!this.loginSession || this.polling) return;

    this.polling = true;

    const pollOnce = () => {
      if (!this.loginSession || this.loggedIn) return;

      // stop if expired
      if (this.remainingSeconds <= 0) {
        this.stopPolling();
        return;
      }

      this.devAuth.confirmLogin({ sessionId: this.loginSession.sessionId }).subscribe({
        next: (res: DevConfirmLoginResponse) => {
          if (res.verified && res.token) {
            this.devAuth.saveToken(res.token);
            this.loggedIn = true;

            this.stopPolling();
            this.stopCountdown();
            this.loginDialogVisible = false;

            this.loadProjects();
          } else {
            this.pollTimerId = setTimeout(pollOnce, 2000);
          }
        },
        error: () => {
          this.pollTimerId = setTimeout(pollOnce, 2000);
        }
      });
    };

    pollOnce();
  }

  public stopPolling() {
    this.polling = false;
    if (this.pollTimerId) {
      clearTimeout(this.pollTimerId);
      this.pollTimerId = undefined;
    }
  }

  private startCountdown(expiresAtUnix: number) {
    this.stopCountdown();

    const tick = () => {
      const now = Math.floor(Date.now() / 1000);
      this.remainingSeconds = Math.max(0, expiresAtUnix - now);

      if (this.remainingSeconds <= 0) {
        this.stopCountdown();
        this.stopPolling();
      }
    };

    tick();
    this.countdownTimerId = setInterval(tick, 1000);
  }

  public stopCountdown() {
    if (this.countdownTimerId) {
      clearInterval(this.countdownTimerId);
      this.countdownTimerId = undefined;
    }
    this.remainingSeconds = 0;
  }

  get remainingTimeLabel() {
    const m = Math.floor(this.remainingSeconds / 60);
    const s = this.remainingSeconds % 60;
    return `${m}:${s.toString().padStart(2, '0')}`;
  }

  logout() {
    this.devAuth.clearToken();
    this.loggedIn = false;
    this.projects = [];
    this.loginSession = undefined;
    this.stopPolling();
    this.stopCountdown();
  }

  // ---------------------------------------------------------------------------
  // PROJECTS
  // ---------------------------------------------------------------------------

  loadProjects() {
    this.error = undefined;
    this.loading = true;

    this.devService.listProjects().subscribe({
      next: (res) => {
        this.projects = res.projects ?? [];
        this.selectedProject = this.projects.find(p => p.projectId == this.selectedProject?.projectId) ?? null;
        this.loading = false;
        // Mark onboarding complete if user has projects
        if (this.projects.length > 0) {
          this.hasCompletedOnboarding = true;
        }
      },
      error: (err) => {
        this.loading = false;
        this.error = this.extractErrorMessage(err) || 'Failed to load projects.';
      }
    });
  }

  createProject() {
    this.error = undefined;
    const name = this.projectName.trim();
    if (!name) {
      this.error = 'Project name is required.';
      return;
    }

    this.loading = true;

    const req: CreateProjectRequest = { name };
    this.devService.createProject(req).subscribe({
      next: (res) => {
        this.lastSecret = res;
        this.showSecretDialog = true;
        this.projectName = '';
        this.loading = false;
        this.onboardingStep = 3;
        this.loadProjects();
      },
      error: (err) => {
        this.loading = false;
        this.error = this.extractErrorMessage(err) || 'Unable to create project.';
      }
    });
  }

  rotateSecret(projectId: string) {
    this.error = undefined;

    if (!confirm('Rotate secret key? The old one stops working immediately.')) {
      return;
    }

    this.loading = true;
    this.devService.rotateSecret(projectId).subscribe({
      next: (res) => {
        this.lastRotatedSecret = res;
        this.showRotateDialog = true;
        this.loading = false;
        this.loadProjects();
      },
      error: (err) => {
        this.loading = false;
        this.error = this.extractErrorMessage(err)  || 'Failed to rotate secret key.';
      }
    });
  }

  // Toggle Active / Paused flag used by <p-tag> and the actions button
  toggleProjectActive(project: UiProject): void {
    const newActive = !project.active;
    this.error = undefined;

    this.devService.updateProjectStatus(project.projectId, newActive).subscribe({
      next: () => {
        project.active = newActive;
        // For OnPush, you’d do: this.projects = [...this.projects];
      },
      error: (err) => {
        this.error = this.extractErrorMessage(err)  || 'Failed to update project status.';
      }
    });
  }

  // ---------------------------------------------------------------------------
  // COPY HELPERS
  // ---------------------------------------------------------------------------

  copyLoginInvoice() {
    if (!this.loginSession?.invoice) return;

    navigator.clipboard.writeText(this.loginSession.invoice);
    this.copiedLoginInvoice = true;

    setTimeout(() => (this.copiedLoginInvoice = false), 1500);
  }

  copySecretKey() {
    const key = this.lastSecret?.secretKey;
    if (!key) return;

    navigator.clipboard.writeText(key);
    this.copiedSecret = true;

    setTimeout(() => (this.copiedSecret = false), 1500);
  }

  copyRotatedSecret() {
    const key = this.lastRotatedSecret?.secretKey;
    if (!key) return;

    navigator.clipboard.writeText(key);
    this.copiedRotatedSecret = true;

    setTimeout(() => (this.copiedRotatedSecret = false), 1500);
  }

  copyProjectId() {
    const id = this.selectedProject?.projectId;
    if (!id) return;

    navigator.clipboard.writeText(id);
    this.copiedProjectId = true;

    setTimeout(() => (this.copiedProjectId = false), 1500);
  }

  copyPublicKey() {
    const pk = this.selectedProject?.publicKey;
    if (!pk) return;

    navigator.clipboard.writeText(pk);
    this.copiedPublicKey = true;

    setTimeout(() => (this.copiedPublicKey = false), 1500);
  }

  // ---------------------------------------------------------------------------
  // PROJECT DETAILS DIALOG (SETTINGS / ANALYTICS / LOGS / KEYS)
  // ---------------------------------------------------------------------------

  // When opening dialog from the table
  openProjectDetails(p: UiProject): void {
    this.selectedProject = p;
    this.showProjectDialog = true;

    // Only reset here ON OPEN, not on every tab change
    this._projectDialogTab = 'overview';
    this.timeRange = '24h';

    this.error = undefined;
    this.analytics = null;
    this.logs = null;
    this.apiKeys = null;
    this.webhooks = null;

    this.devService.getProjectSettings(p.projectId).subscribe({
      next: (res: ProjectSettingsResponse) => {
        this.projectForm = {
          allowedDomains: (res.allowedDomains || []).join('\n'),
          webhookUrl: res.webhookUrl ?? '',
          satsPerLogin: res.satsPerLogin,
          maxAuthsPerIpPerHour: res.maxAuthsPerIpPerHour
        };
      },
      error: (err) => {
        this.error = this.extractErrorMessage(err)  || 'Failed to load project settings.';
      }
    });
  }

  private loadAnalytics(projectId: string) {
    this.error = undefined;

    this.devService.getProjectAnalytics(projectId, this.timeRange)
      .subscribe({
        next: (res) => {
          this.analytics = res;
        },
        error: (err) => {
          this.error = this.extractErrorMessage(err)  || 'Failed to load analytics.';
        }
      });

    this.devService.getProjectUsage(projectId)
      .subscribe({
        next: (res) => {
          this.usage = res;
        },
        error: (err) => {
          // Usage is optional, don't show error
          console.warn('Failed to load usage:', err);
        }
      });
  }

  private loadLogs(projectId: string) {
    this.error = undefined;

    this.devService.getProjectLogs(projectId, this.timeRange, 50)
      .subscribe({
        next: (res) => {
          this.logs = res;
        },
        error: (err) => {
          this.error = this.extractErrorMessage(err)  || 'Failed to load logs.';
        }
      });
  }

  private loadWebhooks(projectId: string): void {
    this.loadingWebhooks = true;
    this.error = undefined;

    this.devService.getProjectWebhooks(projectId, 50).subscribe({
      next: (res) => {
        this.webhooks = res.events;
        this.loadingWebhooks = false;
      },
      error: (err) => {
        this.loadingWebhooks = false;
        this.error = this.extractErrorMessage(err)  || 'Failed to load webhooks.';
      }
    });
  }

  replayWebhook(event: WebhookEventDto): void {
    if (!this.selectedProject) return;
    if (!confirm('Replay this webhook event?')) return;

    this.error = undefined;

    this.devService.replayProjectWebhook(this.selectedProject.projectId, event.id)
      .subscribe({
        next: () => {
          // Reload list to show updated attempts/status
          this.webhooks = null;
          this.loadWebhooks(this.selectedProject!.projectId);
        },
        error: (err) => {
          this.error = this.extractErrorMessage(err)  || 'Failed to replay webhook.';
        }
      });
  }

  saveProjectSettings(): void {
    if (!this.selectedProject) return;

    this.savingSettings = true;
    this.error = undefined;

    const payload = {
      allowedDomains: this.projectForm.allowedDomains
        .split('\n')
        .map(x => x.trim())
        .filter(Boolean),
      webhookUrl: this.projectForm.webhookUrl || null,
      satsPerLogin: this.projectForm.satsPerLogin ?? 0,
      maxAuthsPerIpPerHour: this.projectForm.maxAuthsPerIpPerHour ?? 0
    };

    this.devService.updateProjectSettings(this.selectedProject.projectId, payload)
      .subscribe({
        next: () => {
          this.savingSettings = false;
        },
        error: (err) => {
          this.savingSettings = false;
          this.error = this.extractErrorMessage(err)  || 'Failed to save project settings.';
        }
      });
  }

  // Utility to ensure date pipe gets a proper Date in local time
  // Accepts ISO string, epoch seconds, epoch milliseconds, or Date
  toDate(input: any): Date | null {
    if (!input) return null;
    if (input instanceof Date) return input;

    // Numeric-like strings
    const asStr = String(input).trim();
    if (/^\d+$/.test(asStr)) {
      const num = Number(asStr);
      // If 13 digits: ms, if 10 digits: seconds
      const ms = asStr.length >= 13 ? num : num * 1000;
      const d = new Date(ms);
      return isNaN(d.getTime()) ? null : d;
    }

    // ISO string
    const d = new Date(asStr);
    return isNaN(d.getTime()) ? null : d;
  }

  testWebhook(project: UiProject | null): void {
    if (!project) return;
    this.error = undefined;
    this.loading = true;

    this.devService.testProjectWebhook(project.projectId).subscribe({
      next: () => {
        this.loading = false;
        // optional: toast/snackbar
        console.log('Test webhook sent');
      },
      error: (err) => {
        this.loading = false;
        this.error = this.extractErrorMessage(err)  || 'Failed to send test webhook.';
      }
    });
  }

  // ---------------------------------------------------------------------------
  // API KEYS
  // ---------------------------------------------------------------------------

  private loadApiKeys(projectId: string): void {
    this.loadingKeys = true;
    this.error = undefined;

    this.devService.listProjectApiKeys(projectId).subscribe({
      next: (res) => {
        this.apiKeys = res.keys;
        this.loadingKeys = false;
      },
      error: (err) => {
        this.loadingKeys = false;
        this.error = this.extractErrorMessage(err)  || 'Failed to load API keys.';
      }
    });
  }

  openNewKeyDialog(): void {
    this.newKeyLabel = '';
    this.showNewKeyDialog = true;
  }

  createApiKey(): void {
    if (!this.selectedProject) return;

    const label = this.newKeyLabel.trim();
    this.creatingKey = true;
    this.error = undefined;

    this.devService.createProjectApiKey(this.selectedProject.projectId, { label }).subscribe({
      next: (res) => {
        this.creatingKey = false;
        this.showNewKeyDialog = false;

        // show secret once in separate dialog
        this.lastCreatedKey = res;
        this.showNewKeySecretDialog = true;

        // refresh list
        this.apiKeys = null;
        this.loadApiKeys(this.selectedProject!.projectId);
      },
      error: (err) => {
        this.creatingKey = false;
        this.error = this.extractErrorMessage(err)  || 'Failed to create API key.';
      }
    });
  }

  revokeApiKey(key: ProjectApiKeyDto): void {
    if (!this.selectedProject) return;
    if (!confirm(`Revoke key "${key.label}"? This cannot be undone.`)) return;

    this.error = undefined;

    this.devService.revokeProjectApiKey(this.selectedProject.projectId, key.id).subscribe({
      next: () => {
        // mark as inactive locally
        if (this.apiKeys) {
          this.apiKeys = this.apiKeys.map(k =>
            k.id === key.id ? { ...k, isActive: false } : k
          );
        }
      },
      error: (err) => {
        this.error = this.extractErrorMessage(err)  || 'Failed to revoke API key.';
      }
    });
  }

  renameApiKey(key: ProjectApiKeyDto): void {
    if (!this.selectedProject) return;

    const newLabel = prompt('New label for this key:', key.label);
    if (newLabel === null) return; // cancelled

    const trimmed = newLabel.trim();
    if (!trimmed) {
      alert('Label cannot be empty.');
      return;
    }

    this.error = undefined;

    this.devService.renameProjectApiKey(this.selectedProject.projectId, key.id, trimmed)
      .subscribe({
        next: () => {
          if (this.apiKeys) {
            this.apiKeys = this.apiKeys.map(k =>
              k.id === key.id ? { ...k, label: trimmed } : k
            );
          }
        },
        error: (err) => {
          this.error = this.extractErrorMessage(err)  || 'Failed to rename API key.';
        }
      });
  }

  copyNewKeySecret(): void {
    if (!this.lastCreatedKey?.secretKey) return;

    navigator.clipboard.writeText(this.lastCreatedKey.secretKey);
    this.copiedNewKeySecret = true;
    setTimeout(() => (this.copiedNewKeySecret = false), 1500);
  }

  // Toggle TEST/LIVE environment
  toggleProjectEnvironment(project: UiProject): void {
    const current = (project.environment ?? 'TEST').toUpperCase() as 'TEST' | 'LIVE';
    const next = current === 'LIVE' ? 'TEST' : 'LIVE';

    // 🔒 Safety confirmation when switching to LIVE
    if (next === 'LIVE') {
      if (project.plan === 'pro' && project.proPaidUntil) {
        const expires = new Date(project.proPaidUntil).getTime();
        const now = Date.now();

        if (expires < now) {
          alert(
            'Your Pro subscription has expired.\n\n' +
            'Please renew your subscription to enable LIVE mode.'
          );
          return;
        }
      }

      const configured = project.satsPerLogin ?? 0;
      const clamped = configured < 0 ? 0 : configured;

      const message = [
        `You are about to switch project "${project.name}" to LIVE mode.`,
        '',
        'In LIVE mode:',
        '  • Each successful login will create a real Lightning invoice.',
        `  • Current configured sats per login: ${clamped} sats.`,
        '  • Your users (or you) will actually pay sats on the Lightning Network.',
        '',
        'Make sure you have tested your integration thoroughly in TEST mode first.',
        '',
        'Do you want to proceed and enable LIVE mode for this project?'
      ].join('\n');

      if (!confirm(message)) {
        return;
      }
    } else {
      // Simple confirm when downgrading LIVE → TEST
      if (!confirm(`Switch project "${project.name}" from LIVE to TEST?`)) {
        return;
      }
    }

    this.error = undefined;
    this.loading = true;

    this.devService.updateProjectEnvironment(project.projectId, next).subscribe({
      next: () => {
        project.environment = next;

        // keep the details dialog in sync if it's open on this project
        if (this.selectedProject && this.selectedProject.projectId === project.projectId) {
          this.selectedProject.environment = next;
        }

        this.loading = false;
      },
      error: (err) => {
        this.loading = false;
        const errorMsg = this.extractErrorMessage(err) || 'Failed to update project environment.';
        this.error = errorMsg;
        
        // 🐛 FIX: Surface error to user immediately
        alert(errorMsg);
      }
    });
  }

  // Shows how many sats will *actually* be charged per login,
// based on current environment + satsPerLogin form value.
  getEffectiveSatsLabel(): string {
    if (!this.selectedProject) {
      return '';
    }

    const env = (this.selectedProject.environment ?? 'TEST').toUpperCase() as 'TEST' | 'LIVE';
    const configured = this.projectForm.satsPerLogin ?? 0;
    const clamped = configured < 0 ? 0 : configured;

    if (env === 'TEST') {
      return '0 sats (TEST mode – no Lightning payment required)';
    }

    if (clamped === 0) {
      return '0 sats (LIVE – effectively free, but still counted as LIVE traffic)';
    }

    return `${clamped} sats (LIVE – Lightning invoice per login)`;
  }

  getProjectBillingLabel(p: UiProject): string {
    const env = (p.environment ?? 'TEST').toUpperCase() as 'TEST' | 'LIVE';
    const configured = p.satsPerLogin ?? 0;
    const clamped = configured < 0 ? 0 : configured;

    if (env === 'TEST') {
      return '0 sats (TEST)';
    }

    if (clamped === 0) {
      return '0 sats (LIVE)';
    }

    return `${clamped} sats (LIVE)`;
  }

  getProjectModeBadge(p: UiProject | null): string {
    if (!p) return '';

    const env = (p.environment ?? 'TEST').toUpperCase() as 'TEST' | 'LIVE';
    const sats = p.satsPerLogin ?? 0;

    // TEST always wins
    if (env === 'TEST') {
      return 'TEST mode – invoices skipped';
    }

    // --- LIVE MODE ---

    if (p.plan === 'pro') {
      if (p.proPaidUntil) {
        const paidUntil = new Date(p.proPaidUntil).getTime();
        const now = Date.now();
        const graceEnd = paidUntil + 7 * 24 * 60 * 60 * 1000;

        if (now <= paidUntil) {
          return 'LIVE – Pro active';
        }

        if (now <= graceEnd) {
          return 'LIVE – Pro grace period';
        }

        return 'LIVE – Pro expired';
      }

      return 'LIVE – Pro (status unknown)';
    }

    // FREE plan in LIVE
    if (sats === 0) {
      return 'LIVE – Free (0 sats)';
    }

    return `LIVE – Free (${sats} sats)`;
  }

  getProjectModeSeverity(
    p: UiProject | null
  ): 'info' | 'success' | 'warn' | 'danger' {
    if (!p) return 'info';

    const env = (p.environment ?? 'TEST').toUpperCase();

    if (env === 'TEST') {
      return 'info';
    }

    if (p.plan === 'pro') {
      if (p.proPaidUntil) {
        const paidUntil = new Date(p.proPaidUntil).getTime();
        const now = Date.now();
        const graceEnd = paidUntil + 7 * 24 * 60 * 60 * 1000;

        if (now <= paidUntil) {
          return 'success'; // Pro active
        }

        if (now <= graceEnd) {
          return 'warn'; // ✅ PrimeNG uses "warn"
        }

        return 'danger'; // Expired
      }

      return 'warn';
    }

    // LIVE + free plan
    return 'info';
  }

  startBillingUpgrade() {
    if (!this.selectedProject) return;

    this.billingLoading = true;
    this.billingExpired = false;
    this.billingCopied = false;

    this.devService
      .createSubscriptionInvoice({
        projectId: this.selectedProject.projectId,
        plan: 'pro'
      })
      .subscribe({
        next: (session) => {
          this.billingSession = session;
          this.billingLoading = false;

          this.startBillingPolling();
          this.startBillingCountdown();
        },
        error: (err) => {
          this.billingLoading = false;
          this.error = this.extractErrorMessage(err)  || 'Failed to create subscription invoice.';
        }
      });

  }

  private startBillingPolling() {
    if (!this.billingSession || this.billingPolling) return;

    this.billingPolling = true;

    this.billingPollTimer = setInterval(() => {
      this.devService
        .confirmSubscription(this.billingSession!.sessionId)
        .subscribe({
          next: (res) => {
            if (res.paid) {
              this.stopBillingTimers();

              this.billingPolling = false;
              this.showBillingDialog = false;

              // 🔄 refresh projects to reflect PRO plan
              this.loadProjects();
            }
          },
          error: () => {
            // keep polling silently
          }
        });
    }, 2000);
  }

  private startBillingCountdown() {
    if (!this.billingSession) return;

    this.billingCountdownTimer = setInterval(() => {
      const remainingMs =
        this.billingSession!.expiresAtUnix * 1000 - Date.now();

      if (remainingMs <= 0) {
        this.billingExpired = true;
        this.stopBillingTimers();
        return;
      }

      const mins = Math.floor(remainingMs / 60000);
      const secs = Math.floor((remainingMs % 60000) / 1000);

      this.billingRemainingLabel = `${mins}:${secs
        .toString()
        .padStart(2, '0')}`;
    }, 1000);
  }

  private stopBillingTimers() {
    if (this.billingPollTimer) {
      clearInterval(this.billingPollTimer);
      this.billingPollTimer = undefined;
    }

    if (this.billingCountdownTimer) {
      clearInterval(this.billingCountdownTimer);
      this.billingCountdownTimer = undefined;
    }
  }

  copyBillingInvoice() {
    if (!this.billingSession?.invoice) return;

    navigator.clipboard.writeText(this.billingSession.invoice);
    this.billingCopied = true;

    setTimeout(() => (this.billingCopied = false), 1500);
  }

  onBillingDialogHide() {
    this.stopBillingTimers();
    this.resetBillingState();
  }


  protected getDate(proPaidUntil: Date) {
    return new Date(proPaidUntil).getTime() < Date.now();
  }

  isProExpired(p: UiProject): boolean {
    if (p.plan !== 'pro' || !p.proPaidUntil) return false;
    return new Date(p.proPaidUntil).getTime() < Date.now();
  }

  getProGraceDaysRemaining(p: UiProject): number | null {
    if (!p.proPaidUntil) return null;

    const paidUntil = new Date(p.proPaidUntil).getTime();
    const graceMs = 7 * 24 * 60 * 60 * 1000; // 7 days
    const graceEnd = paidUntil + graceMs;
    const now = Date.now();

    if (now <= paidUntil || now > graceEnd) return null;

    return Math.ceil((graceEnd - now) / (24 * 60 * 60 * 1000));
  }

  isInGracePeriod(p: UiProject): boolean {
    return this.getProGraceDaysRemaining(p) !== null;
  }

  private extractErrorMessage(err: any): string {
    if (!err) return 'Unexpected error';

    // Backend-standard error
    if (err.error?.message) return err.error.message;

    // Fallbacks
    if (typeof err.error === 'string') return err.error;
    if (typeof err.message === 'string') return err.message;

    return 'Something went wrong';
  }
}
