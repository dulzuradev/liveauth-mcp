# CostShield production runbook

Complete this runbook before publishing the CostShield SDK packages.

## 1. Rotate previously committed credentials

The repository previously contained credential values in deployment scripts and
application settings. Treat those values as exposed even though the repository
is private.

Rotate:

- GitHub OAuth client secret
- LiveAuth JWT signing key
- LiveAuth proof-of-work HMAC secret
- Resend API key
- LND macaroon

Removing the values from the current branch does not remove them from Git
history. Rotate first. Decide separately whether repository history should be
rewritten after coordinating with every clone and deployment.

## 2. Create the CostShield signing key

Generate a dedicated RSA key on the production server. Do not reuse the JWT or
proof-of-work signing secrets.

```bash
sudo install -d -m 700 /opt/liveauth/secrets
sudo openssl genpkey \
  -algorithm RSA \
  -pkeyopt rsa_keygen_bits:3072 \
  -out /opt/liveauth/secrets/costshield-signing-key.pem
sudo chmod 600 /opt/liveauth/secrets/costshield-signing-key.pem
```

Encode the PEM as single-line base64:

```bash
sudo base64 -w 0 \
  /opt/liveauth/secrets/costshield-signing-key.pem
```

Store that output as `CostShield__SigningPrivateKeyPemBase64` in the protected
production environment file. Never commit the private PEM or its base64 form.

The initial key ID is `costshield-rs256-v1`. Change the key ID whenever the key
material changes.

## 3. Provision the production environment

Copy `deploy/liveauth.env.example` to `/opt/liveauth/liveauth.env` on the
production server, replace every `CHANGE_ME` value, and restrict access:

```bash
sudo chown liveauth:liveauth /opt/liveauth/liveauth.env
sudo chmod 600 /opt/liveauth/liveauth.env
```

The deployment script refuses to restart the API when the file is missing or
when a required variable is empty. Production startup also validates that the
CostShield key:

- is valid PKCS#8/PEM private key material;
- is at least 2048 bits;
- uses a valid, non-empty key ID.

Development and test environments may continue to use an ephemeral key.

## 4. Deploy and verify

Deploy the private LiveAuth application:

```bash
./scripts/deploy.sh
```

Verify the public key endpoint:

```bash
curl --fail --silent \
  https://api.liveauth.app/api/public/costshield/.well-known/jwks.json
```

Confirm that it returns one RS256 key with the expected `kid`. Restart the API
once and confirm the `kid`, modulus, and exponent are unchanged. A change after
restart means the persistent key was not loaded.

Exercise a TEST project end to end:

1. Create a challenge.
2. Solve and complete the challenge.
3. Verify the returned token with each server SDK.
4. Consume a single-use authorization.
5. Confirm a second consumption is rejected.
6. Confirm excessive completion attempts return HTTP 429 with `Retry-After`.

## 5. Key rotation

The current v0.1 API publishes one active signing key. Replacing it immediately
invalidates outstanding tokens signed by the previous key; their configured
maximum lifetime is one hour.

For an emergency compromise, rotate immediately and accept that invalidation.
Before routine zero-downtime rotation, add overlapping JWKS support so the old
public key remains available until every old token has expired.

## 6. Package release gate

Only publish `@liveauth/sdk` and `LiveAuth.CostShield.AspNetCore` after:

- production startup succeeds with the persistent key;
- JWKS remains stable across a restart;
- the TEST end-to-end flow succeeds;
- npm and NuGet packages pass fresh-install tests from their packed artifacts;
- all previously committed credentials have been rotated.
