# LiveAuth Meter MVP implementation plan

LiveAuth Meter is implemented as an optional, non-custodial extension of an existing
project. Existing authentication, project ownership, webhook delivery, caller-context
hashing, and dashboard conventions are reused; existing L402 and MCP endpoints are not
changed.

## Boundaries

- `MeterProjectSettings` owns the single-origin gateway configuration.
- `MeterRouteRule` provides deterministic method/path pricing.
- `MerchantLightningConnection` stores encrypted merchant-controlled LND REST
  credentials. Production invoices are never created on LiveAuth's node.
- `MeterPaymentChallenge` is both the idempotent invoice record and the atomic
  remaining-use ledger for the signed L402 credential.
- `MeterReceipt` and `MeterUsageEvent` provide signed evidence and analytics.
- `MeterGatewayMiddleware` resolves local path and production hostname routes before
  forwarding to a hardened, DNS-pinned HTTP proxy.

## Delivery order

1. Domain model, indexes, and the idempotent SQLite bootstrap migration.
2. AES-GCM secret protection and merchant LND invoice-provider abstraction.
3. Deterministic route matching, allowance counters, L402 credential issue/validation,
   replay protection, canonical receipts, and analytics/webhook events.
4. Hardened HTTP proxy with URL/DNS validation, size/time limits, safe headers,
   redirect blocking, cancellation, and bounded streaming.
5. Owner-only management APIs and Meter portal area.
6. Node-first `@liveauth/l402-fetch`, mock wallet, sample origin, documentation, and
   automated tests.

## Challenge idempotency

The challenge key is an HMAC over project, environment, caller key, method, normalized
path, matched rule, and (when present) request body hash, plus a short UTC time bucket.
The database has a unique index on this key. Repeated unpaid requests within the bucket
reuse the same unexpired invoice; no raw IP address is stored.

## Route syntax

Paths must start with `/`. Literal segments match exactly, `:name` matches one segment,
and a terminal `*` matches the remainder. Priority sorts descending, specificity sorts
descending, then rule ID sorts ascending, making matches deterministic.
