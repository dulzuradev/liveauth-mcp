#!/usr/bin/env node

/**
 * Simple AI Agent Simulation Test
 * Tests the MCP server as an AI agent would
 */

import { spawn } from 'child_process';
import { Readable } from 'stream';

// Start MCP server in demo mode
const server = spawn('node', ['dist/index.js'], {
  env: { ...process.env, LIVEAUTH_DEMO: 'true' },
  stdio: ['pipe', 'pipe', 'pipe']
});

let requestId = 1;
const pendingRequests = new Map();

server.stdout.on('data', (data) => {
  const lines = data.toString().split('\n').filter(l => l.trim());
  lines.forEach(line => {
    try {
      const msg = JSON.parse(line);
      if (msg.id && pendingRequests.has(msg.id)) {
        const { resolve } = pendingRequests.get(msg.id);
        pendingRequests.delete(msg.id);
        resolve(msg);
      }
    } catch (e) {
      // Not JSON
    }
  });
});

server.stderr.on('data', (data) => {
  console.log('Server:', data.toString().trim());
});

function sendRequest(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = requestId++;
    pendingRequests.set(id, { resolve, reject });
    
    const request = JSON.stringify({
      jsonrpc: '2.0',
      id,
      method,
      params
    });
    
    server.stdin.write(request + '\n');
    
    // Timeout after 10 seconds
    setTimeout(() => {
      if (pendingRequests.has(id)) {
        pendingRequests.delete(id);
        reject(new Error('Request timeout'));
      }
    }, 10000);
  });
}

async function initialize() {
  console.log('=== AI Agent Test: Initialize ===');
  const result = await sendRequest('initialize', {
    protocolVersion: '2024-11-05',
    capabilities: {},
    clientInfo: { name: 'test-agent', version: '1.0.0' }
  });
  console.log('✅ Initialized');
  return result;
}

async function listTools() {
  console.log('\n=== AI Agent Test: List Tools ===');
  const result = await sendRequest('tools/list');
  const tools = result.result?.tools || [];
  console.log(`✅ Found ${tools.length} tools:`);
  tools.forEach(t => console.log(`   - ${t.name}`));
  return tools;
}

async function startSession() {
  console.log('\n=== AI Agent Test: Start Session (Demo Mode) ===');
  const result = await sendRequest('tools/call', {
    name: 'liveauth_mcp_start',
    arguments: {}
  });
  
  const content = JSON.parse(result.result.content[0].text);
  console.log('✅ Session started:', content.quoteId);
  console.log('   Amount:', content.invoice?.amountSats, 'sats');
  console.log('   Mode:', content._demo ? 'DEMO' : 'LIVE');
  return content;
}

async function checkStatus(quoteId) {
  console.log('\n=== AI Agent Test: Check Status ===');
  const result = await sendRequest('tools/call', {
    name: 'liveauth_mcp_status',
    arguments: { quoteId }
  });
  
  const content = JSON.parse(result.result.content[0].text);
  console.log('✅ Status:', content.status);
  console.log('   Payment:', content.paymentStatus);
  return content;
}

async function confirmSession(quoteId) {
  console.log('\n=== AI Agent Test: Confirm Session (Demo Mode) ===');
  const result = await sendRequest('tools/call', {
    name: 'liveauth_mcp_confirm',
    arguments: { quoteId }
  });
  
  const content = JSON.parse(result.result.content[0].text);
  console.log('✅ Session confirmed!');
  console.log('   JWT:', content.jwt?.substring(0, 50) + '...');
  console.log('   Expires in:', content.expiresIn, 'seconds');
  return content;
}

async function checkUsage() {
  console.log('\n=== AI Agent Test: Check Usage ===');
  const result = await sendRequest('tools/call', {
    name: 'liveauth_mcp_usage',
    arguments: {}
  });
  
  const content = JSON.parse(result.result.content[0].text);
  console.log('✅ Usage:', content);
  return content;
}

async function runTest() {
  try {
    await initialize();
    await listTools();
    
    const session = await startSession();
    await checkStatus(session.quoteId);
    await confirmSession(session.quoteId);
    await checkUsage();
    
    console.log('\n=== ✅ All Tests Passed ===');
    console.log('The MCP server works correctly for AI agents!');
    
  } catch (error) {
    console.error('\n❌ Test failed:', error.message);
    process.exit(1);
  } finally {
    server.kill();
  }
}

runTest();
