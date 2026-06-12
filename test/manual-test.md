# Manual Testing Guide

## Test with Claude Desktop

1. **Install the server:**
   ```bash
   npm install -g @liveauth-labs/mcp-server
   ```

2. **Configure Claude Desktop:**
   
   Edit `~/Library/Application Support/Claude/claude_desktop_config.json` (Mac) or 
   `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

   ```json
   {
     "mcpServers": {
       "liveauth": {
         "command": "npx",
         "args": ["-y", "@liveauth-labs/mcp-server"],
         "env": {
           "LIVEAUTH_API_BASE": "https://api.liveauth.app"
         }
       }
     }
   }
   ```

3. **Restart Claude Desktop**

4. **Test in Claude:**
   ```
   Can you list your available tools?
   ```
   You should see `liveauth_mcp_start`, `liveauth_mcp_confirm`, `liveauth_mcp_charge`, `liveauth_mcp_usage`, `liveauth_mcp_status`, `liveauth_mcp_lnurl`, and `liveauth_mcp_refresh`.

   ```
   Use `liveauth_mcp_start` with no arguments for demo mode, or with `forceLightning` / `forceL402` for paid auth modes.
   ```
   Should return a PoW challenge with difficulty, target, and signature.

## Test via stdio (Direct)

```bash
cd liveauth-mcp
node dist/cli.js
```

Then send JSON-RPC messages:

**Initialize:**
```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0.0"}}}
```

**List Tools:**
```json
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
```

**Get Challenge:**
```json
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"liveauth_mcp_start","arguments":{}}}
```

## Expected Results

- Initialize should return server info and capabilities
- List tools should return 3 tools
- Get challenge should return a valid PoW challenge from LiveAuth API

## Test with Live API

The server will make real calls to `https://api.liveauth.app`. 

Test public key: `la_pk_wajRhFpfdc-cnS9Ekj6Otk4m` (demo project)

This should work without authentication since it's calling public endpoints.
