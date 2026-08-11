import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { CommonModule, DatePipe, DecimalPipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { AdminAnalyticsService, PermitSignalAdminOverview } from '../../services/admin-analytics';
import { AdminAuthService } from '../../services/admin-auth';

@Component({
  selector: 'app-permit-signal-admin',
  standalone: true,
  imports: [CommonModule, DatePipe, DecimalPipe, RouterLink],
  templateUrl: './permit-signal-admin.html',
  styleUrls: ['./permit-signal-admin.css']
})
export class PermitSignalAdminComponent implements OnInit {
  overview?: PermitSignalAdminOverview;
  loading = true;
  syncing = false;
  error = '';
  syncMessage = '';

  get paidCalls(): number {
    return this.overview?.tools.reduce((total, tool) => total + tool.calls, 0) ?? 0;
  }

  get grossSats(): number {
    return this.overview?.tools.reduce((total, tool) => total + tool.satsGenerated, 0) ?? 0;
  }

  constructor(
    private analytics: AdminAnalyticsService,
    private auth: AdminAuthService,
    private router: Router,
    private cd: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.auth.checkStatus().subscribe({
      next: status => status.isAuthenticated ? this.load() : this.router.navigate(['/login']),
      error: () => this.router.navigate(['/login'])
    });
  }

  load(): void {
    this.loading = true;
    this.error = '';
    this.analytics.getPermitSignal().subscribe({
      next: overview => {
        this.overview = overview;
        this.loading = false;
        this.cd.detectChanges();
      },
      error: () => {
        this.error = 'PermitSignal status could not be loaded.';
        this.loading = false;
        this.cd.detectChanges();
      }
    });
  }

  sync(source?: string): void {
    if (this.syncing) return;
    this.syncing = true;
    this.syncMessage = source ? `Synchronizing ${source}…` : 'Synchronizing all sources…';
    this.analytics.synchronizePermitSignal(source).subscribe({
      next: results => {
        const processed = results.reduce((total, result) => total + (result.processed ?? 0), 0);
        this.syncMessage = `Synchronization completed; ${processed.toLocaleString()} records processed.`;
        this.syncing = false;
        this.load();
      },
      error: () => {
        this.syncMessage = 'Synchronization failed. Review source status and server logs.';
        this.syncing = false;
        this.cd.detectChanges();
      }
    });
  }

  statusClass(status: string): string {
    const normalized = status.toLowerCase();
    if (normalized === 'healthy' || normalized === 'demo') return 'healthy';
    if (normalized === 'unhealthy') return 'unhealthy';
    return 'pending';
  }
}
