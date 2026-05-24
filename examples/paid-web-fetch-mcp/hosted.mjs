#!/usr/bin/env node

import { config as loadDotEnv } from 'dotenv';
import {
  createLiveAuthGate,
  loadWebFetchConfig
} from './runtime-config.mjs';
import { createHostedWebFetchServer } from './hosted-service.mjs';

loadDotEnv();

const webFetchConfig = loadWebFetchConfig();
const gate = createLiveAuthGate(webFetchConfig);
const server = createHostedWebFetchServer({
  gate,
  limits: webFetchConfig.limits,
  costs: webFetchConfig.costs,
  toolId: webFetchConfig.liveAuthToolId
});

server.listen(webFetchConfig.hosted.port, webFetchConfig.hosted.host, () => {
  console.error(
    `LiveAuth hosted Web Fetch listening on http://${webFetchConfig.hosted.host}:${webFetchConfig.hosted.port}`
  );
});

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => {
    server.close(() => process.exit(0));
  });
}
