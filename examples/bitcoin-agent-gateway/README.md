# Bitcoin Agent Gateway example

This minimal Node 20+ client demonstrates the intended agent loop against LiveAuth's remote MCP JSON-RPC endpoint.

```bash
cp .env.example .env
set -a; source .env; set +a
node client.mjs
```

Set `RAW_TRANSACTION` to a fully constructed and signed transaction. The example never handles private keys or seed phrases. It preflights first and only broadcasts when both `RAW_TRANSACTION` and `BROADCAST=true` are set.

The broadcast call always sends `X-LiveAuth-Idempotency-Key`. Save the returned `receipt` with the agent's audit record, then rerun with `TXID` to observe confirmation status.
