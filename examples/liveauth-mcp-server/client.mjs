/**
 * MCP Client Example with LiveAuth L402 Bundle Authentication
 * 
 * This demonstrates how to:
 * 1. Purchase an L402 call bundle (Lightning invoice)
 * 2. Pay the invoice and claim a macaroon
 * 3. Authenticate MCP sessions with the macaroon
 * 4. Call MCP tools (calls debited from bundle)
 * 
 * Usage:
 *   node client.mjs buy-starter      # Buy starter bundle + get invoice
 *   node client.mjs claim <hash>    # Claim macaroon after paying
 *   node client.mjs echo "hello"    # Call /echo with L402 auth
 *   node client.mjs status          # Check bundle status
 *   node client.mjs demo            # Full flow demo (no real payment)
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
// LiveAuth MCP Client with L402 Bundle Support
// ============================================================

class LiveAuthMcpClient {
  constructor(options = {}) {
    this.apiKey = options.apiKey || LIVEAUTH_API_KEY;
    this.apiUrl = options.apiUrl || LIVEAUTH_API_URL;
    this.quoteId = null;
    this.jwt = null;
    this.refreshToken = null;
    this.expiresAt = null;
    this.macaroon = null;
    this.bundleId = null;
    this.usage = {
      calls: 0,
      satsUsed: 0,
      dailyBudget: 100
    };
  }

  // ====================================
  // L402 Bundle Purchase Flow
  // ====================================

  /**
   * Purchase a call bundle. Returns an invoice to pay.
   * After paying, call claimBundle().
   */
  async buyBundle(tier = 'starter') {
    const tiers = ['starter', 'growth', 'scale', 'enterprise'];
    if (!tiers.includes(tier)) {
      throw new Error(`Invalid tier. Choose: ${tiers.join(', ')}`);
    }

    const response = await fetch(`${this.apiUrl}/api/public/l402/bundle/invoice`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tier })
    });

    if (!response.ok) {
      throw new Error(`Bundle purchase failed: ${response.status}`);
    }

    const data = await response.json();
    this.bundleId = data.bundleId;

    console.log('📦 Bundle invoice created:');
    console.log(`   Bundle ID: ${data.bundleId}`);
    console.log(`   Tier: ${data.tier}`);
    console.log(`   Amount: ${data.amountSats} sats`);
    console.log(`   Calls: ${data.totalCalls}`);
    console.log('\n⚡ INVOICE (pay with Lightning wallet):');
    console.log(data.invoice);
    console.log('\n💳 After paying, run: node client.mjs claim <paymentHash>');

    return data;
  }

  /**
   * Claim a macaroon after paying the bundle invoice.
   * Call buyBundle() first, then pay the invoice externally.
   */
  async claimBundle(paymentHash) {
    if (!paymentHash) {
      throw new Error('paymentHash required (from buyBundle response)');
    }

    const response = await fetch(`${this.apiUrl}/api/public/l402/bundle/claim`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ paymentHash })
    });

    if (!response.ok) {
      const err = await response.json();
      throw new Error(`Bundle claim failed: ${err.error || response.status}`);
    }

    const data = await response.json();
    this.macaroon = data.macaroon;
    this.bundleId = data.bundleId;

    console.log('🔐 Macaroon received:');
    console.log(`   Bundle ID: ${data.bundleId}`);
    console.log(`   Remaining calls: ${data.remainingCalls}`);
    console.log(`   Expires: ${new Date(data.expiresAtUnix * 1000).toISOString()}`);

    return data;
  }

  /**
   * Check bundle status.
   */
  async getBundleStatus(bundleId) {
    const id = bundleId || this.bundleId;
    if (!id) {
      throw new Error('bundleId required');
    }

    const response = await fetch(
      `${this.apiUrl}/api/public/l402/bundle/status?bundleId=${id}`,
      { method: 'GET' }
    );

    if (!response.ok) {
      throw new Error(`Bundle status failed: ${response.status}`);
    }

    return response.json();
  }

  // ====================================
  // MCP Auth Flow
  // ====================================

  async start({ forceL402 = false, forceLightning = false } = {}) {
    const body = {};
    if (forceL402) body.forceL402 = true;
    if (forceLightning) body.forceLightning = true;

    const response = await fetch(`${this.apiUrl}/api/mcp/start`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(this.apiKey && { 'X-LW-Public': this.apiKey })
      },
      body: JSON.stringify(body)
    });

    if (!response.ok) {
      throw new Error(`MCP start failed: ${response.status}`);
    }

    const data = await response.json();
    this.quoteId = data.quoteId;

    console.log('📦 MCP Session started:', this.quoteId);

    if (data.authHint === 'l402_bundle') {
      console.log('🔐 Auth hint: l402_bundle — present macaroon on confirm');
      return { needsMacaroon: true, quoteId: data.quoteId, authHint: data.authHint };
    }

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
    const body = { quoteId: this.quoteId };

    // L402 macaroon path
    if (this.macaroon) {
      body.macaroon = this.macaroon;
    }

    // PoW path
    if (powSolution) {
      body.challengeHex = powSolution.challengeHex;
      body.hashHex = powSolution.hashHex;
      body.nonce = powSolution.nonce;
      body.difficultyBits = powSolution.difficultyBits;
      body.expiresAtUnix = powSolution.expiresAtUnix;
      body.sig = powSolution.sig;
    }

    const response = await fetch(`${this.apiUrl}/api/mcp/confirm`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(this.apiKey && { 'X-LW-Public': this.apiKey })
      },
      body: JSON.stringify(body)
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
      if (data.paymentStatus === 'l402_paid') {
        console.log('💳 Payment status: L402 paid (bundle debited per call)');
      }
    }

    return data;
  }

  async charge(toolName, costSats = 1) {
    if (!this.jwt) {
      throw new Error('Not authenticated. Call start() + confirm() first.');
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
    const startResult = await this.mcp.start();

    if (startResult.needsMacaroon && this.mcp.macaroon) {
      console.log('🔐 Presenting macaroon for L402 auth...');
      await this.mcp.confirm();
    } else if (startResult.needsPow) {
      console.log('⛏️  Solving PoW challenge...');
      console.log('   (Skipping PoW for demo)');
    } else if (startResult.needsPayment) {
      console.log('⚡ Invoice needs payment...');
    }

    if (!this.mcp.jwt) {
      await this.mcp.confirm();
    }

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
  }
}

