import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-mock-login',
  standalone: true,
  imports: [
    CommonModule,
    MatToolbarModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    FormsModule
  ],
  templateUrl: './mock-login.component.html',
  styleUrls: ['./mock-login.component.css']
})
export class MockLoginComponent {
  protected readonly history = history;
  username: string = '';
  password: string = '';
  paymentHash: string = '';
  message: string = '';
  isLoading: boolean = false;

  // Mock invoice fields
  invoiceId: string = '';
  invoiceAmountSats: number = 21;
  invoiceStatus: 'pending' | 'paid' | 'error' | '' = '';
  loggedIn: boolean = false;

  constructor(
    private http: HttpClient,
    private route: ActivatedRoute,
    private router: Router
  ) {
    this.paymentHash = this.route.snapshot.queryParamMap.get('paymentHash') || '';
  }

  private initMockInvoice() {
    // Use paymentHash when available to correlate the session; otherwise create a simple id
    this.invoiceId = this.paymentHash || `inv_${Math.random().toString(36).slice(2, 10)}`;
    this.invoiceStatus = 'pending';
  }

  submitLogin() {
    this.isLoading = true;
    this.message = '';

    const loginData = { username: this.username, password: this.password, paymentHash: this.paymentHash };
    this.http.post('https://api.liveauth.app/api/MockLogin', loginData).subscribe({
      next: (response: any) => {
        this.message = response.message;
        if (response.isSuccessful) {
          this.loggedIn = true;
          // Create a mock invoice and ask the API to "pay" it
          this.initMockInvoice();
          const invoicePayload = {
            invoiceId: this.invoiceId,
            amountSats: this.invoiceAmountSats,
            paymentHash: this.paymentHash
          };

          this.http.post('https://api.liveauth.app/api/MockLogin/PayInvoice', invoicePayload).subscribe({
            next: (payResp: any) => {
              this.invoiceStatus = payResp?.isPaid ? 'paid' : 'error';
              this.message = payResp?.message || (payResp?.isPaid ? 'Invoice paid.' : 'Invoice payment failed.');
              // Navigate after invoice attempt to keep the flow visible in the demo
              if (payResp?.isPaid) {
                this.router.navigate(['/dashboard']);
              }
              this.isLoading = false;
            },
            error: (payErr) => {
              this.invoiceStatus = 'error';
              this.message = payErr.error?.message || 'Invoice payment failed';
              this.isLoading = false;
            }
          });
        } else {
          this.isLoading = false;
        }
      },
      error: (err) => {
        this.isLoading = false;
        this.message = err.error?.message || 'Login failed';
      }
    });
  }
}
