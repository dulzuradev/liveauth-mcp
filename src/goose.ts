import { spawnSync } from 'node:child_process';

export const LIVEAUTH_MCP_PACKAGE = '@liveauth-labs/mcp-server';
export const GOOSE_EXTENSION_ID = 'liveauth';
export const GOOSE_EXTENSION_NAME = 'LiveAuth';
export const GOOSE_EXTENSION_DESCRIPTION =
  'Give Goose agents authenticated access to metered and paid capabilities through LiveAuth.';

export interface GooseDeepLinkOptions {
  command?: string;
  args?: string[];
  timeoutSeconds?: number;
  id?: string;
  name?: string;
  description?: string;
}

export interface GooseSetupOptions {
  detectGoose?: () => boolean;
  write?: (message: string) => void;
}

function requireSafeToken(value: string, label: string): string {
  if (!/^[A-Za-z0-9@._/+:-]+$/.test(value)) {
    throw new Error(`${label} contains unsupported characters`);
  }
  return value;
}

export function buildGooseDeepLink(options: GooseDeepLinkOptions = {}): string {
  const command = requireSafeToken(options.command ?? 'npx', 'Goose command');
  const args = options.args ?? ['-y', LIVEAUTH_MCP_PACKAGE];
  const timeoutSeconds = options.timeoutSeconds ?? 300;
  const id = requireSafeToken(options.id ?? GOOSE_EXTENSION_ID, 'Goose extension id');
  const name = options.name ?? GOOSE_EXTENSION_NAME;
  const description = options.description ?? GOOSE_EXTENSION_DESCRIPTION;

  if (!Number.isInteger(timeoutSeconds) || timeoutSeconds < 1 || timeoutSeconds > 3600) {
    throw new Error('Goose timeout must be an integer between 1 and 3600 seconds');
  }

  const params = new URLSearchParams();
  params.append('cmd', command);
  for (const arg of args) {
    params.append('arg', requireSafeToken(arg, 'Goose command argument'));
  }
  params.append('timeout', String(timeoutSeconds));
  params.append('id', id);
  params.append('name', name);
  params.append('description', description);

  return `goose://extension?${params.toString()}`;
}

export function getGooseManualConfig(): string {
  return `extensions:
  liveauth:
    type: stdio
    name: LiveAuth
    enabled: true
    cmd: npx
    args: ["-y", "${LIVEAUTH_MCP_PACKAGE}"]
    env_keys: []
    envs: {}
    timeout: 300`;
}

export function detectGoose(): boolean {
  const result = spawnSync('goose', ['--version'], {
    shell: false,
    stdio: 'ignore',
    timeout: 5_000,
  });
  return result.status === 0;
}

export function runGooseSetup(options: GooseSetupOptions = {}): number {
  const installed = (options.detectGoose ?? detectGoose)();
  const write = options.write ?? ((message: string) => process.stdout.write(`${message}\n`));
  const deepLink = buildGooseDeepLink();

  write(installed ? 'Goose detected.' : 'Goose was not detected on PATH; you can still install through Goose Desktop.');
  write('');
  write('Install LiveAuth for Goose:');
  write(deepLink);
  write('');
  write('If the deep link is unavailable, run a one-off Goose session with:');
  write(`goose session --with-extension "liveauth:npx -y ${LIVEAUTH_MCP_PACKAGE}"`);
  write('');
  write('Or add this stdio extension through Goose configuration:');
  write(getGooseManualConfig());
  write('');
  write('No credential is required for the first PoW flow. Add LIVEAUTH_API_KEY later through Goose\'s secret settings only when you need a specific LiveAuth project.');
  return 0;
}
