#!/bin/bash
# Full deploy script for LiveAuth
# Usage: ./scripts/deploy.sh

set -e

SERVER="liveauth@64.225.32.102"
LOCAL_DIR="/users/sydney/.openclaw/workspace/LiveAuth"

echo "=== LiveAuth Deploy Script ==="

# Check if running from correct directory
if [[ ! -d "$LOCAL_DIR/LiveAuthCore" ]]; then
    echo "Error: Run from repo root"
    exit 1
fi

# Build and publish .NET
echo "Building API..."
cd $LOCAL_DIR/LiveAuthCore
dotnet publish -c Release -o /tmp/liveauth-publish

# Sync API (only publish folder, NOT root)
echo "Syncing API..."
rsync -avz --progress /tmp/liveauth-publish/ $SERVER:/opt/liveauth/

# Restart API
echo "Restarting API..."
ssh $SERVER "docker restart liveauth-api"

# Build frontend
echo "Building frontend..."
cd $LOCAL_DIR/LiveAuthWeb
npm run build

# Copy demo to browser folder
cp $LOCAL_DIR/docs/demo.html dist/liveauth-web/browser/demo.html

# Sync web files (NEVER --delete on root)
echo "Syncing web files..."
rsync -avz --progress dist/liveauth-web/browser/ $SERVER:/opt/liveauth/LiveAuthWeb/dist/liveauth-web/

# Copy to Caddy container
echo "Copying to container..."
ssh $SERVER "docker cp /opt/liveauth/LiveAuthWeb/dist/liveauth-web/. liveauth-caddy:/srv/browser/"

# Reload Caddy
echo "Reloading Caddy..."
ssh $SERVER "docker exec liveauth-caddy caddy reload --config /etc/caddy/Caddyfile"

echo "=== Done! ==="
