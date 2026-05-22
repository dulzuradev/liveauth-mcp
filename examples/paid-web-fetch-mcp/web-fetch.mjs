import dns from 'node:dns/promises';
import http from 'node:http';
import https from 'node:https';
import net from 'node:net';
import { URL } from 'node:url';

export const DEFAULT_LIMITS = Object.freeze({
  defaultMaxBytes: 200_000,
  maxBytes: 500_000,
  timeoutMs: 10_000,
  maxRedirects: 3,
  userAgent: 'LiveAuth-WebFetch-MCP/1.0'
});

const REDIRECT_STATUSES = new Set([301, 302, 303, 307, 308]);

export class WebFetchError extends Error {
  constructor(message, code = 'web_fetch_error') {
    super(message);
    this.name = 'WebFetchError';
    this.code = code;
  }
}

export function normalizeLimits(overrides = {}) {
  const maxBytes = clampInteger(overrides.maxBytes ?? DEFAULT_LIMITS.maxBytes, 1, DEFAULT_LIMITS.maxBytes);
  const defaultMaxBytes = clampInteger(
    overrides.defaultMaxBytes ?? DEFAULT_LIMITS.defaultMaxBytes,
    1,
    maxBytes
  );

  return {
    defaultMaxBytes,
    maxBytes,
    timeoutMs: clampInteger(overrides.timeoutMs ?? DEFAULT_LIMITS.timeoutMs, 100, 60_000),
    maxRedirects: clampInteger(overrides.maxRedirects ?? DEFAULT_LIMITS.maxRedirects, 0, 10),
    userAgent: String(overrides.userAgent || DEFAULT_LIMITS.userAgent)
  };
}

export function validateHttpUrl(rawUrl) {
  let parsed;
  try {
    parsed = new URL(rawUrl);
  } catch {
    throw new WebFetchError('url must be an absolute http or https URL', 'invalid_url');
  }

  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    throw new WebFetchError('only http and https URLs are allowed', 'blocked_scheme');
  }

  if (!parsed.hostname) {
    throw new WebFetchError('url must include a hostname', 'invalid_url');
  }

  const hostname = parsed.hostname.toLowerCase();
  if (hostname === 'localhost' || hostname.endsWith('.localhost')) {
    throw new WebFetchError('localhost targets are blocked', 'blocked_host');
  }

  const ipVersion = net.isIP(hostname);
  if (ipVersion && isBlockedIp(hostname)) {
    throw new WebFetchError('private, local, and link-local IP targets are blocked', 'blocked_ip');
  }

  parsed.hash = '';
  return parsed;
}

export async function resolvePublicAddress(hostname) {
  const ipVersion = net.isIP(hostname);
  if (ipVersion) {
    if (isBlockedIp(hostname)) {
      throw new WebFetchError('private, local, and link-local IP targets are blocked', 'blocked_ip');
    }

    return { address: hostname, family: ipVersion };
  }

  let records;
  try {
    records = await dns.lookup(hostname, { all: true, verbatim: true });
  } catch (error) {
    throw new WebFetchError(`DNS lookup failed for ${hostname}: ${error.message}`, 'dns_failed');
  }

  const blocked = records.filter(record => isBlockedIp(record.address));
  if (blocked.length > 0) {
    throw new WebFetchError('hostname resolves to a private, local, or link-local address', 'blocked_ip');
  }

  const selected = records.find(record => record.family === 4) ?? records[0];
  if (!selected) {
    throw new WebFetchError(`DNS lookup returned no addresses for ${hostname}`, 'dns_failed');
  }

  return selected;
}

export function isBlockedIp(address) {
  const normalized = address.toLowerCase();
  const version = net.isIP(normalized);

  if (version === 4) {
    const parts = normalized.split('.').map(part => Number(part));
    if (parts.length !== 4 || parts.some(part => !Number.isInteger(part) || part < 0 || part > 255)) {
      return true;
    }

    const [a, b] = parts;
    return (
      a === 0 ||
      a === 10 ||
      a === 127 ||
      a === 169 && b === 254 ||
      a === 172 && b >= 16 && b <= 31 ||
      a === 192 && b === 168 ||
      a === 100 && b >= 64 && b <= 127 ||
      a >= 224
    );
  }

  if (version === 6) {
    if (
      normalized === '::' ||
      normalized === '::1' ||
      normalized.startsWith('fc') ||
      normalized.startsWith('fd') ||
      normalized.startsWith('fe80:')
    ) {
      return true;
    }

    if (normalized.startsWith('::ffff:')) {
      const embedded = normalized.slice('::ffff:'.length);
      return net.isIP(embedded) === 4 ? isBlockedIp(embedded) : true;
    }

    return false;
  }

  return true;
}

export async function fetchWebPage(rawUrl, options = {}) {
  const limits = normalizeLimits(options.limits);
  const maxBytes = clampInteger(options.maxBytes ?? limits.defaultMaxBytes, 1, limits.maxBytes);
  const includeHtml = options.includeHtml === true;
  const response = await requestWithRedirects(rawUrl, {
    limits,
    maxBytes,
    redirectCount: 0
  });

  const html = response.body.toString('utf8');
  const title = extractTitle(html);
  const text = htmlToText(html);

  return {
    url: response.url,
    status: response.status,
    contentType: response.contentType,
    title,
    text,
    ...(includeHtml ? { html } : {}),
    truncated: response.truncated,
    fetchedAt: new Date().toISOString()
  };
}

