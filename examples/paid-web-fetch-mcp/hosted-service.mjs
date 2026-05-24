import http from 'node:http';
import { createWebFetchToolDefinitions } from './tool-definitions.mjs';
import { WebFetchError } from './web-fetch.mjs';
import {
  runWebFetch,
  runWebFetchMetadata
} from './web-fetch-runner.mjs';

const JSON_LIMIT_BYTES = 64 * 1024;

export function createHostedWebFetchServer(options) {
  const tools = createWebFetchToolDefinitions(options.limits);

  return http.createServer(async (req, res) => {
    try {
      await routeRequest(req, res, { ...options, tools });
    } catch (error) {
      writeError(res, error);
    }
  });
}

async function routeRequest(req, res, options) {
  const url = new URL(req.url || '/', 'http://localhost');

  if (req.method === 'OPTIONS') {
    writeCorsHeaders(res);
    res.writeHead(204);
    res.end();
    return;
  }

  if (req.method === 'GET' && url.pathname === '/healthz') {
    writeJson(res, 200, {
      status: 'ok',
      service: 'liveauth-paid-web-fetch',
      toolId: options.toolId,
      tools: options.tools.map(tool => tool.name)
    });
    return;
  }

  if (req.method === 'GET' && url.pathname === '/tools') {
    writeJson(res, 200, { tools: options.tools });
    return;
  }

  if (req.method === 'POST' && url.pathname === '/tools/web_fetch') {
    const payload = await readJsonBody(req);
    const result = await runWebFetch(payload, {
      ...options,
      jwt: resolveJwt(req, payload),
      agentId: resolveAgentId(req, payload)
    });
    writeJson(res, 200, result);
    return;
  }

  if (req.method === 'POST' && url.pathname === '/tools/web_fetch_metadata') {
    const payload = await readJsonBody(req);
    const result = await runWebFetchMetadata(payload, {
      ...options,
      jwt: resolveJwt(req, payload),
      agentId: resolveAgentId(req, payload)
    });
    writeJson(res, 200, result);
    return;
  }

  writeJson(res, 404, {
    error: 'not_found',
    message: 'Use GET /tools, POST /tools/web_fetch, or POST /tools/web_fetch_metadata.'
  });
}

function resolveJwt(req, payload) {
  const authorization = req.headers.authorization || '';
  const match = authorization.match(/^Bearer\s+(.+)$/i);
  const jwt = match?.[1] || payload.liveauthJwt;

  if (!jwt || typeof jwt !== 'string') {
    const error = new Error('Missing LiveAuth MCP JWT. Send Authorization: Bearer <jwt>.');
    error.status = 401;
    error.code = 'unauthorized';
    throw error;
  }

  return jwt.trim();
}

function resolveAgentId(req, payload) {
  const header = req.headers['x-liveauth-agent-id'];
  if (typeof header === 'string' && header.trim()) return header.trim();
  if (typeof payload.agentId === 'string' && payload.agentId.trim()) return payload.agentId.trim();
  return undefined;
}

async function readJsonBody(req) {
  const chunks = [];
  let received = 0;

  for await (const chunk of req) {
    received += chunk.length;
    if (received > JSON_LIMIT_BYTES) {
      const error = new Error('JSON body is too large');
      error.status = 413;
      error.code = 'body_too_large';
      throw error;
    }

    chunks.push(chunk);
  }

  if (chunks.length === 0) return {};

  try {
    const payload = JSON.parse(Buffer.concat(chunks).toString('utf8'));
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
      throw new Error('json_body_must_be_object');
    }

    return payload;
  } catch {
    const error = new Error('Request body must be valid JSON');
    error.status = 400;
    error.code = 'invalid_json';
    throw error;
  }
}

function writeJson(res, status, body) {
  writeCorsHeaders(res);
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store'
  });
  res.end(JSON.stringify(body, null, 2));
}

function writeError(res, error) {
  const status = errorStatus(error);
  writeJson(res, status, {
    error: errorCode(error),
    message: error instanceof Error ? error.message : 'Unexpected error',
    ...(error?.details ? { details: error.details } : {})
  });
}

function errorStatus(error) {
  if (error instanceof WebFetchError) return 400;
  if (typeof error?.status === 'number') return error.status;
  if (error?.name === 'UnauthorizedError') return 401;
  if (error?.name === 'BudgetExceededError') return 402;
  return 500;
}

function errorCode(error) {
  if (error instanceof WebFetchError) return error.code;
  if (typeof error?.code === 'string') return error.code;
  if (error?.name === 'UnauthorizedError') return 'unauthorized';
  if (error?.name === 'BudgetExceededError') return 'budget_exceeded';
  return 'hosted_web_fetch_error';
}

function writeCorsHeaders(res) {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET,POST,OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Authorization,Content-Type,X-LiveAuth-Agent-Id');
}
