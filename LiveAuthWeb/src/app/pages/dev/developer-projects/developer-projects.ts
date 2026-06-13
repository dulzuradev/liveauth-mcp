import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';

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
  ProjectUsageResponse,
  McpToolDto,
  McpToolRevenueEventDto,
  McpToolRevenueOverviewResponse,
  McpToolRevenueSummaryResponse,
  CreateMcpToolRequest,
  TestMcpToolChargeResponse,
  LightningFeeSettingsResponse
} from '../../../services/developer-projects.service';

import {
  DevAuthService,
  DevStartLoginResponse,
  DevConfirmLoginResponse
} from '../../../services/dev-auth.service';
import { Tab, TabList, TabPanel, TabPanels, Tabs } from 'primeng/tabs';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import {LocalTimePipe} from '../../../directives/local-time.pipe';


//
// UI-only types
//

// Extend API DTO with UI fields used by the template
export type UiProject = ProjectDto;
export type ConsolePage = 'projects' | 'project-detail' | 'mcp';

export interface ProjectSettingsForm {
  allowedDomains: string;
  webhookUrl: string;
  satsPerLogin: number | null;
  maxAuthsPerIpPerHour: number | null;
  mcpSatsPerCall: number | null;
  mcpInvoiceCallCredits: number | null;
  mcpMaxSatsPerDay: number | null;
  mcpMaxCallsPerMinute: number | null;
  // Custom LND node config
  useCustomNode: boolean;
  lndBaseUrl: string;
  lndMacaroon: string;
}

export interface McpToolForm {
  projectId: string;
  name: string;
  slug: string;
  description: string;
  category: string;
  visibility: 'Private' | 'Unlisted' | 'Public';
  status: 'Draft' | 'Active' | 'Paused';
  defaultCostSats: number | null;
  minCostSats: number | null;
  maxCostSats: number | null;
  websiteUrl: string;
  docsUrl: string;
  webhookUrl: string;
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
    ToggleSwitchModule,
    LocalTimePipe
  ],
  templateUrl: './developer-projects.html',
  styleUrls: ['./developer-projects.css']
})
export class DeveloperProjectsComponent implements OnInit, OnDestroy {

  consolePage: ConsolePage = 'projects';
  routedProjectId = '';

  get isProjectsPage(): boolean {
    return this.consolePage === 'projects';
  }

  get isProjectDetailPage(): boolean {
    return this.consolePage === 'project-detail';
  }

  get isMcpPage(): boolean {
    return this.consolePage === 'mcp';
  }

  get consoleTitle(): string {
    if (this.isProjectDetailPage) {
      return this.selectedProject ? this.selectedProject.name : 'Project';
    }

    return this.isMcpPage ? 'MCP Tools' : 'Projects';
  }

  get consoleSubtitle(): string {
    if (!this.loggedIn) {
      return 'Sign in to manage your LiveAuth workspace';
    }

    if (this.isProjectDetailPage) {
      return this.selectedProject?.projectId ?? 'Project workspace';
    }

    if (this.isMcpPage) {
      return 'Tool registry and paid-call ledger';
    }

    return `Welcome back, ${this.developerEmail || 'developer'}`;
  }

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
  githubEnabled = false;

  // Email / Password Auth State
  emailMode: 'login' | 'register' = 'login';
  emailForm = { email: '', password: '', confirmPassword: '' };
  emailLoading = false;
  emailSuccess = '';
  emailError = '';
  showForgotPassword = false;
  forgotPasswordEmail = '';
  forgotPasswordLoading = false;
  forgotPasswordSuccess = false;

  // Login tab selection ('github' | 'lightning' | 'email')
  selectedLoginTab = 'github';

  // Convenience getters for template bindings with whitespace
  get emailTabToggleLabel(): string {
    return this.emailMode === 'login' ? 'Need an account?' : 'Have an account?';
  }

  get emailSubmitLabel(): string {
    return this.emailMode === 'login' ? 'Sign In' : 'Create Account';
  }

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

  // Project detail route state
  selectedProject: UiProject | null = null;
  private loadedProjectDetailId = '';

  projectForm: ProjectSettingsForm = {
    allowedDomains: '',
    webhookUrl: '',
    satsPerLogin: null,
    maxAuthsPerIpPerHour: null,
    mcpSatsPerCall: 1,
    mcpInvoiceCallCredits: 10,
    mcpMaxSatsPerDay: 10000,
    mcpMaxCallsPerMinute: 60,
    useCustomNode: false,
    lndBaseUrl: '',
    lndMacaroon: ''
  };

  savingSettings = false;

  // Webhooks
  webhooks: WebhookEventDto[] | null = null;
  loadingWebhooks = false;

