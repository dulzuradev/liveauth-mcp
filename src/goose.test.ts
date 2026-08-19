import { describe, expect, it } from 'vitest';
import {
  buildGooseDeepLink,
  getGooseManualConfig,
  runGooseSetup,
} from './goose.js';

describe('Goose setup', () => {
  it('builds the current official extension deep link with separately escaped npm arguments', () => {
    const link = buildGooseDeepLink({ name: 'LiveAuth & Goose' });
    const url = new URL(link);

    expect(url.protocol).toBe('goose:');
    expect(url.hostname).toBe('extension');
    expect(url.searchParams.get('cmd')).toBe('npx');
    expect(url.searchParams.getAll('arg')).toEqual(['-y', '@liveauth-labs/mcp-server']);
    expect(url.searchParams.get('timeout')).toBe('300');
    expect(url.searchParams.get('id')).toBe('liveauth');
    expect(url.searchParams.get('name')).toBe('LiveAuth & Goose');
  });

  it('rejects unsafe command arguments rather than constructing an injectable link', () => {
    expect(() => buildGooseDeepLink({ args: ['-y', '@liveauth-labs/mcp-server;open'] }))
      .toThrow('unsupported characters');
  });

  it('prints useful fallbacks when Goose is not installed and never embeds a secret', () => {
    const lines: string[] = [];
    const status = runGooseSetup({ detectGoose: () => false, write: (line) => lines.push(line) });
    const output = lines.join('\n');

    expect(status).toBe(0);
    expect(output).toContain('Goose was not detected on PATH');
    expect(output).toContain('goose://extension?');
    expect(output).toContain('goose session --with-extension');
    expect(output).toContain('No credential is required');
    expect(output).not.toContain('la_pk_');
  });

  it('provides non-destructive manual stdio configuration with no plaintext environment values', () => {
    expect(getGooseManualConfig()).toContain('type: stdio');
    expect(getGooseManualConfig()).toContain('args: ["-y", "@liveauth-labs/mcp-server"]');
    expect(getGooseManualConfig()).toContain('env_keys: []');
    expect(getGooseManualConfig()).toContain('envs: {}');
  });
});
