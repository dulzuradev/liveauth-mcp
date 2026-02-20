import { Component } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BASE_API_URL } from '../../config';

@Component({
  selector: 'app-sats-printer',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="hero">
      <div class="hero-bg"></div>
      <div class="hero-inner">
        <div class="hero-copy">
          <h1 class="glow">🖨️ Sats Printer</h1>
          <p class="hero-sub">
            Send sats to any Lightning address. Perfect for funding agents, 
            faucet-style distributions, or test payments.
          </p>
          <ul class="usecases">
            <li>Fund agent wallets instantly</li>
            <li>Programmatically send sats to agents for tasks</li>
            <li>Demo Lightning payments without real money</li>
          </ul>
        </div>
        <div class="hero-card">
          <h3>Print Sats</h3>
          <form (submit)="printSats($event)" class="sats-form" *ngIf="!result">
            <label>
              Lightning Address
              <input type="text" name="address" [(ngModel)]="lightningAddress" placeholder="agent@getalby.com" required />
            </label>
            <label>
              Amount (sats)
              <input type="number" name="amount" [(ngModel)]="amount" min="1" required />
            </label>
            <button type="submit" [disabled]="loading">{{ loading ? 'Generating…' : 'Generate Invoice' }}</button>
            <p *ngIf="error" class="error">{{ error }}</p>
          </form>
          
          <div *ngIf="result && result.status === 'pending_payment'" class="invoice-display">
            <p class="success">Invoice generated! {{ result.amount }} sats to {{ result.lightningAddress }}</p>
            <p class="muted">In production, scan QR to pay. For demo:</p>
            <button (click)="simulatePayment()" [disabled]="simulating" class="simulate-btn">
              {{ simulating ? 'Confirming…' : 'Simulate Payment' }}
            </button>
          </div>
          
          <div *ngIf="result && result.status === 'paid'" class="success-display">
            <p class="success">✅ {{ result.amount }} sats sent to {{ result.lightningAddress }}!</p>
            <button (click)="reset()" class="reset-btn">Send More</button>
          </div>
        </div>
      </div>
    </section>
  `,
  styles: [`
    :host { display: block; background: #0a0f1e; color: #e3e7ee; min-height: 100vh; }
    .glow { font-weight: 800; background: linear-gradient(135deg,#00C2FF,#F2A900); -webkit-background-clip: text; -webkit-text-fill-color: transparent; text-shadow: 0 0 18px rgba(0,194,255,.35); }
    .muted { opacity: .8; }

    .hero { position: relative; padding: 42px 16px 24px; overflow: hidden; }
    .hero-bg { position: absolute; inset: -25% -25% auto; height: 360px; background: radial-gradient(700px circle at 10% 5%, rgba(0,194,255,.18), transparent 42%), radial-gradient(700px circle at 90% 10%, rgba(242,169,0,.12), transparent 48%), radial-gradient(800px circle at 40% 85%, rgba(0,194,255,.1), transparent 55%); filter: blur(30px); pointer-events: none; }
    .hero-inner { max-width: 1100px; margin: 0 auto; display: grid; grid-template-columns: 1fr; gap: 20px; position: relative; z-index: 1; align-items: start; }
    .hero-copy { max-width: 640px; }
    .hero-sub { font-size: 1.05rem; opacity: .92; line-height: 1.5; margin: 8px 0 14px; }
    .usecases { margin: 0; padding-left: 18px; opacity: .9; line-height: 1.5; }

    .hero-card { background: rgba(17,24,45,.8); border: 1px solid rgba(0,194,255,.15); border-radius: 12px; padding: 24px; }
    .hero-card h3 { margin: 0 0 16px; font-size: 1.1rem; }
    .sats-form { display: grid; gap: 12px; }
    .sats-form label { display: grid; gap: 4px; font-size: .85rem; opacity: .9; }
    .sats-form input { background: #0d1422; border: 1px solid rgba(0,194,255,.25); border-radius: 6px; padding: 10px; color: #e3e7ee; font-size: 1rem; }
    .sats-form input:focus { outline: none; border-color: #00C2FF; }
    .sats-form button { background: linear-gradient(135deg,#00C2FF,#0099cc); border: none; border-radius: 6px; padding: 12px; color: #fff; font-weight: 600; cursor: pointer; transition: opacity .2s; }
    .sats-form button:hover { opacity: .9; }
    .sats-form button:disabled { opacity: .5; cursor: not-allowed; }
    .error { color: #ff6b6b; font-size: .85rem; margin: 0; }
    .success { color: #00ff88; font-weight: 600; }
    
    .invoice-display, .success-display { text-align: center; }
    .simulate-btn { background: linear-gradient(135deg,#F2A900,#cc8800); border: none; border-radius: 6px; padding: 12px 24px; color: #fff; font-weight: 600; cursor: pointer; margin: 12px 0; }
    .reset-btn { background: rgba(0,194,255,.2); border: 1px solid rgba(0,194,255,.4); border-radius: 6px; padding: 10px 20px; color: #00C2FF; cursor: pointer; }
  `]
})
export class SatsPrinterComponent {
  private readonly baseUrl = BASE_API_URL;
  lightningAddress = '';
  amount = 100;
  loading = false;
  simulating = false;
  error = '';
  result: any = null;

  constructor(private http: HttpClient) {}

  printSats(event: Event) {
    event.preventDefault();
    this.error = '';
    this.result = null;
    this.loading = true;

    const payload = { 
      amount: this.amount,
      lightningAddress: this.lightningAddress 
    };

    this.http.post(`${this.baseUrl}/api/SatsPrinter/demo/print`, payload).subscribe({
      next: (data) => { 
        this.result = data; 
        this.loading = false; 
      },
      error: (err) => { 
        this.error = err?.error?.message || 'Error printing sats'; 
        this.loading = false; 
      }
    });
  }

  simulatePayment() {
    if (!this.result?.id) return;
    this.simulating = true;
    
    this.http.post(`${this.baseUrl}/api/SatsPrinter/demo/confirm`, { invoiceId: this.result.id }).subscribe({
      next: (data: any) => {
        this.result = { ...this.result, status: 'paid' };
        this.simulating = false;
      },
      error: (err) => {
        this.error = err?.error?.message || 'Confirmation failed';
        this.simulating = false;
      }
    });
  }

  reset() {
    this.result = null;
    this.lightningAddress = '';
    this.amount = 100;
  }
}
