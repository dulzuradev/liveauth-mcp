/**
 * MCP Client Example with LiveAuth Authentication
 * 
 * This demonstrates how to:
 * 1. Authenticate with LiveAuth MCP Gate
 * 2. Connect to an MCP server
 * 3. Call tools and report usage
 */

import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';
import { config } from 'dotenv';

config();

// ============================================================
// Configuration
// ============================================================

const LIVEAUTH_API_URL = process.env.LIVEAUTH_API_URL || 'https://api.liveauth.app';
const LIVEAUTH_API_KEY = process.env.LIVEAUTH_API_KEY || '';
const MCP_SERVER_URL = process.env.MCP_SERVER_URL || 'http://localhost:3000';

// ============================================================
// LiveAuth MCP Client
// ============================================================

class LiveAuthMcpClient {
  constructor(options = {}) {
    this.apiKey = options.apiKey || LIVEAUTH_API_KEY;
    this.apiUrl = options.apiUrl || LIVEAUTH_API_URL;
    this.quoteId = null;
    this.jwt = null;
    this.refreshToken = null;
    this.expiresAt = null;
    this.usage = {
      calls: 0,
      satsUsed: 0,
      dailyBudget: 100
    };
  }

  async start(powChallenge = null) {
    // Step 1: Start MCP session with LiveAuth
    const response = await fetch(`${this.apiUrl}/api/mcp/start`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(this.apiKey && { 'X-LW-Public': this.apiKey })
      },
      body: JSON.stringify({
        forceLightning: false  // Use PoW by default
      })
    });

    if (!response.ok) {
      throw new Error(`MCP start failed: ${response.status}`);
    }

    const data = await response.json();
    this.quoteId = data.quoteId;

    console.log('📦 MCP Session started:', this.quoteId);

    if (data.powChallenge) {
      console.log('⛏️  PoW Challenge received (difficulty:', data.powChallenge.difficultyBits, 'bits)');
      return { needsPow: true, challenge: data.powChallenge };
    }

    if (data.invoice) {
      console.log('⚡ Invoice created:', data.invoice.amountSats, 'sats');
      return { needsPayment: true, invoice: data.invoice };
    }

    return data;
  }

  async confirm(powSolution = null) {
    // Step 2: Confirm MCP session
    const response = await fetch(`${this.apiUrl}/api/mcp/confirm`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(this.apiKey && { 'X-LW-Public': this.apiKey })
      },
      body: JSON.stringify({
        quoteId: this.quoteId,
        ...(powSolution && {
          challengeHex: powSolution.challengeHex,
          hashHex: powSolution.hashHex,
          nonce: powSolution.nonce,
          difficultyBits: powSolution.difficultyBits,
          expiresAtUnix: powSolution.expiresAtUnix,
          sig: powSolution.sig
        })
      })
    });

    if (!response.ok) {
      throw new Error(`MCP confirm failed: ${response.status}`);
    }

    const data = await response.json();

    if (data.jwt) {
      this.jwt = data.jwt;
      this.refreshToken = data.refreshToken;
      this.expiresAt = Date.now() + (data.expiresIn * 1000);
      this.usage.dailyBudget = data.remainingBudgetSats;

      console.log('✅ JWT obtained, expires in', data.expiresIn, 'seconds');
      console.log('💰 Budget:', data.remainingBudgetSats, 'sats');
    }

    return data;
  }

  async charge(toolName, costSats) {
    // Step 3: Report usage after tool call
    if (this.usage.satsUsed + costSats > this.usage.dailyBudget) {
      console.log('⚠️ Budget exceeded!');
      return { decision: 'deny' };
    }

    const response = await fetch(`${this.apiUrl}/api/mcp/charge`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${this.jwt}`
      },
      body: JSON.stringify({ callCostSats: costSats })
    });

    if (!response.ok) {
      console.error('Charge failed:', response.status);
      return { decision: 'error' };
    }

    const result = await response.json();

    this.usage.calls++;
    this.usage.satsUsed += costSats;

    return result;
  }

  async getUsage() {
    const response = await fetch(`${this.apiUrl}/api/mcp/usage`, {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${this.jwt}`
      }
    });

    if (!response.ok) {
      return this.usage;
    }

    return response.json();
  }
}

// ============================================================
// MCP Client Wrapper
// ============================================================

class McpClientWrapper {
  constructor(mcpClient, serverCommand) {
    this.mcp = mcpClient;
    this.serverCommand = serverCommand;
    this.client = null;
  }

  async connect() {
    // Start MCP session with LiveAuth
    const startResult = await this.mcp.start();

    if (startResult.needsPow) {
      console.log('⛏️  Solving PoW challenge...');
      // In production, solve the PoW here
      // For demo, we'll skip
      console.log('   (Skipping PoW for demo)');
    }

    await this.mcp.confirm();

    // Connect to MCP server with JWT
    const transport = new StdioClientTransport({
      command: this.serverCommand.command,
      args: this.serverCommand.args,
      env: {
        ...process.env,
        LIVEAUTH_JWT: this.mcp.jwt
      }
    });

    this.client = new Client(
      { name: 'liveauth-mcp-client', version: '1.0.0' },
      { capabilities: {} }
    );

    await this.client.connect(transport);
    console.log('🔗 Connected to MCP server');
  }

  async listTools() {
    const response = await this.client.request(
      { method: 'tools/list' },
      { schema: { type: 'object', properties: {} } }
    );
    return response.tools;
  }

  async callTool(name, args) {
    // Report usage before call
    await this.mcp.charge(name, 1);

    // Call the tool
    const response = await this.client.request(
      {
        method: 'tools/call',
        params: { name, arguments: args }
      },
      { schema: { type: 'object' } }
    );

    return response;
  }

  async disconnect() {
    if (this.client) {
      await this.client.close();
    }

    const usage = await this.mcp.getUsage();
    console.log('\n📊 Session Summary:');
    console.log('   Calls:', usage.callsUsed || this.mcp.usage.calls);
    console.log('   Sats used:', usage.satsUsed || this.mcp.usage.satsUsed);
    console.log('   Budget:', usage.maxSatsPerDay || this.mcp.usage.dailyBudget, 'sats/day');
  }
}

// ============================================================
// Demo
// ============================================================

async function demo() {
  console.log('🚀 LiveAuth MCP Client Demo\n');

  const client = new LiveAuthMcpClient({
    apiKey: LIVEAUTH_API_KEY || undefined
  });

  try {
    // Connect to MCP server
    await client.connect();

    // List available tools
    console.log('\n📋 Available tools: calculator, random_fact, weather');

    // Example calls
    console.log('\n--- Calculator ---');
    // (Would call MCP server here in production)

    console.log('\n--- Random Fact ---');
    // (Would call MCP server here in production)

    console.log('\n--- Weather ---');
    // (Would call MCP server here in production)

    // Get final usage
    const usage = await client.getUsage();
    console.log('\n📊 Final Usage:', usage);

  } catch (error) {
    console.error('❌ Error:', error.message);
  }
}

// Run demo
demo();
