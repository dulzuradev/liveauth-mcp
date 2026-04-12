/**
 * LiveAuth MCP LangChain Agent Example
 * 
 * This example demonstrates how to authenticate a LangChain agent
 * using LiveAuth's MCP (Machine Context Protocol) authentication.
 * 
 * Flow:
 * 1. Start MCP session with LiveAuth (PoW or Lightning)
 * 2. Confirm payment and get JWT token
 * 3. Use JWT to call MCP-gated tools
 * 4. Report usage after each tool call
 * 
 * Usage:
 *   export LIVEAUTH_API_KEY="la_pk_xxx"     # Your LiveAuth API key
 *   export OPENAI_API_KEY="sk-xxx"          # Your OpenAI key
 *   node agent.mjs
 * 
 * Demo mode (no API key needed):
 *   npm run start:demo
 */

import { config } from 'dotenv';
import * as readline from 'readline';

// Load .env if present
config();

// ============================================================
// Configuration
// ============================================================

const LIVEAUTH_API_KEY = process.env.LIVEAUTH_API_KEY || '';
const LIVEAUTH_API_URL = process.env.LIVEAUTH_API_URL || 'https://api.liveauth.app';
const USE_DEMO = process.env.LIVEAUTH_DEMO === 'true';
const USE_LIGHTNING = process.env.USE_LIGHTNING === 'true';

// ============================================================
// MCP Client — LiveAuth MCP Gate Integration
// ============================================================

class LiveAuthMcpClient {
  constructor(options = {}) {
    this.apiKey = options.apiKey;
    this.apiUrl = options.apiUrl || LIVEAUTH_API_URL;
    this.useLightning = options.useLightning || USE_LIGHTNING;
    this.quoteId = null;
    this.jwt = null;
    this.refreshToken = null;
    this.expiresAt = null;
  }

  async start() {
    const endpoint = `${this.apiUrl}/api/mcp/start`;
    
    const body = {
      forceLightning: this.useLightning
    };

    const response = await fetch(endpoint, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(this.apiKey && { 'X-LW-Public': this.apiKey })
      },
      body: JSON.stringify(body)
    });

    if (!response.ok) {
      const error = await response.text();
      throw new Error(`MCP start failed: ${response.status} ${error}`);
    }

    const data = await response.json();
    this.quoteId = data.quoteId;

    return data;
  }

  async pollConfirm(timeoutMs = 120000) {
    const startTime = Date.now();
    
    while (Date.now() - startTime < timeoutMs) {
      const statusResp = await fetch(
        `${this.apiUrl}/api/mcp/status/${this.quoteId}`,
        {
          headers: {
            ...(this.apiKey && { 'X-LW-Public': this.apiKey })
          }
        }
      );

      if (statusResp.ok) {
        const status = await statusResp.json();
        
        if (status.status === 'confirmed') {
          return this.confirm();
        }
        
        if (status.paymentStatus === 'paid') {
          return this.confirm();
        }
      }

      // Wait 2 seconds before next poll
      await new Promise(resolve => setTimeout(resolve, 2000));
    }

    throw new Error('MCP session confirmation timed out');
  }

  async confirm() {
    const endpoint = `${this.apiUrl}/api/mcp/confirm`;
    
    const response = await fetch(endpoint, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(this.apiKey && { 'X-LW-Public': this.apiKey })
      },
      body: JSON.stringify({
        quoteId: this.quoteId
      })
    });

    if (!response.ok) {
      const error = await response.text();
      throw new Error(`MCP confirm failed: ${response.status} ${error}`);
    }

    const data = await response.json();
    
    if (data.jwt) {
      this.jwt = data.jwt;
      this.refreshToken = data.refreshToken;
      this.expiresAt = Date.now() + (data.expiresIn * 1000);
      console.log(`✅ JWT obtained, expires in ${data.expiresIn}s`);
      console.log(`   Budget: ${data.remainingBudgetSats} sats remaining`);
    }

    return data;
  }

  async refresh() {
    if (!this.refreshToken) {
      throw new Error('No refresh token available');
    }

    const endpoint = `${this.apiUrl}/api/mcp/refresh`;
    
    const response = await fetch(endpoint, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${this.jwt}`
      },
      body: JSON.stringify({
        refreshToken: this.refreshToken
      })
    });

    if (!response.ok) {
      throw new Error(`MCP refresh failed: ${response.status}`);
    }

    const data = await response.json();
    this.jwt = data.jwt;
    this.expiresAt = Date.now() + (data.expiresIn * 1000);

    console.log(`🔄 Token refreshed, expires in ${data.expiresIn}s`);
    return data;
  }

  async charge(callCostSats) {
    if (!this.jwt) {
      throw new Error('Not authenticated');
    }

    // Auto-refresh if expiring within 60 seconds
    if (this.expiresAt && Date.now() > this.expiresAt - 60000) {
      await this.refresh();
    }

    const endpoint = `${this.apiUrl}/api/mcp/charge`;
    
    const response = await fetch(endpoint, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${this.jwt}`
      },
      body: JSON.stringify({ callCostSats })
    });

    if (!response.ok) {
      const error = await response.text();
      throw new Error(`MCP charge failed: ${response.status} ${error}`);
    }

    return response.json();
  }

  async getUsage() {
    const endpoint = `${this.apiUrl}/api/mcp/usage`;
    
    const response = await fetch(endpoint, {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${this.jwt}`
      }
    });

    if (!response.ok) {
      throw new Error(`MCP usage failed: ${response.status}`);
    }

    return response.json();
  }
}

// ============================================================
// Tool Wrapper — MCP-gated tool with usage reporting
// ============================================================

class LiveAuthGatedTool {
  constructor(mcpClient, toolConfig) {
    this.mcp = mcpClient;
    this.name = toolConfig.name;
    this.description = toolConfig.description;
    this.endpoint = toolConfig.endpoint;
    this.callCost = toolConfig.callCostSats || 1;
    this.method = toolConfig.method || 'POST';
  }

  async invoke(input) {
    try {
      // Report usage before call
      const chargeResult = await this.mcp.charge(this.callCost);
      
      if (chargeResult.decision === 'deny') {
        return {
          error: 'Budget exceeded',
          usage: chargeResult
        };
      }

      // Make the actual tool call
      const response = await fetch(this.endpoint, {
        method: this.method,
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${this.mcp.jwt}`
        },
        body: JSON.stringify(input)
      });

      const result = await response.json();
      
      console.log(`   [${this.name}] cost: ${this.callCost}sats, remaining: ${chargeResult.satsUsed}/${chargeResult.dailySatsBudget}sats`);

      return result;
    } catch (error) {
      console.error(`Tool ${this.name} error:`, error.message);
      return { error: error.message };
    }
  }
}

