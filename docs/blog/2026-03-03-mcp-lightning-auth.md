---
title: How to Auth Your MCP Agent with Lightning
description: Add Lightning Network payments to your AI agents in 5 minutes using the LiveAuth MCP server.
date: 2026-03-03
author: Scott
---

# How to Auth Your MCP Agent with Lightning

AI agents are exploding. Every startup is building agents. But here's the problem: **how do you get paid for agent API usage?**

CAPTCHAs don't work. API keys get leaked. Rate limiting is easily bypassed.

**Enter LiveAuth:** Proof-of-work + Lightning Network authentication for AI agents.

## The Problem

You're building an AI agent that calls external APIs. You need to:
1. Verify the agent is legitimate (not a bot)
2. Meter usage (pay-per-call)
3. Do this without user friction

Traditional solutions:
- **API keys** — get leaked, abused, rotated
- **CAPTCHA** — broken by AI, frustrates users
- **OAuth** — overkill for agent-to-agent

## The Solution: Lightning + PoW

LiveAuth uses two authentication mechanisms:

1. **Proof-of-work** — agent solves a computational puzzle (free, no wallet needed)
2. **Lightning payments** — agent pays per verification (1-10 sats)

Both generate a JWT the agent uses for API access.

## Quick Start (5 Minutes)

### Step 1: Add MCP Server

```bash
npx @liveauth-labs/mcp-server
```

That's it! Demo mode runs without any config.

### Step 2: Add to Claude Desktop

```json
{
  "mcpServers": {
    "liveauth": {
      "command": "npx",
      "args": ["-y", "@liveauth-labs/mcp-server"],
      "env": {
        "LIVEAUTH_API_KEY": "la_pk_xxx"
      }
    }
  }
}
```

### Step 3: Use in Your Agent

The MCP server exposes tools:

```
liveauth_mcp_start     → get PoW challenge or Lightning invoice
liveauth_mcp_confirm  → submit proof, get JWT
liveauth_mcp_charge   → meter generic usage (sats per call)
```

For paid MCP tools that need revenue attribution, import the SDK and configure `createMcpGate({ toolId })`. That routes charges to `/api/mcp/tools/{toolId}/charge` and records gross sats, LiveAuth platform fee, net sats, a revenue event ID, and a signed per-call receipt.

## How It Works

```
┌─────────────┐     ┌──────────────┐     ┌─────────────┐
│  AI Agent   │────▶│ MCP Server   │────▶│  LiveAuth   │
│             │     │              │     │    API      │
│ 1. Start   │     │ /api/mcp    │     │ PoW/LN     │
│ 2. Prove   │     │ /confirm    │     │ JWT        │
│ 3. Charge  │     │ /charge     │     │ Meter      │
│ 4. Paid    │     │ /tools/{id} │     │ Revenue    │
└─────────────┘     └──────────────┘     └─────────────┘
```

## Pricing

- **Demo:** 3 sats per verification (free)
- **Production:** 1-10 sats per verification (you set)

For reference, 1 sat ≈ $0.0004 USD. So 10 sats = 1/250th of a penny.

## Why This Matters

1. **Permissionless** — no account signup, no OAuth flow
2. **Cryptographic** — proof of work/payment is verifiable
3. **Micropayments** — pay per call, no subscription
4. **Agent-native** — designed for AI agents, not humans

## What's Next?

The AI agent economy needs payment infrastructure. We're building:
- **Agent reputation scores** — trust scoring based on payment history
- **Agent-to-agent invoicing** — agents paying each other
- **Enterprise SLAs** — high-volume pricing

Try it now:
```bash
npx @liveauth-labs/mcp-server
```

Docs: [liveauth.app](https://liveauth.app)
