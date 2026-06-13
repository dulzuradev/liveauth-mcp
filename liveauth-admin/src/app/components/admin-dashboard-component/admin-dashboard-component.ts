import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule, DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { catchError, forkJoin, interval, of, startWith, Subject, switchMap, takeUntil } from 'rxjs';
import { AdminAnalyticsService, LightningFeeSettingsResponse } from '../../services/admin-analytics';
import { AdminAuthService } from '../../services/admin-auth';
import {
  AdminAnalyticsOverviewResponse,
  AdminAuthEventDto,
  AdminProjectUsageDto,
  AdminSubscriptionDto
} from '../../admin-analytics.models';
import { AdminAuthsLineChartComponent } from '../admin-auths-line-chart/admin-auths-line-chart';
import { AdminProjectsDonutComponent } from '../admin-projects-donut/admin-projects-donut';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

type DashboardTab = 'projects' | 'subscriptions' | 'events';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    DecimalPipe,
    DatePipe,
    FormsModule,
    RouterLink,
    AdminAuthsLineChartComponent,
    AdminProjectsDonutComponent,
    TableModule,
    TagModule,
    ButtonModule,
    SelectModule,
    ProgressSpinnerModule
  ],
  templateUrl: './admin-dashboard-component.html',
  styleUrls: ['./admin-dashboard-component.css']
})
export class AdminDashboardComponent implements OnInit, OnDestroy {
  data?: AdminAnalyticsOverviewResponse;
  loading = true;
  refreshing = false;
  error?: string;

  windowHours = 24;
  activeTab: DashboardTab = 'projects';

  projectUsage: AdminProjectUsageDto[] = [];
  subscriptions: AdminSubscriptionDto[] = [];
  authEvents: AdminAuthEventDto[] = [];
  lightningFees?: LightningFeeSettingsResponse;
  feeForm = {
    invoiceFeeBasisPoints: 200,
    invoiceMinimumFeeSats: 1,
    bundleMarkupBasisPoints: 1500,
    bundleMarkupMinimumFeeSats: 1
  };
  savingFees = false;
  feeMessage = '';

  projectSearch = '';
  subscriptionSearch = '';
  eventSearch = '';

  selectedProject: AdminProjectUsageDto | null = null;

  readonly windowOptions = [
    { label: '24 Hours', value: 24 },
    { label: '7 Days', value: 168 },
    { label: '30 Days', value: 720 }
  ];

  private readonly destroy$ = new Subject<void>();
  private readonly refresh$ = new Subject<void>();

  constructor(
    private analytics: AdminAnalyticsService,
    private auth: AdminAuthService,
    private router: Router
  ) {}

  get authSeries() {
    return this.data?.authsOverTime ?? [];
  }

  get isAuthSeriesEmpty(): boolean {
    return this.authSeries.length === 0 ||
      this.authSeries.every(point => point.successful === 0 && point.failed === 0);
  }

  get successRate(): number {
    return this.percent(this.data?.successfulAuths ?? 0, this.data?.totalAuths ?? 0);
  }

  get failureRate(): number {
    return this.percent(this.data?.failedAuths ?? 0, this.data?.totalAuths ?? 0);
  }

  get rateLimitPercent(): number {
    return this.percent(this.data?.rateLimitHits ?? 0, this.data?.totalAuths ?? 0);
  }

  get avgSatsPerPaidAuth(): number {
    const paidAuths = this.data?.paidAuths ?? 0;
    return paidAuths > 0 ? (this.data?.totalSatsPaid ?? 0) / paidAuths : 0;
  }

  get totalSatsEarned(): number {
    if (!this.data) return 0;
    return this.data.totalSatsPaid + this.data.mcpSatsEarned + this.data.l402SatsEarned;
  }

  get totalSatsEarnedUsd(): number | null {
    if (!this.data) return null;
    if (this.data.totalSatsEarnedUsd != null) return this.data.totalSatsEarnedUsd;
    if (this.data.btcUsdRate == null) return null;
    return this.totalSatsEarned / 100_000_000 * this.data.btcUsdRate;
  }

  get filteredProjects(): AdminProjectUsageDto[] {
    const query = this.normalizeSearch(this.projectSearch);
    if (!query) return this.projectUsage;
    return this.projectUsage.filter(project => [
      project.name,
      project.projectId,
      project.plan
    ].some(value => this.matches(value, query)));
  }

  get filteredSubscriptions(): AdminSubscriptionDto[] {
    const query = this.normalizeSearch(this.subscriptionSearch);
    if (!query) return this.subscriptions;
    return this.subscriptions.filter(subscription => [
      subscription.projectName,
      subscription.projectId,
      subscription.plan,
      subscription.isPaid ? 'paid active' : 'pending unpaid',
      this.isExpiringSoon(subscription.expiresAt) ? 'expiring' : ''
    ].some(value => this.matches(value, query)));
  }

  get filteredEvents(): AdminAuthEventDto[] {
    const query = this.normalizeSearch(this.eventSearch);
    if (!query) return this.authEvents;
    return this.authEvents.filter(event => [
      event.projectName,
      event.projectId,
      event.eventType,
      event.reason,
      event.clientIpMasked,
      event.success ? 'success ok' : 'failed fail'
    ].some(value => this.matches(value, query)));
  }

  get selectedProjectEvents(): AdminAuthEventDto[] {
    if (!this.selectedProject) return [];
    return this.authEvents.filter(event => event.projectId === this.selectedProject?.projectId);
  }

