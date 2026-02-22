import {Component, OnInit, OnDestroy, ChangeDetectorRef} from '@angular/core';
import { CommonModule, DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminAnalyticsService } from '../../services/admin-analytics';
import { AdminAuthService } from '../../services/admin-auth';
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
import { SelectModule } from 'primeng/select';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    DecimalPipe,
    DatePipe,
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
  filteredProjects: AdminProjectUsageDto[] = [];
  subscriptions: AdminSubscriptionDto[] = [];
  authEvents: AdminAuthEventDto[] = [];
  filteredEvents: AdminAuthEventDto[] = [];

  // Search
  projectSearch = '';
  eventSearch = '';
  
  // Modal
  selectedProject: AdminProjectUsageDto | null = null;
  showProjectModal = false;
  projectEvents: AdminAuthEventDto[] = [];

  // Active tab
  activeTab: 'projects' | 'subscriptions' | 'events' = 'projects';

  windowOptions = [
    { label: '24 Hours', value: 24 },
    { label: '7 Days', value: 168 },
    { label: '30 Days', value: 720 },
    { label: '90 Days', value: 2160 }
  ];

  private destroy$ = new Subject<void>();
  private windowHours$ = new Subject<number>();

  private btcToUsd = 100000;

  constructor(
    private analytics: AdminAnalyticsService,
    private auth: AdminAuthService,
    private router: Router,
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

  // ================= SEARCH =================

  onProjectSearch() {
    const search = this.projectSearch.toLowerCase();
    if (!search) {
      this.filteredProjects = this.projectUsage;
    } else {
      this.filteredProjects = this.projectUsage.filter(p => 
        p.name.toLowerCase().includes(search) ||
        p.projectId.toLowerCase().includes(search)
      );
    }
  }

  onEventSearch() {
    const search = this.eventSearch.toLowerCase();
    if (!search) {
      this.filteredEvents = this.authEvents;
    } else {
      this.filteredEvents = this.authEvents.filter(e =>
        e.projectName.toLowerCase().includes(search) ||
        e.projectId.toLowerCase().includes(search) ||
        e.eventType.toLowerCase().includes(search) ||
        (e.clientIpMasked && e.clientIpMasked.toLowerCase().includes(search))
      );
    }
  }

  // ================= PROJECT DRILL-DOWN =================

  viewProject(project: AdminProjectUsageDto) {
    this.selectedProject = project;
    // Filter events for this project
    this.projectEvents = this.authEvents.filter(e => e.projectId === project.projectId);
    this.showProjectModal = true;
    this.changeDetector.detectChanges();
  }

  closeProjectModal() {
    this.showProjectModal = false;
    this.selectedProject = null;
    this.projectEvents = [];
  }

  // ================= HELPER METHODS =================

  getSuccessRate(project: AdminProjectUsageDto): number {
    if (project.auths === 0) return 0;
    return Math.round((project.successes / project.auths) * 100);
  }

  isExpiringSoon(expiresAt: string): boolean {
    const expiry = new Date(expiresAt);
    const now = new Date();
    const daysUntilExpiry = (expiry.getTime() - now.getTime()) / (1000 * 60 * 60 * 24);
    return daysUntilExpiry < 7 && daysUntilExpiry > 0;
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleString();
  }

  // ================= EXPORT FUNCTIONALITY =================

  exportChart(chartType: string) {
    console.log(`Exporting chart: ${chartType}`);
    alert(`Export ${chartType} chart - Not yet implemented`);
  }

  exportTable(tableType: string) {
    let data: any[] = [];
    let filename = '';

    switch (tableType) {
      case 'projects':
        data = this.filteredProjects;
        filename = 'project-usage';
        break;
      case 'subscriptions':
        data = this.subscriptions;
        filename = 'subscriptions';
        break;
      case 'events':
        data = this.filteredEvents;
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
    const headers = Object.keys(data[0]);
    const csvRows = [
      headers.join(','),
      ...data.map(row =>
        headers.map(header => {
          const value = row[header];
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
    // Verify auth status before making API calls
    this.auth.checkStatus().subscribe({
      next: (status) => {
        if (!status.isAuthenticated) {
          this.router.navigate(['/login']);
          return;
        }
        this.loadData();
      },
      error: () => {
        this.router.navigate(['/login']);
      }
    });
  }

  private loadData() {
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
          this.filteredEvents = [...this.authEvents];
          this.loading = false;
          this.changeDetector.detectChanges();
        },
        error: () => {
          this.error = 'Failed to load analytics';
          this.loading = false;
        }
      });

    interval(30_000)
      .pipe(
        takeUntil(this.destroy$),
        switchMap(() => this.analytics.getOverview(this.windowHours))
      )
      .subscribe(res => {
        this.data = res;
        this.authEvents = res.recentEvents ?? [];
        this.filteredEvents = [...this.authEvents];
        this.changeDetector.detectChanges();
      });

    this.windowHours$.next(this.windowHours);
  }

  reload(windowHours: number) {
    this.windowHours = +windowHours;
    this.windowHours$.next(this.windowHours);

    this.analytics.getProjects(this.windowHours)
      .subscribe(x => {
        this.projectUsage = x;
        this.filteredProjects = [...x];
        this.onProjectSearch();
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
