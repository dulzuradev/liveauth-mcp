# `@liveauth/sdk`

LiveAuth CostShield protects expensive application actions with adaptive
proof-of-work and action-bound authorization tokens.

The browser client requests a challenge, solves it in a Web Worker, and
returns a short-lived signed token. Server helpers validate that token before
your application makes the expensive provider call.

## Browser

```bash
npm install @liveauth/sdk
```

```ts
import { LiveAuth } from '@liveauth/sdk';

const liveAuth = new LiveAuth({
  publicKey: 'la_pk_...',
  environment: 'TEST'
});

const authorization = await liveAuth.protect({
  action: 'ai.generate_image',
  onProgress: progress => {
    console.log(`Tried ${progress.attempts} proofs`);
  }
});

await fetch('/api/generate', {
  method: 'POST',
  headers: {
    Authorization: `Bearer ${authorization.token}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({ prompt: 'A lighthouse at dusk' })
});
```

Proof-of-work runs in `dist/pow-worker.js`; modern Angular, Vite, webpack, and
Rollup builds resolve that worker from the package automatically.

## Express

Keep the project secret key on the server. Never include it in browser code.

```ts
import { CostShieldVerifier } from '@liveauth/sdk/server';

const costShield = new CostShieldVerifier({
  projectId: 'your-project-uuid',
  environment: 'TEST',
  secretKey: process.env.LIVEAUTH_SECRET_KEY
});

app.post(
  '/api/generate',
  costShield.protect('ai.generate_image', {
    origin: 'https://your-app.example'
  }),
  async (req, res) => {
    // The token is valid and single-use tokens have been consumed.
    const image = await expensiveImageProvider(req.body);
    res.json(image);
  }
);
```

The verifier caches LiveAuth's public JWKS and validates RS256 signatures,
issuer, audience, expiration, project, environment, action, and origin
locally. Single-use tokens are then consumed through LiveAuth using the secret
project key.

## Next.js route handlers

```ts
import { CostShieldVerifier } from '@liveauth/sdk/server';

const costShield = new CostShieldVerifier({
  projectId: process.env.LIVEAUTH_PROJECT_ID!,
  environment: 'LIVE',
  secretKey: process.env.LIVEAUTH_SECRET_KEY!
});

export async function POST(request: Request) {
  await costShield.authorizeRequest(request, {
    action: 'ai.generate_image',
    origin: 'https://your-app.example'
  });

  // Call the expensive provider only after authorization succeeds.
  return Response.json({ ok: true });
}
```

## TEST and LIVE

- Use `TEST` while configuring actions and integration.
- A token is bound to one project, environment, action, and configured origin.
- Switch both the protected action and SDK/server configuration to `LIVE`
  before production traffic.
- Local verification is sufficient for reusable tokens. Single-use tokens
  must be consumed remotely to enforce replay protection.

The package has no runtime dependencies and supports Node.js 18 or newer.

## Local end-to-end smoke test

With a local LiveAuth API running and a TEST protected action configured:

```bash
LIVEAUTH_API_URL=http://127.0.0.1:5167 \
LIVEAUTH_PUBLIC_KEY=la_pk_... \
LIVEAUTH_SECRET_KEY=la_sk_... \
LIVEAUTH_PROJECT_ID=your-project-uuid \
LIVEAUTH_ACTION=ai.generate_image \
LIVEAUTH_ORIGIN=http://localhost:4200 \
npm run smoke:local
```

The smoke runner requests and solves a real challenge, obtains a token,
verifies its RS256 signature, and consumes it through the LiveAuth API.
