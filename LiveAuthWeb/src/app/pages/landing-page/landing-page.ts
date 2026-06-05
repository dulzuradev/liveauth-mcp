import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

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
  heroImageUrl = 'assets/images/liveauth_logo_4.png';
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

}
