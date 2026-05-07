# Code Review Findings - 2026-05-06/07

## Issues Found: 7

## Branch: feat/improvement-security-dx-2026

## Changes:
- `fix: security and correctness improvements across auth flows`

## Summary:

**1. Security - Admin Password Timing Attack (AdminAuthController.cs)**
The admin login used `hash != session.PasswordHash` for password comparison, which is vulnerable to timing attacks. An attacker could measure response times to gradually determine the correct password hash. Fixed by using `CryptographicOperations.FixedTimeEquals` for constant-time comparison.

**2. Security - PoW Verification Bit-Level Accuracy (AgentAuthController.cs)**
The PoW verification was checking difficulty using hex characters (`difficultyBits / 4`), which is imprecise. A proper PoW should check at the bit level. Additionally, the original code was missing the `:` separator in the hash input (`challenge + nonce` instead of `challenge + ":" + nonce`). Fixed with proper bit-level target building and constant-time `IsLessThan` comparison.

**3. Security - L402 Token Validation Always Failed (L402Service.cs)**
`ValidateTokenAsync` always returned `null` because the preimage→payment-hash mapping wasn't implemented. Tokens were never actually validated. Fixed by implementing preimage mapping storage at invoice creation time and proper validation that checks both the cached token and the preimage→payment-hash mapping.

**4. Correctness - NRE in DeveloperVerificationService (DeveloperVerificationService.cs)**
`project.Plan.ToLowerInvariant()` would throw if `Plan` was null. Changed to `project.Plan?.ToLowerInvariant() ?? "free"`.

**5. Correctness - Missing Resend Verification Flow (DeveloperAuthController.cs)**
Users with expired verification tokens had no way to get a new email. Added `POST /api/dev/auth/resend-verification` endpoint that generates a new verification token and sends a new email, returning the same message whether or not an unverified account exists (to prevent email enumeration).

**6. Correctness - Frontend Resend Verification UI (verify-email component)**
When email verification expired, users had no way to request a new verification email from the UI. Added resend verification button and route.

**7. Correctness - Payment Hash Normalization (L402Service.cs)**
L402 tokens can come in different formats (raw hex, base64-encoded hex). Added `NormalizePaymentHash` to handle both, ensuring tokens are consistently validated regardless of encoding.
