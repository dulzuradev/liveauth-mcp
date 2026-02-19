import { Component } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

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
            Mint ecash tokens on-demand via Cashu and Lightning. Perfect for demos, faucets,
            test funding, or programmatic sats distribution to agents.
          </p>
          <ul class="usecases">
            <li>Seed test wallets and dev environments</li>
            <li>Programmatically issue sats to agents for tasks</li>
            <li>Demonstrate Cashu minting + redemption flows</li>
          </ul>
        </div>
        <div class="hero-card">
          <h3>Mint tokens</h3>
          <form (submit)="printSats($event)" class="sats-form">
            <label>
              Amount (sats)
              <input type="number" name="amount" [(ngModel)]="amount" min="1" required />
            </label>
            <label>
              Mint URL (optional)
              <input type="text" name="mint_url" [(ngModel)]="mintUrl" placeholder="https://mint.example.com" />
            </label>
            <button type="submit" [disabled]="loading">{{ loading ? 'Minting…' : 'Print Sats' }}</button>
            <p *ngIf="error" class="error">{{ error }}</p>
          </form>
        </div>
      </div>
    </section>

    <section *ngIf="result" class="section">
      <h2>Minted Tokens</h2>
      <p class="muted">Copy and store securely. You can redeem these with any compatible Cashu wallet.</p>
      <pre class="code">{{ result | json }}</pre>
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

    .hero-card { width: 100%; max-width: 420px; background: #00000080; border: 1px solid rgba(0,194,255,.2); border-radius: 14px; padding: 16px; box-shadow: 0 18px 48px #00c2ff26; }
    .sats-form { display: grid; gap: 12px; }
    label { display: grid; gap: 6px; font-weight: 600; }
    input { width: 100%; padding: 8px 10px; border-radius: 8px; border: 1px solid rgba(0,194,255,.25); background: rgba(0,0,0,0.35); color: #e3e7ee; }
    button { width: fit-content; padding: 10px 14px; border-radius: 10px; background: linear-gradient(135deg,#00C2FF,#0099cc); color: #0a0f1e; font-weight: 800; border: 0; box-shadow: 0 8px 20px #00c2ff40; }
    button[disabled] { filter: grayscale(.4); opacity: .7; }
    .error { color: #f37575; margin-top: 6px; }

    .section { max-width: 1100px; margin: 0 auto; padding: 12px 16px 42px; }
    .code { background: #0009; border: 1px solid rgba(0,194,255,.15); padding: 14px; border-radius: 10px; overflow: auto; }

    @media (min-width: 720px) {
      .hero-inner { grid-template-columns: 1.15fr .85fr; align-items: center; }
    }
  `]
})
export class SatsPrinterComponent {
  amount = 100;
  mintUrl = '';
  result: any = null;
  error = '';
  loading = false;

  constructor(private http: HttpClient) {}

  printSats(event: Event) {
    event.preventDefault();
    this.error = '';
    this.result = null;
    this.loading = true;

    const payload: any = { amount: this.amount };
    if (this.mintUrl?.trim()) payload.mint_url = this.mintUrl.trim();

    this.http.post('/api/SatsPrinter/demo/print', payload).subscribe({
      next: (data) => { this.result = data; this.loading = false; },
      error: (err) => { this.error = err?.error?.message || 'Error printing sats'; this.loading = false; }
    });
  }
}
