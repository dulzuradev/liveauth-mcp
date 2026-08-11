import { config } from 'dotenv';
import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { CallToolRequestSchema, ListToolsRequestSchema } from '@modelcontextprotocol/sdk/types.js';

config();

const apiUrl = (process.env.LIVEAUTH_API_URL || 'http://127.0.0.1:5088').replace(/\/$/, '');
const publicKey = process.env.LIVEAUTH_API_KEY || '';
const jwt = process.env.LIVEAUTH_JWT || '';

const workCategories = [
  'GeneralConstruction', 'Roofing', 'HVAC', 'Electrical', 'Plumbing', 'Solar',
  'FireProtection', 'Mechanical', 'Structural', 'Demolition', 'NewConstruction',
  'Renovation', 'TenantImprovement', 'Other'
];

const tools = [
  {
    name: 'search_projects',
    description: 'Paid (default 5 sats): search normalized public construction permits across Austin, San Francisco, and Seattle with date, value, permit type, trade category, occupancy, keyword, contractor, and location filters. Returns official source provenance.',
    inputSchema: {
      type: 'object', additionalProperties: false,
      properties: {
        location: { type: 'string', description: 'City or City, ST' },
        municipality: { type: 'string' }, state: { type: 'string' },
        issued_after: { type: 'string', format: 'date-time' }, issued_before: { type: 'string', format: 'date-time' },
        minimum_project_value: { type: 'number', minimum: 0 }, maximum_project_value: { type: 'number', minimum: 0 },
        permit_type: { type: 'string' }, work_category: { type: 'string', enum: workCategories },
        commercial_only: { type: 'boolean', default: false }, residential_only: { type: 'boolean', default: false },
        keywords: { type: 'string' }, contractor_name: { type: 'string' },
        limit: { type: 'integer', minimum: 1, maximum: 100, default: 25 }
      }
    }
  },
  {
    name: 'find_opportunities',
    description: 'Paid (default 10 sats): find and score recent, realistic sales opportunities for a construction trade. Every score includes its matched trade, strength, and additive reasons.',
    inputSchema: {
      type: 'object', required: ['trade'], additionalProperties: false,
      properties: {
        location: { type: 'string', description: 'City or City, ST' }, state: { type: 'string' },
        trade: { type: 'string', description: 'HVAC, Electrical, Plumbing, Roofing, Solar, FireProtection, Mechanical, Structural, Demolition, or GeneralConstruction' },
        issued_within_days: { type: 'integer', minimum: 1, maximum: 3650, default: 7 },
        minimum_project_value: { type: 'number', minimum: 0 }, commercial_only: { type: 'boolean', default: false },
        limit: { type: 'integer', minimum: 1, maximum: 100, default: 25 }
      }
    }
  },
  {
    name: 'analyze_project',
    description: 'Paid (default 15 sats): analyze one permit by PermitSignal project ID, official record ID, or permit number. Returns scope, stage, likely trades, supplier opportunities, signals, and provenance.',
    inputSchema: {
      type: 'object', required: ['project_id'], additionalProperties: false,
      properties: { project_id: { type: 'string' } }
    }
  },
  {
    name: 'property_history',
    description: 'Paid (default 20 sats): retrieve exact-normalized permit history for a property, with summary statistics, common categories, major projects, and provenance.',
    inputSchema: {
      type: 'object', required: ['address'], additionalProperties: false,
      properties: {
        address: { type: 'string' }, municipality: { type: 'string' }, state: { type: 'string' },
        limit: { type: 'integer', minimum: 1, maximum: 100, default: 50 }
      }
    }
  }
];

async function callPermitSignal(name, args) {
  if (!publicKey || !jwt) {
    throw new Error('LIVEAUTH_API_KEY and LIVEAUTH_JWT are required. Authenticate with /api/mcp/start and /api/mcp/confirm first.');
  }

  const response = await fetch(`${apiUrl}/api/permitsignal/mcp`, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      'authorization': `Bearer ${jwt}`,
      'x-lw-public': publicKey,
      'x-liveauth-idempotency-key': crypto.randomUUID()
    },
    body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/call', params: { name, arguments: args || {} } })
  });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.error_description || body.error || `PermitSignal HTTP ${response.status}`);
  if (body.error) throw new Error(`${body.error.message}${body.error.data?.reason ? ` (${body.error.data.reason})` : ''}`);
  return body.result;
}

const server = new Server({ name: 'permitsignal', version: '1.0.0' }, { capabilities: { tools: {} } });
server.setRequestHandler(ListToolsRequestSchema, async () => ({ tools }));
server.setRequestHandler(CallToolRequestSchema, async request => {
  try {
    return await callPermitSignal(request.params.name, request.params.arguments);
  } catch (error) {
    return { content: [{ type: 'text', text: JSON.stringify({ error: error.message }) }], isError: true };
  }
});

await server.connect(new StdioServerTransport());
console.error(`PermitSignal MCP connected to ${apiUrl}`);
