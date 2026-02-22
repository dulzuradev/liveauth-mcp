import {ChangeDetectorRef, Component, OnDestroy, NgZone} from '@angular/core';
import {AdminAuthService, AdminPaymentResponse, AdminVerifyResponse, AdminSetupResponse, AdminLoginResponse} from '../../services/admin-auth';
import {FormsModule} from '@angular/forms';
import { CommonModule } from '@angular/common';
import {Router} from '@angular/router';
import {QRCodeComponent} from 'angularx-qrcode';

type AdminLoginState = 
  | 'checking'
  | 'payment'
  | 'waiting'
  | 'setup'
  | 'login'
  | 'success'
  | 'expired'
  | 'error';

@Component({
  selector: 'app-admin-login-component',
  templateUrl: './admin-login-component.html',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    QRCodeComponent
  ]
})
export class AdminLoginComponent implements OnDestroy {
  // Form fields
  username = '';
  password = '';
  confirmPassword = '';
  
  // State
  state: AdminLoginState = 'checking';
  error?: string;
  
  // Payment
  paymentSession?: AdminPaymentResponse;
  canSetPassword = false;
  
  // Timers
  remainingSeconds = 0;
  remainingLabel = '';
  copied = false;

  get qrCodeValue(): string {
    if (!this.paymentSession?.invoice) return '';
    return `LIGHTNING:${this.paymentSession.invoice.trim()}`;
  }

  private pollTimer?: any;
  private countdownTimer?: any;

  constructor(
    private auth: AdminAuthService, 
    private changeDetector: ChangeDetectorRef,
    private ngZone: NgZone,
    private router: Router
  ) {
    // Don't auto-check auth - wait for user to click button
    this.state = 'payment';
  }

  private checkAuth() {
    this.auth.checkStatus().subscribe({
      next: (res) => {
        if (res.isAuthenticated) {
          this.router.navigate(['/admin']);
        } else {
          this.state = 'payment';
          this.createPayment();
        }
      },
      error: () => {
        this.state = 'payment';
        this.createPayment();
      }
    });
  }

  createPayment() {
    this.error = undefined;
    this.state = 'waiting';
    
    this.auth.createPayment().subscribe({
      next: (res) => {
        setTimeout(() => {
          this.paymentSession = res;
          this.canSetPassword = res.isSetup;
          this.state = 'waiting';
          this.startCountdown(res.expiresAtUnix);
          this.startPolling();
          this.changeDetector.detectChanges();
        }, 0);
      },
      error: (err) => {
        setTimeout(() => {
          this.error = err?.error?.message || 'Failed to create payment';
          this.state = 'error';
          this.changeDetector.detectChanges();
        }, 0);
      }
    });
  }

  private startPolling() {
    if (!this.paymentSession || this.pollTimer) return;

    // Run polling outside Angular zone for setTimeout, but update UI manually
    this.ngZone.runOutsideAngular(() => {
      const pollOnce = () => {
        if (!this.paymentSession || this.remainingSeconds <= 0) {
          return;
        }

        this.auth.verifyPayment(this.paymentSession.sessionId).subscribe({
          next: (res) => {
            // We're outside Angular zone, manually trigger change detection
            this.ngZone.run(() => {
              if (res.paid) {
                this.stopPolling();
                this.stopCountdown();
                
                if (res.canSetPassword) {
                  this.state = 'setup';
                } else {
                  this.state = 'login';
                }
                this.changeDetector.detectChanges();
                return;
              }
              
              if (res.error) {
                this.error = res.error;
                this.state = 'error';
                this.stopPolling();
                this.changeDetector.detectChanges();
                return;
              }
              
              this.pollTimer = setTimeout(pollOnce, 2000);
            });
          },
          error: () => {
            this.pollTimer = setTimeout(pollOnce, 2000);
          }
        });
      };

      pollOnce();
    });
  }

  private startCountdown(expiresAtUnix: number): void {
    this.stopCountdown();

    const expiresAtSec = expiresAtUnix > 10_000_000_000 
      ? Math.floor(expiresAtUnix / 1000) 
      : expiresAtUnix;

    const tick = () => {
      const now = Math.floor(Date.now() / 1000);
      this.remainingSeconds = Math.max(0, expiresAtSec - now);

      const m = Math.floor(this.remainingSeconds / 60);
      const s = this.remainingSeconds % 60;
      this.remainingLabel = `${m}:${s.toString().padStart(2, '0')}`;

      if (this.remainingSeconds <= 0) {
        this.stopCountdown();
        this.stopPolling();
        this.state = 'expired';
      }
    };

    tick();
    this.countdownTimer = setInterval(tick, 1000);
  }

  setup() {
    this.error = undefined;
    
    if (!this.username || !this.password) {
      this.error = 'Username and password required';
      return;
    }
    
    if (this.password !== this.confirmPassword) {
      this.error = 'Passwords do not match';
      return;
    }
    
    if (this.password.length < 8) {
      this.error = 'Password must be at least 8 characters';
      return;
    }

    this.auth.setupAdmin(this.username, this.password).subscribe({
      next: (res) => {
        if (res.success) {
          this.auth.saveToken(res.token);
          this.auth.saveUsername(res.username);
          this.state = 'success';
          setTimeout(() => this.router.navigate(['/admin']), 1500);
        }
      },
      error: (err) => {
        this.error = err?.error?.error || 'Setup failed';
      }
    });
  }

  login() {
    this.error = undefined;
    
    if (!this.username || !this.password) {
      this.error = 'Username and password required';
      return;
    }

    this.auth.login(this.username, this.password).subscribe({
      next: (res) => {
        if (res.success) {
          this.state = 'success';
          setTimeout(() => this.router.navigate(['/admin']), 1000);
        } else {
          this.error = res.error || 'Login failed';
        }
      },
      error: (err) => {
        this.error = err?.error?.error || 'Login failed';
      }
    });
  }

  copyInvoice() {
    if (!this.paymentSession?.invoice) return;
    navigator.clipboard.writeText(this.paymentSession.invoice);
    this.copied = true;
    setTimeout(() => (this.copied = false), 1200);
  }

  stopPolling() {
    if (this.pollTimer) {
      clearTimeout(this.pollTimer);
      this.pollTimer = undefined;
    }
  }

  stopCountdown() {
    if (this.countdownTimer) {
      clearInterval(this.countdownTimer);
      this.countdownTimer = undefined;
    }
  }

  ngOnDestroy() {
    this.stopPolling();
    this.stopCountdown();
  }
}
