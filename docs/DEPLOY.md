# LiveAuth Deployment Guide

## Quick Deploy (One Command)

From your local machine, in the repo root:

```bash
cd /users/sydney/.openclaw/workspace/LiveAuth
./scripts/deploy.sh
```

That's it. The script handles:
1. Building the API image locally
2. Pushing to server
3. Building frontend
4. Syncing files
5. Restarting services via docker-compose

## URLs

| Site | URL |
|------|-----|
| Main App | https://liveauth.app |
| Demo | https://liveauth.app/demo |
| API | https://api.liveauth.app |
| Docs | https://docs.liveauth.app |

## Verify Deployment

```bash
# Check all public sites
curl -s -o /dev/null -w "%{http_code}" https://liveauth.app/
curl -s -o /dev/null -w "%{http_code}" https://liveauth.app/demo
curl -s -o /dev/null -w "%{http_code}" https://docs.liveauth.app/
curl -s -o /dev/null -w "%{http_code}" https://api.liveauth.app/api/health
```

## Manual Deploy (If Script Fails)

### Option 1: Just Frontend

```bash
# Build locally
cd LiveAuthWeb && npm run build

# Sync to server (uses temp dir to avoid permissions)
rsync -avz --delete dist/liveauth-web/ liveauth@64.225.32.102:/tmp/liveauth-web/
ssh liveauth@64.225.32.102 "rm -rf /opt/liveauth/LiveAuthWeb/dist-old && mv /opt/liveauth/LiveAuthWeb/dist /opt/liveauth/LiveAuthWeb/dist-old && mv /tmp/liveauth-web /opt/liveauth/LiveAuthWeb/dist"

# Reload Caddy
ssh liveauth@64.225.32.102 "docker exec liveauth-caddy caddy reload"
```

### Option 2: Just API

```bash
# Build and push image
docker build -t liveauth-api:latest ./LiveAuthCore
docker save liveauth-api:latest | ssh liveauth@64.225.32.102 "docker load"

# Restart container
ssh liveauth@64.225.32.102 "docker compose -f /opt/liveauth/docker-compose.yml restart liveauth-api"
```

## Server Info

- **IP:** 64.225.32.102
- **SSH:** `ssh liveauth@64.225.32.102`
- **Docker:** Services managed via `/opt/liveauth/docker-compose.yml`

### Check Services

```bash
ssh liveauth@64.225.32.102 "docker ps"
```

## Config

All environment variables are in `docker-compose.yml`:
- GitHub OAuth credentials
- LND settings
- JWT signing key
- Demo project ID

After editing `docker-compose.yml`, redeploy with `./scripts/deploy.sh` or:

```bash
ssh liveauth@64.225.32.102 "cd /opt/liveauth && docker compose up -d"
```