export async function fetchWebMetadata(rawUrl, options = {}) {
  const limits = normalizeLimits(options.limits);
  const response = await requestWithRedirects(rawUrl, {
    limits,
    maxBytes: Math.min(64_000, limits.maxBytes),
    redirectCount: 0
  });

  const html = response.body.toString('utf8');

  return {
    url: response.url,
    status: response.status,
    contentType: response.contentType,
    title: extractTitle(html),
    description: extractMetaDescription(html),
    fetchedAt: new Date().toISOString()
  };
}

async function requestWithRedirects(rawUrl, options) {
  const url = validateHttpUrl(rawUrl);
  const address = await resolvePublicAddress(url.hostname);
  const response = await requestOnce(url, address, options);

  if (REDIRECT_STATUSES.has(response.status) && response.location) {
    if (options.redirectCount >= options.limits.maxRedirects) {
      throw new WebFetchError('too many redirects', 'too_many_redirects');
    }

    const nextUrl = new URL(response.location, url);
    return requestWithRedirects(nextUrl.toString(), {
      ...options,
      redirectCount: options.redirectCount + 1
    });
  }

  return response;
}

function requestOnce(url, address, options) {
  const transport = url.protocol === 'https:' ? https : http;

  return new Promise((resolve, reject) => {
    const req = transport.request(
      url,
      {
        method: 'GET',
        timeout: options.limits.timeoutMs,
        headers: {
          'Accept': 'text/html, text/plain;q=0.9, */*;q=0.2',
          'User-Agent': options.limits.userAgent
        },
        lookup(_hostname, _lookupOptions, callback) {
          callback(null, address.address, address.family);
        }
      },
      res => {
        const chunks = [];
        let received = 0;
        let truncated = false;
        let settled = false;

        const finish = () => {
          if (settled) return;
          settled = true;
          resolve({
            url: url.toString(),
            status: res.statusCode ?? 0,
            contentType: String(res.headers['content-type'] || ''),
            location: typeof res.headers.location === 'string' ? res.headers.location : undefined,
            body: Buffer.concat(chunks),
            truncated
          });
        };

        res.on('data', chunk => {
          if (truncated) return;

          const remaining = options.maxBytes - received;
          if (chunk.length > remaining) {
            chunks.push(chunk.subarray(0, Math.max(0, remaining)));
            received = options.maxBytes;
            truncated = true;
            finish();
            res.destroy();
            return;
          }

          chunks.push(chunk);
          received += chunk.length;
        });

        res.on('end', finish);
      }
    );

    req.on('timeout', () => req.destroy(new WebFetchError('request timed out', 'timeout')));
    req.on('error', reject);

    req.end();
  });
}

export function extractTitle(html) {
  const match = html.match(/<title[^>]*>([\s\S]*?)<\/title>/i);
  return match ? decodeEntities(stripTags(match[1]).trim()) : '';
}

export function extractMetaDescription(html) {
  const match = html.match(/<meta\s+[^>]*(?:name|property)=["'](?:description|og:description)["'][^>]*content=["']([^"']*)["'][^>]*>/i)
    ?? html.match(/<meta\s+[^>]*content=["']([^"']*)["'][^>]*(?:name|property)=["'](?:description|og:description)["'][^>]*>/i);

  return match ? decodeEntities(match[1].trim()) : '';
}

export function htmlToText(html) {
  return decodeEntities(
    stripTags(
      html
        .replace(/<script\b[\s\S]*?<\/script>/gi, ' ')
        .replace(/<style\b[\s\S]*?<\/style>/gi, ' ')
        .replace(/<noscript\b[\s\S]*?<\/noscript>/gi, ' ')
        .replace(/<svg\b[\s\S]*?<\/svg>/gi, ' ')
        .replace(/<(br|p|div|li|tr|h[1-6])\b[^>]*>/gi, '\n')
    )
  )
    .replace(/[ \t]+\n/g, '\n')
    .replace(/\n{3,}/g, '\n\n')
    .replace(/[ \t]{2,}/g, ' ')
    .trim();
}

function stripTags(value) {
  return String(value || '').replace(/<[^>]+>/g, ' ');
}

function decodeEntities(value) {
  return String(value || '')
    .replace(/&nbsp;/gi, ' ')
    .replace(/&amp;/gi, '&')
    .replace(/&lt;/gi, '<')
    .replace(/&gt;/gi, '>')
    .replace(/&quot;/gi, '"')
    .replace(/&#39;/gi, "'")
    .replace(/&#(\d+);/g, (_match, code) => String.fromCodePoint(Number(code)))
    .replace(/&#x([0-9a-f]+);/gi, (_match, code) => String.fromCodePoint(Number.parseInt(code, 16)));
}

function clampInteger(value, min, max) {
  const number = Number(value);
  if (!Number.isFinite(number)) return min;
  return Math.min(max, Math.max(min, Math.floor(number)));
}
