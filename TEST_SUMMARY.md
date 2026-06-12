# LiveAuth MCP Server - Test Summary

## Current Status

The automated suite now covers the package SDK helpers and the actual MCP stdio server handlers.

Latest local verification:

```bash
npm test
npm run build
```

Result:

- 2 Vitest files passed.
- 29 tests passed.
- TypeScript build passed.

## Covered Surfaces

### SDK

- `createMcpClient` sends project headers and remembers confirmed JWTs.
- Confirmed sessions auto-refresh before expiry.
- PoW solving uses the backend `projectPublicKey:challengeHex:nonce` payload.
- `createMcpGate` validates sessions, charges generic usage, and routes paid tool calls to `/api/mcp/tools/{toolId}/charge`.
- Paid tool charge responses preserve revenue accounting and signed receipts.
- Budget-denied gate calls throw `BudgetExceededError`.

### MCP Server Tools

The in-memory MCP integration tests list and call the real stdio server handlers for:

- `liveauth_mcp_start`
- `liveauth_mcp_confirm`
- `liveauth_mcp_charge`
- `liveauth_mcp_usage`
- `liveauth_mcp_status`
- `liveauth_mcp_lnurl`
- `liveauth_mcp_refresh`

The tests cover both:

- No-config demo mode, including simulated confirm, charge, usage, lnurl, and refresh.
- Production forwarding, including project headers, cached JWT authorization, `forceL402`, and `macaroon`.

## Remaining Manual Checks

- Run one live API smoke with a real project public key.
- Run one real Lightning or L402 bundle flow if payment infrastructure is available.
- Test the published package through `npx @liveauth-labs/mcp-server` after packing/publishing.
