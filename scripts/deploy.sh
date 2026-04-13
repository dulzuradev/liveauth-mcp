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

# Build frontend (doesn't need Docker)
echo "Building frontend..."
cd $LOCAL_DIR/LiveAuthWeb
npm run build

# Copy docs into the Angular dist output (Caddy serves /srv/docs)
mkdir -p dist/liveauth-web/docs
cp -r $LOCAL_DIR/docs/* dist/liveauth-web/docs/

# Build admin panel (separate Angular project)
echo "Building admin panel..."
cd $LOCAL_DIR/liveauth-admin
npm run build

# Copy admin build to main web dist under liveauth-admin/ for syncing
mkdir -p $LOCAL_DIR/LiveAuthWeb/dist/liveauth-admin
cp -r dist/liveauth-admin/* $LOCAL_DIR/LiveAuthWeb/dist/liveauth-admin/

# Angular 19 build outputs: index.html + JS/CSS chunks in browser/ subdirectory.
# Caddy serves from /srv/ (flat), so we must flatten: copy browser/ contents up to root.
# This fixes the "missing chunk -> falls back to index.html" loop that broke JS.
flatten_dir() {
    local browser="$1/browser"
    local dest="$1"
    if [[ -d "$browser" ]]; then
        echo "Flattening $browser -> $dest..."
        # Copy all files (skip if same name exists as subdir)
        for f in "$browser"/*; do
            [[ -f "$f" ]] && cp -p "$f" "$dest/" 2>/dev/null || true
            [[ -d "$f" && ! -e "$dest/$(basename "$f")" ]] && cp -r "$f" "$dest/" 2>/dev/null || true
        done
    fi
}

flatten_dir "dist/liveauth-web"
# The admin was copied above, flatten its browser/ output too
flatten_dir "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-admin"

# Sync to server using atomic swap
echo "Syncing web files to server..."
ssh "$SERVER" "rm -rf $REMOTE_DIR/LiveAuthWeb/dist-new 2>/dev/null || true"
rsync -avz --delete \
    "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/" "$SERVER:$REMOTE_DIR/LiveAuthWeb/dist-new/"

# Copy admin panel from its build output (flat, already flattened above)
ADMIN_SRC="$LOCAL_DIR/LiveAuthWeb/dist/liveauth-admin"
if [[ -d "$ADMIN_SRC" ]]; then
    echo "Syncing admin panel..."
    ssh "$SERVER" "mkdir -p $REMOTE_DIR/LiveAuthWeb/dist-new/liveauth-admin"
    rsync -avz --delete "$ADMIN_SRC/" "$SERVER:$REMOTE_DIR/LiveAuthWeb/dist-new/liveauth-admin/"
fi

# Copy docs (served at /srv/docs)
if [[ -d "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/docs" ]]; then
    echo "Syncing docs..."
    ssh "$SERVER" "mkdir -p $REMOTE_DIR/LiveAuthWeb/dist-new/docs"
    rsync -avz --delete "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/docs/" "$SERVER:$REMOTE_DIR/LiveAuthWeb/dist-new/docs/"
fi

# Also copy demo.html if present
if [[ -f "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/demo.html" ]]; then
    rsync -avz "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/demo.html" "$SERVER:$REMOTE_DIR/LiveAuthWeb/dist-new/"
fi

# Atomic swap: old -> dist-old, new -> dist
# Note: rm may fail if Caddy holds files open; that's OK — the mv is the critical part
ssh "$SERVER" "
mv $REMOTE_DIR/LiveAuthWeb/dist $REMOTE_DIR/LiveAuthWeb/dist-old 2>/dev/null || true
mv $REMOTE_DIR/LiveAuthWeb/dist-new $REMOTE_DIR/LiveAuthWeb/dist
" 2>/dev/null || ssh "$SERVER" "mv $REMOTE_DIR/LiveAuthWeb/dist-new $REMOTE_DIR/LiveAuthWeb/dist"

# Sync Caddyfile (if changed)
echo "Syncing Caddyfile..."
rsync -avz "$LOCAL_DIR/Caddyfile" "$SERVER:$REMOTE_DIR/"

# Reload Caddy (no restart needed for Caddyfile changes)
echo "Reloading Caddy..."
ssh "$SERVER" "docker exec liveauth-caddy caddy reload --config /etc/caddy/Caddyfile 2>/dev/null || docker restart liveauth-caddy"

echo "=== Done! ==="
