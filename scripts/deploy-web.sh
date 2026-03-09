#!/bin/bash
# Safer deploy script for LiveAuth
# Usage: ./deploy.sh

set -e

SERVER="liveauth@64.22532.102"
LOCAL_DIR="/users/sydney/.openclaw/workspace/LiveAuth"

echo "=== LiveAuth Deploy Script ==="

# Check if running from correct directory
if [[ ! -d "$LOCAL_DIR/LiveAuthWeb" ]]; then
    echo "Error: Run from repo root"
    exit 1
fi

# Build frontend
echo "Building frontend..."
cd $LOCAL_DIR/LiveAuthWeb
npm run build

# Copy demo to browser folder
cp $LOCAL_DIR/docs/demo.html dist/liveauth-web/browser/demo.html

# Sync only the web app (NOT the whole /opt/liveauth)
echo "Syncing web files..."
rsync -avz --progress dist/liveauth-web/browser/ $SERVER:/opt/liveauth/LiveAuthWeb/dist/liveauth-web/

# Copy to Caddy container (safely)
echo "Copying to container..."
ssh $SERVER "docker cp /opt/liveauth/LiveAuthWeb/dist/liveauth-web/. liveauth-caddy:/srv/browser/"

# Sync docs
echo "Syncing docs..."
rsync -avz --progress $LOCAL_DIR/docs/ $SERVER:/opt/liveauth/docs/

ssh $SERVER "docker cp /opt/liveauth/docs/. liveauth-caddy:/srv/browser/docs/"

echo "=== Done! ==="
echo "Sites should be live now."
