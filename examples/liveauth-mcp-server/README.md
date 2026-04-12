# LiveAuth Sample MCP Server

A reference implementation showing how to add **LiveAuth pay-per-call authentication** to an MCP server.

## What This Is

This is a working MCP server you can:
1. **Run locally** to test MCP protocol
2. **Fork and modify** to add LiveAuth auth to your MCP server
3. **Learn from** to understand MCP + LiveAuth integration

## Tools Available

| Tool | Description | Cost |
|------|-------------|------|
| `calculator` | Evaluate math expressions | 1 sat |
| `random_fact` | Get a random interesting fact | 1 sat |
| `weather` | Get weather for a city (mock) | 2 sats |

## Quick Start

### 1. Install

```bash
cd examples/liveauth-mcp-server
npm install
```

### 2. Run (Local Mode - No Auth)

```bash
npm start
```

This runs the server without LiveAuth authentication — useful for testing.

### 3. Connect with MCP Client

Test with the [MCP Inspector](https://modelcontextprotocol.io/docs/tools/debugger):

```bash
npx @modelcontextprotocol/inspector
```

Or use any MCP-compatible client (Claude Desktop, Cursor, etc.).

## Adding LiveAuth Authentication

Here's how to gate your MCP server with LiveAuth:

### 1. Create a LiveAuth MCP Project

```bash
# Register your MCP server with LiveAuth
curl -X POST https://api.liveauth.app/api/mcpproxy \
  -H "Authorization: Bearer YOUR_JWT" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "my-mcp-server",
    "upstreamUrl": "http://localhost:3000",
    "satsPerRequest": 1
  }'
```

### 2. Add JWT Validation to Your Server

```javascript
import { validateJWT } from '@liveauth-labs/mcp-server';

// Add middleware to validate JWT on each tool call
server.setRequestHandler(CallToolRequestSchema, async (request) => {
  // Extract JWT from Authorization header
  const authHeader = request.headers?.authorization;
  const jwt = authHeader?.replace('Bearer ', '');
  
  if (!jwt) {
    throw new Error('Missing authorization token');
  }
  
  // Validate with LiveAuth
  const { projectId, remainingBudget } = await validateJWT(jwt);
  
  if (remainingBudget <= 0) {
    throw new Error('Budget exceeded');
  }
  
  // Process tool call...
});
```

### 3. Report Usage After Each Call

```javascript
// After successful tool execution
await reportUsage({
  projectId,
  jwt,
  toolName,
  costSats: 1
});
```

## Architecture

```
┌─────────────┐      ┌──────────────┐      ┌─────────────────┐
│   AI Agent  │─────▶│  LiveAuth    │─────▶│  Your MCP       │
│  (Claude,   │      │  MCP Gate    │      │  Server         │
│   etc.)     │◀─────│  (auth,      │◀─────│  (tools)        │
│             │      │   billing)   │      │                 │
└─────────────┘      └──────────────┘      └─────────────────┘
                           │
                           ▼
                    ┌──────────────┐
                    │  Lightning   │
                    │  Network     │
                    │  (payment)   │
                    └──────────────┘
```

## Flow

1. **Agent requests tool** → LiveAuth MCP Gate
2. **Gate validates JWT** → checks budget
3. **Gate proxies to MCP server** → passes tool call
4. **Server returns result** → through gate
5. **Gate reports usage** → deducts from budget
6. **Agent gets response** → with updated budget info

## Production Checklist

- [ ] Add JWT validation middleware
- [ ] Implement usage reporting
- [ ] Set up rate limiting
- [ ] Add error handling
- [ ] Configure sats per tool
- [ ] Test with real Lightning payments

## See Also

- [LiveAuth MCP SDK](https://www.npmjs.com/package/@liveauth-labs/mcp-server)
- [MCP Protocol Spec](https://modelcontextprotocol.io)
- [LiveAuth Documentation](https://docs.liveauth.app)