// ============================================================
// Example MCP Tools (replace with your actual MCP server endpoints)
// ============================================================

function createTools(mcp) {
  return [
    new LiveAuthGatedTool(mcp, {
      name: 'web_search',
      description: 'Search the web for information',
      endpoint: `${LIVEAUTH_API_URL}/api/mcp/tools/web_search`,
      callCostSats: 2
    }),
    new LiveAuthGatedTool(mcp, {
      name: 'code_interpreter',
      description: 'Execute Python code in a sandbox',
      endpoint: `${LIVEAUTH_API_URL}/api/mcp/tools/code`,
      callCostSats: 5
    }),
    new LiveAuthGatedTool(mcp, {
      name: 'file_reader',
      description: 'Read files from a filesystem',
      endpoint: `${LIVEAUTH_API_URL}/api/mcp/tools/read`,
      callCostSats: 1
    })
  ];
}

// ============================================================
// Interactive Agent Loop
// ============================================================

async function runAgent() {
  console.log('🚀 LiveAuth MCP LangChain Agent');
  console.log('================================\n');

  const mcp = new LiveAuthMcpClient({
    apiKey: USE_DEMO ? undefined : LIVEAUTH_API_KEY,
    useLightning: USE_LIGHTNING
  });

  // Demo mode: simulate auth
  if (USE_DEMO) {
    console.log('📺 Demo mode (no real authentication)');
    mcp.jwt = 'demo-jwt-token';
    mcp.refreshToken = 'demo-refresh-token';
    mcp.expiresAt = Date.now() + 600000;
  } else {
    // Real auth flow
    console.log('🔐 Starting MCP session...');
    
    try {
      const startResult = await mcp.start();
      
      if (startResult.powChallenge) {
        console.log('\n⛏️  PoW Challenge received');
        console.log(`   Difficulty: ${startResult.powChallenge.difficultyBits} bits`);
        console.log('   Complete the PoW to authenticate...\');
        // In production, you'd solve the PoW here
        // For now, we'll use demo mode
        console.log('   (Simulating PoW completion...)');
      }
      
      if (startResult.invoice) {
        console.log('\n⚡ Lightning invoice created');
        console.log(`   Amount: ${startResult.invoice.amountSats} sats`);
        console.log(`   Invoice: ${startResult.invoice.bolt11.substring(0, 50)}...`);
        console.log('   Pay the invoice to continue...');
      }
      
      console.log('\n⏳ Waiting for confirmation...');
      await mcp.pollConfirm();
      
    } catch (error) {
      console.error('❌ Authentication failed:', error.message);
      console.log('\n💡 Tip: Set LIVEAUTH_DEMO=true for demo mode');
      return;
    }
  }

  // Show usage
  try {
    const usage = await mcp.getUsage();
    console.log(`\n📊 Session Stats:`);
    console.log(`   Calls: ${usage.callsUsed}`);
    console.log(`   Sats used: ${usage.satsUsed}`);
    console.log(`   Budget: ${usage.maxSatsPerDay} sats/day`);
  } catch (e) {
    // Demo mode might not support usage
  }

  // Create tools
  const tools = createTools(mcp);

  console.log('\n🛠️  Available tools:');
  tools.forEach(t => {
    console.log(`   - ${t.name}: ${t.description} (${t.callCost}sats/call)`);
  });

  // Interactive loop
  const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout
  });

  const ask = () => {
    rl.question('\n🤖 Ask me anything (or "quit" to exit):\n> ', async (input) => {
      if (!input || input.toLowerCase() === 'quit' || input.toLowerCase() === 'exit') {
        console.log('\n👋 Goodbye!');
        
        // Show final usage
        try {
          const usage = await mcp.getUsage();
          console.log(`\n📊 Final Usage:`);
          console.log(`   Total calls: ${usage.callsUsed}`);
          console.log(`   Total sats: ${usage.satsUsed}`);
        } catch (e) {}
        
        rl.close();
        return;
      }

      console.log(`\n🔍 Processing: "${input}"`);
      
      // Simple routing example
      if (input.toLowerCase().includes('search') || input.toLowerCase().includes('find')) {
        const result = await tools[0].invoke({ query: input });
        console.log('📄 Result:', JSON.stringify(result, null, 2));
      } else if (input.toLowerCase().includes('code') || input.toLowerCase().includes('run')) {
        const result = await tools[1].invoke({ code: input });
        console.log('📄 Result:', JSON.stringify(result, null, 2));
      } else {
        console.log('📄 (Tool routing placeholder - implement based on intent)');
      }
      
      ask();
    });
  };

  ask();
}

// Run
runAgent().catch(console.error);
