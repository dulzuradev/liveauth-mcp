import { describe, expect, it } from 'vitest';
import { getLightningAppHtml, LIGHTNING_APP_MIME_TYPE, LIGHTNING_APP_URI } from './lightning-app.js';

describe('Lightning MCP App', () => {
  it('uses the stable MCP App resource identity and lifecycle', () => {
    const html = getLightningAppHtml();
    expect(LIGHTNING_APP_URI).toBe('ui://liveauth/lightning-payment');
    expect(LIGHTNING_APP_MIME_TYPE).toBe('text/html;profile=mcp-app');
    expect(html).toContain("request('ui/initialize'");
    expect(html).toContain("notify('ui/notifications/initialized')");
    expect(html).toContain("message.method === 'ui/resource-teardown'");
  });

  it('renders pending, paid, and expired copy and a wallet action', () => {
    const html = getLightningAppHtml();
    expect(html).toContain('Waiting for payment…');
    expect(html).toContain('Payment received ✓');
    expect(html).toContain('Invoice expired');
    expect(html).toContain('Open Wallet');
    expect(html).toContain("request('ui/open-link', { url: current.lightningUri })");
  });

  it('polls status through MCP and accepts only a validated lightning URI', () => {
    const html = getLightningAppHtml();
    expect(html).toContain("request('tools/call', { name: 'liveauth_mcp_status'");
    expect(html).toContain("/^lightning:[A-Za-z0-9]+$/");
  });
});
