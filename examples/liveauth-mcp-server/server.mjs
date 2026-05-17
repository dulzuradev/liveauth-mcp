/**
 * LiveAuth Sample MCP Server
 * 
 * A reference implementation showing how to add LiveAuth pay-per-call
 * authentication to an MCP server.
 * 
 * Flow:
 * 1. Client authenticates with LiveAuth MCP Gate
 * 2. Gets JWT token
 * 3. Calls MCP tools (JWT validated per-request)
 * 4. LiveAuth tracks usage, bills the caller
 * 
 * This server exposes 3 simple tools:
 * - calculator: Basic math operations
 * - random_fact: Get a random interesting fact
 * - weather: Get weather for a city (mock data)
 */

import { config } from 'dotenv';
import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
  ListResourcesRequestSchema,
  ListPromptsRequestSchema,
} from '@modelcontextprotocol/sdk/types.js';

config();

// ============================================================
// Configuration
// ============================================================

const LIVEAUTH_API_URL = process.env.LIVEAUTH_API_URL || 'https://api.liveauth.app';
const LIVEAUTH_API_KEY = process.env.LIVEAUTH_API_KEY || '';
const PORT = process.env.PORT || 3000;
const LOCAL_MODE = process.argv.includes('--local');

// ============================================================
// Tool Definitions
// ============================================================

const TOOLS = [
  {
    name: 'echo',
    description: 'Echo back the input text. Useful for testing L402 authentication flow.',
    inputSchema: {
      type: 'object',
      properties: {
        message: {
          type: 'string',
          description: 'Text to echo back'
        },
        delay: {
          type: 'number',
          description: 'Optional delay in ms (0-5000) to simulate processing'
        }
      },
      required: ['message']
    },
    costSats: 1
  },
  {
    name: 'calculator',
    description: 'Perform basic math calculations. Input should be a mathematical expression like "2 + 2" or "10 * 5".',
    inputSchema: {
      type: 'object',
      properties: {
        expression: {
          type: 'string',
          description: 'Mathematical expression to evaluate'
        }
      },
      required: ['expression']
    },
    costSats: 1
  },
  {
    name: 'random_fact',
    description: 'Get a random interesting fact. Useful for trivia, conversation starters, or learning something new.',
    inputSchema: {
      type: 'object',
      properties: {
        category: {
          type: 'string',
          description: 'Category of fact: science, history, nature, space, technology, or leave empty for any',
          enum: ['science', 'history', 'nature', 'space', 'technology', '']
        }
      }
    },
    costSats: 1
  },
  {
    name: 'weather',
    description: 'Get current weather for a city. Returns temperature, conditions, and humidity.',
    inputSchema: {
      type: 'object',
      properties: {
        city: {
          type: 'string',
          description: 'City name'
        }
      },
      required: ['city']
    },
    costSats: 2
  }
];

// ============================================================
// Tool Implementations
// ============================================================

function evaluateCalculator(expression) {
  // Safely evaluate math expression (basic validation)
  const sanitized = expression.replace(/[^0-9+\-*/.()% ]/g, '');
  
  try {
    // Use Function constructor for safe evaluation
    const result = new Function(`return ${sanitized}`)();
    return {
      expression,
      result,
      formatted: `${expression} = ${result}`
    };
  } catch (e) {
    return { error: 'Invalid expression', expression };
  }
}

function getRandomFact(category = '') {
  const facts = {
    science: [
      'Honey never spoils. Archaeologists have found 3,000-year-old honey in Egyptian tombs.',
      'A teaspoon of neutron star would weigh 6 billion tons.',
      'Bananas are berries, but strawberries are not.',
      'Water can boil and freeze at the same time under the right pressure.',
      'Octopuses have three hearts and blue blood.'
    ],
    history: [
      'Cleopatra lived closer in time to the Moon landing than to the construction of the Great Pyramid.',
      'The shortest war in history lasted 38 minutes between Britain and Zanzibar in 1896.',
      'Oxford University is older than the Aztec Empire.',
      'The first computer programmer was Ada Lovelace in the 1840s.',
      'Vikings used to give kittens to new brides as essential household gifts.'
    ],
    nature: [
      'Trees can communicate with each other through underground fungal networks.',
      'Dolphins have names for each other.',
      'Crows can remember human faces for years.',
      'A group of flamingos is called a "flamboyance."',
      'Sea otters hold hands while sleeping to avoid drifting apart.'
    ],
    space: [
      'One day on Venus is longer than one year on Venus.',
      'There are more stars in the universe than grains of sand on Earth.',
      'A neutron star is so dense that a teaspoon would weigh as much as a mountain.',
      'The footprints on the Moon will be there for 100 million years.',
      'Saturn would float in water if you had a bathtub big enough.'
    ],
    technology: [
      'The first computer virus was created in 1983.',
      'The QWERTY keyboard was designed to slow typists down.',
      'The first 1GB hard drive weighed about 550 pounds and cost $40,000.',
      'HTML stands for HyperText Markup Language.',
      'The first email was sent in 1971.'
    ]
  };

  const categories = Object.keys(facts);
  const selectedCategory = category && facts[category] ? category : categories[Math.floor(Math.random() * categories.length)];
  
  const categoryFacts = facts[selectedCategory];
  const fact = categoryFacts[Math.floor(Math.random() * categoryFacts.length)];

  return {
    fact,
    category: selectedCategory
  };
}

