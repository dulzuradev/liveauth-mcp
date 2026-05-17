export * from './types.js';
export * from './errors.js';
export * from './pow.js';
export * from './client.js';
export * from './server-gate.js';

import { LiveAuthMcpClient } from './client.js';
import { LiveAuthMcpServerGate } from './server-gate.js';
import type { LiveAuthMcpClientConfig, LiveAuthMcpServerGateConfig } from './types.js';

export function createMcpClient(config: LiveAuthMcpClientConfig): LiveAuthMcpClient {
  return new LiveAuthMcpClient(config);
}

export function createMcpGate(config: LiveAuthMcpServerGateConfig): LiveAuthMcpServerGate {
  return new LiveAuthMcpServerGate(config);
}
