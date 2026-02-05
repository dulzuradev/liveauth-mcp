import {Component, OnInit, OnDestroy, ChangeDetectorRef} from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminAnalyticsService } from '../../services/admin-analytics';
import {
  AdminAnalyticsOverviewResponse,
  AdminAuthEventDto,
  AdminProjectUsageDto,
  AdminSubscriptionDto
} from '../../admin-analytics.models';
import { AdminAuthsLineChartComponent } from '../admin-auths-line-chart/admin-auths-line-chart';
import { AdminProjectsDonutComponent } from '../admin-projects-donut/admin-projects-donut';
import {Subject, interval, startWith, switchMap, takeUntil, map} from 'rxjs';

// PrimeNG Imports
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
// PrimeNG v21 replaced DropdownModule with SelectModule
import { SelectModule } from 'primeng/select';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    DecimalPipe,
    FormsModule,
    AdminAuthsLineChartComponent,
    AdminProjectsDonutComponent,
    TableModule,
    TagModule,
    ButtonModule,
    InputTextModule,
    SelectModule,
    ProgressSpinnerModule
  ],
  templateUrl: './admin-dashboard-component.html',
  styleUrls: ['./admin-dashboard-component.css']
})
export class AdminDashboardComponent implements OnInit, OnDestroy {
  data?: AdminAnalyticsOverviewResponse;
  loading = true;
  error?: string;

  windowHours = 24;
  projectUsage: AdminProjectUsageDto[] = [];
  subscriptions: AdminSubscriptionDto[] = [];
  authEvents: AdminAuthEventDto[] = [];

  windowOptions = [
    { label: '24 Hours', value: 24 },
    { label: '7 Days', value: 168 },
    { label: '30 Days', value: 720 },
    { label: '90 Days', value: 2160 }
  ];

  private destroy$ = new Subject<void>();
  private windowHours$ = new Subject<number>();

  // Estimated BTC to USD conversion rate (could be fetched from API)
  private btcToUsd = 100000; // $100k per BTC

  constructor(
    private analytics: AdminAnalyticsService,
    private changeDetector: ChangeDetectorRef
  ) {}

  // ================= COMPUTED PROPERTIES =================

  get authSeries() {
    return this.data?.authsOverTime ?? [];
  }

  get isAuthSeriesEmpty(): boolean {
    return (
      this.authSeries.length === 0 ||
      this.authSeries.every(p => p.successful === 0 && p.failed === 0)
    );
  }

  get successRate(): number {
    if (!this.data || this.data.totalAuths === 0) return 0;
    return Math.round((this.data.successfulAuths / this.data.totalAuths) * 100);
  }

  get rateLimitPercent(): number {
    if (!this.data || this.data.totalAuths === 0) return 0;
    return Number(((this.data.rateLimitHits / this.data.totalAuths) * 100).toFixed(1));
  }

  get avgSatsPerAuth(): number {
    if (!this.data || this.data.totalAuths === 0) return 0;
    return this.data.totalSatsPaid / this.data.totalAuths;
  }

  get usdEquivalent(): number {
    if (!this.data) return 0;
    const btc = this.data.totalSatsPaid / 100_000_000;
    return btc * this.btcToUsd;
  }

  // ================= HELPER METHODS =================

  getSuccessRate(project: AdminProjectUsageDto): number {
    if (project.auths === 0) return 0;
    return (project.successes / project.auths) * 100;
  }

  isExpiringSoon(expiresAt: string): boolean {
    const expiry = new Date(expiresAt);
    const now = new Date();
    const daysUntilExpiry = (expiry.getTime() - now.getTime()) / (1000 * 60 * 60 * 24);
    return daysUntilExpiry < 7 && daysUntilExpiry > 0;
  }

  // ================= EXPORT FUNCTIONALITY =================

  exportChart(chartType: string) {
    console.log(`Exporting chart: ${chartType}`);
    // TODO: Implement chart export (canvas to PNG/CSV)
    alert(`Export ${chartType} chart - Not yet implemented`);
  }

  exportTable(tableType: string) {
    let data: any[] = [];
    let filename = '';

    switch (tableType) {
      case 'projects':
        data = this.projectUsage;
        filename = 'project-usage';
        break;
      case 'subscriptions':
        data = this.subscriptions;
        filename = 'subscriptions';
        break;
      case 'events':
        data = this.authEvents;
        filename = 'auth-events';
        break;
    }

    if (data.length === 0) {
      alert('No data to export');
      return;
    }

    this.downloadCSV(data, filename);
  }

  private downloadCSV(data: any[], filename: string) {
    // Convert to CSV
    const headers = Object.keys(data[0]);
    const csvRows = [
      headers.join(','),
      ...data.map(row =>
        headers.map(header => {
          const value = row[header];
          // Escape commas and quotes
          const escaped = String(value).replace(/"/g, '""');
          return `"${escaped}"`;
        }).join(',')
      )
    ];

    const csvContent = csvRows.join('\n');
    const blob = new Blob([csvContent], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${filename}-${Date.now()}.csv`;
    link.click();
    window.URL.revokeObjectURL(url);
  }

  // ================= LIFECYCLE =================

  ngOnInit() {
    this.windowHours$
      .pipe(
        startWith(this.windowHours),
        switchMap(window => {
          this.loading = true;
          this.error = undefined;

          return this.analytics.getOverview(window).pipe(
            map(res => ({ res, window }))
          );
        }),
        takeUntil(this.destroy$)
      )
      .subscribe({
        next: ({ res }) => {
          this.data = res;
          this.authEvents = res.recentEvents ?? [];
          this.loading = false;
          this.changeDetector.detectChanges();
        },
        error: () => {
          this.error = 'Failed to load analytics';
          this.loading = false;
        }
      });

    // Auto-refresh every 30 seconds
    interval(30_000)
      .pipe(
        takeUntil(this.destroy$),
        switchMap(() => this.analytics.getOverview(this.windowHours))
      )
      .subscribe(res => {
        this.data = res;
        this.authEvents = res.recentEvents ?? [];
        this.changeDetector.detectChanges();
      });

    // Initial load
    this.windowHours$.next(this.windowHours);
  }

  reload(windowHours: number) {
    this.windowHours = +windowHours;
    this.windowHours$.next(this.windowHours);

    // Load additional data
    this.analytics.getProjects(this.windowHours)
      .subscribe(x => {
        this.projectUsage = x;
        this.changeDetector.detectChanges();
      });

    this.analytics.getSubscriptions()
      .subscribe(x => {
        this.subscriptions = x;
        this.changeDetector.detectChanges();
      });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
