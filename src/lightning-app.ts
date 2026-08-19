export const LIGHTNING_APP_URI = 'ui://liveauth/lightning-payment';
export const LIGHTNING_APP_MIME_TYPE = 'text/html;profile=mcp-app';

export function getLightningAppHtml(): string {
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>LiveAuth Lightning payment</title>
  <style>
    :root { color-scheme: light dark; font-family: ui-sans-serif, system-ui, sans-serif; }
    * { box-sizing: border-box; }
    body { margin: 0; padding: 12px; background: transparent; color: var(--color-text-primary, #171426); }
    .card { max-width: 360px; margin: auto; padding: 22px; text-align: center; border: 1px solid var(--color-border, #ddd8eb); border-radius: 18px; background: var(--color-background-primary, #fff); box-shadow: 0 10px 32px rgba(20, 12, 42, .08); }
    .brand { display: flex; align-items: center; justify-content: center; gap: 8px; font-weight: 750; }
    .bolt { color: #f5a623; }
    .amount { margin: 18px 0 12px; font-size: 30px; font-weight: 800; }
    .qr { display: block; width: 236px; height: 236px; margin: 0 auto 16px; padding: 8px; border-radius: 12px; background: #fff; object-fit: contain; }
    button { width: 100%; border: 0; border-radius: 10px; padding: 12px 16px; background: #251a3d; color: #fff; font: inherit; font-weight: 700; cursor: pointer; }
    button[hidden], .qr[hidden] { display: none; }
    .status { margin: 14px 0 4px; font-weight: 700; }
    .pending { color: #9b6500; } .paid { color: #138044; } .expired { color: #a13232; }
    .expires { min-height: 20px; margin: 0; color: var(--color-text-secondary, #6b6577); font-size: 13px; }
    .invoice { margin-top: 12px; overflow: hidden; color: var(--color-text-secondary, #6b6577); font: 11px ui-monospace, monospace; text-overflow: ellipsis; white-space: nowrap; }
  </style>
</head>
<body>
  <main class="card" aria-live="polite">
    <div class="brand"><span class="bolt">⚡</span><span>LiveAuth</span></div>
    <div id="amount" class="amount">Lightning payment</div>
    <img id="qr" class="qr" alt="Lightning invoice QR code" hidden>
    <button id="wallet" type="button" hidden>Open Wallet</button>
    <p id="status" class="status pending">Waiting for payment…</p>
    <p id="expires" class="expires"></p>
    <p id="invoice" class="invoice"></p>
  </main>
  <script>
    (() => {
      let requestId = 0;
      let pollTimer;
      let expiryTimer;
      let current;
      let quoteId;
      const pending = new Map();
      const el = (id) => document.getElementById(id);

      function post(message) { window.parent.postMessage(message, '*'); }
      function request(method, params) {
        const id = ++requestId;
        post({ jsonrpc: '2.0', id, method, params });
        return new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
      }
      function notify(method, params) { post({ jsonrpc: '2.0', method, params }); }
      function resize() {
        notify('ui/notifications/size-changed', {
          width: Math.ceil(document.documentElement.scrollWidth),
          height: Math.ceil(document.documentElement.scrollHeight)
        });
      }
      function stopTimers() {
        if (pollTimer) clearInterval(pollTimer);
        if (expiryTimer) clearInterval(expiryTimer);
        pollTimer = undefined;
        expiryTimer = undefined;
      }
      function findQr(result) {
        const image = Array.isArray(result && result.content)
          ? result.content.find((item) => item && item.type === 'image' && item.mimeType === 'image/png')
          : undefined;
        return image && typeof image.data === 'string' ? 'data:image/png;base64,' + image.data : undefined;
      }
      function updateExpiry() {
        if (!current || current.status !== 'pending' || !current.expiresAt) return;
        const seconds = Math.max(0, Math.ceil((Date.parse(current.expiresAt) - Date.now()) / 1000));
        if (seconds === 0) {
          current = Object.assign({}, current, { status: 'expired' });
          renderState();
          return;
        }
        el('expires').textContent = 'Expires in ' + seconds + 's';
      }
      function renderState(qrSource) {
        if (!current) {
          el('status').textContent = 'No Lightning payment is required.';
          el('wallet').hidden = true;
          el('qr').hidden = true;
          resize();
          return;
        }
        const status = current.status === 'paid' ? 'paid' : current.status === 'expired' ? 'expired' : 'pending';
        el('amount').textContent = typeof current.amountSats === 'number' ? '⚡ ' + current.amountSats + ' sats' : 'Lightning payment';
        el('status').className = 'status ' + status;
        el('status').textContent = status === 'paid' ? 'Payment received ✓' : status === 'expired' ? 'Invoice expired' : 'Waiting for payment…';
        el('invoice').textContent = current.invoice || '';
        el('wallet').hidden = status !== 'pending';
        if (qrSource) el('qr').src = qrSource;
        el('qr').hidden = status !== 'pending' || !el('qr').src;
        el('expires').textContent = status === 'paid' ? 'LiveAuth can continue.' : status === 'expired' ? 'Request a new invoice to continue.' : '';
        if (status !== 'pending') stopTimers();
        updateExpiry();
        resize();
      }
      function applyResult(result) {
        const structured = result && result.structuredContent;
        current = structured && structured.lightning;
        quoteId = structured && typeof structured.quoteId === 'string' ? structured.quoteId : quoteId;
        renderState(findQr(result));
        if (current && current.status === 'pending') startPolling();
      }
      async function poll() {
        if (!quoteId || !current || current.status !== 'pending') return;
        try {
          const result = await request('tools/call', { name: 'liveauth_mcp_status', arguments: { quoteId } });
          applyResult(result);
        } catch (_) {
          el('status').textContent = 'Waiting for payment…';
        }
      }
      function startPolling() {
        if (!pollTimer && quoteId) pollTimer = setInterval(poll, 3000);
        if (!expiryTimer) expiryTimer = setInterval(updateExpiry, 1000);
      }

      el('wallet').addEventListener('click', async () => {
        if (!current || !/^lightning:[A-Za-z0-9]+$/.test(current.lightningUri || '')) return;
        await request('ui/open-link', { url: current.lightningUri });
      });

      window.addEventListener('message', async (event) => {
        if (event.source !== window.parent) return;
        const message = event.data;
        if (!message || message.jsonrpc !== '2.0') return;
        if (message.id !== undefined && (message.result !== undefined || message.error !== undefined)) {
          const handler = pending.get(message.id);
          if (!handler) return;
          pending.delete(message.id);
          message.error ? handler.reject(message.error) : handler.resolve(message.result);
          return;
        }
        if (message.method === 'ui/notifications/tool-input') {
          quoteId = message.params && message.params.arguments && message.params.arguments.quoteId;
        } else if (message.method === 'ui/notifications/tool-result') {
          applyResult(message.params);
        } else if (message.method === 'ui/resource-teardown' && message.id !== undefined) {
          stopTimers();
          post({ jsonrpc: '2.0', id: message.id, result: {} });
        }
      });

      request('ui/initialize', {
        appInfo: { name: 'LiveAuth Lightning Payment', version: '1.1.0' },
        appCapabilities: {},
        protocolVersion: '2026-01-26'
      }).then(() => {
        notify('ui/notifications/initialized');
        resize();
      }).catch(() => {
        el('status').textContent = 'This client does not support the LiveAuth payment view.';
      });
    })();
  </script>
</body>
</html>`;
}