  ngOnInit(): void {
    this.auth.checkStatus().subscribe({
      next: status => {
        if (!status.isAuthenticated) {
          this.router.navigate(['/login']);
          return;
        }
        this.loadData();
      },
      error: () => this.router.navigate(['/login'])
    });
  }

  reload(windowHours = this.windowHours): void {
    this.windowHours = Number(windowHours);
    this.refresh$.next();
  }

  selectTab(tab: DashboardTab): void {
    this.activeTab = tab;
  }

  viewProject(project: AdminProjectUsageDto): void {
    this.selectedProject = project;
  }

  closeProjectModal(): void {
    this.selectedProject = null;
  }

  getSuccessRate(project: AdminProjectUsageDto): number {
    return this.percent(project.successes, project.auths);
  }

  getSubscriptionStatus(subscription: AdminSubscriptionDto): 'success' | 'warn' | 'danger' {
    if (!subscription.isPaid) return 'danger';
    return this.isExpiringSoon(subscription.expiresAt) ? 'warn' : 'success';
  }

  isExpiringSoon(expiresAt: string): boolean {
    const expiry = new Date(expiresAt).getTime();
    const now = Date.now();
    const sevenDays = 7 * 24 * 60 * 60 * 1000;
    return expiry > now && expiry - now <= sevenDays;
  }

  exportTable(tableType: DashboardTab): void {
    const data = tableType === 'projects'
      ? this.filteredProjects
      : tableType === 'subscriptions'
        ? this.filteredSubscriptions
        : this.filteredEvents;

    if (data.length === 0) return;
    this.downloadCSV(data, tableType);
  }

  saveFeeSettings(): void {
    this.savingFees = true;
    this.feeMessage = '';

    this.analytics.updateLightningFeeSettings({
      invoiceFeeBasisPoints: Math.max(0, Number(this.feeForm.invoiceFeeBasisPoints) || 0),
      invoiceMinimumFeeSats: Math.max(0, Number(this.feeForm.invoiceMinimumFeeSats) || 0),
      bundleMarkupBasisPoints: Math.max(0, Number(this.feeForm.bundleMarkupBasisPoints) || 0),
      bundleMarkupMinimumFeeSats: Math.max(0, Number(this.feeForm.bundleMarkupMinimumFeeSats) || 0)
    }).subscribe({
      next: settings => {
        this.applyFeeSettings(settings);
        this.savingFees = false;
        this.feeMessage = 'Fee settings saved.';
      },
      error: () => {
        this.savingFees = false;
        this.feeMessage = 'Failed to save fee settings.';
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadData(): void {
    this.refresh$
      .pipe(
        startWith(undefined),
        switchMap(() => {
          this.loading = !this.data;
          this.refreshing = !!this.data;
          this.error = undefined;

          return forkJoin({
            overview: this.analytics.getOverview(this.windowHours),
            projects: this.analytics.getProjects(this.windowHours),
            subscriptions: this.analytics.getSubscriptions(),
            lightningFees: this.analytics.getLightningFeeSettings()
          }).pipe(
            catchError(() => {
              this.error = 'Failed to load admin analytics. Check the API, token, and selected time window.';
              return of(null);
            })
          );
        }),
        takeUntil(this.destroy$)
      )
      .subscribe(result => {
        if (result) {
          this.data = result.overview;
          this.projectUsage = result.projects;
          this.subscriptions = result.subscriptions;
          this.applyFeeSettings(result.lightningFees);
          this.authEvents = result.overview.recentEvents ?? [];
          this.reconcileSelectedProject();
        }

        this.loading = false;
        this.refreshing = false;
      });

    interval(30_000)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => this.refresh$.next());
  }

  private percent(value: number, total: number): number {
    return total > 0 ? Number(((value / total) * 100).toFixed(1)) : 0;
  }

  private applyFeeSettings(settings: LightningFeeSettingsResponse): void {
    this.lightningFees = settings;
    this.feeForm = {
      invoiceFeeBasisPoints: settings.invoiceFeeBasisPoints,
      invoiceMinimumFeeSats: settings.invoiceMinimumFeeSats,
      bundleMarkupBasisPoints: settings.bundleMarkupBasisPoints,
      bundleMarkupMinimumFeeSats: settings.bundleMarkupMinimumFeeSats
    };
  }

  private normalizeSearch(value: string): string {
    return value.trim().toLowerCase();
  }

  private matches(value: unknown, query: string): boolean {
    return String(value ?? '').toLowerCase().includes(query);
  }

  private reconcileSelectedProject(): void {
    if (!this.selectedProject) return;
    this.selectedProject = this.projectUsage.find(project =>
      project.projectId === this.selectedProject?.projectId
    ) ?? null;
  }

  private downloadCSV(data: unknown[], filename: string): void {
    const headers = Object.keys(data[0] as Record<string, unknown>);
    const csvRows = [
      headers.join(','),
      ...data.map(row => headers
        .map(header => this.csvCell((row as Record<string, unknown>)[header]))
        .join(','))
    ];

    const blob = new Blob([csvRows.join('\n')], { type: 'text/csv;charset=utf-8' });
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `liveauth-admin-${filename}-${Date.now()}.csv`;
    link.click();
    window.URL.revokeObjectURL(url);
  }

  private csvCell(value: unknown): string {
    return `"${String(value ?? '').replace(/"/g, '""')}"`;
  }
}
