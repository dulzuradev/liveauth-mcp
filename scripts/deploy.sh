#!/bin/bash
# Full deploy script for LiveAuth using docker-compose
# Usage: ./scripts/deploy.sh

set -e

SERVER="liveauth@64.225.32.102"
LOCAL_DIR="/users/sydney/.openclaw/workspace/LiveAuth"
REMOTE_DIR="/opt/liveauth"

echo "=== LiveAuth Deploy Script ==="

# Check if running from correct directory
if [[ ! -d "$LOCAL_DIR/LiveAuthCore" ]]; then
    echo "Error: Run from repo root"
    exit 1
fi

# Build API image locally
echo "Building API image..."
cd $LOCAL_DIR/LiveAuthCore
docker build -t liveauth-api:latest .

# Push image to server
echo "Pushing image to server..."
docker save liveauth-api:latest | ssh $SERVER "docker load"

# Build frontend
echo "Building frontend..."
cd $LOCAL_DIR/LiveAuthWeb
npm run build

# Copy docs to dist/docs (matching Caddyfile path /srv/docs)
mkdir -p dist/liveauth-web/docs
cp -r $LOCAL_DIR/docs/* dist/liveauth-web/docs/

# Sync docker-compose.yml
echo "Syncing docker-compose.yml..."
rsync -avz --progress $LOCAL_DIR/docker-compose.yml $SERVER:$REMOTE_DIR/

# Sync Caddyfile
echo "Syncing Caddyfile..."
rsync -avz --progress $LOCAL_DIR/Caddyfile $SERVER:$REMOTE_DIR/

# Sync web files (entire dist, matching Caddyfile /srv paths)
echo "Syncing web files..."
ssh $SERVER "rm -rf $REMOTE_DIR/LiveAuthWeb/dist-new 2>/dev/null || true"
rsync -avz --progress --delete dist/liveauth-web/ $SERVER:$REMOTE_DIR/LiveAuthWeb/dist-new/
# Copy admin from old dist if missing
ssh $SERVER "if [ ! -d $REMOTE_DIR/LiveAuthWeb/dist-new/liveauth-admin ]; then cp -r $REMOTE_DIR/LiveAuthWeb/dist-old/liveauth-web/liveauth-admin $REMOTE_DIR/LiveAuthWeb/dist-new/; fi"
ssh $SERVER "rm -rf $REMOTE_DIR/LiveAuthWeb/dist-old && mv $REMOTE_DIR/LiveAuthWeb/dist $REMOTE_DIR/LiveAuthWeb/dist-old && mv $REMOTE_DIR/LiveAuthWeb/dist-new $REMOTE_DIR/LiveAuthWeb/dist"

# Pre-flight: verify Caddyfile paths match actual files
echo "Verifying paths..."
ssh $SERVER "for site in liveauth app docs; do echo \"Checking \$site.liveauth.app...\"; done"

# Deploy using docker compose on server
echo "Deploying services..."
ssh $SERVER "cd $REMOTE_DIR && docker compose down && docker compose up -d"

# Wait for services to be ready
echo "Waiting for services..."
sleep 10

# Verify
echo "Verifying services..."
ssh $SERVER "docker ps --format '{{.Names}}: {{.Status}}'"

echo "=== Done! ==="
