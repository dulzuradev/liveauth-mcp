#!/bin/bash
# Full deploy script for LiveAuth (bare-metal Caddy + dotnet)
# Works without Docker - uses Caddy as system package
# Usage: ./scripts/deploy.sh

set -e

SERVER="liveauth@64.225.32.102"
LOCAL_DIR="/users/sydney/.openclaw/workspace/LiveAuth"
REMOTE_WEB_DIR="/srv"

echo "=== LiveAuth Deploy Script ==="

# Check if running from correct directory
if [[ ! -d "$LOCAL_DIR/LiveAuthCore" ]]; then
    echo "Error: Run from repo root"
    exit 1
fi

# Run pre-deploy checklist (catch config issues before building)
echo "Running deploy checklist..."
if ! bash "$LOCAL_DIR/scripts/deploy-checklist.sh" 2>/dev/null; then
    echo ""
    echo "WARNING: Deploy checklist had issues (continuing anyway)..."
fi

# Build frontend
echo "Building frontend..."
cd $LOCAL_DIR/LiveAuthWeb
npm run build

# Build admin panel
echo "Building admin panel..."
cd $LOCAL_DIR/liveauth-admin
npm run build

# Flatten and prepare dist locally
FLAT_DIR="/tmp/liveauth-deploy-$$"
mkdir -p "$FLAT_DIR"

# Flatten main web app: browser/* → root
for f in "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/browser/"*; do
    [[ -f "$f" ]] && cp -p "$f" "$FLAT_DIR/" 2>/dev/null || true
    [[ -d "$f" ]] && cp -r "$f" "$FLAT_DIR/" 2>/dev/null || true
done

# Copy demo.html if present
[[ -f "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/demo.html" ]] && \
    cp "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/demo.html" "$FLAT_DIR/"

# Flatten admin panel: browser/* → liveauth-admin/
mkdir -p "$FLAT_DIR/liveauth-admin"
for f in "$LOCAL_DIR/liveauth-admin/dist/liveauth-admin/browser/"*; do
    [[ -f "$f" ]] && cp -p "$f" "$FLAT_DIR/liveauth-admin/" 2>/dev/null || true
    [[ -d "$f" ]] && cp -r "$f" "$FLAT_DIR/liveauth-admin/" 2>/dev/null || true
done

# Flatten docs: browser/docs/* → docs/
mkdir -p "$FLAT_DIR/docs"
if [[ -d "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/browser/docs" ]]; then
    cp -r "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/browser/docs/"* "$FLAT_DIR/docs/" 2>/dev/null || true
fi

echo "Flattened build prepared at $FLAT_DIR"

# Sync to server using atomic swap
echo "Syncing web files to server..."
ssh "$SERVER" "sudo rm -rf /tmp/srv-new 2>/dev/null || true"
rsync -avz --delete "$FLAT_DIR/" "$SERVER:/tmp/srv-new/"

# Atomic swap on server: current /srv → /srv-old, new → /srv
echo "Performing atomic swap..."
ssh "$SERVER" "
    sudo rm -rf /srv-old && sudo mv /srv /srv-old && sudo mv /tmp/srv-new /srv && \
    sudo chown -R root:root /srv && \
    sudo chmod -R 755 /srv && \
    sudo chmod 755 /srv/docs && \
    echo 'Atomic swap complete'
"

# Cleanup local flatten dir
rm -rf "$FLAT_DIR"

# Sync Caddyfile (the one in the repo has correct /srv paths, not /srv/browser)
echo "Syncing Caddyfile..."
rsync -avz "$LOCAL_DIR/caddy/Caddyfile" "$SERVER:/tmp/Caddyfile"
ssh "$SERVER" "sudo cp /tmp/Caddyfile /etc/caddy/Caddyfile"

# Reload Caddy (pkill + start as systemd or user process)
echo "Reloading Caddy..."
ssh "$SERVER" "
    # Try systemd first
    sudo systemctl reload caddy 2>/dev/null && echo 'Caddy reloaded via systemd' && exit 0
    # Fallback: pkill and restart as root caddy process
    sudo pkill caddy 2>/dev/null || true
    sleep 2
    sudo caddy run --config /etc/caddy/Caddyfile &
    echo 'Caddy started as background process'
"

# Quick verification
echo "Verifying sites..."
sleep 3
HTTP_LIVE=$(curl -s -o /dev/null -w "%{http_code}" https://liveauth.app/ 2>/dev/null || echo "000")
HTTP_ADMIN=$(curl -s -o /dev/null -w "%{http_code}" https://admin.liveauth.app/ 2>/dev/null || echo "000")
HTTP_API=$(curl -s -o /dev/null -w "%{http_code}" https://api.liveauth.app/api/health 2>/dev/null || echo "000")

echo "  liveauth.app: $HTTP_LIVE"
echo "  admin.liveauth.app: $HTTP_ADMIN"
echo "  api.liveauth.app: $HTTP_API"

if [[ "$HTTP_LIVE" == "200" && "$HTTP_ADMIN" == "200" ]]; then
    echo ""
    echo "=== Deploy successful! ==="
else
    echo ""
    echo "=== Deploy completed with warnings (check sites manually) ==="
fi
