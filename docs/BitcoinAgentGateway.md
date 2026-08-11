# LiveAuth Bitcoin Agent Gateway

Bitcoin infrastructure built for AI agents.

The Bitcoin Agent Gateway gives an authenticated autonomous agent a narrow, metered interface to LiveAuth-operated Bitcoin infrastructure. An agent can query fee estimates, inspect a compact mempool summary, preflight a signed raw transaction, broadcast it with safety and idempotency controls, and observe confirmation state. Paid observations and executions include signed LiveAuth receipts suitable for an audit trail.

LiveAuth does **not** custody funds, create wallets, construct transactions, hold private keys, accept seed phrases, or sign transactions. The caller constructs and signs the transaction elsewhere.

## Endpoints

- MCP JSON-RPC: `POST /api/bitcoin/mcp`
- REST:
  - `GET /api/bitcoin/fees`
  - `GET /api/bitcoin/mempool`
  - `POST /api/bitcoin/transactions/preflight`
  - `POST /api/bitcoin/transactions/broadcast`
  - `GET /api/bitcoin/transactions/{txid}`

Both surfaces use the same application services, LiveAuth MCP identity, budgets, receipt signer, revenue ledger, webhook queue, and analytics records.

## Authentication

Obtain an MCP bearer token through the existing [LiveAuth MCP Gate](mcp-liveauth-gate.md), then send it as:

```http
Authorization: Bearer <liveauth-mcp-jwt>
```

The JWT must have the `McpClient` role and identify an active LiveAuth project and MCP gate token. Authentication failures are rejected before Bitcoin RPC work and are not charged.

An MCP client that supports remote HTTP servers can be configured along these lines:

```json
{
  "mcpServers": {
    "liveauth-bitcoin": {
      "url": "https://api.liveauth.app/api/bitcoin/mcp",
      "headers": {
        "Authorization": "Bearer ${LIVEAUTH_MCP_JWT}"
      }
    }
  }
}
```

## Tools and pricing

| Tool | Default price | Behavior |
| --- | ---: | --- |
| `bitcoin_get_fee_estimates` | 3 sats | Node-backed targets for 1, 3, 6, 25, and 144 blocks |
| `bitcoin_get_mempool_summary` | 3 sats | Compact node mempool state; never returns the full mempool |
| `bitcoin_preflight_transaction` | 5 sats | Calls `testmempoolaccept`; **never broadcasts** |
| `bitcoin_get_transaction_status` | 3 sats | Returns `mempool`, `confirmed`, or `not_found` |
| `bitcoin_broadcast_transaction` | 25 sats on success | Preflights, applies policy, and **can submit to Bitcoin** |

These are seed values. `McpTools.DefaultCostSats` is the runtime pricing authority, and bootstrap configuration updates the registered tool prices.

### Charging behavior

- Malformed input, failed authentication, node unavailability, and internal failure before meaningful work: no charge.
- Fee/mempool/status calls: charged only after a successful node-backed or explicitly marked cached observation.
- Preflight: charged when the node produces a policy result, including a normal rejected result.
- Broadcast preflight/policy rejection: no broadcast charge.
- Successful or timeout-recovered broadcast: 25 sats by default.
- Idempotent replay of a successful broadcast: returns the stored result and receipt with no second submission or charge.
- A charge is reserved only after broadcast preflight succeeds. If submission fails and cannot be observed on the node, the reservation is cancelled and budget usage is restored.

## Tool schemas

### `bitcoin_get_fee_estimates`

Input: `{}`

```json
{
  "estimates": [
    { "targetBlocks": 1, "satPerVbyte": 8.4 },
    { "targetBlocks": 3, "satPerVbyte": 6.2 }
  ],
  "observedAt": "2026-08-10T21:00:00Z",
  "source": "liveauth-bitcoin-node",
  "cached": false,
  "stale": false,
  "receipt": { "version": "mcp-call-receipt-v1" }
}
```

An unavailable target has `satPerVbyte: null` and an `unavailableReason`. Estimates are observations and never guarantee confirmation.

### `bitcoin_get_mempool_summary`

Input: `{}`

```json
{
  "transactionCount": 48321,
  "virtualSize": 183948201,
  "memoryUsageBytes": 421337600,
  "totalFeesSats": 125000000,
  "mempoolMinFeeSatVb": 1.1,
  "incrementalRelayFeeSatVb": 1.0,
  "observedAt": "2026-08-10T21:00:00Z",
  "source": "liveauth-bitcoin-node",
  "cached": false,
  "stale": false
}
```

