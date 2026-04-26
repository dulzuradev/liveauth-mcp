import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule, DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute, RouterLink } from '@angular/router';
import { AdminAnalyticsService } from '../../services/admin-analytics';
import { AdminAuthService } from '../../services/admin-auth';
import {
  AdminUserDto,
  AdminUserDetailResponse,
  AdminUserProjectDto
} from '../../admin-analytics.models';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

@Component({
  selector: 'app-admin-users',
  standalone: true,
  imports: [
    CommonModule,
    DecimalPipe,
    DatePipe,
    FormsModule,
    RouterLink,
    TagModule,
    ButtonModule,
    InputTextModule,
    TableModule,
    ProgressSpinnerModule
  ],
  templateUrl: './admin-users.html',
  styleUrls: ['./admin-users.css']
})
export class AdminUsersComponent implements OnInit {
  // ── List mode ───────────────────────────────────────
  users: AdminUserDto[] = [];
  total = 0;
  loading = false;
  searchQuery = '';
  offset = 0;
  limit = 50;

  // ── Detail mode ─────────────────────────────────────
  detail: AdminUserDetailResponse | null = null;
  detailLoading = false;

  constructor(
    private analytics: AdminAnalyticsService,
    private auth: AdminAuthService,
    private router: Router,
    private route: ActivatedRoute,
    private cd: ChangeDetectorRef
  ) {}

  ngOnInit() {
    this.auth.checkStatus().subscribe({
      next: (status) => {
        if (!status.isAuthenticated) {
          this.router.navigate(['/login']);
          return;
        }
        this.route.paramMap.subscribe(p => {
          const id = p.get('id');
          if (id) this.loadDetail(id);
          else this.loadList();
        });
      },
      error: () => this.router.navigate(['/login'])
    });
  }

  // ── List ───────────────────────────────────────────

  loadList() {
    this.loading = true;
    this.analytics.getUsers(this.searchQuery, this.limit, this.offset).subscribe({
      next: (res) => {
        this.users = res.users;
        this.total = res.total;
        this.loading = false;
        this.cd.detectChanges();
      },
      error: () => { this.loading = false; this.cd.detectChanges(); }
    });
  }

  onSearch() {
    this.offset = 0;
    this.loadList();
  }

  viewUser(user: AdminUserDto) {
    this.router.navigate(['/users', user.id]);
  }

  // ── Detail ─────────────────────────────────────────

  loadDetail(id: string) {
    this.detailLoading = true;
    this.analytics.getUser(id).subscribe({
      next: (res) => {
        this.detail = res;
        this.detailLoading = false;
        this.cd.detectChanges();
      },
      error: () => { this.detailLoading = false; this.cd.detectChanges(); }
    });
  }

  backToList() {
    this.router.navigate(['/users']);
  }

  viewProject(project: AdminUserProjectDto) {
    this.router.navigate(['/transactions'], {
      queryParams: { projectId: project.id, projectName: project.name },
      queryParamsHandling: 'merge'
    });
  }

  // ── Helpers ────────────────────────────────────────

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleString();
  }

  planColor(plan: string): string {
    return plan === 'pro' ? 'success' : 'info';
  }

  get totalSats(): number {
    if (!this.detail) return 0;
    return this.detail.projects.reduce((sum, p) => sum + p.totalSats, 0);
  }

  get totalAuths(): number {
    if (!this.detail) return 0;
    return this.detail.projects.reduce((sum, p) => sum + p.totalAuths, 0);
  }
}
