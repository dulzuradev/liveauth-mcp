# PermitSignal MCP bridge

This stdio server exposes PermitSignal's four paid MCP tools to desktop clients. Tool
execution and metering happen atomically in LiveAuth Core; this process only bridges
stdio MCP to the authenticated HTTP MCP endpoint.

```bash
npm install
cp .env.example .env
npm start
```

Set `LIVEAUTH_API_KEY` to the project's public key and `LIVEAUTH_JWT` to an active MCP
Gate token. See [`../../docs/PermitSignal.md`](../../docs/PermitSignal.md) for the local
PoW authentication flow and client configuration.

For TEST mode, `npm run auth:test` solves LiveAuth's local proof-of-work challenge and
prints a short-lived JWT plus an export command. It does not require a Lightning payment.
