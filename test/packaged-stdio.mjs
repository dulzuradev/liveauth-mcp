import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { after, before, test } from 'node:test';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

const root = mkdtempSync(join(tmpdir(), 'liveauth stdio #'));
const packageDir = join(root, 'node_modules/@liveauth-labs/mcp-server');
before(() => {
  const packed = JSON.parse(execFileSync('npm', ['pack', '--json', '--pack-destination', root], { encoding: 'utf8' }));
  execFileSync('npm', ['install', '--prefix', root, join(root, packed[0].filename), '--no-audit', '--no-fund'], { stdio: 'pipe' });
});
after(() => rmSync(root, { recursive: true, force: true }));

for (const entry of ['dist/cli.js', 'npm-bin']) {
  test(`packed ${entry} initializes and lists tools without credentials`, { timeout: 15000 }, async () => {
    const errors = [];
    const sent = [];
    const received = [];
    let stderr = '';
    const transport = new StdioClientTransport({
      command: process.execPath,
      args: [entry === 'npm-bin' ? join(root, 'node_modules/.bin/liveauth-mcp') : join(packageDir, entry)],
      env: { PATH: process.env.PATH, LIVEAUTH_API_BASE: 'http://127.0.0.1:1', LIVEAUTH_API_KEY: '', LIVEAUTH_DEMO: 'false' },
      stderr: 'pipe',
    });
    transport.stderr.on('data', chunk => { stderr += chunk; });
    const send = transport.send.bind(transport);
    transport.send = async message => { sent.push(message.method); await send(message); };
    const client = new Client({ name: 'packaged-stdio-test', version: '1.0.0' });
    client.onerror = error => errors.push(error);
    try {
      await client.connect(transport, { timeout: 5000 });
      const onmessage = transport.onmessage;
      transport.onmessage = message => { received.push(message); onmessage(message); };
      const result = await client.listTools({}, { timeout: 5000 });
      assert.equal(client.getServerVersion().name, 'liveauth-mcp');
      assert.equal(result.tools.length, 8);
      assert.deepEqual(sent, ['initialize', 'notifications/initialized', 'tools/list']);
      assert.equal(received.length, 1);
      assert.deepEqual(errors, [], 'stdout must contain only valid MCP messages');
      // Also ensure startup still enforces authentication for paid operations.
      const charge = await client.callTool({ name: 'liveauth_mcp_charge', arguments: {} });
      assert.equal(charge.isError, true);
      assert.match(charge.content[0].text, /confirmed LiveAuth session JWT is required/);
      assert.match(stderr, /running on stdio/);
    } finally { await client.close(); }
  });
}

test('importing the CLI does not start the server', () => {
  const stdout = execFileSync(process.execPath, ['--input-type=module', '-e', `await import(${JSON.stringify(pathToFileURL(resolve(packageDir, 'dist/cli.js')).href)})`], { encoding: 'utf8', timeout: 5000 });
  assert.equal(stdout, '');
});