  // MCP tool revenue dashboard
  mcpTools: McpToolDto[] | null = null;
  selectedMcpToolId = '';
  mcpRevenueOverview: McpToolRevenueOverviewResponse | null = null;
  mcpRevenue: McpToolRevenueSummaryResponse | null = null;
  mcpRevenueEvents: McpToolRevenueEventDto[] | null = null;
  mcpRevenueRange: '1h' | '24h' | '7d' = '24h';
  loadingMcpTools = false;
  loadingMcpRevenueOverview = false;
  loadingMcpRevenue = false;
  showMcpToolDialog = false;
  editingMcpTool: McpToolDto | null = null;
  savingMcpTool = false;
  copiedMcpSnippet = false;
  copiedMcpSnippetKey = '';
  mcpToolForm: McpToolForm = this.createEmptyMcpToolForm();
  mcpTestProjectId = '';
  mcpTestCostSats: number | null = null;
  testingMcpTool = false;
  mcpTestResult: TestMcpToolChargeResponse | null = null;
  mcpTestError = '';
  mcpWebhookEvents: WebhookEventDto[] | null = null;
  loadingMcpWebhookEvents = false;
  lightningFeeSettings: LightningFeeSettingsResponse | null = null;

  // Tabs
  _projectDialogTab: 'overview' | 'analytics' | 'usage' | 'logs' | 'keys' | 'billing' | 'webhooks' = 'overview';
  timeRange: '1h' | '24h' | '7d' = '24h';

  private get windowHours(): number {
    switch (this.timeRange) {
      case '1h':  return 1;
      case '7d':  return 24 * 7;
      default:    return 24;
    }
  }

  get mcpWindowLabel(): string {
    switch (this.mcpRevenueRange) {
      case '1h':
        return 'Last 1 hour';
      case '7d':
        return 'Last 7 days';
      default:
        return 'Last 24 hours';
    }
  }

  get feeDisclosureText(): string {
    const settings = this.lightningFeeSettings;
    if (!settings) {
      return 'LiveAuth charges 2% on Lightning auth invoices, minimum 1 sat, 15% on bundle purchases, and 5% on paid MCP tool calls.';
    }

    return `LiveAuth charges ${this.formatFee(settings.invoiceFeeBasisPoints, settings.invoiceMinimumFeeSats)} on Lightning auth invoices, ${this.formatFee(settings.bundleMarkupBasisPoints, settings.bundleMarkupMinimumFeeSats)} on bundle purchases, and ${this.formatFee(settings.mcpPaidToolFeeBasisPoints, settings.mcpPaidToolMinimumFeeSats)} on paid MCP tool calls.`;
  }

  // Called by (valueChange) on <p-tabs>
  setProjectDialogTab(tab: 'overview' | 'analytics' | 'usage' | 'logs' | 'keys' | 'billing' | 'webhooks' | any) {
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
    private devService: DeveloperProjectsService,
    private http: HttpClient,
    private route: ActivatedRoute,
    private router: Router
  ) {}

  private syncRouteState(): void {
    const page = this.route.snapshot.data['consolePage'] as ConsolePage | undefined;
    const projectId = this.route.snapshot.paramMap.get('projectId') ?? '';

    this.routedProjectId = projectId;
    this.consolePage = projectId ? 'project-detail' : page ?? 'projects';
  }

  private handleConsoleRoute(): void {
    if (!this.loggedIn) return;

    if (this.isProjectDetailPage) {
      this.openProjectDetailsById(this.routedProjectId);
      return;
    }

    this.selectedProject = null;
    this.loadedProjectDetailId = '';

    if (this.isMcpPage && this.mcpTools === null) {
      this.loadMcpTools();
    }
  }

  private openProjectDetailsById(projectId: string): void {
    if (!projectId) return;

    const project = this.projects.find(p => p.projectId === projectId);
    if (!project) {
      if (!this.loading && this.projects.length > 0) {
        this.error = 'Project not found.';
      }
      return;
    }

    if (this.loadedProjectDetailId === project.projectId && this.selectedProject?.projectId === project.projectId) {
      return;
    }

    this.activateProjectDetails(project);
  }

  navigateToConsole(page: 'projects' | 'mcp'): void {
    this.router.navigate([page === 'mcp' ? '/dev/mcp' : '/dev/projects']);
  }

  goBackToProjects(): void {
    this.router.navigate(['/dev/projects']);
  }

  // ---------------------------------------------------------------------------
  // LIFECYCLE
  // ---------------------------------------------------------------------------

  ngOnInit() {
    this.syncRouteState();
    this.route.paramMap.subscribe(() => {
      this.syncRouteState();
      this.handleConsoleRoute();
    });
    this.route.data.subscribe(() => {
      this.syncRouteState();
      this.handleConsoleRoute();
    });

    // Check for token in URL (from GitHub OAuth redirect)
    const urlParams = new URLSearchParams(window.location.search);
    const authMode = urlParams.get('mode');
    if (authMode === 'register' || authMode === 'login') {
      this.selectedLoginTab = 'email';
      this.emailMode = authMode === 'register' ? 'register' : 'login';
    }

    const githubError = urlParams.get('githubError');
    if (githubError) {
      this.devAuth.clearToken();
      this.loggedIn = false;
      this.selectedLoginTab = 'github';
      this.error = this.githubLoginErrorMessage(githubError);
      window.history.replaceState({}, '', '/dev/projects');
    }

    const tokenFromUrl = urlParams.get('token');
    if (tokenFromUrl) {
      this.devAuth.saveToken(tokenFromUrl);
      // Remove token from URL
      window.history.replaceState({}, '', '/dev/projects');
      this.loggedIn = true;
      this.loadProjects();
    }

    // Check if GitHub OAuth is enabled
    this.devAuth.getGitHubStatus().subscribe({
      next: (res) => {
        this.githubEnabled = res.enabled;
      },
      error: () => {
        this.githubEnabled = false;
      }
    });

    const jwt = this.devAuth.getToken();
    if (jwt) {
      this.loggedIn = true;
      this.loadProjects();
    }
  }

