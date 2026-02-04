import {Component, OnInit, OnDestroy, ChangeDetectorRef} from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { AdminAnalyticsService } from '../../services/admin-analytics';
import {
  AdminAnalyticsOverviewResponse,
  AdminAuthEventDto,
  AdminProjectUsageDto,
  AdminSubscriptionDto
} from '../../admin-analytics.models';
import { AdminAuthsLineChartComponent } from '../admin-auths-line-chart/admin-auths-line-chart';
import { AdminProjectsDonutComponent } from '../admin-projects-donut/admin-projects-donut';
import { ChartOptions } from 'chart.js';
import {Subject, interval, startWith, switchMap, takeUntil, map} from 'rxjs';
import { TableModule } from 'primeng/table';
import {Tag, TagModule} from 'primeng/tag';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    DecimalPipe,
    AdminAuthsLineChartComponent,
    AdminProjectsDonutComponent,
    TableModule,
    Tag
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

  private destroy$ = new Subject<void>();
  private windowHours$ = new Subject<number>();

  // ---- Chart options (unchanged)
  options: ChartOptions<'line'> = {
    responsive: true,
    maintainAspectRatio: false,
    scales: {
      y: {
        beginAtZero: true,
        ticks: { precision: 0 }
      },
      x: {
        ticks: { maxRotation: 0 }
      }
    },
    plugins: {
      legend: { display: true }
    }
  };

  constructor(private analytics: AdminAnalyticsService, private changeDetector: ChangeDetectorRef) {}

  // ---- Derived helpers for template
  get authSeries() {
    return this.data?.authsOverTime ?? [];
  }

  get isAuthSeriesEmpty(): boolean {
    return (
      this.authSeries.length === 0 ||
      this.authSeries.every(p => p.successful === 0 && p.failed === 0)
    );
  }

  // ---- Lifecycle
  ngOnInit() {
    this.windowHours$
      .pipe(
        startWith(this.windowHours), // initial 24h
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

    // Polling (clean & safe)
    interval(30_000)
      .pipe(
        takeUntil(this.destroy$),
        switchMap(() => this.analytics.getOverview(this.windowHours))
      )
      .subscribe(res => {
        this.data = res;
        this.authEvents = res.recentEvents ?? [];
      });

    // Initial emit
    this.windowHours$.next(this.windowHours);
  }


  reload(windowHours: number) {
    this.windowHours = +windowHours;
    this.windowHours$.next(this.windowHours);

    this.analytics.getProjects(this.windowHours)
      .subscribe(x => this.projectUsage = x);

    this.analytics.getSubscriptions()
      .subscribe(x => this.subscriptions = x);
  }


  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
