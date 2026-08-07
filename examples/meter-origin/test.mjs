import test from 'node:test';
import assert from 'node:assert/strict';
import { createOrigin } from './server.mjs';

test('serves the sample weather origin', async () => {
  const server = createOrigin().listen(0, '127.0.0.1');
  await new Promise(resolve => server.once('listening', resolve));
  try {
    const { port } = server.address();
    const response = await fetch(`http://127.0.0.1:${port}/weather/seattle`);
    assert.equal(response.status, 200);
    assert.equal((await response.json()).temperatureC, 21);
  } finally { server.close(); }
});
