#!/usr/bin/env node

import { spawn } from 'child_process';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

// Start the MCP server
const serverPath = join(__dirname, '../dist/index.js');
const server = spawn('node', [serverPath], {
  stdio: ['pipe', 'pipe', 'pipe'],
  env: { ...process.env }
});

let responses = [];

server.stdout.on('data', (data) => {
  const lines = data.toString().split('\n').filter(l => l.trim());
  lines.forEach(line => {
    try {
      const parsed = JSON.parse(line);
      responses.push(parsed);
      console.log('Response:', JSON.stringify(parsed, null, 2));
    } catch (e) {
      // Not JSON
    }
  });
});

server.stderr.on('data', (data) => {
  console.log('Server stderr:', data.toString());
});

// Send initialize request
const initRequest = {
  jsonrpc: '2.0',
  id: 1,
  method: 'initialize',
  params: {
    protocolVersion: '2024-11-05',
    capabilities: {},
    clientInfo: {
      name: 'test-client',
      version: '1.0.0',
    },
  },
};

console.log('\n=== Test 1: Initialize ===');
server.stdin.write(JSON.stringify(initRequest) + '\n');

setTimeout(() => {
  // Test liveauth_mcp_start (the correct tool name)
  const startRequest = {
    jsonrpc: '2.0',
    id: 2,
    method: 'tools/call',
    params: {
      name: 'liveauth_mcp_start',
      arguments: {},
    },
  };

  console.log('\n=== Test 2: Start MCP Session (PoW) ===');
  server.stdin.write(JSON.stringify(startRequest) + '\n');

  setTimeout(() => {
    const startResponse = responses.find(r => r.id === 2);
    if (startResponse && startResponse.result && !startResponse.result.isError) {
      try {
        const content = startResponse.result.content[0].text;
        const result = JSON.parse(content);
        
        if (result.powChallenge) {
          console.log('\n✅ PoW Challenge received!');
          console.log(`   Difficulty: ${result.powChallenge.difficultyBits} bits`);
          console.log(`   Expires: ${new Date(result.powChallenge.expiresAtUnix * 1000)}`);
        } else if (result.invoice) {
          console.log('\n✅ Lightning invoice received!');
          console.log(`   Amount: ${result.invoice.amountSats} sats`);
          console.log(`   Invoice: ${result.invoice.bolt11.substring(0, 50)}...`);
        }
        
        // Now test status
        const statusRequest = {
          jsonrpc: '2.0',
          id: 3,
          method: 'tools/call',
          params: {
            name: 'liveauth_mcp_status',
            arguments: { quoteId: result.quoteId },
          },
        };
        
        console.log('\n=== Test 3: Check Status ===');
        server.stdin.write(JSON.stringify(statusRequest) + '\n');
        
      } catch (e) {
        console.log('❌ Failed to parse response:', e.message);
      }
    } else {
      console.log('❌ Start request failed');
      console.log(startResponse);
    }

    setTimeout(() => {
      const statusResponse = responses.find(r => r.id === 3);
      if (statusResponse && statusResponse.result && !statusResponse.result.isError) {
        console.log('\n✅ Status check worked!');
      }
      
      console.log('\n=== Test Complete ===');
      server.kill();
      process.exit(0);
    }, 2000);
    
  }, 2000);
}, 1000);

setTimeout(() => {
  console.log('\n⏱️  Test timeout - killing server');
  server.kill();
  process.exit(1);
}, 15000);
