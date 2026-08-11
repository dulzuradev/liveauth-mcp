const apiUrl = (process.env.LIVEAUTH_API_URL || 'https://api.liveauth.app').replace(/\/$/, '');
const token = process.env.LIVEAUTH_MCP_JWT;
if (!token) throw new Error('LIVEAUTH_MCP_JWT is required');

let requestId = 0;
async function callTool(name, args = {}, idempotencyKey) {
  const response = await fetch(`${apiUrl}/api/bitcoin/mcp`, {
    method: 'POST',
    headers: {
      authorization: `Bearer ${token}`,
      'content-type': 'application/json',
      ...(idempotencyKey ? { 'x-liveauth-idempotency-key': idempotencyKey } : {})
    },
    body: JSON.stringify({
      jsonrpc: '2.0',
      id: ++requestId,
      method: 'tools/call',
      params: { name, arguments: args }
    })
  });
  const message = await response.json();
  if (message.error) {
    const error = new Error(`${message.error.data?.code || message.error.code}: ${message.error.message}`);
    error.retryable = message.error.data?.retryable === true;
    throw error;
  }
  return message.result.structuredContent;
}

const fees = await callTool('bitcoin_get_fee_estimates');
console.log('fee estimates', fees.estimates);

const rawTransaction = process.env.RAW_TRANSACTION;
if (rawTransaction) {
  const preflight = await callTool('bitcoin_preflight_transaction', { rawTransaction });
  console.log('preflight', { accepted: preflight.accepted, txid: preflight.txid, receipt: preflight.receipt });

  if (preflight.accepted && process.env.BROADCAST === 'true') {
    const broadcast = await callTool(
      'bitcoin_broadcast_transaction',
      { rawTransaction },
      process.env.IDEMPOTENCY_KEY || `broadcast-${preflight.txid}`
    );
    console.log('broadcast', { txid: broadcast.txid, recovered: broadcast.recovered, receipt: broadcast.receipt });
  }
}

if (process.env.TXID) {
  const status = await callTool('bitcoin_get_transaction_status', { txid: process.env.TXID });
  console.log('status', status);
}
