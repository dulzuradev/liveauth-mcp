import { createMcpGate } from '@liveauth-labs/mcp-server';
import { normalizeLimits } from './web-fetch.mjs';

export const DEFAULT_LIVEAUTH_API_URL = 'https://api.liveauth.app';
export const DEFAULT_WEB_FETCH_TOOL_ID = '00000000-0000-0000-0000-000000000005';

export function loadWebFetchConfig(env = process.env) {
  const liveAuthApiUrl = env.LIVEAUTH_API_URL || DEFAULT_LIVEAUTH_API_URL;
  const liveAuthPublicKey = env.LIVEAUTH_PUBLIC_KEY || env.LIVEAUTH_API_KEY || '';
  const liveAuthToolId = env.LIVEAUTH_TOOL_ID || DEFAULT_WEB_FETCH_TOOL_ID;

  const costs = {
    webFetch: numberFromEnv(env, 'WEB_FETCH_DEFAULT_COST_SATS', 5),
    metadata: numberFromEnv(env, 'WEB_FETCH_METADATA_COST_SATS', 1)
  };

  const limits = normalizeLimits({
    defaultMaxBytes: numberFromEnv(env, 'WEB_FETCH_DEFAULT_MAX_BYTES', 200_000),
    maxBytes: numberFromEnv(env, 'WEB_FETCH_MAX_BYTES', 500_000),
    timeoutMs: numberFromEnv(env, 'WEB_FETCH_TIMEOUT_MS', 10_000),
    maxRedirects: numberFromEnv(env, 'WEB_FETCH_MAX_REDIRECTS', 3),
    userAgent: env.WEB_FETCH_USER_AGENT || undefined
  });

  return {
    liveAuthApiUrl,
    liveAuthPublicKey,
    liveAuthToolId,
    costs,
    limits,
    hosted: {
      host: env.HOST || '0.0.0.0',
      port: numberFromEnv(env, 'PORT', 8787)
    }
  };
}

export function createLiveAuthGate(config) {
  if (!config.liveAuthPublicKey) {
    throw new Error('LIVEAUTH_PUBLIC_KEY is required');
  }

  return createMcpGate({
    publicKey: config.liveAuthPublicKey,
    baseUrl: config.liveAuthApiUrl,
    toolId: config.liveAuthToolId,
    defaultCostSats: config.costs.webFetch
  });
}

function numberFromEnv(env, name, fallback) {
  const raw = env[name];
  if (!raw) return fallback;
  const value = Number(raw);
  return Number.isFinite(value) ? value : fallback;
}