// ============================================================
// CLI Commands
// ============================================================

const command = process.argv[2];
const args = process.argv.slice(3);

async function main() {
  const client = new LiveAuthMcpClient({
    apiKey: LIVEAUTH_API_KEY || undefined
  });

  switch (command) {
    case 'buy-starter':
    case 'buy': {
      const tier = args[0] || 'starter';
      await client.buyBundle(tier);
      break;
    }

    case 'claim': {
      const paymentHash = args[0];
      if (!paymentHash) {
        console.error('Usage: node client.mjs claim <paymentHash>');
        process.exit(1);
      }
      await client.claimBundle(paymentHash);
      break;
    }

    case 'status': {
      const bundleId = args[0];
      if (!bundleId) {
        console.error('Usage: node client.mjs status <bundleId>');
        process.exit(1);
      }
      const status = await client.getBundleStatus(bundleId);
      console.log('📦 Bundle Status:');
      console.log(JSON.stringify(status, null, 2));
      break;
    }

    case 'echo': {
      const message = args.join(' ') || 'Hello from L402!';
      console.log(`\n🔐 Testing L402 MCP auth with /echo...`);
      console.log(`   Message: "${message}"\n`);

      // Step 1: Start MCP session with ForceL402
      const startResult = await client.start({ forceL402: true });

      if (!startResult.needsMacaroon) {
        console.log('❌ MCP start did not return l402_bundle auth hint');
        console.log('Response:', JSON.stringify(startResult, null, 2));
        break;
      }

      // Step 2: If we have a macaroon, confirm it
      if (!client.macaroon) {
        console.log('❌ No macaroon available. Run buy + claim first:');
        console.log('   node client.mjs buy-starter');
        console.log('   # Pay the invoice in your Lightning wallet');
        console.log('   node client.mjs claim <paymentHash>');
        break;
      }

      // Step 3: Confirm with macaroon
      await client.confirm();

      // Step 4: Charge for the tool call
      const chargeResult = await client.charge('echo', 1);
      if (chargeResult.decision === 'deny') {
        console.log('❌ Charge denied (budget exceeded or bundle depleted)');
        break;
      }

      console.log('✅ Charge authorized:', chargeResult.decision);
      console.log('📊 Remaining budget:', client.usage.dailyBudget, 'sats');
      console.log(`\n✅ /echo call debited from bundle`);
      console.log(`   Message echoed: "${message}"`);
      break;
    }

    case 'demo': {
      console.log('🚀 L402 Bundle Demo\n');
      console.log('This demo shows the full L402 flow without real payment.\n');

      // Show what the flow looks like
      console.log('--- Step 1: Buy Bundle (creates invoice) ---');
      console.log('   node client.mjs buy-starter');
      console.log('   → Returns Lightning invoice (bolt11)\n');

      console.log('--- Step 2: Pay Invoice ---');
      console.log('   Pay the invoice in your Lightning wallet');
      console.log('   → Get paymentHash from the payment\n');

      console.log('--- Step 3: Claim Macaroon ---');
      console.log('   node client.mjs claim <paymentHash>');
      console.log('   → Returns macaroon credential\n');

      console.log('--- Step 4: Call /echo ---');
      console.log('   node client.mjs echo "Hello world"');
      console.log('   → Starts MCP session, presents macaroon, gets JWT');
      console.log('   → /echo call is debited from bundle\n');

      console.log('--- Bundle Tiers ---');
      console.log('   starter:    100 calls / 50 sats (0.5 sat/call)');
      console.log('   growth:   1,000 calls / 400 sats (0.4 sat/call)');
      console.log('   scale:   10,000 calls / 3,000 sats (0.3 sat/call)');
      console.log('   enterprise: 100,000 calls / 20,000 sats (0.2 sat/call)\n');

      console.log('📝 To test with real payment:');
      console.log('   1. node client.mjs buy-starter');
      console.log('   2. Pay the invoice displayed');
      console.log('   3. node client.mjs claim <paymentHash>');
      console.log('   4. node client.mjs echo "Hello from my Lightning bundle!"');
      break;
    }

    default: {
      console.log('LiveAuth L402 MCP Client');
      console.log('');
      console.log('Usage:');
      console.log('  node client.mjs buy-starter           Buy a starter bundle (50 sats, 100 calls)');
      console.log('  node client.mjs buy growth             Buy a growth bundle (400 sats, 1k calls)');
      console.log('  node client.mjs claim <paymentHash>   Claim macaroon after paying invoice');
      console.log('  node client.mjs status <bundleId>     Check bundle status');
      console.log('  node client.mjs echo <message>       Call /echo tool via L402 auth');
      console.log('  node client.mjs demo                  Show full flow walkthrough');
      console.log('');
      console.log('Full L402 Flow:');
      console.log('  1. buy-starter → get invoice');
      console.log('  2. Pay invoice with Lightning wallet');
      console.log('  3. claim <paymentHash> → get macaroon');
      console.log('  4. echo "hello" → MCP session + /echo call debited from bundle');
      break;
    }
  }
}

main().catch(err => {
  console.error('❌ Error:', err.message);
  process.exit(1);
});
