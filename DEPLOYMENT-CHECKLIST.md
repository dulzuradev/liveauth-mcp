# Deployment Checklist

For CostShield production key provisioning, credential rotation, and release
verification, also complete
[`docs/COSTSHIELD-PRODUCTION.md`](docs/COSTSHIELD-PRODUCTION.md).

## Pre-Deploy Verification

Before running any deploy commands, verify these items:

### 1. Demo Links Check
```bash
# Check Angular landing page
grep -o 'docs.liveauth.app/demo[^"]*' LiveAuthWeb/dist/liveauth-web/browser/main-*.js | sort -u

# Check static docs
grep -o 'docs.liveauth.app/demo[^"]*' docs/index.html

# Verify /demo redirect works
curl -sI https://docs.liveauth.app/demo | head -1
```

Expected: All links should point to `/demo.html` (not `/demo`)

### 2. Build Test
```bash
cd LiveAuthWeb && npm run build
```

### 3. Docker Services Health
```bash
docker ps --format "table {{.Names}}\t{{.Status}}"
docker logs liveauth-api --tail 10
docker logs liveauth-caddy --tail 5
```

## Deploy Commands

### Frontend (liveauth.app)
```bash
cd LiveAuthWeb
npm run build
rsync -avz --exclude '*.map' --exclude '*.gz' -e ssh dist/liveauth-web/browser/ liveauth@64.225.32.102:/opt/liveauth/LiveAuthWeb/dist/liveauth-web/browser/
```

### Static Docs (docs.liveauth.app)
```bash
rsync -avz -e ssh docs/index.html docs/demo.html liveauth@64.225.32.102:/opt/liveauth/LiveAuthWeb/dist/liveauth-web/browser/docs/
```

### Reload Caddy
```bash
ssh liveauth@64.225.32.102 "docker exec liveauth-caddy caddy reload --config /etc/caddy/Caddyfile"
```

## Post-Deploy Verification

```bash
# Verify liveauth.app demo links
curl -s https://liveauth.app/ | grep -o 'docs.liveauth.app/demo[^"]*'

# Verify docs.liveauth.app demo links
curl -s https://docs.liveauth.app/ | grep -o 'docs.liveauth.app/demo[^"]*'

# Verify /demo redirect works
curl -sI https://docs.liveauth.app/demo | head -1

# Verify demo page loads with Lightning tab
curl -s https://docs.liveauth.app/demo.html | grep -q "Lightning" && echo "✅ Lightning tab present"
```

## Known Issues & Fixes

### Issue: Two demo files
- `docs/demo.html` ✅ (has Lightning tab)
- `docs/demo/` ❌ (PoW only - DELETE THIS)

Fix: Delete `docs/demo/` folder, add Caddy redirect /demo → /demo.html

### Issue: Demo links point to wrong URL
- Should point to: `https://docs.liveauth.app/demo.html`
- Was pointing to: `https://docs.liveauth.app/demo`

Fix: Update Angular landing-page.html and docs/index.html
