import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { BASE_API_URL } from '../../config';

// Angular Material
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';

@Component({
  selector: 'app-landing-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    FormsModule,
    MatToolbarModule,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatChipsModule
  ],
  templateUrl: './landing-page.html',
  styleUrls: ['./landing-page.css']
})
export class LandingPageComponent {
  constructor(private http: HttpClient) {}

  heroImageUrl = 'assets/images/liveauth_logo_4.png';
  waitlist = {
    email: '',
    useCase: '',
    githubOrTwitter: ''
  };
  waitlistSubmitting = false;
  waitlistStatus: 'idle' | 'success' | 'error' = 'idle';
  waitlistMessage = '';

  liveAuthSnippet = `import { LiveAuthMcpClient } from '@liveauth-labs/mcp-server/client';

const liveauth = new LiveAuthMcpClient({
  publicKey: 'la_pk_...',
  onInvoice(invoice) {
    renderQrCode(invoice.bolt11);
  }
});

const session = await liveauth.start({ forceLightning: true });
const token = await liveauth.confirmLightning(session);`;

  serverGateSnippet = `import { LiveAuthMcpServerGate } from '@liveauth-labs/mcp-server/server';

const gate = new LiveAuthMcpServerGate({
  publicKey: 'la_pk_...',
  defaultCostSats: 1
});

await gate.gateTool(jwt, args, runTool, context);`;

  submitWaitlist() {
    const payload = {
      email: this.waitlist.email.trim(),
      useCase: this.waitlist.useCase.trim(),
      githubOrTwitter: this.waitlist.githubOrTwitter.trim() || null,
      source: 'liveauth.app'
    };

    if (!payload.email || !payload.useCase) {
      this.waitlistStatus = 'error';
      this.waitlistMessage = 'Email and use case are required.';
      return;
    }

    this.waitlistSubmitting = true;
    this.waitlistStatus = 'idle';
    this.waitlistMessage = '';

    this.http.post(`${BASE_API_URL}/api/public/waitlist`, payload).subscribe({
      next: () => {
        this.waitlistSubmitting = false;
        this.waitlistStatus = 'success';
        this.waitlistMessage = 'Thanks. I will follow up with the smallest usable MCP payment demo.';
        this.waitlist = { email: '', useCase: '', githubOrTwitter: '' };
      },
      error: () => {
        this.waitlistSubmitting = false;
        this.waitlistStatus = 'error';
        this.waitlistMessage = 'Could not join right now. Email hello@liveauth.app and I will add you manually.';
      }
    });
  }

}
