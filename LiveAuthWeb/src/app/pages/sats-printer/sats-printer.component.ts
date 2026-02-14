import { Component } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-sats-printer',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <h2>Sats Printer</h2>
    <form (submit)="printSats($event)" class="sats-form">
      <label>
        Amount (sats):
        <input type="number" name="amount" [(ngModel)]="amount" min="1" required />
      </label>
      <label>
        Mint URL (optional):
        <input type="text" name="mint_url" [(ngModel)]="mintUrl" placeholder="https://mint.example.com" />
      </label>
      <button type="submit">Print Sats</button>
    </form>

    <section *ngIf="result" class="result">
      <h3>Minted Tokens</h3>
      <pre>{{ result | json }}</pre>
    </section>

    <p *ngIf="error" class="error">{{ error }}</p>
  `,
  styles: [`
    .sats-form { display: grid; gap: 12px; max-width: 420px; }
    input { width: 100%; padding: 6px 8px; }
    button { width: fit-content; }
    .error { color: #d33; }
  `]
})
export class SatsPrinterComponent {
  amount = 100;
  mintUrl = '';
  result: any = null;
  error = '';

  constructor(private http: HttpClient) {}

  printSats(event: Event) {
    event.preventDefault();
    this.error = '';
    this.result = null;

    const payload: any = { amount: this.amount };
    if (this.mintUrl?.trim()) payload.mint_url = this.mintUrl.trim();

    this.http.post('/api/sats/print', payload).subscribe({
      next: (data) => { this.result = data; },
      error: (err) => { this.error = err?.error?.message || 'Error printing sats'; }
    });
  }
}
