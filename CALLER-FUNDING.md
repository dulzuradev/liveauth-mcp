# Caller-funded MCP calls

The LiveAuth project public key identifies the MCP provider. It does not fund caller tool usage.

The provider's LiveAuth Pro subscription is independent of per-call sats paid by MCP callers.

```text
MCP Caller
    │ paid tool request + caller JWT + stable operation ID
    ▼
liveauth-mcp / InvokeWorks
    │ LiveAuth project identity + registered tool + argument hash
    ▼
LiveAuthCore
    ├── authorize caller/session and resolve registered price
    ├── request caller Lightning payment if funds are insufficient
    ├── verify payment through LiveAuth's settlement node
    ├── debit caller funding lots and record usage atomically
    ├── create signed receipt and provider revenue event
    └── issue one execution authorization
    │
    ▼
InvokeWorks executes the protected tool
```

## Provider integration

`@liveauth-labs/mcp-server` 1.3.0 extends the existing `createMcpGate` API. Register each tool in LiveAuth with its provider project, price, and `fundingMode: "caller"`. A stored caller-funded tool cannot be downgraded by a request. Providers can explicitly request caller funding even before changing a legacy tool's configuration:

```typescript
import { createMcpGate, toMcpPaymentResult, mcpIdempotencyKey } from '@liveauth-labs/mcp-server';

const gate = createMcpGate({
  publicKey: process.env.LIVEAUTH_PUBLIC_KEY!,
  toolName: 'http_inspect',
  fundingMode: 'caller'
});

// Inside a registered MCP tool handler, after validating the tool arguments:
try {
  return await gate.invoke(jwt, input, protectedHandler, context, {
    costSats: 2,
    toolMethodName: 'http_inspect',
    idempotencyKey: mcpIdempotencyKey(requestHeader, rpcRequestId)
  });
} catch (error) {
  const paymentResult = toMcpPaymentResult(error);
  if (paymentResult) return paymentResult;
  throw error;
}
```

The gate hashes validated JSON arguments canonically. LiveAuth resolves the authoritative database price and rejects mismatches. A capability check runs before explicit caller-funded charging, so an old backend cannot silently reinterpret the request as provider-funded. `tools/list` and initialization remain outside the gate. Register free tools with price zero; authentication, idempotency, and rate limits still apply, but no invoice is needed.

## Caller flow and API

1. Use the existing MCP PoW start/confirm flow to obtain a caller JWT and refresh token. No developer account or Pro subscription is required. PoW gives an identity and spending limit, **zero funded sats**.
2. Invoke the tool. Use a unique `X-Request-Id` for the logical operation. Keep it, the caller session, tool and arguments unchanged for retries. Without the header, InvokeWorks derives a key from the JSON-RPC ID; a new RPC ID requires explicitly forwarding the original retry key.
3. Insufficient funds yield an MCP tool result with `isError: true`, structured content, text JSON and `_meta.liveauth`. The connection remains usable. LiveAuth's charge API uses HTTP 402 for this challenge:

```json
{
  "status": "deny",
  "reason": "payment_required",
  "fundingMode": "caller",
  "grossSats": 2,
  "callerBalanceSats": 0,
  "remainingBudgetSats": 10000,
  "payment": {
    "paymentId": "<invoice-session-uuid>",
    "method": "lightning",
    "amountSats": 2,
    "creditSats": 2,
    "invoice": "<BOLT11>",
    "expiresAt": "<UTC expiry>",
    "confirmPath": "/api/mcp/payments/<invoice-session-uuid>/confirm",
    "retryIdempotencyKey": "agent-operation-123"
  }
}
```

4. Pay the BOLT11 using the caller's wallet. `amountSats` includes any configured invoice fee; `creditSats` excludes it. Confirm using the same caller JWT with `POST /api/mcp/payments/{paymentId}/confirm`, `client.confirmPayment(paymentId)`, or the stdio tool `liveauth_mcp_payment_confirm`. It returns `pending`, `paid`, or `expired`. A paid confirmation is idempotent and never creates additional credits.
5. Retry the original tool. The retry can also confirm settlement automatically. Once confirmed, LiveAuth debits the caller and the provider executes. A later duplicate returns the original receipt/payment status through `operation_already_authorized`; the gate does not execute again.

The stdio `liveauth_mcp_charge` tool accepts `fundingMode`, `toolName`, `idempotencyKey`, `requestHash`, and optional `callCostSats`. It delegates to the same gate. Normally invoke the provider's protected tool directly; charging an operation independently consumes its single authorization.

`GET /api/mcp/usage` adds `callerBalanceSats`. `remainingBudgetSats` is a daily spending limit, not wallet money. Refresh preserves the caller session and funds. Funds never reset at midnight. Existing authentication-only Lightning/L402 session allowances are not retroactively converted into wallets; they retain their legacy semantics. L402 bundle validation and PoW are not evidence of per-call caller payment.

## Accounting, security and retries

Existing `McpGateSessions` store caller-bound Lightning funding lots. Existing `McpToolRevenueEvents` store pending payment operations and final charges; no separate wallet/accounting table was added. A charged caller event has:

