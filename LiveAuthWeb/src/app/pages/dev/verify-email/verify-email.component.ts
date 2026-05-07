import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';

import { DevAuthService } from '../../../services/dev-auth.service';

type VerifyState = 'verifying' | 'success' | 'error';

@Component({
  selector: 'app-verify-email',
  standalone: true,
  imports: [CommonModule, ButtonModule, MessageModule],
  template: `
    <div class="verify-page">
      <div class="verify-card">
        <div class="verify-icon">
          <i class="pi" [class.pi-check-circle]="state === 'success'" [class.pi-times-circle]="state === 'error'" [class.pi-spin]="state === 'verifying'" [class.pi-spinner]="state === 'verifying'"></i>
        </div>

        <h2 class="gradient-text">
          {{ state === 'verifying' ? 'Verifying…' : state === 'success' ? 'Email Verified!' : 'Verification Failed' }}
        </h2>

        <p-message
          *ngIf="state === 'success'"
          severity="success"
          [text]="'Your email is verified. Redirecting to your dashboard…'"
          class="mb-3">
        </p-message>

        <p-message
          *ngIf="state === 'error'"
          severity="error"
          [text]="errorMessage"
          class="mb-3">
        </p-message>

        <div *ngIf="state === 'error'" class="error-actions">
          <button *ngIf="isExpired"
            pButton
            label="Resend Verification Email"
            icon="pi pi-envelope"
            class="resend-btn"
            (click)="goToResend()">
          </button>
          <button
            pButton
            [label]="isExpired ? 'Back to Login' : 'Go to Login'"
            icon="pi pi-arrow-right"
            iconPos="right"
            class="login-btn"
            (click)="goToLogin()">
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .verify-page {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      background: #0a0a0f;
    }

    .verify-card {
      background: #12121a;
      border: 1px solid #1e1e2e;
      border-radius: 16px;
      padding: 48px;
      text-align: center;
      max-width: 440px;
      width: 100%;
    }

    .verify-icon {
      font-size: 64px;
      margin-bottom: 24px;
      color: #00C2FF;
    }

    .verify-icon .pi-check-circle { color: #64ff8f; }
    .verify-icon .pi-times-circle { color: #ff6b6b; }

    h2 { margin-bottom: 16px; }

    p { color: #888; margin-bottom: 24px; }

    .error-actions {
      display: flex;
      flex-direction: column;
      gap: 12px;
      margin-top: 8px;
    }

    .error-actions button {
      width: 100%;
    }

    .resend-btn {
      background: #00C2FF !important;
      border-color: #00C2FF !important;
    }
  `]
})
export class VerifyEmailComponent implements OnInit {
  state: VerifyState = 'verifying';
  errorMessage = 'Something went wrong. The link may be expired or invalid.';
  isExpired = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private devAuth: DevAuthService
  ) {}

  ngOnInit() {
    const token = this.route.snapshot.queryParamMap.get('token');

    if (!token) {
      this.state = 'error';
      this.errorMessage = 'No verification token found. Please check your email and click the link again.';
      return;
    }

    this.devAuth.verifyEmail({ token }).subscribe({
      next: (res) => {
        if (res.success && res.token) {
          this.devAuth.saveToken(res.token);
          this.state = 'success';
          setTimeout(() => {
            this.router.navigate(['/dev/projects']);
          }, 1500);
        } else {
          this.state = 'error';
          this.errorMessage = res.message || 'Verification failed.';
        }
      },
      error: (err) => {
        this.state = 'error';
        const msg = err.error?.error || err.message || '';
        if (msg.includes('expired')) {
          this.errorMessage = 'Verification token has expired. Please request a new one.';
          this.isExpired = true;
        } else if (err.error?.error) {
          this.errorMessage = err.error.error;
        } else {
          this.errorMessage = msg || 'Something went wrong. The link may be expired or invalid.';
        }
      }
    });
  }

  goToLogin() {
    this.router.navigate(['/dev/login']);
  }

  goToResend() {
    this.router.navigate(['/dev/resend-verification']);
  }
}
