import http from 'node:http';

export function createOrigin() {
  return http.createServer(async (request, response) => {
    const chunks = [];
    for await (const chunk of request) chunks.push(chunk);
    response.setHeader('content-type', 'application/json');
    if (request.url === '/health') return response.end(JSON.stringify({ ok: true }));
    if (request.method === 'GET' && request.url.startsWith('/weather/'))
      return response.end(JSON.stringify({ location: request.url.split('/').at(-1), temperatureC: 21 }));
    if (request.method === 'POST' && request.url === '/research')
      return response.end(JSON.stringify({ accepted: true, bytes: Buffer.concat(chunks).length }));
    response.statusCode = 404;
    response.end(JSON.stringify({ error: 'not_found' }));
  });
}

if (process.argv[1] === new URL(import.meta.url).pathname) {
  const port = Number(process.env.PORT ?? 4010);
  createOrigin().listen(port, '127.0.0.1', () => console.log(`Meter sample origin: http://127.0.0.1:${port}`));
}
