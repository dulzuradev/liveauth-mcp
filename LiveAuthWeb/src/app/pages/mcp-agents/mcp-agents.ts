import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatTabsModule } from '@angular/material/tabs';

@Component({
  selector: 'app-mcp-agents',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    MatToolbarModule,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatChipsModule,
    MatTabsModule
  ],
  templateUrl: './mcp-agents.html',
  styleUrls: ['./mcp-agents.css']
})
export class McpAgentsComponent {
  heroImageUrl = 'assets/images/liveauth_logo_4.png';

  // SDK snippet shown in the demo
  sdkSnippet = `import { LiveAuth } from '@liveauth/sdk';

const liveauth = new LiveAuth({
  publicKey: 'la_pk_your_key'
});

const result = await liveauth.verify();
// { authenticated: true, sessionToken: '...' }`;

  // MCP server snippet
  mcpServerSnippet = `import { McpServer } from '@liveauth-labs/mcp-server';

const server = new McpServer({
  publicKey: 'la_pk_your_key',
  satsPerCall: 10
});

server.addTool('browser_use', async (params) => {
  // Tool logic here
});

// Authenticate every call — pay per request
server.listen(3000);`;

  // Pricing tiers
  tiers = [
    {
      name: 'Per Call',
      price: '10 sats',
      period: 'per MCP call',
      description: 'Pay-as-you-go. No commitment.',
      features: ['10 sats per authenticated call', 'No monthly fee', 'Best for low-volume agents'],
      highlight: false,
      cta: 'Start Free'
    },
    {
      name: 'Unlimited',
      price: '$29',
      period: '/month',
      description: 'Unlimited calls for power users.',
      features: ['Unlimited MCP calls', 'Priority support', 'Analytics dashboard', 'Webhook integrations'],
      highlight: true,
      cta: 'Get Started'
    }
  ];

  // How it works steps
  steps = [
    {
      number: '1',
      title: 'Add SDK to your agent',
      description: 'Integrate @liveauth-labs/sdk into your AI agent in minutes. Supports Node.js, Python, and any HTTP client.',
      code: `npm install @liveauth-labs/sdk`
    },
    {
      number: '2',
      title: 'Agent authenticates per call',
      description: 'Each tool call triggers a PoW challenge. The agent solves it locally — no user interaction required.',
      code: `const result = await liveauth.verify();`
    },
    {
      number: '3',
      title: 'Settle over Lightning',
      description: 'Payments are atomic and trustless. Pay per call or flat-rate. No invoices, no KYC, no hassle.',
      code: `// 10 sats deducted per call automatically`
    }
  ];

  // Use cases
  useCases = [
    {
      icon: 'smart_toy',
      title: 'AI Agents',
      description: 'Every tool call authenticated. No API keys to leak, no secrets to store.'
    },
    {
      icon: 'terminal',
      title: 'CLI Tools',
      description: 'Programmatic auth for scripts and automation. Pay per execution.'
    },
    {
      icon: 'cloud',
      title: 'Serverless Functions',
      description: 'Authenticate transient workloads. Pay only for what runs.'
    },
    {
      icon: 'robot',
      title: 'Robotic Process Automation',
      description: 'RPA bots authenticating actions without human intervention.'
    }
  ];
}