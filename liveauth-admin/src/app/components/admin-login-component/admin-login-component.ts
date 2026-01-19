import {ChangeDetectorRef, Component, OnDestroy} from '@angular/core';
import {AdminAuthService, AdminStartLoginResponse} from '../../services/admin-auth';
import {FormsModule} from '@angular/forms';
import { QRCodeComponent } from 'angularx-qrcode';
import { CommonModule } from '@angular/common';
import {Router} from '@angular/router';



type AdminLoginState =
  | 'idle'
  | 'generating'
  | 'invoice'
  | 'waiting'
  | 'success'
  | 'expired';


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
  email = '';
  error?: string;

  session?: AdminStartLoginResponse;

  loginInProgress = false;
  polling = false;

  remainingSeconds = 0;
  remainingLabel = '';

  copied = false;
  state: AdminLoginState = 'idle';


  private pollTimer?: any;
  private countdownTimer?: any;
  private _changeDetector: ChangeDetectorRef;

  constructor(private auth: AdminAuthService, private changeDetector: ChangeDetectorRef, private router: Router) {
    this._changeDetector = changeDetector;
  }

  start() {
    this.error = undefined;
    const email = this.email.trim();
    if (!email) {
      this.error = 'Enter your admin email.';
      return;
    }

    this.state = 'generating';
    this.loginInProgress = true;

    this.auth.startLogin(email).subscribe({
      next: (res) => {
        this.session = res;
        this.state = 'invoice';

        setTimeout(() => {
          this.state = 'waiting';
        }, 300);

        this.loginInProgress = false;
        this._changeDetector.detectChanges();
        this.startCountdown(res.expiresAtUnix);
        this.startPolling();
      },
      error: (err) => {
        this.error = err?.error?.message ?? err?.error ?? 'Failed to start login.';
        this.loginInProgress = false;
        this.state = 'idle';
      }
    });
  }

  private startPolling() {
    if (!this.session || this.polling) return;
    this.polling = true;

    const pollOnce = () => {
      if (!this.session) return;

      if (this.remainingSeconds <= 0) {
        this.stopPolling();
        return;
      }

      this.auth.confirmLogin(this.session.sessionId).subscribe({
        next: (res) => {
          if (res.verified && res.token) {
            this.auth.saveToken(res.token);

            this.stopPolling();
            this.stopCountdown();
            this.state = 'success';

            this.router.navigate(['/admin']);

            return;
          }

          this.pollTimer = setTimeout(pollOnce, 2000);
        },
        error: () => {
          this.pollTimer = setTimeout(pollOnce, 2000);
        }
      });
    };

    pollOnce();
  }

  private startCountdown(expiresAtUnix: number): void {
    this.stopCountdown();

    // 🔒 Normalize: if it's too large, it's probably milliseconds
    const expiresAtSec =
      expiresAtUnix > 10_000_000_000
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

  copyInvoice() {
    if (!this.session?.invoice) return;
    navigator.clipboard.writeText(this.session.invoice);
    this.copied = true;
    setTimeout(() => (this.copied = false), 1200);
  }

  stopPolling() {
    this.polling = false;
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

  protected readonly HTMLInputElement = HTMLInputElement;
}
