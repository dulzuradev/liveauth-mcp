export class HostedWebFetchError extends Error {
  constructor(message, { status, code, details } = {}) {
    super(message);
    this.name = 'HostedWebFetchError';
    this.status = status;
    this.code = code;
    this.details = details;
  }
}

export async function callHostedTool({ baseUrl, toolName, args, jwt, agentId }) {
  const { liveauthJwt: _liveauthJwt, ...body } = args;
  const response = await fetch(`${cleanBaseUrl(baseUrl)}/tools/${toolName}`, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${jwt}`,
      'Content-Type': 'application/json',
      ...(agentId ? { 'X-LiveAuth-Agent-Id': agentId } : {})
    },
    body: JSON.stringify(body)
  });

  const payload = await readJsonResponse(response);
  if (!response.ok) {
    throw new HostedWebFetchError(payload.message || `Hosted ${toolName} failed`, {
      status: response.status,
      code: payload.error,
      details: payload.details
    });
  }

  return payload;
}

export function cleanBaseUrl(value) {
  return String(value || '').replace(/\/+$/, '');
}

async function readJsonResponse(response) {
  const text = await response.text();
  if (!text) return {};

  try {
    return JSON.parse(text);
  } catch {
    throw new HostedWebFetchError('Hosted Web Fetch returned invalid JSON', {
      status: response.status,
      code: 'invalid_hosted_response',
      details: text.slice(0, 500)
    });
  }
}
