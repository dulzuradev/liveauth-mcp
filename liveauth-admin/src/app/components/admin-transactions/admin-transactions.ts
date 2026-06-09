import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AdminAnalyticsService } from '../../services/admin-analytics';
import { TransactionDto, TransactionDetailDto } from '../../admin-analytics.models';

@Component({
  selector: 'app-admin-transactions',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="transactions-page">
      <header class="page-header">
        <div class="header-left">
          @if (projectId) {
            <button class="btn-back" (click)="backToUsers()">
              <i class="pi pi-arrow-left"></i> Users
            </button>
          }
          <div>
            <h1>Transactions</h1>
            @if (projectId && projectLabel) {
              <span class="filter-badge">{{ projectLabel }}</span>
            }
          </div>
        </div>
        <div class="summary">
          <div class="stat">
            <span class="label">Showing</span>
            <span class="value">{{ total | number }}</span>
          </div>
          <div class="stat">
            <span class="label">Total Sats</span>
            <span class="value">{{ totalSats | number }}</span>
          </div>
        </div>
      </header>

      <div class="filters">
        <input
          type="text"
          [(ngModel)]="searchQuery"
          (keyup.enter)="loadTransactions()"
          placeholder="Search by payment hash, invoice, or ID..."
          class="search-input"
        />
        <button (click)="loadTransactions()" class="btn-primary">Search</button>
        @if (projectId) {
          <button (click)="clearProjectFilter()" class="btn-ghost">Clear Filter</button>
        }
      </div>

      <div class="transactions-table" *ngIf="transactions.length > 0">
        <table>
          <thead>
            <tr>
              <th>ID</th>
              <th>Type</th>
              <th>Project</th>
              <th>Amount</th>
              <th>Status</th>
              <th>Payment Hash</th>
              <th>Created</th>
              <th>Paid</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let tx of transactions" (click)="selectTransaction(tx)" [class.selected]="selectedTransaction?.id === tx.id">
              <td class="mono">{{ tx.id | slice:0:8 }}...</td>
              <td>
                <span class="badge" [class]="tx.type.toLowerCase()">{{ tx.type }}</span>
              </td>
              <td>{{ tx.projectName || '(unknown)' }}</td>
              <td class="sats">{{ tx.amountSats }} sats</td>
              <td>
                <span class="badge" [class.paid]="tx.status === 'PAID'" [class.pending]="tx.status === 'PENDING'">
                  {{ tx.status }}
                </span>
              </td>
              <td class="mono hash" (click)="copyToClipboard(tx.paymentHash)" title="Click to copy">
                {{ tx.paymentHash | slice:0:16 }}...
              </td>
              <td>{{ formatDate(tx.createdAt) }}</td>
              <td>{{ tx.paidAt ? formatDate(tx.paidAt) : '-' }}</td>
              <td>
                <button class="btn-small" (click)="selectTransaction(tx); $event.stopPropagation()">View</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="no-data" *ngIf="transactions.length === 0 && !loading">
        <p>No transactions found</p>
      </div>

      <div class="loading" *ngIf="loading">
        Loading...
      </div>

      <!-- Transaction Detail Modal -->
      <div class="modal-overlay" *ngIf="selectedTransaction" (click)="closeModal()">
        <div class="modal" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h2>Transaction Details</h2>
            <button class="close-btn" (click)="closeModal()">×</button>
          </div>
          <div class="modal-body">
            <div class="detail-row">
              <span class="label">ID</span>
              <span class="value mono">{{ selectedTransaction.id }}</span>
            </div>
            <div class="detail-row">
              <span class="label">Type</span>
              <span class="badge" [class]="selectedTransaction.type.toLowerCase()">{{ selectedTransaction.type }}</span>
            </div>
            <div class="detail-row">
              <span class="label">Project</span>
              <span class="value">{{ selectedTransaction.projectName }} ({{ selectedTransaction.projectPublicKey }})</span>
            </div>
            <div class="detail-row">
              <span class="label">Amount</span>
              <span class="value sats">{{ selectedTransaction.amountSats }} sats</span>
            </div>
            <div class="detail-row">
              <span class="label">Status</span>
              <span class="badge" [class.paid]="selectedTransaction.status === 'PAID'">{{ selectedTransaction.status }}</span>
            </div>
            <div class="detail-row">
              <span class="label">Payment Hash</span>
              <span class="value mono">{{ selectedTransaction.paymentHash }}</span>
            </div>
            <div class="detail-row">
              <span class="label">Invoice</span>
              <span class="value mono invoice" (click)="copyToClipboard(selectedTransaction.invoice)" title="Click to copy">
                {{ selectedTransaction.invoice | slice:0:40 }}...
              </span>
            </div>
            <div class="detail-row">
              <span class="label">Client IP</span>
              <span class="value">{{ selectedTransaction.clientIp || '-' }}</span>
            </div>
            <div class="detail-row">
              <span class="label">Environment</span>
              <span class="value">{{ selectedTransaction.environment || '-' }}</span>
            </div>
            <div class="detail-row">
              <span class="label">Created</span>
              <span class="value">{{ formatDate(selectedTransaction.createdAt) }}</span>
            </div>
            <div class="detail-row">
              <span class="label">Paid At</span>
              <span class="value">{{ selectedTransaction.paidAt ? formatDate(selectedTransaction.paidAt) : '-' }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .transactions-page {
      min-height: 100vh;
      padding: 1.5rem;
      background: var(--liveauth-bg, #090909);
      color: var(--liveauth-text, #f7f9fb);
    }

    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 1.5rem;
    }

    .page-header h1 {
      margin: 0;
      font-size: 1.75rem;
    }

    .summary {
      display: flex;
      gap: 2rem;
    }

    .stat {
      display: flex;
      flex-direction: column;
      align-items: flex-end;
    }

    .stat .label {
      font-size: 0.75rem;
      color: var(--liveauth-muted, rgba(255, 255, 255, 0.64));
      text-transform: uppercase;
    }

    .stat .value {
      font-size: 1.5rem;
      font-weight: 600;
      color: var(--liveauth-orange, #f59e0b);
    }

    .filters {
      display: flex;
      gap: 1rem;
      margin-bottom: 1.5rem;
    }

    .search-input {
      flex: 1;
      padding: 0.75rem 1rem;
      border: 1px solid var(--liveauth-border, rgba(255, 255, 255, 0.12));
      border-radius: 0.5rem;
      background: var(--liveauth-input, #0d0d0d);
      color: var(--liveauth-text, #f7f9fb);
      font-size: 0.9rem;
    }

    .search-input:focus {
      outline: none;
      border-color: var(--liveauth-orange, #f59e0b);
    }

    .btn-primary {
      padding: 0.75rem 1.5rem;
      background: var(--liveauth-orange, #f59e0b);
      color: #090909;
      border: none;
      border-radius: 0.5rem;
      font-weight: 600;
      cursor: pointer;
    }

    .btn-primary:hover {
      background: var(--liveauth-orange-bright, #fbbf24);
    }

    .btn-small {
      padding: 0.25rem 0.75rem;
      background: rgba(245, 158, 11, 0.14);
      color: var(--liveauth-orange-bright, #fbbf24);
      border: 1px solid rgba(245, 158, 11, 0.32);
      border-radius: 0.25rem;
      cursor: pointer;
      font-size: 0.8rem;
    }

    .btn-small:hover {
      background: rgba(245, 158, 11, 0.22);
    }

    .btn-ghost {
      padding: 0.75rem 1rem;
      border: 1px solid var(--liveauth-border, rgba(255, 255, 255, 0.12));
      border-radius: 0.5rem;
      background: var(--liveauth-panel-soft, rgba(255, 255, 255, 0.055));
      color: var(--liveauth-muted, rgba(255, 255, 255, 0.64));
      cursor: pointer;
      font-weight: 600;
    }

    .btn-ghost:hover {
      border-color: rgba(245, 158, 11, 0.42);
      background: rgba(245, 158, 11, 0.14);
      color: var(--liveauth-text, #f7f9fb);
    }

    .transactions-table {
      overflow-x: auto;
      border: 1px solid var(--liveauth-border, rgba(255, 255, 255, 0.12));
      border-radius: 0.5rem;
      background: var(--liveauth-panel, #151719);
      box-shadow: 0 16px 36px rgba(0, 0, 0, 0.22);
    }

    table {
      width: 100%;
      border-collapse: collapse;
      background: transparent;
      overflow: hidden;
    }

    th, td {
      padding: 0.75rem 1rem;
      text-align: left;
      border-bottom: 1px solid var(--liveauth-border, rgba(255, 255, 255, 0.12));
    }

    th {
      background: #0f0f0f;
      font-weight: 600;
      font-size: 0.8rem;
      text-transform: uppercase;
      color: var(--liveauth-muted, rgba(255, 255, 255, 0.64));
    }

    tr:hover {
      background: rgba(245, 158, 11, 0.08);
    }

    tr.selected {
      background: rgba(245, 158, 11, 0.1);
      border-left: 3px solid var(--liveauth-orange, #f59e0b);
    }

    .mono {
      font-family: monospace;
      font-size: 0.8rem;
    }

    .hash {
      cursor: pointer;
    }

    .hash:hover {
      color: var(--liveauth-orange, #f59e0b);
    }

    .badge {
      padding: 0.25rem 0.5rem;
      border-radius: 0.25rem;
      font-size: 0.75rem;
      font-weight: 600;
      text-transform: uppercase;
    }

    .badge.auth {
      background: rgba(245, 158, 11, 0.18);
      color: var(--liveauth-orange-bright, #fbbf24);
    }

    .badge.mcp {
      background: rgba(255, 255, 255, 0.12);
      color: var(--liveauth-text, #f7f9fb);
    }

    .badge.paid {
      background: #10b981;
      color: white;
    }

    .badge.pending {
      background: #f59e0b;
      color: #090909;
    }

    .sats {
      color: var(--liveauth-orange, #f59e0b);
      font-weight: 600;
    }

    .no-data, .loading {
      text-align: center;
      padding: 3rem;
      color: var(--liveauth-muted, rgba(255, 255, 255, 0.64));
    }

    /* Modal */
    .modal-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.76);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    }

    .modal {
      background: var(--liveauth-panel, #151719);
      border: 1px solid var(--liveauth-border, rgba(255, 255, 255, 0.12));
      border-radius: 0.5rem;
      width: 90%;
      max-width: 600px;
      max-height: 90vh;
      overflow-y: auto;
    }

    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 1.5rem;
      border-bottom: 1px solid var(--liveauth-border, rgba(255, 255, 255, 0.12));
    }

    .modal-header h2 {
      margin: 0;
      font-size: 1.25rem;
    }

    .close-btn {
      background: none;
      border: none;
      color: var(--liveauth-muted, rgba(255, 255, 255, 0.64));
      font-size: 1.5rem;
      cursor: pointer;
    }

    .close-btn:hover {
      color: var(--liveauth-text, #f7f9fb);
    }

    .modal-body {
      padding: 1.5rem;
    }

    .detail-row {
      display: flex;
      justify-content: space-between;
      padding: 0.75rem 0;
      border-bottom: 1px solid var(--liveauth-border, rgba(255, 255, 255, 0.12));
    }

    .detail-row .label {
      color: var(--liveauth-muted, rgba(255, 255, 255, 0.64));
    }

    .detail-row .value {
      text-align: right;
      max-width: 60%;
      word-break: break-all;
    }

    .invoice {
      cursor: pointer;
    }

    .invoice:hover {
      color: var(--liveauth-orange, #f59e0b);
    }

    @media (max-width: 760px) {
      .transactions-page {
        padding: 0.875rem;
      }

      .page-header,
      .filters {
        align-items: stretch;
        flex-direction: column;
      }

      .summary {
        justify-content: space-between;
        gap: 1rem;
      }

      .stat {
        align-items: flex-start;
      }

      .filters,
      .btn-primary,
      .btn-ghost {
        width: 100%;
      }

      .modal {
        width: calc(100% - 1.5rem);
      }

      .detail-row {
        flex-direction: column;
        gap: 0.35rem;
      }

      .detail-row .value {
        max-width: 100%;
        text-align: left;
      }
    }
  `]
})
export class AdminTransactionsComponent implements OnInit {
  transactions: TransactionDto[] = [];
  selectedTransaction: TransactionDetailDto | null = null;
  searchQuery = '';
  loading = false;
  total = 0;
  totalSats = 0;
  projectId: string | null = null;
  projectLabel = '';

  constructor(
    private analytics: AdminAnalyticsService,
    private route: ActivatedRoute,
    private router: Router
  ) {}

  ngOnInit() {
    this.route.queryParamMap.subscribe(params => {
      this.projectId = params.get('projectId');
      this.projectLabel = params.get('projectName') || '';
      this.loadTransactions();
    });
  }

  loadTransactions() {
    this.loading = true;
    this.analytics.getTransactions(this.searchQuery, 50, 0, this.projectId || undefined).subscribe({
      next: (res) => {
        this.transactions = res.transactions;
        this.total = res.total;
        this.totalSats = res.totalSats;
        this.loading = false;
      },
      error: (err) => {
        console.error('Failed to load transactions:', err);
        this.loading = false;
      }
    });
  }

  selectTransaction(tx: TransactionDto) {
    this.analytics.getTransaction(tx.id).subscribe({
      next: (detail) => {
        this.selectedTransaction = detail;
      },
      error: (err) => {
        console.error('Failed to load transaction details:', err);
      }
    });
  }

  closeModal() {
    this.selectedTransaction = null;
  }

  formatDate(dateStr: string): string {
    if (!dateStr) return '-';
    const date = new Date(dateStr);
    return date.toLocaleString();
  }

  clearProjectFilter() {
    this.router.navigate([], { queryParams: {}, queryParamsHandling: 'replace' });
  }

  backToUsers() {
    this.router.navigate(['/users']);
  }

  setProjectLabel(name: string) {
    this.projectLabel = name;
  }

  copyToClipboard(text: string) {
    navigator.clipboard.writeText(text);
  }
}
