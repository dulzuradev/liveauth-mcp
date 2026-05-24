export function createWebFetchToolDefinitions(limits) {
  return [
    {
      name: 'web_fetch',
      description: 'Fetch a public http/https URL and return cleaned text plus metadata. Blocks private/local network targets.',
      inputSchema: {
        type: 'object',
        properties: {
          url: { type: 'string', description: 'Public http/https URL to fetch' },
          maxBytes: {
            type: 'number',
            description: `Maximum response bytes to read, capped at ${limits.maxBytes}`
          },
          includeHtml: {
            type: 'boolean',
            description: 'Include raw HTML in addition to cleaned text'
          },
          liveauthJwt: {
            type: 'string',
            description: 'Optional LiveAuth MCP JWT. Defaults to LIVEAUTH_JWT env.'
          },
          idempotencyKey: {
            type: 'string',
            description: 'Optional retry-safe call ID. Defaults to a generated UUID.'
          }
        },
        required: ['url']
      }
    },
    {
      name: 'web_fetch_metadata',
      description: 'Fetch low-cost page metadata for a public http/https URL. Blocks private/local network targets.',
      inputSchema: {
        type: 'object',
        properties: {
          url: { type: 'string', description: 'Public http/https URL to inspect' },
          liveauthJwt: {
            type: 'string',
            description: 'Optional LiveAuth MCP JWT. Defaults to LIVEAUTH_JWT env.'
          },
          idempotencyKey: {
            type: 'string',
            description: 'Optional retry-safe call ID. Defaults to a generated UUID.'
          }
        },
        required: ['url']
      }
    }
  ];
}
