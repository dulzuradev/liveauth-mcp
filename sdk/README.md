# LiveAuth Agent SDK

AI agent authentication for LiveAuth using Proof-of-Work verification.

## Installation

```bash
npm install @liveauth-labs/sdk
```

## Usage

```typescript
import { AgentAuth } from '@liveauth-labs/sdk';

// Create agent auth instance
const agent = new AgentAuth({
    agentId: 'my-agent-001',
    publicKey: 'la_pk_xxx',      // From dashboard
    apiKey: 'la_sk_xxx',         // From dashboard (secret)
    baseUrl: 'https://api.liveauth.app'  // Optional
});

// Full auth flow with your own PoW solver
const token = await agent.authenticate(async (challenge, difficultyBits) => {
    // Your PoW implementation
    return solvePoW(challenge, difficultyBits);
});

// Or manual flow
const { sessionId, challenge, difficultyBits } = await agent.start();
const solution = await myPowSolver(challenge, difficultyBits);
const result = await agent.verify(sessionId, solution);

// Validate existing token
const validation = await agent.validate(token);
console.log(validation.valid);  // true/false
```

## PoW Solver Example

```typescript
async function solvePoW(challenge: string, difficultyBits: number): Promise<string> {
    const targetZeros = Math.ceil(difficultyBits / 4);
    const target = '0'.repeat(targetZeros);
    let nonce = 0;
    
    while (true) {
        const data = challenge + ':' + nonce;
        const hash = await sha256(data);
        
        if (hash.startsWith(target)) {
            return challenge + ':' + nonce;
        }
        nonce++;
    }
}

async function sha256(message: string): Promise<string> {
    const msgBuffer = new TextEncoder().encode(message);
    const hashBuffer = await crypto.subtle.digest('SHA-256', msgBuffer);
    const hashArray = Array.from(new Uint8Array(hashBuffer));
    return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
}
```

## API

### `new AgentAuth(config)`

- `config.agentId` - Unique identifier for the agent
- `config.publicKey` - Project public key (from dashboard)
- `config.apiKey` - Project secret key (from dashboard)
- `config.baseUrl` - Optional API URL (default: `https://api.liveauth.app`)

### Methods

- `start()` - Get PoW challenge
- `verify(sessionId, solution)` - Verify solution, get token
- `validate(token)` - Validate existing token
- `authenticate(powSolver)` - Full flow with custom PoW solver

## Pricing

- 1-5 sats per authentication (configurable in dashboard)
- Token valid for 24 hours