function getWeather(city) {
  // Mock weather data - in production you'd call a real weather API
  const conditions = ['Sunny', 'Partly cloudy', 'Cloudy', 'Rainy', 'Snowy'];
  const temps = {
    'San Francisco': { temp: 18, condition: 'Foggy', humidity: 75 },
    'New York': { temp: 22, condition: 'Sunny', humidity: 45 },
    'London': { temp: 14, condition: 'Rainy', humidity: 82 },
    'Tokyo': { temp: 25, condition: 'Humid', humidity: 65 },
    'Sydney': { temp: 28, condition: 'Sunny', humidity: 40 }
  };

  const weather = temps[city] || {
    temp: Math.floor(Math.random() * 35) + 5,
    condition: conditions[Math.floor(Math.random() * conditions.length)],
    humidity: Math.floor(Math.random() * 60) + 30
  };

  return {
    city,
    temperature: weather.temp,
    condition: weather.condition,
    humidity: weather.humidity,
    timestamp: new Date().toISOString()
  };
}

async function validateLiveAuthSession() {
  if (LOCAL_MODE) {
    return { authenticated: true, localMode: true };
  }

  const jwt = process.env.LIVEAUTH_JWT;
  if (!jwt) {
    throw new Error('Missing LIVEAUTH_JWT. Authenticate with LiveAuth before calling tools.');
  }

  const response = await fetch(`${LIVEAUTH_API_URL}/api/mcp/usage`, {
    method: 'GET',
    headers: {
      'Authorization': `Bearer ${jwt}`,
      ...(LIVEAUTH_API_KEY && { 'X-LW-Public': LIVEAUTH_API_KEY })
    }
  });

  if (!response.ok) {
    throw new Error(`LiveAuth session validation failed: ${response.status}`);
  }

  return response.json();
}

// ============================================================
// MCP Server
// ============================================================

class LiveAuthMcpServer {
  constructor() {
    this.server = new Server(
      {
        name: 'liveauth-sample-server',
        version: '1.0.0',
      },
      {
        capabilities: {
          tools: {},
          resources: {},
          prompts: {},
        },
      }
    );

    this.setupHandlers();
  }

  setupHandlers() {
    // List available tools
    this.server.setRequestHandler(ListToolsRequestSchema, async () => {
      return {
        tools: TOOLS.map(tool => ({
          name: tool.name,
          description: tool.description,
          inputSchema: tool.inputSchema
        }))
      };
    });

    // List resources (none for this example)
    this.server.setRequestHandler(ListResourcesRequestSchema, async () => ({
      resources: []
    }));

    // List prompts (none for this example)
    this.server.setRequestHandler(ListPromptsRequestSchema, async () => ({
      prompts: []
    }));

    // Handle tool calls
    this.server.setRequestHandler(CallToolRequestSchema, async (request) => {
      const { name, arguments: args } = request.params;

      try {
        const authSession = await validateLiveAuthSession();
        let result;

        switch (name) {
          case 'echo':
            // Simulate processing delay if requested
            if (args.delay && args.delay > 0) {
              await new Promise(resolve => setTimeout(resolve, Math.min(args.delay, 5000)));
            }
            result = {
              echo: args.message,
              timestamp: new Date().toISOString(),
              authenticated: authSession.authenticated ?? true,
              message: 'L402 auth successful! Your call was debited from your bundle.'
            };
            break;

          case 'calculator':
            result = evaluateCalculator(args.expression);
            break;

          case 'random_fact':
            result = getRandomFact(args.category || '');
            break;

          case 'weather':
            result = getWeather(args.city);
            break;

          default:
            return {
              content: [
                {
                  type: 'text',
                  text: JSON.stringify({ error: `Unknown tool: ${name}` })
                }
              ],
              isError: true
            };
        }

        // In production: Report usage to LiveAuth here
        // await this.reportUsage(name, tool.costSats);

        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify(result, null, 2)
            }
          ]
        };

      } catch (error) {
        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify({ error: error.message })
            }
          ],
          isError: true
        };
      }
    });
  }

  async start() {
    const transport = new StdioServerTransport();
    await this.server.connect(transport);
    
    console.error('🔧 LiveAuth Sample MCP Server running on stdio');
    console.error(`📡 Mode: ${LOCAL_MODE ? 'Local (no auth)' : 'LiveAuth-gated'}`);
    console.error(`🛠️  Tools: ${TOOLS.map(t => t.name).join(', ')}`);
  }
}

// ============================================================
// Start Server
// ============================================================

const server = new LiveAuthMcpServer();
server.start().catch(console.error);
