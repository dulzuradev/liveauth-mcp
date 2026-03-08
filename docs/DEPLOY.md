# LiveAuth Deployment Guide

## Server Structure

```
/opt/liveauth/
├── LiveAuthWeb/dist/liveauth-web/     # Main app (liveauth.app)
│   └── browser/                      # Angular build output
│       ├── index.html                # SPA entry
│       └── demo.html                 # PoW demo page
│
├── LiveAuthAdmin/                    # Admin panel (admin.liveauth.app)
│   └── browser/                      # Angular build output
│
├── docs/                            # Static docs
│
└── Caddyfile                        # Web server config
```

## URLs

| Site | URL | Source |
|------|-----|--------|
| Main App | https://liveauth.app | `/srv/browser` (from LiveAuthWeb build) |
| Demo | https://liveauth.app/demo.html | `/srv/browser/demo.html` |
| Admin | https://admin.liveauth.app | `/srv/liveauth-admin` |
| API | https://api.liveauth.app | Docker: `liveauth-api:8080` |
| Docs | https://docs.liveauth.app | `/srv/browser/docs` |

## Building & Deploying

### 1. Build Frontend (Local)
```bash
cd LiveAuthWeb
npm install && npm run build
# Output: dist/liveauth-web/browser/
```

### 2. Build Admin (Local)
```bash
cd LiveAuthAdmin
npm install && npm run build
# Output: LiveAuthAdmin/browser/
```

### 3. Deploy to Server
```bash
# Sync main app
rsync -avz LiveAuthWeb/dist/liveauth-web/ liveauth@64.225.32.102:/opt/liveauth/LiveAuthWeb/dist/liveauth-web/

# Copy admin build
scp -r LiveAuthAdmin/browser/* liveauth@64.225.32.102:/opt/liveauth/LiveAuthAdmin/

# Sync docs
rsync -avz docs/ liveauth@64.225.32.102:/opt/liveauth/docs/
```

### 4. Update Static Files (Inside Container)
```bash
# Copy built files to container mount points
docker cp LiveAuthWeb/dist/liveauth-web/browser/* liveauth-caddy:/srv/browser/
docker cp LiveAuthAdmin/browser/* liveauth-caddy:/srv/liveauth-admin/
docker cp docs/* liveauth-caddy:/srv/browser/docs/
```

### 5. Reload Caddy
```bash
ssh liveauth@64.225.32.102 "docker exec liveauth-caddy caddy reload --config /etc/caddy/Caddyfile"
```

## Caddyfile Locations

The Caddyfile maps domains to static file directories:
- `liveauth.app` → `/srv/browser`
- `admin.liveauth.app` → `/srv/liveauth-admin`  
- `docs.liveauth.app` → `/srv/browser/docs`
- `api.liveauth.app` → `liveauth-api:8080` (reverse proxy)

## Common Issues

### 404 on new route
- Angular SPA: check `try_files {path} /index.html` in Caddyfile
- Static file: verify file exists in correct directory inside container

### Demo page shows Angular
- Demo must be at `/srv/browser/demo.html` (not in `/browser/` subfolder)

### Admin 404
- Verify files at `/srv/liveauth-admin/` inside container
- Check Caddyfile has correct `root` path

## Quick Deploy Script

```bash
#!/bin/bash
# deploy.sh - Run from repo root

SERVER="liveauth@64.225.32.102"

echo "Building..."
cd LiveAuthWeb && npm run build
cd ../LiveAuthAdmin && npm run build
cd ..

echo "Syncing files..."
rsync -avz --delete LiveAuthWeb/dist/liveauth-web/ $SERVER:/opt/liveauth/LiveAuthWeb/dist/liveauth-web/
scp -r LiveAuthAdmin/browser/* $SERVER:/opt/liveauth/LiveAuthAdmin/
rsync -avz --delete docs/ $SERVER:/opt/liveauth/docs/

echo "Updating container..."
ssh $SERVER "docker cp /opt/liveauth/LiveAuthWeb/dist/liveauth-web/browser/* liveauth-caddy:/srv/browser/ && \
  docker cp /opt/liveauth/LiveAuthAdmin/browser/* liveauth-caddy:/srv/liveauth-admin/ && \
  docker exec liveauth-caddy caddy reload --config /etc/caddy/Caddyfile"

echo "Done!"
```
