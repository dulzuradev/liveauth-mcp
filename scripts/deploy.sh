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

# Build frontend FIRST (doesn't need Docker on this machine)
echo "Building frontend..."
cd $LOCAL_DIR/LiveAuthWeb
npm run build

# Copy docs into the Angular dist output (Caddy serves /srv/docs)
mkdir -p dist/liveauth-web/docs
cp -r $LOCAL_DIR/docs/* dist/liveauth-web/docs/

# Angular 19 build outputs: index.html + JS/CSS chunks in browser/ subdirectory.
# Caddy serves from /srv/ (flat), so we must flatten: copy browser/ contents up to root.
# This fixes the "missing chunk -> falls back to index.html" loop that broke JS.
flatten_dir() {
    local dir="$1"
    local browser="$dir/browser"
    if [[ -d "$browser" ]]; then
        echo "Flattening $dir/browser/ -> $dir/..."
        cp "$browser"/*.js "$dir/" 2>/dev/null || true
        cp "$browser"/*.css "$dir/" 2>/dev/null || true
        cp "$browser"/*.ico "$dir/" 2>/dev/null || true
        cp "$browser"/index.html "$dir/" 2>/dev/null || true
        cp "$browser"/3rdpartylicenses.txt "$dir/" 2>/dev/null || true
        cp "$browser"/prerendered-routes.json "$dir/" 2>/dev/null || true
        cp -r "$browser"/assets "$dir/" 2>/dev/null || true
        cp -r "$browser"/media "$dir/" 2>/dev/null || true
    fi
}

flatten_dir "dist/liveauth-web"
flatten_dir "dist/liveauth-web/liveauth-admin"

# Sync to server using atomic swap
echo "Syncing web files to server..."
ssh "$SERVER" "rm -rf $REMOTE_DIR/LiveAuthWeb/dist-new 2>/dev/null || true"
rsync -avz --delete \
    --exclude='liveauth-web' \
    --exclude='browser' \
    dist/liveauth-web/ "$SERVER:$REMOTE_DIR/LiveAuthWeb/dist-new/"

# Copy admin panel (it lives in dist/liveauth-web/liveauth-admin/ in the new build)
if [[ -d "dist/liveauth-web/liveauth-admin" ]]; then
    echo "Syncing admin panel..."
    ssh "$SERVER" "mkdir -p $REMOTE_DIR/LiveAuthWeb/dist-new/liveauth-admin"
    rsync -avz --delete \
        dist/liveauth-web/liveauth-admin/ "$SERVER:$REMOTE_DIR/LiveAuthWeb/dist-new/liveauth-admin/"
fi

# Copy docs (served at /srv/docs)
if [[ -d "dist/liveauth-web/docs" ]]; then
    echo "Syncing docs..."
    ssh "$SERVER" "mkdir -p $REMOTE_DIR/LiveAuthWeb/dist-new/docs"
    rsync -avz --delete \
        dist/liveauth-web/docs/ "$SERVER:$REMOTE_DIR/LiveAuthWeb/dist-new/docs/"
fi

# Also copy demo.html if present
if [[ -f "dist/liveauth-web/demo.html" ]]; then
    rsync -avz dist/liveauth-web/demo.html "$SERVER:$REMOTE_DIR/LiveAuthWeb/dist-new/"
fi

# Atomic swap: old -> dist-old, new -> dist
ssh "$SERVER" "rm -rf $REMOTE_DIR/LiveAuthWeb/dist-old && mv $REMOTE_DIR/LiveAuthWeb/dist $REMOTE_DIR/LiveAuthWeb/dist-old && mv $REMOTE_DIR/LiveAuthWeb/dist-new $REMOTE_DIR/LiveAuthWeb/dist"

# Sync Caddyfile (if changed)
echo "Syncing Caddyfile..."
rsync -avz "$LOCAL_DIR/Caddyfile" "$SERVER:$REMOTE_DIR/"

# Reload Caddy (no restart needed for Caddyfile changes)
echo "Reloading Caddy..."
ssh "$SERVER" "docker exec liveauth-caddy caddy reload --config /etc/caddy/Caddyfile 2>/dev/null || docker restart liveauth-caddy"

echo "=== Done! ==="
