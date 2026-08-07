# @liveauth/l402-fetch

Node-first L402 fetch wrapper for LiveAuth Meter. It enforces `maxSats` before
calling the wallet, retries at most once, caches reusable credentials in memory,
and exposes receipt headers as `response.liveAuthReceipt`.

```ts
import { liveAuthFetch } from '@liveauth/l402-fetch';

const response = await liveAuthFetch('https://demo.pay.liveauth.app/research', {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ question: 'Why Lightning?' }),
  maxSats: 500,
  wallet: walletAdapter
});
```

Implement `L402WalletAdapter.payInvoice(invoice, { maxSats, signal })`. Keep wallet
secrets server-side; browser use is experimental and should use a user-approved
wallet bridge rather than embedded credentials.
