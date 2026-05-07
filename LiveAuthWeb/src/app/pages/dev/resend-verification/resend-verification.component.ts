import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';

import { DevAuthService } from '../../../services/dev-auth.service';

@Component({
  selector: 'app-resend-verification',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, InputTextModule, MessageModule],
  template: `
    <div class="verify-page">
      <div class="verify-card">
        <div class="verify-icon">
          <i class="pi pi-envelope"></i>
        </div>

        <h2 class="gradient-text">Resend Verification Email</h2>

        <p *ngIf="!submitted" class="subtitle">
          Enter your email address and we'll send you a new verification link.
        </p>

        <p-message
          *ngIf="submitted"
          severity="success"
          text="Check your email for a new verification link."
          class="mb-3">
        </p-message>

        <p-message
          *ngIf="errorMsg"
          severity="error"
          [text]="errorMsg"
          class="mb-3">
        </p-message>

        <form *ngIf="!submitted" (ngSubmit)="submit()" class="form">
          <input
            pInputText
            type="email"
            [(ngModel)]="email"
            name="email"
            placeholder="your@email.com"
            class="email-input"
            [disabled]="loading" />

          <button
            pButton
            type="submit"
            [label]="loading ? 'Sending…' : 'Resend Verification'"
            [icon]="loading ? 'pi pi-spin pi-spinner' : 'pi pi-send'"
            [disabled]="loading || !email"
            class="submit-btn">
          </button>
        </form>

        <div *ngIf="submitted" class="actions">
          <button
            pButton
            label="Back to Login"
            icon="pi pi-arrow-right"
            iconPos="right"
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

    h2 { margin-bottom: 8px; }

    .subtitle {
      color: #888;
      margin-bottom: 32px;
    }

    .form {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .email-input {
      width: 100%;
      padding: 12px 16px;
      border-radius: 8px;
      font-size: 16px;
    }

    .submit-btn {
      width: 100%;
    }

    .actions {
      margin-top: 16px;
    }
  `]
})
export class ResendVerificationComponent {
  email = '';
  loading = false;
  submitted = false;
  errorMsg = '';

  constructor(private devAuth: DevAuthService, private router: Router) {}

  submit() {
    this.loading = true;
    this.errorMsg = '';

    this.devAuth.resendVerification({ email: this.email }).subscribe({
      next: () => {
        this.submitted = true;
        this.loading = false;
      },
      error: (err: any) => {
        this.loading = false;
        // Even on server error, show success to prevent email enumeration
        this.submitted = true;
      }
    });
  }

  goToLogin() {
    this.router.navigate(['/dev/login']);
  }
}