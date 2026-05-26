# Code Review Findings - 2026-05-26

## Issues Found: 3

## Branches & Changes

### Branch: feat/improvement-concurrency-bundle-decrement

**Commit:** `3a1ffa5` - fix: atomic bundle decrement to prevent concurrent over-depletion

**Finding:** TOCTOU race condition in `L402Service.ValidateMacaroonAsync`
- Multiple concurrent MCP requests could both read `RemainingCalls=1`, both decrement via EF, resulting in `RemainingCalls=-1` or over-depletion
- The pre-check `if (bundle.RemainingCalls <= 0)` was a read-modify-write race

**Fix:** Replaced in-memory check + EF modification with single atomic SQL UPDATE:
```sql
UPDATE L402Bundles
  SET "RemainingCalls" = "RemainingCalls" - 1,
      "Status" = CASE WHEN "RemainingCalls" - 1 <= 0 THEN 'depleted' ELSE "Status" END
  WHERE "BundleId" = {bid} AND "RemainingCalls" > 0
```
The WHERE clause guarantees no decrement if calls are exhausted. Also added `CancellationToken ct` parameter for consistency.

---

### Branch: feat/improvement-dev-auth-error-responses

**Commit:** `45e54a6` - fix: add error details to DevConfirmLoginResponse for DX improvement

**Finding:** `DevConfirmLoginResponse` returned only `{Verified, Token}` on all failure paths, making it impossible for clients to distinguish:
- SESSION_NOT_FOUND: session doesn't exist
- SESSION_EXPIRED: invoice expired before payment
- EMAIL_IDENTITY_MISMATCH: email registered with a different Lightning identity (potential hijack attempt)
- Payment still pending (no error returned at all)

**Fix:** Added optional `Error` (human message) and `ErrorCode` (machine-readable) fields to `DevConfirmLoginResponse`. All failure paths now return structured error codes. Also fixed GitHub OAuth callback leaking `ex.Message` in 500 responses.

---

### Branch: feat/improvement-l402-header-refactor

**Commit:** `bf16077` - refactor: replace Console.WriteLine debug statements with ILogger in LightningService

**Finding:** `LightningService.GetInvoiceStatusAsync` contained 6 `Console.WriteLine` debug statements that:
- Pollute stdout in production
- Leak internal state (payment hashes, LND URLs) to console
- Cannot be filtered/aggregated via standard logging frameworks

**Fix:** Replaced all `Console.WriteLine` with `ILogger.LogDebug` using structured logging (`{Property}` placeholders). Also added `ILogger<LightningService>` injection to the service constructor.

---

## Summary

Three targeted improvements across correctness and DX:

1. **Correctness (Security)**: Atomic bundle decrement prevents concurrent over-depletion via SQL atomicity, closing a revenue/leak vector.
2. **DX**: Dev auth error responses now return structured error codes so clients can take appropriate action instead of retrying blindly.
3. **Observability**: Debug output moved to structured ILogger; production logs remain clean while debug traces stay available via log level configuration.