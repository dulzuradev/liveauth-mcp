import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { QrcodeComponent } from 'qrcode-angular';

import { MatToolbarModule } from '@angular/material/toolbar';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDividerModule } from '@angular/material/divider';

import { Router, RouterModule } from '@angular/router';
import { Clipboard, ClipboardModule } from '@angular/cdk/clipboard';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { LiveAuthClientService } from '../../../services/liveauth-client.service';

type AuthStatus = 'idle' | 'verifying' | 'success' | 'failed';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    CommonModule,
    QrcodeComponent,
    MatToolbarModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatChipsModule,
    MatSnackBarModule,
    MatDividerModule,
    RouterModule,
    ClipboardModule,
    MatFormFieldModule,
    MatInputModule
  ],
  templateUrl: './login-component.html',
  styleUrls: ['./login-component.css']
})
export class LoginComponent implements OnInit, OnDestroy {

  /* =====================================================
   * UI STATE
   * ===================================================== */

  status: AuthStatus = 'idle';
  statusMessage = '';
  loading = false;
  copied = false;

  // Lightning fallback
  invoiceUri: string | null = null;
  amount = 0;

  // Proof transparency (Steps 6–7)
  debugEnabled = false;
  showDetails = false;
  lastMethod: 'pow' | 'lightning' | null = null;
  lastSolveMs?: number;
  lastDifficultyBits?: number;

  qrSize = 320;
  private sub?: Subscription;
  private navTimeout?: number;
  private boundResize = this.updateQrSize.bind(this);

  get lightningQrValue(): string {
    if (!this.invoiceUri) return '';
    return `LIGHTNING:${this.invoiceUri.trim()}`;
  }

  constructor(
    private liveAuth: LiveAuthClientService,
    private snackBar: MatSnackBar,
    private router: Router,
    private clipboard: Clipboard
  ) {}

  /* =====================================================
   * LIFECYCLE
   * ===================================================== */

  ngOnInit(): void {
    this.debugEnabled = this.isDebugEnabled();
    this.updateQrSize();
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    if (this.navTimeout) {
      clearTimeout(this.navTimeout);
      this.navTimeout = undefined;
    }
  }

  updateQrSize(): void {
    // Fixed size for reliable Lightning scanning on iOS
    this.qrSize = 280;
  }


  /* =====================================================
   * DEFAULT: PoW → auto Lightning fallback
   * ===================================================== */

  initiateLogin(): void {
    this.resetUi();

    this.loading = true;
    this.status = 'verifying';
    this.statusMessage = 'Verifying you’re human…';

    this.sub = this.liveAuth.verifyHuman().subscribe({
      next: result => {
        this.loading = false;
        this.status = 'success';
        this.statusMessage = 'Access granted!';

        this.lastMethod = result.method;
        this.lastSolveMs = result.solveMs;
        this.lastDifficultyBits = result.difficultyBits;

        if (this.debugEnabled) {
          this.showDetails = true;
        }

        this.snackBar.open('Verification successful 🎉', 'Close', {
          duration: 2000
        });

        this.router.navigate(['/mock-login'], {
          queryParams: { token: result.token }
        });
      },
      error: err => {
        console.error(err);
        this.loading = false;
        this.status = 'failed';
        this.statusMessage = 'Verification failed';
      }
    });
  }

  /* =====================================================
   * DEMO: Force Lightning flow
   * ===================================================== */

  forceLightningDemo(): void {
    this.resetUi();

    this.loading = true;
    this.status = 'verifying';
    this.statusMessage = 'Waiting for Lightning payment…';
    this.lastMethod = 'lightning';

    this.sub = this.liveAuth.startLightningDemo().subscribe({
      next: res => {
        this.loading = false;

        this.invoiceUri = res.invoice.trim();
        console.log(this.lightningQrValue);

        this.amount = res.amountSats;
        this.lastMethod = 'lightning';

        // Start polling for payment (demo uses demo/confirm endpoint)
        this.sub = this.liveAuth.pollDemoLightning(res.sessionId).subscribe({
          next: token => {
            console.log('Lightning payment confirmed', token);
            this.status = 'success';
            this.statusMessage = 'Access granted!';

            this.snackBar.open('Lightning payment confirmed ⚡', 'Close', {
              duration: 2000
            });

            // Optional navigation
            // this.router.navigate(['/mock-login'], {
            //   queryParams: { token }
            // });
            this.goToDemoLogin(token);
          },
          error: err => {
            console.error(err);
            this.status = 'failed';
            this.statusMessage = 'Lightning verification failed';
          }
        });
      },
      error: err => {
        console.error(err);
        this.loading = false;
        this.status = 'failed';
        this.statusMessage = 'Failed to start Lightning verification';
      }
    });
  }

  /* =====================================================
   * STEP 6: PROOF INFO (SNACKBAR)
   * ===================================================== */

  showProofInfo(): void {
    if (!this.lastMethod) return;

    const title =
      this.lastMethod === 'pow'
        ? `Proof-of-Work (${this.lastDifficultyBits} bits)`
        : 'Lightning fallback';

    const body =
      this.lastMethod === 'pow'
        ? `Solved in ${this.lastSolveMs} ms\nNo tracking · No identifiers`
        : `PoW skipped → Lightning payment used`;

    this.snackBar.open(`${title}\n${body}`, 'Close', {
      duration: 6000
    });
  }

  /* =====================================================
   * LIGHTNING INVOICE COPY
   * ===================================================== */

  copyInvoice(): void {
    if (!this.invoiceUri) return;

    this.clipboard.copy(this.invoiceUri);
    this.copied = true;
    setTimeout(() => (this.copied = false), 1500);
  }

  /* =====================================================
   * HELPERS
   * ===================================================== */

  private resetUi(): void {
    this.sub?.unsubscribe();
    this.invoiceUri = null;
    this.amount = 0;
    this.showDetails = false;
    this.copied = false;
  }

  private isDebugEnabled(): boolean {
    const params = new URLSearchParams(window.location.search);
    if (params.get('liveauth_debug') === '1') return true;
    if (localStorage.getItem('LIVEAUTH_DEBUG') === '1') return true;
    return false;
  }

  private goToDemoLogin(token: string): void {
    // Navigate to the mock-login after a 10 second delay
    this.navTimeout = window.setTimeout(() => {
      this.router.navigate(['/mock-login'], {
        queryParams: { token }
      });
    }, 2_000);
  }
}
