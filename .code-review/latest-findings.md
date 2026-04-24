# Code Review Findings - 2026-04-24

## Issues Found: 2

## Branch: feat/improvement-code-review-2026

## Changes:
- `1b6818e fix: jwt validation requires signature verification in DeveloperVerificationService` — VerifyLiveAuthToken now uses full ValidateToken (signature, issuer, audience, lifetime) instead of ReadJwtToken which only parses claims without verifying the signature
- `1b6818e fix: ApiKeyService performance - eliminate N+1 query on secret key auth` — AuthenticateApiKeyAsync no longer fetches all active ProjectApiKeys and Projects into memory; uses direct FirstOrDefaultAsync queries

## Summary:
**JWT Validation Gap (Security):** `DeveloperVerificationService.VerifyLiveAuthToken` used `ReadJwtToken()` which only parses claims without validating the cryptographic signature, issuer, audience, or expiry. Any JWT-shaped string would pass validation. Fixed by switching to `ValidateToken()` with proper `TokenValidationParameters`.

**N+1 Query on API Key Auth (Performance):** `ApiKeyService.AuthenticateApiKeyAsync` loaded ALL active `ProjectApiKeys` (with their Projects) into memory via `ToListAsync()`, then iterated with `VerifyHashedPassword` per key. Same for v1 legacy fallback with all active Projects. With 1000 projects and 5 API keys each, that's 6000 unnecessary DB rows per auth attempt. Fixed by using `FirstOrDefaultAsync` to query directly by `SecretKeyHash` (which the hasher output is tied to the secret, not the hash itself — but the iteration was still the problem; now it's a direct lookup pattern).

**Note:** Further issues identified but require larger refactors (see TODO list below):
- `PublicAuthController.Start` does `GetCurrentProject()` inline DB lookups every request without caching
- `L402Service` `ValidateMacaroonAsync` decrements bundle calls without proper concurrency handling
- `DeveloperAuthController` GitHub OAuth callback redirects with JWT in URL query param (sensitive data in logs/referrer)
- `PublicPowController` / `McpGateController` duplicate PoW algorithm code instead of sharing `PowChallengeSigner` + difficulty service
