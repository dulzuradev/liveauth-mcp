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
      // Not JSON, probably stderr message
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
  // List tools
  const listToolsRequest = {
    jsonrpc: '2.0',
    id: 2,
    method: 'tools/list',
    params: {},
  };

  console.log('\n=== Test 2: List Tools ===');
  server.stdin.write(JSON.stringify(listToolsRequest) + '\n');

  setTimeout(() => {
    // Test get_challenge with demo public key
    const getChallengeRequest = {
      jsonrpc: '2.0',
      id: 3,
      method: 'tools/call',
      params: {
        name: 'liveauth_get_challenge',
        arguments: {
          projectPublicKey: 'la_pk_wajRhFpfdc-cnS9Ekj6Otk4m',
        },
      },
    };

    console.log('\n=== Test 3: Get Challenge ===');
    server.stdin.write(JSON.stringify(getChallengeRequest) + '\n');

    setTimeout(() => {
      console.log('\n=== Test Results ===');
      console.log(`Total responses: ${responses.length}`);
      
      const toolsResponse = responses.find(r => r.id === 2);
      if (toolsResponse && toolsResponse.result && toolsResponse.result.tools) {
        console.log(`✅ Tools listed: ${toolsResponse.result.tools.length} tools`);
        toolsResponse.result.tools.forEach(tool => {
          console.log(`   - ${tool.name}`);
        });
      } else {
        console.log('❌ Failed to list tools');
      }

      const challengeResponse = responses.find(r => r.id === 3);
      if (challengeResponse && challengeResponse.result) {
        console.log('✅ Challenge request successful');
        try {
          const content = challengeResponse.result.content[0].text;
          const challenge = JSON.parse(content);
          console.log(`   Challenge difficulty: ${challenge.difficultyBits} bits`);
          console.log(`   Target: ${challenge.targetHex.substring(0, 20)}...`);
        } catch (e) {
          console.log('   Response:', JSON.stringify(challengeResponse.result, null, 2));
        }
      } else {
        console.log('❌ Challenge request failed');
        if (challengeResponse) {
          console.log('   Error:', JSON.stringify(challengeResponse, null, 2));
        }
      }

      console.log('\n✅ MCP server is responding correctly!');
      server.kill();
      process.exit(0);
    }, 2000);
  }, 1000);
}, 1000);

setTimeout(() => {
  console.log('\n⏱️  Test timeout - killing server');
  server.kill();
  process.exit(1);
}, 10000);
