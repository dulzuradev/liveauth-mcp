import { randomUUID } from 'node:crypto';
import {
  fetchWebMetadata,
  fetchWebPage,
  validateHttpUrl
} from './web-fetch.mjs';

export async function runWebFetch(args, options) {
  const url = requireString(args.url, 'url');
  const parsed = validateHttpUrl(url);
  const idempotencyKey = String(args.idempotencyKey || randomUUID());
  const maxBytes = args.maxBytes ?? options.limits.defaultMaxBytes;

  return options.gate.invoke(
    options.jwt,
    {
      url,
      maxBytes,
      includeHtml: args.includeHtml === true
    },
    async (input, context) => ({
      ...await (options.fetchWebPageImpl || fetchWebPage)(input.url, {
        maxBytes: input.maxBytes,
        includeHtml: input.includeHtml,
        limits: options.limits
      }),
      charge: context.liveAuth.charge
    }),
    {},
    {
      costSats: options.costs.webFetch,
      validateFirst: true,
      toolMethodName: 'web_fetch',
      idempotencyKey,
      ...(options.agentId ? { agentId: options.agentId } : {}),
      metadata: {
        urlHost: parsed.hostname
      }
    }
  );
}

export async function runWebFetchMetadata(args, options) {
  const url = requireString(args.url, 'url');
  const parsed = validateHttpUrl(url);
  const idempotencyKey = String(args.idempotencyKey || randomUUID());

  return options.gate.invoke(
    options.jwt,
    { url },
    async (input, context) => ({
      ...await (options.fetchWebMetadataImpl || fetchWebMetadata)(input.url, { limits: options.limits }),
      charge: context.liveAuth.charge
    }),
    {},
    {
      costSats: options.costs.metadata,
      validateFirst: true,
      toolMethodName: 'web_fetch_metadata',
      idempotencyKey,
      ...(options.agentId ? { agentId: options.agentId } : {}),
      metadata: {
        urlHost: parsed.hostname
      }
    }
  );
}

export function resolveJwtFromArgs(args, env = process.env) {
  const jwt = args.liveauthJwt || env.LIVEAUTH_JWT;
  if (!jwt || typeof jwt !== 'string') {
    throw new Error('Missing LiveAuth MCP JWT. Set LIVEAUTH_JWT or pass liveauthJwt.');
  }

  return jwt;
}

function requireString(value, name) {
  if (typeof value !== 'string' || !value.trim()) {
    throw new Error(`${name} is required`);
  }

  return value.trim();
}