### `bitcoin_preflight_transaction`

Input:

```json
{ "rawTransaction": "<fully-signed-transaction-hex>" }
```

The Gateway validates hex and size, parses the transaction locally, then calls Bitcoin Core `testmempoolaccept`. It does not call `sendrawtransaction`.

```json
{
  "accepted": true,
  "txid": "...",
  "wtxid": "...",
  "vsize": 141,
  "fees": { "baseSats": 1200, "effectiveSatPerVbyte": 8.511 },
  "rejectCode": null,
  "rejectReason": null,
  "observedAt": "2026-08-10T21:00:00Z",
  "source": "liveauth-bitcoin-node",
  "receipt": { "version": "mcp-call-receipt-v1" }
}
```

### `bitcoin_broadcast_transaction`

Input is identical to preflight. LiveAuth always performs this sequence:

1. Validate and locally identify the transaction.
2. Claim or replay the durable idempotency record.
3. Call `testmempoolaccept`.
4. Enforce maximum absolute fee and fee rate.
5. Reserve the configured MCP charge.
6. Call `sendrawtransaction` exactly once for the active execution.
7. Recover by txid after an ambiguous timeout when possible.
8. Commit the charge, sign the execution receipt, store the normalized result, and enqueue webhooks.

```json
{
  "accepted": true,
  "broadcasted": true,
  "alreadyKnown": false,
  "recovered": false,
  "txid": "...",
  "broadcastAt": "2026-08-10T21:00:00Z",
  "source": "liveauth-bitcoin-node",
  "receipt": {
    "version": "mcp-call-receipt-v1",
    "body": {
      "grossSats": 25,
      "attestation": {
        "kind": "execution",
        "operation": "bitcoin.broadcast_transaction",
        "subjectId": "<txid>"
      }
    }
  }
}
```

### `bitcoin_get_transaction_status`

Input:

```json
{ "txid": "<64-character-transaction-id>" }
```

Confirmed lookup uses Bitcoin Core `getrawtransaction` and `getblockheader`. A node without `txindex` may return `not_found` for older confirmed transactions it cannot resolve; the Gateway does not fabricate an answer or scan the chain.

## Idempotency

Send `X-LiveAuth-Idempotency-Key` on broadcast requests:

```http
X-LiveAuth-Idempotency-Key: agent-order-42-broadcast
```

The key is scoped by project and operation. Reusing it with different raw transaction bytes returns `LIVEAUTH_BITCOIN_IDEMPOTENCY_CONFLICT`. A successful replay returns the persisted normalized result and the same signed receipt. If no key is provided, LiveAuth scopes broadcast idempotency to the locally calculated txid.

Only a SHA-256 commitment to the raw bytes is stored. Raw transaction hex is not retained in the operation or revenue record.

## Signed receipts

Bitcoin receipts extend the existing `mcp-call-receipt-v1` envelope with a signed `attestation`:

- `kind`: `observation` or `execution`
- `operation`: stable Bitcoin operation name
- `observedAt`, `source`, and configured network
- `subjectId`: txid when applicable
- `canonicalClaims` and `claimsSha256`

The HMAC signature covers the normal billing fields plus every attestation field. Receipt signing occurs inside the charge transaction; LiveAuth will not commit a charge if it cannot produce the receipt.

## Errors

REST returns `{ "error": { ... } }`. MCP returns the same stable code in JSON-RPC error `data`.

| Code | Retryable | Meaning |
| --- | --- | --- |
| `LIVEAUTH_BITCOIN_INVALID_TX` | No | Missing, oversized, malformed, or non-transaction hex |
| `LIVEAUTH_BITCOIN_TX_REJECTED` | No | Node policy rejected the transaction |
| `LIVEAUTH_BITCOIN_FEE_LIMIT_EXCEEDED` | No | Configured broadcast fee policy rejected it |
| `LIVEAUTH_BITCOIN_MEMPOOL_CONFLICT` | No | Conflicts with a mempool transaction |
| `LIVEAUTH_BITCOIN_MISSING_INPUT` | Usually no | An input is unavailable to the node |
| `LIVEAUTH_BITCOIN_ALREADY_KNOWN` | No | Node already knows the transaction; use status or idempotent replay |
| `LIVEAUTH_BITCOIN_NODE_UNAVAILABLE` | Yes | Node/circuit unavailable |
| `LIVEAUTH_BITCOIN_RPC_TIMEOUT` | Yes | RPC exceeded the configured timeout |
| `LIVEAUTH_BITCOIN_RATE_LIMITED` | Yes | Client/operation rate exceeded |
| `LIVEAUTH_BITCOIN_OPERATION_IN_PROGRESS` | Yes | Same broadcast key currently executing |
| `LIVEAUTH_BITCOIN_IDEMPOTENCY_CONFLICT` | No | Same key, different transaction |
| `LIVEAUTH_BITCOIN_PAYMENT_DENIED` | Depends | MCP token, tool, or budget did not authorize charging |