  ngOnDestroy() {
    this.stopPolling();
    this.stopCountdown();
    this.stopBillingTimers();
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

  // GitHub OAuth login
  startGitHubLogin(): void {
    console.log('Starting GitHub login...');
    // Auto-detect dev environment: use bypass if API is localhost
    const isLocalApi = this.devAuth.getApiUrl().includes('localhost');
    this.devAuth.startGitHubLogin(isLocalApi);
  }

  logout() {
    // Call the backend to clear the GitHub OAuth state cookie
    this.http.post(`${this.devAuth.getApiUrl()}/api/dev/auth/logout`, {}, { withCredentials: true }).subscribe({
      next: () => {
        // Clear local token and reset state
        this.devAuth.clearToken();
        this.loggedIn = false;
        this.projects = [];
        this.selectedProject = null;
        this.resetMcpRevenueState();
        this.loginSession = undefined;
        this.stopPolling();
        this.stopCountdown();
      },
      error: () => {
        // Still clear local state even if the API call fails
        this.devAuth.clearToken();
        this.loggedIn = false;
        this.projects = [];
        this.selectedProject = null;
        this.resetMcpRevenueState();
        this.loginSession = undefined;
        this.stopPolling();
        this.stopCountdown();
      }
    });
  }

  private githubLoginErrorMessage(error: string): string {
    if (error === 'invalid_state') {
      return 'GitHub sign-in expired. Please try again.';
    }

    if (error === 'missing_code') {
      return 'GitHub did not return an authorization code. Please try again.';
    }

    return 'GitHub sign-in could not be completed. Please try again.';
  }

  // ---------------------------------------------------------------------------
  // EMAIL / PASSWORD AUTH
  // ---------------------------------------------------------------------------

  toggleEmailMode() {
    this.emailMode = this.emailMode === 'login' ? 'register' : 'login';
    this.emailError = '';
    this.emailSuccess = '';
  }

  resetEmailForm() {
    this.emailForm = { email: '', password: '', confirmPassword: '' };
    this.emailError = '';
    this.emailSuccess = '';
    this.emailLoading = false;
  }

  emailLogin() {
    this.emailError = '';
    const { email, password } = this.emailForm;

    if (!email || !password) {
      this.emailError = 'Email and password are required.';
      return;
    }

    this.emailLoading = true;

    this.devAuth.emailLogin({ email, password }).subscribe({
      next: (res) => {
        this.emailLoading = false;
        if (res.verified && res.token) {
          this.devAuth.saveToken(res.token);
          this.loggedIn = true;
          this.loginDialogVisible = false;
          this.resetEmailForm();
          this.loadProjects();
        } else {
          this.emailError = res.message || 'Login failed.';
        }
      },
      error: (err) => {
        this.emailLoading = false;
        this.emailError = this.extractErrorMessage(err) || 'Login failed.';
      }
    });
  }

  emailRegister() {
    this.emailError = '';
    const { email, password, confirmPassword } = this.emailForm;

    if (!email || !password) {
      this.emailError = 'Email and password are required.';
      return;
    }

    if (password.length < 12) {
      this.emailError = 'Password must be at least 12 characters.';
      return;
    }

    if (password !== confirmPassword) {
      this.emailError = 'Passwords do not match.';
      return;
    }

    this.emailLoading = true;

    this.devAuth.register({ email, password }).subscribe({
      next: (res) => {
        this.emailLoading = false;
        if (res.emailSent) {
          this.emailSuccess = 'Check your email to verify your address.';
          this.emailForm = { email, password: '', confirmPassword: '' };
        } else {
          this.emailError = res.message || 'Registration failed.';
        }
      },
      error: (err) => {
        this.emailLoading = false;
        this.emailError = this.extractErrorMessage(err) || 'Registration failed.';
      }
    });
  }

  sendForgotPassword() {
    if (!this.forgotPasswordEmail) {
      return;
    }

    this.forgotPasswordLoading = true;

    this.devAuth.forgotPassword({ email: this.forgotPasswordEmail }).subscribe({
      next: (res) => {
        this.forgotPasswordLoading = false;
        this.forgotPasswordSuccess = true;
      },
      error: (err) => {
        this.forgotPasswordLoading = false;
        // Still show success to prevent email enumeration
        this.forgotPasswordSuccess = true;
      }
    });
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
        if (this.selectedProject) {
          this.selectedProject = this.projects.find(p => p.projectId == this.selectedProject?.projectId) ?? null;
        }
        this.loading = false;
        this.handleConsoleRoute();
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

  private resetMcpRevenueState(): void {
    this.mcpTools = null;
    this.selectedMcpToolId = '';
    this.mcpRevenueOverview = null;
    this.mcpRevenue = null;
    this.mcpRevenueEvents = null;
    this.mcpWebhookEvents = null;
    this.mcpTestResult = null;
    this.mcpTestError = '';
    this.mcpTestProjectId = '';
    this.mcpTestCostSats = null;
    this.loadingMcpTools = false;
    this.loadingMcpRevenueOverview = false;
    this.loadingMcpRevenue = false;
    this.loadingMcpWebhookEvents = false;
  }

  loadMcpTools(): void {
    this.loadingMcpTools = true;
    const previousSelectedToolId = this.selectedMcpToolId;
    this.loadLightningFeeSettings();

    this.devService.listMcpTools().subscribe({
      next: (res) => {
        this.mcpTools = res.tools ?? [];
        this.loadingMcpTools = false;
        this.loadMcpRevenueOverview();

        if (!this.mcpTools.length) {
          this.selectedMcpToolId = '';
          this.mcpRevenue = null;
          this.mcpRevenueEvents = null;
          return;
        }

        const selectedStillVisible = this.mcpTools.some(t => t.id === this.selectedMcpToolId);
        if (!selectedStillVisible) {
          this.selectedMcpToolId = this.mcpTools[0].id;
        }

        if (this.selectedMcpToolId !== previousSelectedToolId || !this.mcpTestProjectId) {
          this.resetMcpSetupStateForSelectedTool();
        }

        this.loadMcpRevenue();
      },
      error: (err) => {
        this.loadingMcpTools = false;
        this.mcpTools = [];
        this.mcpRevenueOverview = null;
        console.warn('Failed to load MCP tools:', err);
      }
    });
  }

  selectMcpTool(tool: McpToolDto): void {
    if (this.selectedMcpToolId === tool.id) return;
    this.selectedMcpToolId = tool.id;
    this.resetMcpSetupStateForSelectedTool();
    this.loadMcpRevenue();
  }

  onMcpRevenueRangeChange(range: '1h' | '24h' | '7d'): void {
    this.mcpRevenueRange = range;
    this.loadMcpRevenueOverview();
    this.loadMcpRevenue();
  }

  private loadMcpRevenueOverview(): void {
    this.loadingMcpRevenueOverview = true;

    this.devService.getMcpToolsRevenueOverview(this.mcpRevenueRange, 10).subscribe({
      next: (res) => {
        this.mcpRevenueOverview = res;
        this.loadingMcpRevenueOverview = false;
      },
      error: (err) => {
        this.loadingMcpRevenueOverview = false;
        this.mcpRevenueOverview = null;
        console.warn('Failed to load MCP revenue overview:', err);
      }
    });
  }

  private loadMcpRevenue(): void {
    if (!this.selectedMcpToolId) return;

    this.loadingMcpRevenue = true;
    this.error = undefined;

    this.devService.getMcpToolRevenue(this.selectedMcpToolId, this.mcpRevenueRange).subscribe({
      next: (res) => {
        this.mcpRevenue = res;
        this.loadingMcpRevenue = false;
      },
      error: (err) => {
        this.loadingMcpRevenue = false;
        this.mcpRevenue = null;
        this.error = this.extractErrorMessage(err) || 'Failed to load MCP tool revenue.';
      }
    });

    this.devService.getMcpToolRevenueEvents(this.selectedMcpToolId, 50).subscribe({
      next: (res) => {
        this.mcpRevenueEvents = res.events ?? [];
      },
      error: (err) => {
        this.mcpRevenueEvents = [];
        console.warn('Failed to load MCP revenue events:', err);
      }
    });
  }

  private loadLightningFeeSettings(): void {
    this.devService.getLightningFeeSettings().subscribe({
      next: (settings) => {
        this.lightningFeeSettings = settings;
      },
      error: (err) => {
        console.warn('Failed to load Lightning fee settings:', err);
      }
    });
  }

  private formatFee(basisPoints: number, minimumFeeSats: number): string {
    if (basisPoints <= 0) return '0%';

    const percent = basisPoints / 100;
    const percentText = Number.isInteger(percent)
      ? percent.toFixed(0)
      : percent.toFixed(2).replace(/0+$/, '').replace(/\.$/, '');

    return minimumFeeSats > 0
      ? `${percentText}%, minimum ${minimumFeeSats} sat${minimumFeeSats === 1 ? '' : 's'}`
      : `${percentText}%`;
  }

  get selectedMcpTool(): McpToolDto | null {
    return this.mcpTools?.find(t => t.id === this.selectedMcpToolId) ?? null;
  }

  selectMcpToolById(toolId: string): void {
    const tool = this.mcpTools?.find(t => t.id === toolId);
    if (!tool) return;
    this.selectMcpTool(tool);
  }

  get selectedMcpProject(): UiProject | null {
    const tool = this.selectedMcpTool;
    return this.projects.find(p => p.projectId === (this.mcpTestProjectId || tool?.projectId)) ??
      this.projects.find(p => p.projectId === tool?.projectId) ??
      this.projects[0] ??
      null;
  }

  get mcpToolIntegrationSnippet(): string {
    return this.mcpToolIdIntegrationSnippet;
  }

  get mcpToolIdIntegrationSnippet(): string {
    const tool = this.selectedMcpTool;
    const project = this.selectedMcpProject;
    const publicKey = project?.publicKey || 'la_pk_your_project_public_key';
    const toolId = tool?.id || 'your-tool-id';
    const methodName = this.selectedMcpMethodName;

    return `import { createMcpGate } from '@liveauth-labs/mcp-server';

const gate = createMcpGate({
  publicKey: process.env.LIVEAUTH_PUBLIC_KEY ?? '${publicKey}',
  baseUrl: process.env.LIVEAUTH_API_URL ?? 'https://api.liveauth.app',
  toolId: process.env.LIVEAUTH_TOOL_ID ?? '${toolId}',
});

const result = await gate.invoke(
  liveAuthJwt,
  input,
  async (args) => runYourTool(args),
  { requestId },
  {
    toolMethodName: '${methodName}',
    idempotencyKey: requestId,
    metadata: { operation: '${methodName}' },
  }
);`;
  }

  get mcpToolNameIntegrationSnippet(): string {
    const tool = this.selectedMcpTool;
    const project = this.selectedMcpProject;
    const publicKey = project?.publicKey || 'la_pk_your_project_public_key';
    const toolName = tool?.slug || 'paid-research-tool';
    const methodName = this.selectedMcpMethodName;

    return `import { createMcpGate } from '@liveauth-labs/mcp-server';

const gate = createMcpGate({
  publicKey: process.env.LIVEAUTH_PUBLIC_KEY ?? '${publicKey}',
  baseUrl: process.env.LIVEAUTH_API_URL ?? 'https://api.liveauth.app',
  toolName: '${toolName}',
});

const result = await gate.invoke(
  liveAuthJwt,
  input,
  async (args) => runYourTool(args),
  { requestId },
  {
    toolMethodName: '${methodName}',
    idempotencyKey: requestId,
    metadata: { operation: '${methodName}' },
  }
);`;
  }

  get mcpToolChargeByIdCurlSnippet(): string {
    const tool = this.selectedMcpTool;
    const project = this.selectedMcpProject;
    const publicKey = project?.publicKey || 'la_pk_your_project_public_key';
    const toolId = tool?.id || 'your-tool-id';
    const methodName = this.selectedMcpMethodName;
    const cost = tool?.defaultCostSats || 1;

    return `curl -X POST https://api.liveauth.app/api/mcp/tools/${toolId}/charge \\
  -H "Content-Type: application/json" \\
  -H "Authorization: Bearer $LIVEAUTH_MCP_JWT" \\
  -H "X-LW-Public: ${publicKey}" \\
  -d '{
    "toolMethodName": "${methodName}",
    "callCostSats": ${cost},
    "idempotencyKey": "request-or-call-id",
    "metadata": {
      "operation": "${methodName}"
    }
  }'`;
  }

  get mcpToolChargeByNameCurlSnippet(): string {
    const tool = this.selectedMcpTool;
    const project = this.selectedMcpProject;
    const publicKey = project?.publicKey || 'la_pk_your_project_public_key';
    const toolName = tool?.slug || 'paid-research-tool';
    const methodName = this.selectedMcpMethodName;

    return `curl -X POST https://api.liveauth.app/api/mcp/charge \\
  -H "Content-Type: application/json" \\
  -H "Authorization: Bearer $LIVEAUTH_MCP_JWT" \\
  -H "X-LW-Public: ${publicKey}" \\
  -d '{
    "toolName": "${toolName}",
    "toolMethodName": "${methodName}",
    "idempotencyKey": "request-or-call-id"
  }'`;
  }

  get mcpTestResultJson(): string {
    return this.mcpTestResult ? JSON.stringify(this.mcpTestResult, null, 2) : '';
  }

  get mcpPaidWebhookEvents(): WebhookEventDto[] {
    return (this.mcpWebhookEvents ?? [])
      .filter(e => e.eventType.includes('mcp.tool.paid_call'))
      .slice(0, 5);
  }

  private get selectedMcpMethodName(): string {
    return (this.selectedMcpTool?.slug || 'my_tool').replace(/-/g, '_');
  }

  openCreateMcpToolDialog(): void {
    this.editingMcpTool = null;
    this.mcpToolForm = this.createEmptyMcpToolForm();
    this.showMcpToolDialog = true;
  }

  openEditMcpToolDialog(tool: McpToolDto): void {
    if (!tool.developerId) return;

    this.editingMcpTool = tool;
    this.mcpToolForm = {
      projectId: tool.projectId ?? '',
      name: tool.name,
      slug: tool.slug,
      description: tool.description ?? '',
      category: tool.category ?? '',
      visibility: (tool.visibility as McpToolForm['visibility']) || 'Private',
      status: (tool.status === 'Removed' ? 'Paused' : tool.status as McpToolForm['status']) || 'Draft',
      defaultCostSats: tool.defaultCostSats,
      minCostSats: tool.minCostSats,
      maxCostSats: tool.maxCostSats,
      websiteUrl: tool.websiteUrl ?? '',
      docsUrl: tool.docsUrl ?? '',
      webhookUrl: tool.webhookUrl ?? ''
    };
    this.showMcpToolDialog = true;
  }

  saveMcpTool(): void {
    this.error = undefined;
    const req = this.buildMcpToolRequest();
    if (!req) return;

    this.savingMcpTool = true;
    const request$ = this.editingMcpTool
      ? this.devService.updateMcpTool(this.editingMcpTool.id, req)
      : this.devService.createMcpTool(req as CreateMcpToolRequest);

    request$.subscribe({
      next: (tool) => {
        this.savingMcpTool = false;
        this.showMcpToolDialog = false;
        this.selectedMcpToolId = tool.id;
        this.loadMcpTools();
      },
      error: (err) => {
        this.savingMcpTool = false;
        this.error = this.extractErrorMessage(err) || 'Failed to save MCP tool.';
      }
    });
  }

  deleteMcpTool(tool: McpToolDto): void {
    if (!tool.developerId) return;
    if (!confirm(`Delete "${tool.name}"? Existing revenue events will remain visible in the ledger.`)) return;

    this.devService.deleteMcpTool(tool.id).subscribe({
      next: () => {
        this.selectedMcpToolId = '';
        this.loadMcpTools();
      },
      error: (err) => {
        this.error = this.extractErrorMessage(err) || 'Failed to delete MCP tool.';
      }
    });
  }

  copyMcpIntegrationSnippet(): void {
    this.copyMcpText(this.mcpToolIdIntegrationSnippet, 'tool-id-snippet');
  }

  copyMcpText(text: string, key: string): void {
    if (!text) return;
    navigator.clipboard.writeText(text);
    this.copiedMcpSnippet = true;
    this.copiedMcpSnippetKey = key;
    setTimeout(() => {
      this.copiedMcpSnippet = false;
      if (this.copiedMcpSnippetKey === key) {
        this.copiedMcpSnippetKey = '';
      }
    }, 1500);
  }

  onMcpTestProjectChange(): void {
    this.mcpTestResult = null;
    this.mcpTestError = '';
    this.loadMcpWebhookEvents();
  }

  testSelectedMcpToolPaidCall(): void {
    const tool = this.selectedMcpTool;
    if (!tool) return;

    const projectId = this.mcpTestProjectId || tool.projectId || this.projects[0]?.projectId || '';
    if (!projectId) {
      this.mcpTestError = 'Create a project before testing a paid MCP tool.';
      return;
    }

    const callCostSats = Number(this.mcpTestCostSats ?? tool.defaultCostSats);
    if (callCostSats < tool.minCostSats || (tool.maxCostSats > 0 && callCostSats > tool.maxCostSats)) {
      this.mcpTestError = `Test cost must be between ${tool.minCostSats} and ${tool.maxCostSats} sats.`;
      return;
    }

    this.testingMcpTool = true;
    this.mcpTestError = '';
    this.mcpTestResult = null;

    this.devService.testMcpToolCharge(tool.id, {
      projectId,
      callCostSats,
      toolMethodName: this.selectedMcpMethodName,
      agentId: 'dashboard-test',
      metadata: {
        source: 'developer-dashboard',
        toolSlug: tool.slug
      }
    }).subscribe({
      next: (res) => {
        this.testingMcpTool = false;
        this.mcpTestResult = res;
        this.loadMcpWebhookEvents(projectId);
      },
      error: (err) => {
        this.testingMcpTool = false;
        this.mcpTestError = this.extractErrorMessage(err) || 'Failed to run test paid call.';
      }
    });
  }

  private resetMcpSetupStateForSelectedTool(): void {
    const tool = this.selectedMcpTool;
    this.mcpTestResult = null;
    this.mcpTestError = '';
    this.mcpTestCostSats = tool?.defaultCostSats ?? null;
    this.mcpTestProjectId = tool?.projectId ?? this.projects[0]?.projectId ?? '';
    this.mcpWebhookEvents = null;
    this.loadMcpWebhookEvents();
  }

  loadMcpWebhookEvents(projectId: string = this.mcpTestProjectId): void {
    if (!projectId) {
      this.mcpWebhookEvents = [];
      return;
    }

    this.loadingMcpWebhookEvents = true;
    this.devService.getProjectWebhooks(projectId, 50).subscribe({
      next: (res) => {
        this.mcpWebhookEvents = res.events ?? [];
        this.loadingMcpWebhookEvents = false;
      },
      error: (err) => {
        this.mcpWebhookEvents = [];
        this.loadingMcpWebhookEvents = false;
        console.warn('Failed to load MCP webhook events:', err);
      }
    });
  }

  getMcpToolWebhookMode(tool: McpToolDto): string {
    return tool.webhookUrl ? 'Tool webhook' : 'Project fallback';
  }

  getMcpToolWebhookDestinationLabel(tool: McpToolDto): string {
    return tool.webhookUrl || 'Uses the project webhook URL when configured';
  }

  getWebhookStatusSeverity(status: string): 'success' | 'info' | 'warn' | 'danger' | 'secondary' | 'contrast' {
    switch ((status || '').toLowerCase()) {
      case 'delivered':
        return 'success';
      case 'dead':
      case 'failed':
        return 'danger';
      case 'inprogress':
      case 'pending':
        return 'warn';
      default:
        return 'info';
    }
  }

  private createEmptyMcpToolForm(): McpToolForm {
    return {
      projectId: '',
      name: '',
      slug: '',
      description: '',
      category: '',
      visibility: 'Private',
      status: 'Draft',
      defaultCostSats: 1,
      minCostSats: 1,
      maxCostSats: 100,
      websiteUrl: '',
      docsUrl: '',
      webhookUrl: ''
    };
  }

  private buildMcpToolRequest(): CreateMcpToolRequest | null {
    const name = this.mcpToolForm.name.trim();
    if (!name) {
      this.error = 'Tool name is required.';
      return null;
    }

    const minCostSats = Number(this.mcpToolForm.minCostSats ?? 1);
    const defaultCostSats = Number(this.mcpToolForm.defaultCostSats ?? minCostSats);
    const maxCostSats = Number(this.mcpToolForm.maxCostSats ?? defaultCostSats);

    if (minCostSats < 1 || defaultCostSats < minCostSats || maxCostSats < defaultCostSats) {
      this.error = 'Cost bounds must satisfy 1 <= min <= default <= max.';
      return null;
    }

    return {
      clearProject: this.editingMcpTool ? !this.mcpToolForm.projectId : null,
      projectId: this.mcpToolForm.projectId || null,
      name,
      slug: this.mcpToolForm.slug.trim() || null,
      description: this.mcpToolForm.description.trim() || null,
      category: this.mcpToolForm.category.trim() || null,
      visibility: this.mcpToolForm.visibility,
      status: this.mcpToolForm.status,
      defaultCostSats,
      minCostSats,
      maxCostSats,
      websiteUrl: this.mcpToolForm.websiteUrl.trim() || null,
      docsUrl: this.mcpToolForm.docsUrl.trim() || null,
      webhookUrl: this.mcpToolForm.webhookUrl.trim() || null
    };
  }

  getMcpToolScopeLabel(tool: McpToolDto): string {
    if (tool.projectId) return 'Project tool';
    if (tool.developerId) return 'Developer tool';
    return 'First-party';
  }

  getMcpToolStatusSeverity(status: string): 'success' | 'info' | 'warn' | 'danger' | 'secondary' | 'contrast' {
    switch ((status || '').toLowerCase()) {
      case 'active':
        return 'success';
      case 'paused':
        return 'warn';
      case 'removed':
        return 'danger';
      default:
        return 'info';
    }
  }

  getMcpRevenueMetadataLabel(event: McpToolRevenueEventDto): string {
    if (!event.metadataJson) return '—';

    try {
      const metadata = JSON.parse(event.metadataJson);
      return metadata.urlHost || metadata.host || metadata.url || event.metadataJson;
    } catch {
      return event.metadataJson;
    }
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

  deleteProject(project: any) {
    if (!confirm(`Are you sure you want to delete "${project.name}"? This cannot be undone.`)) {
      return;
    }

    this.error = undefined;

    this.devService.deleteProject(project.projectId).subscribe({
      next: () => {
        // Remove from list
        this.projects = this.projects.filter(p => p.projectId !== project.projectId);
        if (this.selectedProject?.projectId === project.projectId) {
          this.selectedProject = null;
          this.loadedProjectDetailId = '';
          if (this.isProjectDetailPage) {
            this.goBackToProjects();
          }
        }
      },
      error: (err) => {
        this.error = this.extractErrorMessage(err) || 'Failed to delete project.';
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
  // PROJECT DETAIL ROUTE (SETTINGS / ANALYTICS / LOGS / KEYS)
  // ---------------------------------------------------------------------------

  openProjectDetails(p: UiProject): void {
    this.router.navigate(['/dev/projects', p.projectId]);
  }

  private activateProjectDetails(p: UiProject): void {
    if (this.loadedProjectDetailId === p.projectId && this.selectedProject?.projectId === p.projectId) {
      return;
    }

    this.loadedProjectDetailId = p.projectId;
    this.selectedProject = p;

    this._projectDialogTab = 'overview';
    this.timeRange = '24h';

    this.error = undefined;
    this.analytics = null;
    this.logs = null;
    this.apiKeys = null;
    this.webhooks = null;

    this.devService.getProjectSettings(p.projectId).subscribe({
      next: (res: ProjectSettingsResponse) => {
        this.applyProjectSettings(res);
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

    // Only include macaroon if it was changed (not masked)
    const includeMacaroon = this.projectForm.lndMacaroon && 
                           !this.projectForm.lndMacaroon.startsWith('••');

    const payload: any = {
      allowedDomains: this.projectForm.allowedDomains
        .split('\n')
        .map(x => x.trim())
        .filter(Boolean),
      webhookUrl: this.projectForm.webhookUrl || null,
      satsPerLogin: this.projectForm.satsPerLogin ?? 0,
      maxAuthsPerIpPerHour: this.projectForm.maxAuthsPerIpPerHour ?? 0,
      mcpSatsPerCall: this.projectForm.mcpSatsPerCall ?? 1,
      mcpInvoiceCallCredits: this.projectForm.mcpInvoiceCallCredits ?? 10,
      mcpMaxSatsPerDay: this.projectForm.mcpMaxSatsPerDay ?? 10000,
      mcpMaxCallsPerMinute: this.projectForm.mcpMaxCallsPerMinute ?? 60,
      useCustomNode: this.projectForm.useCustomNode,
      lndBaseUrl: this.projectForm.lndBaseUrl || null
    };

    if (includeMacaroon) {
      payload.lndMacaroon = this.projectForm.lndMacaroon;
    }

    this.devService.updateProjectSettings(this.selectedProject.projectId, payload)
      .subscribe({
        next: (res) => {
          this.savingSettings = false;
          if (res) {
            this.applyProjectSettings(res);
          } else {
            this.devService.getProjectSettings(this.selectedProject!.projectId).subscribe({
              next: (settings) => this.applyProjectSettings(settings),
              error: (err) => {
                this.error = this.extractErrorMessage(err) || 'Settings saved, but failed to reload them.';
              }
            });
          }
        },
        error: (err) => {
          this.savingSettings = false;
          this.error = this.extractErrorMessage(err)  || 'Failed to save project settings.';
        }
      });
  }

  private applyProjectSettings(res: ProjectSettingsResponse): void {
    const hasMcpSettings =
      res.mcpSatsPerCall != null &&
      res.mcpInvoiceCallCredits != null &&
      res.mcpMaxSatsPerDay != null &&
      res.mcpMaxCallsPerMinute != null;

    this.projectForm = {
      allowedDomains: (res.allowedDomains || []).join('\n'),
      webhookUrl: res.webhookUrl ?? '',
      satsPerLogin: res.satsPerLogin,
      maxAuthsPerIpPerHour: res.maxAuthsPerIpPerHour,
      mcpSatsPerCall: res.mcpSatsPerCall ?? this.projectForm.mcpSatsPerCall ?? 1,
      mcpInvoiceCallCredits: res.mcpInvoiceCallCredits ?? this.projectForm.mcpInvoiceCallCredits ?? 10,
      mcpMaxSatsPerDay: res.mcpMaxSatsPerDay ?? this.projectForm.mcpMaxSatsPerDay ?? 10000,
      mcpMaxCallsPerMinute: res.mcpMaxCallsPerMinute ?? this.projectForm.mcpMaxCallsPerMinute ?? 60,
      useCustomNode: res.useCustomNode ?? false,
      lndBaseUrl: res.lndBaseUrl ?? '',
      lndMacaroon: res.lndMacaroon ? '••••••••' : ''
    };

    if (!hasMcpSettings) {
      this.error = 'The API did not return MCP gate settings. Restart or redeploy the backend so the new settings fields are available.';
    }
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

  testingLnd = false;
  lndTestResult: { success: boolean; version?: string; error?: string } | null = null;

  testLndConnection(): void {
    if (!this.selectedProject || !this.projectForm.lndBaseUrl) return;
    
    this.testingLnd = true;
    this.lndTestResult = null;
    this.error = undefined;

    // Only pass macaroon if it was changed (not masked)
    const macaroon = this.projectForm.lndMacaroon && !this.projectForm.lndMacaroon.startsWith('••')
      ? this.projectForm.lndMacaroon
      : null;

    this.devService.testLndConnection(
      this.selectedProject.projectId,
      this.projectForm.lndBaseUrl,
      macaroon
    ).subscribe({
      next: (res: any) => {
        this.testingLnd = false;
        this.lndTestResult = res;
      },
      error: (err) => {
        this.testingLnd = false;
        this.lndTestResult = { success: false, error: this.extractErrorMessage(err) || 'Connection failed' };
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

    // Backend-standard error: { error: "..." }
    if (err.error?.error) return err.error.error;
    if (err.error?.message) return err.error.message;

    // Fallbacks
    if (typeof err.error === 'string') return err.error;
    if (typeof err.message === 'string') return err.message;

    return 'Something went wrong';
  }
}
