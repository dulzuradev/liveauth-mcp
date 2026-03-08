# LiveAuth Agent SDK

AI agent authentication for LiveAuth using Proof-of-Work verification.

## Installation

```bash
npm install @liveauth-labs/sdk
```

## Quick Start

```typescript
import { AgentAuth } from '@liveauth-labs/sdk';

// Create agent auth instance
const agent = new AgentAuth({
    agentId: 'my-agent-001',
    publicKey: 'la_pk_xxx',      // From dashboard
    apiKey: 'la_sk_xxx',         // From dashboard (secret)
    baseUrl: 'https://api.liveauth.app'  // Optional
});

// Full auth flow - automatically solves PoW and gets token
const token = await agent.authenticate(async (challenge, difficultyBits) => {
    return solvePoW(challenge, difficultyBits);
});
console.log('Authenticated! Token:', token);
```

## Manual Flow

If you need more control:

```typescript
// Step 1: Start auth - get PoW challenge
const { sessionId, challenge, difficultyBits, expiresAtUnix } = await agent.start();

// Step 2: Solve PoW (compute nonce that makes hash start with zeros)
const solution = await solvePoW(challenge, difficultyBits);

// Step 3: Verify solution - get auth token
const result = await agent.verify(sessionId, solution);

if (result.verified && result.token) {
    console.log('Auth token:', result.token);
}

// Step 4: Validate existing token (check if still valid)
const validation = await agent.validate(result.token!);
console.log('Token valid:', validation.valid);
console.log('Agent ID:', validation.agentId);
console.log('Project:', validation.projectName);
```

## PoW Solver Implementation

The proof-of-work requires finding a nonce that makes the SHA256 hash start with a certain number of zeros:

```typescript
async function solvePoW(challenge: string, difficultyBits: number): Promise<string> {
    const targetZeros = Math.ceil(difficultyBits / 4);
    const target = '0'.repeat(targetZeros);
    let nonce = 0;
    
    while (true) {
        const data = challenge + ':' + nonce;
        const hash = await sha256(data);
        
        if (hash.startsWith(target)) {
            // Return format: challenge:nonce
            return challenge + ':' + nonce;
        }
        nonce++;
        
        // Add timeout protection
        if (nonce > 1000000) {
            throw new Error('PoW solving took too long');
        }
    }
}

async function sha256(message: string): Promise<string> {
    const msgBuffer = new TextEncoder().encode(message);
    const hashBuffer = await crypto.subtle.digest('SHA-256', msgBuffer);
    const hashArray = Array.from(new Uint8Array(hashBuffer));
    return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
}
```

## OpenClaw Integration

Example integration with OpenClaw agents:

```typescript
import { AgentAuth } from '@liveauth-labs/sdk';

// In your OpenClaw agent
const agent = new AgentAuth({
    agentId: process.env.AGENT_ID || 'openclaw-agent',
    publicKey: process.env.LIVEAUTH_PUBLIC_KEY,
    apiKey: process.env.LIVEAUTH_SECRET_KEY
});

// Authenticate on startup
const token = await agent.authenticate(solvePoW);

// Use token for API calls
async function callProtectedAPI(endpoint: string) {
    return fetch(endpoint, {
        headers: {
            'Authorization': `Bearer ${token}`
        }
    });
}
```

## API Reference

### `new AgentAuth(config)`

| Parameter | Required | Description |
|-----------|----------|-------------|
| `agentId` | Yes | Unique identifier for the agent |
| `publicKey` | Yes | Project public key from dashboard |
| `apiKey` | Yes | Project secret key from dashboard |
| `baseUrl` | No | API URL (default: `https://api.liveauth.app`) |

### Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `start()` | `{sessionId, challenge, difficultyBits, expiresAtUnix}` | Get PoW challenge |
| `verify(sessionId, solution)` | `{verified, token?, expiresAtUnix?, error?}` | Verify solution, get token |
| `validate(token)` | `{valid, agentId?, projectId?, projectName?, expiresAtUnix?}` | Validate token |
| `authenticate(powSolver)` | `string` | Full flow: start → solve → verify |

## Pricing

- **1-5 sats** per authentication (configurable in dashboard)
- **Token valid for 24 hours**
- Pay-per-call MCP mode also available

## Related

- [Agent Auth API Docs](https://docs.liveauth.app) - REST API reference
- [MCP Server](https://github.com/dulzuradev/liveauth-mcp) - Drop-in MCP authentication
- [LiveAuth Dashboard](https://liveauth.app) - Get your API keys