## Configuration

Use environment variables in production. Never commit RPC or receipt secrets.

```bash
BitcoinGateway__Enabled=true
BitcoinGateway__RpcUrl=http://127.0.0.1:8332
BitcoinGateway__RpcUser=liveauth
BitcoinGateway__RpcPassword=replace-me
# Alternatively use BitcoinGateway__RpcCookieFile=/run/bitcoind/.cookie
BitcoinGateway__Network=mainnet
BitcoinGateway__MaxRawTransactionBytes=400000
BitcoinGateway__MaxFeeRateSatPerVbyte=1000
BitcoinGateway__MaxAbsoluteFeeSats=10000000
BitcoinGateway__RpcTimeoutMs=10000
BitcoinGateway__CircuitBreakerFailureThreshold=5
BitcoinGateway__CircuitBreakerBreakSeconds=30
BitcoinGateway__FeeEstimateCacheSeconds=30
BitcoinGateway__MempoolSummaryCacheSeconds=15
BitcoinGateway__ReadRateLimitPerMinute=60
BitcoinGateway__BroadcastRateLimitPerMinute=5
BitcoinGateway__OperationRetentionDays=90
BitcoinGateway__CleanupIntervalHours=24
```

The RPC URL is application configuration, never request input. The typed client exposes only fixed methods; there is no arbitrary RPC passthrough. Wallet RPC is neither enabled nor used.

## REST examples

```bash
curl -sS https://api.liveauth.app/api/bitcoin/fees \
  -H "Authorization: Bearer $LIVEAUTH_MCP_JWT"

curl -sS https://api.liveauth.app/api/bitcoin/transactions/preflight \
  -H "Authorization: Bearer $LIVEAUTH_MCP_JWT" \
  -H 'Content-Type: application/json' \
  --data "{\"rawTransaction\":\"$RAW_TX\"}"

curl -sS https://api.liveauth.app/api/bitcoin/transactions/broadcast \
  -H "Authorization: Bearer $LIVEAUTH_MCP_JWT" \
  -H 'Content-Type: application/json' \
  -H 'X-LiveAuth-Idempotency-Key: agent-order-42' \
  --data "{\"rawTransaction\":\"$RAW_TX\"}"
```

## Agent workflow

```text
bitcoin_get_fee_estimates
  -> construct and sign externally
  -> bitcoin_preflight_transaction (never broadcasts)
  -> accepted?
  -> bitcoin_broadcast_transaction (idempotency key; can broadcast)
  -> save signed execution receipt
  -> bitcoin_get_transaction_status
  -> save signed observation receipt
```

See [`examples/bitcoin-agent-gateway`](../examples/bitcoin-agent-gateway/README.md) for a runnable Node client.

## Operations and retention

- Fee estimates and mempool summaries use short caches with an explicitly marked bounded stale fallback when the node is temporarily unhealthy.
- Read and broadcast limits are partitioned by MCP identity; broadcast has the lower default.
- Admin analytics are available at `GET /api/admin/bitcoin-gateway` and reuse MCP revenue plus durable broadcast operation records.
- Webhooks: `liveauth.bitcoin.preflight.completed`, `liveauth.bitcoin.transaction.broadcast`, `liveauth.bitcoin.transaction.rejected`, and the existing `liveauth.mcp.tool.paid_call`.
- Stored data is limited to txid, request hash, project/token attribution, timestamps, price/outcome, normalized errors, result/receipt, and request/idempotency metadata.
- Broadcast operation/idempotency records are pruned after 90 days by default; expired unfinished reservations are cancelled before removal.
- RPC credentials, raw transactions, private keys, and seed phrases are never logged or stored by this feature.

## Deliberately out of scope

V1 does not provide address history, address UTXOs, Electrs, arbitrary RPC, block-explorer views, projected mempool blocks, wallets, balances, transaction construction/signing, PSBT signing, fee bumping, RBF/CPFP construction, custody, mining, or Lightning wallet functionality.
