# Build from the directory that contains both repos:
#   docker build -f LiveAuth/examples/paid-web-fetch-mcp/Dockerfile -t liveauth-web-fetch-mcp .
#
# Expected build context:
#   ./LiveAuth
#   ./liveauth-mcp

FROM node:20-alpine AS sdk
WORKDIR /workspace/liveauth-mcp
COPY liveauth-mcp/package*.json ./
COPY liveauth-mcp/tsconfig.json ./
COPY liveauth-mcp/src ./src
RUN npm ci && npm run build

FROM node:20-alpine AS runtime
WORKDIR /app
RUN printf '%s\n' \
  '{"name":"liveauth-paid-web-fetch-mcp","version":"0.1.0","type":"module","scripts":{"start:hosted":"node hosted.mjs"},"dependencies":{"@modelcontextprotocol/sdk":"^0.5.0","dotenv":"^16.4.5"}}' \
  > package.json
RUN npm install --omit=dev --ignore-scripts
COPY --from=sdk /workspace/liveauth-mcp node_modules/@liveauth-labs/mcp-server
COPY LiveAuth/examples/paid-web-fetch-mcp/*.mjs ./
COPY LiveAuth/examples/paid-web-fetch-mcp/README.md ./
COPY LiveAuth/examples/paid-web-fetch-mcp/.env.example ./

ENV NODE_ENV=production
ENV HOST=0.0.0.0
ENV PORT=8787

EXPOSE 8787
CMD ["npm", "run", "start:hosted"]