- `FundingMode = caller`, explicit `ProviderProjectId`, and `PayingProjectId = null`.
- Existing token/session IDs identifying the caller, plus its logical operation key and canonical argument hash.
- Gross caller spend, platform fee and net provider revenue. Provider revenue is an earned ledger amount; this change does not add an automatic payout system.
- Signed payment allocations linking invoice-session IDs and consumed sats to the operation.
- Stored signed receipt, returned unchanged on replay. Existing receipts use HMAC-SHA256; verification requires a trusted verifier/signing key and is not public-key verification.
- Funding environment in the receipt. TEST funds cannot be used after promotion to LIVE.

A SQLite write transaction encloses balance checks, payment crediting, lot debits, daily/rate counters, ledger changes and receipt signing. The unique index scopes caller operations by tool, caller session and key; project ownership is checked server-side. A concurrent retry can get the existing status but cannot receive another execution grant. Expired unconfirmed invoices cannot fund an operation. A confirmed balance remains usable beyond invoice expiry. Payment IDs alone never authorize access to another caller's funds.

The gate provides **at-most-once execution authorization**. There is an unavoidable failure window between committing a charge and executing a remote tool: if a process crashes or its successful charge response is lost, the operation can remain paid with no result. Retrying returns the recorded payment state without re-execution. This implementation does not promise exactly-once completion across that failure window or durable replay of tool output. Reconciliation/refunds for those cases are operational follow-up work. Handler failures after authorization remain billable and preserve the receipt. Do not retry them under a new key unless a new paid attempt is intended.

Logs use caller/session, project, tool, payment and revenue IDs. JWTs, refresh tokens, node credentials, preimages, invoice bodies and raw LND responses must stay out of logs. Caller invoices are settled through LiveAuth's configured node, not a provider-controlled custom node.

## Provider-funded compatibility

Historical tools and events default to `provider`; no historical usage is relabeled as caller revenue. The prior per-session allowance mechanism remains supported for subsidized integrations. Spending actual `Projects.L402BalanceSats` now additionally requires the matching server-side `X-LW-Secret`, configured through `providerSecret` on the gate. This is a deliberate security migration: a public key plus a PoW JWT cannot withdraw provider deposits. Keep this credential exclusively on the provider server.

The first-party reserve/commit service has no provider spending credential, so it retains allowance metering without debiting project deposits and refuses caller-mode tools (`caller_gate_required`). First-party products needing caller funding must adopt the reusable caller gate. InvokeWorks always requests caller funding and never supplies a provider secret or uses provider funds as fallback.

## Migration and deployment

1. Back up the production SQLite database. Stop/drain backend writers and deploy LiveAuthCore. Its existing idempotent startup schema guards add funding fields, receipt storage, and a caller retry index. New databases get the same schema through EF. Existing allowance/credit fields are never backfilled into spendable balances. Production data was not accessed or migrated during development.
2. Preserve the current production signing keys and configure the real settlement node. Caller settlement requires HTTPS with normal certificate/hostname validation, or a trusted `Lnd:CertificateSha256` pin for a self-signed LND certificate. Plain HTTP is allowed only on loopback; redirects are refused. Do not enable `Lnd:UseMock` in production. Set invoice/tool fees explicitly; the 2-sat acceptance smoke uses zero fees so gross and net both equal 2.
3. Register/update provider tools using the authenticated developer API with `fundingMode: "caller"`, matching provider project and exact prices. InvokeWorks supplies `scripts/configure-liveauth-tools.mjs`, driven by the complete current catalog. Review its target project before running it with `LIVEAUTH_DEVELOPER_TOKEN`.
4. Review/test/package `@liveauth-labs/mcp-server` 1.3.0. Publish only through the operator's normal release workflow. Nothing is automatically published by these changes.
5. InvokeWorks `packages/liveauth` consumes 1.3.0. The prepared checkout uses a packaged local SDK override for reproducible pre-publication validation. After publishing, remove that override and regenerate the lockfile against `^1.3.0`. Build/test InvokeWorks and then deploy its server and updated public documentation. Preserve Authorization, MCP-Protocol-Version, Accept and X-Request-Id through the reverse proxy.
6. Verify with the TEST smoke below, then repeat in a deployed TEST project. A real Lightning payment/LIVE settlement and automatic provider payout are not asserted by the simulated test.

## Verification

Use sibling `LiveAuth`, `liveauth-mcp`, and `invokeworks` checkouts, Node 22+, pnpm 10.28.2, Python 3 and the .NET SDK/runtime. `LIVEAUTH_REPO` can override the sibling backend path.

```sh
# LiveAuth
 dotnet test LiveAuthCore.Tests/LiveAuthCore.Tests.csproj
# liveauth-mcp
 npm ci
 npm test
 npm run build
# InvokeWorks (prepared local SDK override)
 pnpm install --frozen-lockfile
 pnpm typecheck
 pnpm test
 pnpm --filter @invokeworks/server... build
 node scripts/caller-funded-smoke.mjs
```

The smoke creates a new isolated SQLite database and TEST project, registers all catalog tools, starts real LiveAuthCore and InvokeWorks HTTP servers, authenticates with real PoW, challenges an unfunded call, uses LiveAuth's existing TEST Lightning simulation, performs an actual `http_inspect` of example.com, verifies the receipt and usage, retries, and asserts one execution. It reads the actual ledger to prove caller spend +2, caller allowance -2, provider balance unchanged, and provider revenue +2. It never enables InvokeWorks' test bypass. TEST simulation does not spend real Lightning sats.
