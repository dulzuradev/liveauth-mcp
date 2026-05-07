# Code Review Findings - 2026-05-06

## Issues Found: 5

## Branch: feat/improvement-review-20260506

## Changes:
- `fix: improve security, add database indexes, and fix N+1 query patterns`

## Summary:

**1. Security - JWT Exception Handling (DeveloperVerificationService.cs)**
The `VerifyLiveAuthToken` method used a bare `catch (Exception ex)` which is bad practice — it swallows all exceptions including `OutOfMemoryException`, `StackOverflowException`, etc. If an unexpected exception occurs during JWT validation, the method would return `false` (correct behavior), but a developer monitoring logs wouldn't know whether it was a `SecurityTokenExpiredException` (expected) vs. a more serious error. The fix adds explicit catch blocks for each `SecurityToken*Exception` type with appropriate comments, and a final catch for unexpected exceptions that explicitly returns `false` (fail-secure).

**2. Performance - AuthEvent Missing Indexes (LiveAuthDbContext.cs)**
The `AuthEvents` table had no indexes defined. Every query filtering by `ProjectId` or `EventType` (e.g., in admin analytics, PoW logging, usage dashboards) would trigger a full table scan. Added three indexes: `ProjectId`, `EventType`, and a composite `(ProjectId, EventType)`.

**3. Performance - L402Macaroon.BundleId Missing Index (LiveAuthDbContext.cs)**
The `L402Macaroon.BundleId` column had no index, but `ValidateMacaroonAsync` performs lookups by `BundleId`. Added a dedicated index on `BundleId`.

**4. Performance - McpProxyMiddleware N+1 In-Memory Query (McpProxyMiddleware.cs)**
`FindProxyAsync` had a critical N+1 pattern: when looking up by ID prefix, it first fetched ALL active proxies into memory (`ToListAsync()`), then filtered in C# using `FirstOrDefault`. For projects with many proxies, this was both slow and memory-intensive. Fixed by using `EF.Functions.Like` to do prefix matching in the database query itself, returning only the matching record.

**5. Performance - Admin User Search with ToLower().Contains() (AdminUsersController.cs)**
The admin user search used `d.Email.ToLower().Contains(sl)` which cannot use any index (requires scanning every row and applying `ToLower()` to each). Replaced with `EF.Functions.Like()` which can leverage database indexes for case-insensitive pattern matching, and is portable across SQLite and Postgres.