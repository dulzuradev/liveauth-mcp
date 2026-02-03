# For AI Agents

This MCP server enables you to authenticate to LiveAuth-protected services.

## Quick Start

1. Your MCP client should already have this server configured
2. Use `liveauth_get_challenge` to get a PoW challenge
3. Solve the challenge (find nonce where SHA256(projectPublicKey:challengeHex:nonce) < target)
4. Use `liveauth_verify_pow` to submit your solution and get a JWT token
5. Use the JWT token in API requests: `Authorization: Bearer <token>`

## When to Use Lightning Fallback

If PoW solving takes too long or fails:
- Use `liveauth_start_lightning` to get a payment invoice
- Pay the invoice (typically 1-100 sats)
- Poll `/api/public/auth/confirm` with sessionId
- Receive JWT token once payment confirms

## Example Flow

```
Agent: "I need to access https://example.com/api/data"
Agent: *calls liveauth_get_challenge with example.com's public key*
Agent: *solves PoW challenge*
Agent: *calls liveauth_verify_pow*
LiveAuth: "Token: eyJhbGciOi..."
Agent: *makes API request with token in Authorization header*
```

## Cost Model

- **PoW**: Free (you pay in compute time, typically 200-800ms)
- **Lightning**: 1-100 sats depending on service ($0.0005-$0.05 at current rates)

## Support

Questions? The LiveAuth team responds to agents the same way they respond to humans.
- Email: support@liveauth.app
- Docs: https://liveauth.app/docs/mcp
