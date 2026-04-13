# LiveAuth LangChain Agent Example

A working example of a LangChain agent authenticated via LiveAuth's MCP (Machine Context Protocol) gateway.

## What This Does

1. Authenticates with LiveAuth using PoW or Lightning payment
2. Gets a short-lived JWT token for MCP tool access
3. Calls MCP-gated tools with automatic usage reporting
4. Reports per-call costs in sats

## Quick Start

### 1. Install Dependencies

```bash
cd examples/langchain-agent
npm install
```

### 2. Set Environment Variables

```bash
# Your LiveAuth API key (from https://liveauth.app/dashboard)
export LIVEAUTH_API_KEY="la_pk_your_key_here"

# Your OpenAI key (for LangChain LLM)
export OPENAI_API_KEY="sk-your_openai_key"

# Optional: API URL (default: https://api.liveauth.app)
export LIVEAUTH_API_URL="https://api.liveauth.app"
```

### 3. Run

```bash
npm start
```

### Demo Mode (No API Key)

For testing without real authentication:

```bash
npm run start:demo
```

## How It Works

```
┌─────────────────────────────────────────────────────────────┐
│                     LiveAuth MCP Flow                        │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  Agent                      LiveAuth                    Your  │
│  Code                      API                       MCP     │
│                             ⬆️                           Server│
│  1. POST /mcp/start  ────▶  Create session                   │
│                             │                                │
│  2. PoW challenge ◀─────────┼── or ──▶ ⚡ Invoice            │
│                             │                                │
│  3. Complete PoW/Pay invoice │                                │
│                             │                                │
│  4. POST /mcp/confirm ◀─────┼───────────────────────────────│
│                             │                                │
│  5. JWT token ──────────────┼───────────────────────────────▶│
│                             │                                │
│  6. Tool call (JWT)  ──────┼───────────────────────────────▶│
│                             │                                │
│  7. POST /mcp/charge  ──────┼───────────────────────────────▶│
│                             │                                │
│  8. Usage response   ◀───────┼───────────────────────────────│
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

## Key Classes

### `LiveAuthMcpClient`

Handles all LiveAuth MCP authentication:

```javascript
const mcp = new LiveAuthMcpClient({
  apiKey: 'la_pk_xxx',        // Your API key
  apiUrl: 'https://api.liveauth.app',
  useLightning: false          // true for Lightning, false for PoW
});

// Start session
const startResult = await mcp.start();

// Wait for confirmation (PoW completion or invoice payment)
await mcp.pollConfirm();

// JWT is now available
console.log(mcp.jwt);

// Report usage
await mcp.charge(1); // Charge 1 sat

// Get current usage
const usage = await mcp.getUsage();
```

### `LiveAuthGatedTool`

Wraps MCP tools with automatic usage reporting:

```javascript
const tool = new LiveAuthGatedTool(mcp, {
  name: 'web_search',
  description: 'Search the web',
  endpoint: 'https://api.liveauth.app/api/mcp/tools/web_search',
  callCostSats: 2
});

// Call the tool (automatically reports usage)
const result = await tool.invoke({ query: 'bitcoin price' });
```

## Integrating with LangChain

Here's how to integrate with LangChain's tool calling:

```javascript
import { ChatOpenAI } from '@langchain/openai';
import { agent } from 'langchain/agents';

// Initialize LLM
const llm = new ChatOpenAI({
  modelName: 'gpt-4',
  temperature: 0
});

// Wrap LiveAuth tools for LangChain
const langChainTools = tools.map(t => 
  new DynamicTool({
    name: t.name,
    description: t.description,
    func: async (input) => {
      const result = await t.invoke({ input });
      return JSON.stringify(result);
    }
  })
);

// Create agent
const agent = await agent({
  llm,
  tools: langChainTools
});

// Run
const result = await agent.invoke({
  input: 'Search for the latest Bitcoin news'
});
```

## MCP Endpoints Reference

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/mcp/start` | POST | Start MCP session |
| `/api/mcp/confirm` | POST | Confirm PoW/payment |
| `/api/mcp/refresh` | POST | Refresh JWT token |
| `/api/mcp/charge` | POST | Report call cost |
| `/api/mcp/usage` | GET | Get usage stats |
| `/api/mcp/status/{quoteId}` | GET | Check session status |
| `/api/mcp/lnurl/{quoteId}` | GET | Get LNURL for invoice |

## Costs

- **PoW mode**: CPU cost only (difficulty auto-adjusted)
- **Lightning mode**: Configurable sats per call (default from project settings)
- **Demo mode**: Free, no real authentication

## Next Steps

1. Replace placeholder tool endpoints with your actual MCP server
2. Add more sophisticated tool routing
3. Implement caching to reduce costs
4. Add retry logic for failed tool calls
5. Monitor usage and set budget alerts

## Troubleshooting

**"MCP start failed: 401"**
- Check your API key is correct
- Ensure the project is active

**"MCP confirm failed: 400"**
- PoW not completed correctly
- Invoice not yet paid (Lightning mode)

**"Budget exceeded"**
- Increase daily budget in project settings
- Wait for daily reset at midnight UTC

## See Also

- [LiveAuth MCP SDK](https://www.npmjs.com/package/@liveauth-labs/mcp-server)
- [LiveAuth Documentation](https://docs.liveauth.app)
- [MCP Protocol Spec](https://modelcontextprotocol.io)
