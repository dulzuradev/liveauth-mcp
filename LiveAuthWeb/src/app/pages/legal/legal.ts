import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-legal',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="legal-page">
      <header class="legal-header">
        <a routerLink="/" class="back-link">← Back to LiveAuth</a>
      </header>

      <main class="legal-content">
        <!-- Terms of Service -->
        <section id="terms">
          <h1>Terms of Service</h1>
          <p class="last-updated">Last updated: February 24, 2026</p>

          <h2>1. Acceptance</h2>
          <p>By using LiveAuth, you agree to these terms. If you don't agree, don't use the service.</p>

          <h2>2. Service Description</h2>
          <p>LiveAuth provides proof-of-work and Lightning Network-based authentication for applications. We aim for 99.9% uptime but don't guarantee it. The service is provided "as is."</p>

          <h2>3. Payments & Subscriptions</h2>
          <p>Pro subscriptions are billed in Bitcoin via Lightning. All payments are final. You can cancel anytime. Unused sats in your account don't expire.</p>

          <h2>4. Restrictions</h2>
          <p>You may not:</p>
          <ul>
            <li>Use LiveAuth for illegal purposes</li>
            <li>Attempt to circumvent authentication</li>
            <li>Resell the service without permission</li>
            <li>Spam or abuse the infrastructure</li>
          </ul>

          <h2>5. Liability</h2>
          <p>LiveAuth is not liable for any indirect, incidental, or consequential damages. Our total liability is limited to the amount you paid in the last 12 months.</p>

          <h2>6. Termination</h2>
          <p>We may terminate access for violation of these terms. You can delete your account anytime.</p>
        </section>

        <hr/>

        <!-- Privacy Policy -->
        <section id="privacy">
          <h1>Privacy Policy</h1>
          <p class="last-updated">Last updated: February 24, 2026</p>

          <h2>1. Data We Collect</h2>
          <ul>
            <li><strong>Account data:</strong> Email, project names, API keys</li>
            <li><strong>Usage data:</strong> Authentication attempts, success/failure, sats paid</li>
            <li><strong>Technical data:</strong> IP addresses (masked in logs), Lightning payment hashes</li>
          </ul>

          <h2>2. How We Use Data</h2>
          <p>We use data to:</p>
          <ul>
            <li>Provide authentication services</li>
            <li>Process Lightning payments</li>
            <li>Generate analytics for your projects</li>
            <li>Improve our service</li>
          </ul>

          <h2>3. Data Retention</h2>
          <p>Account data kept while active. Usage logs retained for 90 days. Lightning payment data kept for tax/compliance purposes.</p>

          <h2>4. No Selling</h2>
          <p>We don't sell your data. Ever. We don't run ads. Your project data is yours.</p>

          <h2>5. Lightning Payments</h2>
          <p>Payments via Lightning Network are pseudonymous. We see payment hashes but not your wallet balance or transaction history beyond our invoices.</p>

          <h2>6. Security</h2>
          <p>We use industry-standard encryption (TLS 1.3), hash secrets, and follow security best practices. No warrant canary—we'll fight for privacy.</p>

          <h2>7. GDPR / CCPA</h2>
          <p>You can request deletion of your data at any time. Contact us at support&#64;liveauth.app</p>
        </section>

        <hr/>

        <section>
          <h2>Contact</h2>
          <p>Questions? Email: <a href="mailto:support@liveauth.app">support&#64;liveauth.app</a></p>
        </section>
      </main>

      <footer class="legal-footer">
        <p>© 2026 LiveAuth. Built for the agent economy.</p>
      </footer>
    </div>
  `,
  styles: [`
    .legal-page {
      min-height: 100vh;
      background: var(--wall-deep, #0a0f1e);
      color: var(--text-primary, #e3e7ee);
      padding: 2rem;
    }

    .legal-header {
      max-width: 800px;
      margin: 0 auto 2rem;
    }

    .back-link {
      color: #00C2FF;
      text-decoration: none;
      font-weight: 500;
    }

    .back-link:hover {
      text-decoration: underline;
    }

    .legal-content {
      max-width: 800px;
      margin: 0 auto;
      line-height: 1.7;
    }

    h1 {
      font-size: 2rem;
      margin-bottom: 0.5rem;
      color: #fff;
    }

    h2 {
      font-size: 1.25rem;
      margin-top: 2rem;
      margin-bottom: 0.75rem;
      color: #00C2FF;
    }

    .last-updated {
      color: var(--text-secondary, #8b95a5);
      font-size: 0.875rem;
      margin-bottom: 2rem;
    }

    ul {
      padding-left: 1.5rem;
    }

    li {
      margin-bottom: 0.5rem;
    }

    hr {
      border: none;
      border-top: 1px solid #1e2a45;
      margin: 3rem 0;
    }

    a {
      color: #00C2FF;
    }

    .legal-footer {
      max-width: 800px;
      margin: 4rem auto 0;
      text-align: center;
      color: var(--text-secondary, #8b95a5);
      font-size: 0.875rem;
    }
  `]
})
export class LegalComponent {}
