---
title: "How to Add Lightning Payments to Your AI Agent"
date: 2026-03-09
tags: bitcoin,lightning,ai,authentication,mcp
---

# How to Add Lightning Payments to Your AI Agent

AI agents are popping up everywhere. But how do you monetize them? LiveAuth lets you add Proof-of-Work or Lightning payment verification to any agent in minutes.

## The Problem

Traditional auth (CAPTCHAs, JWTs) doesn't work for AI agents. You need:
- Something agents can solve (PoW)
- Something agents can pay (Lightning)

## The Solution

LiveAuth provides two verification methods:

### 1. Proof-of-Work (Free)
Your agent solves a cryptographic puzzle to prove it's a real device.

### 2. Lightning Payments (1 sat)
Agents pay a tiny amount (~$0.0004) to authenticate.

## Quick Start

```bash
# Install the MCP server
npx @liveauth-labs/mcp-server

# Configure with your API key
export LIVEAUTH_API_KEY=la_sk_xxx
```

## The Code

```javascript
import { AgentAuth } from '@liveauth-labs/sdk';

const agent = new AgentAuth({
  agentId: 'my-agent',
  publicKey: 'la_pk_xxx',
  apiKey: 'la_sk_xxx'
});

// Full auth flow
const token = await agent.authenticate(solvePoW);
```

## Why This Matters

- **No CAPTCHA** - PoW proves humanity without annoying users
- **Pay for access** - Earn sats for API usage
- **Agent economy** - Every AI agent needs auth

## Get Started

1. Visit liveauth.app
2. Create a project
3. Get your API keys
4. Integrate in minutes

---

*LiveAuth combines Proof-of-Work verification with Lightning payments to create the perfect auth layer for the agent economy.*
