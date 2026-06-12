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

# Build backend API
echo "Building backend API..."
cd "$LOCAL_DIR/LiveAuthCore"
dotnet publish -c Release -r linux-x64 --self-contained false -o /tmp/liveauth-x64 2>&1 | tail -3

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
ssh "$SERVER" 'bash -s' <<'REMOTE'
    # Use restart (not reload) — works even when admin endpoint is disabled
    if sudo systemctl restart caddy 2>/dev/null; then
        echo "Caddy restarted via systemd"
        exit 0
    fi
    # Last-resort fallback: pkill any orphan caddy and start detached
    sudo pkill -9 caddy 2>/dev/null || true
    sleep 2
    LOG=~/caddy-deploy.log
    sudo nohup setsid caddy run --config /etc/caddy/Caddyfile > "$LOG" 2>&1 < /dev/null &
    disown
    echo "Caddy started as detached process (log: $LOG)"
REMOTE

# Quick verification (web only — API is verified later)
echo "Verifying web sites..."
sleep 3
HTTP_LIVE=$(curl -s -o /dev/null -w "%{http_code}" https://liveauth.app/ 2>/dev/null || echo "000")
HTTP_ADMIN=$(curl -s -o /dev/null -w "%{http_code}" https://admin.liveauth.app/ 2>/dev/null || echo "000")

echo "  liveauth.app: $HTTP_LIVE"
echo "  admin.liveauth.app: $HTTP_ADMIN"

if [[ "$HTTP_LIVE" != "200" || "$HTTP_ADMIN" != "200" ]]; then
    echo ""
    echo "=== Web deploy failed — aborting before API restart ==="
    exit 1
fi

# === Backend API deploy ===
# Sync the freshly-built DLL to /opt/liveauth on the server.
echo "Syncing backend DLL..."
rsync -avz /tmp/liveauth-x64/LiveAuthCore.dll "$SERVER:/opt/liveauth/LiveAuthCore.dll" 2>&1 | tail -2

# Find the running API by port 8081 (avoid pkill -f gotcha: the env string in
# our SSH session contains "LiveAuthCore", and the absolute-path pattern fails
# when the process is launched as ./LiveAuthCore — we get the relative path in
# ps). Port-based PID lookup is the only reliable way.
echo "Restarting API..."
OLD_PID=$(ssh "$SERVER" "ss -tlnp 2>/dev/null | grep ':8081' | grep -oP 'pid=\\K[0-9]+' | head -1" 2>/dev/null || true)
if [ -n "$OLD_PID" ]; then
    ssh "$SERVER" "kill -9 $OLD_PID" 2>/dev/null || true
    echo "  Stopped old API (PID $OLD_PID)"
    # Wait for the port to free up
    for i in 1 2 3 4 5; do
        sleep 1
        STILL=$(ssh "$SERVER" "ss -tlnp 2>/dev/null | grep -c ':8081'" 2>/dev/null || echo "0")
        if [ "$STILL" = "0" ]; then break; fi
    done
fi

# Start the new API as a detached process. Writes to /tmp/liveauth-new.log on
# the server (liveauth-writable; the legacy /tmp/liveauth-prod.log is
# root-owned and the redirect silently fails when run as the liveauth user).
ssh "$SERVER" "cd /opt/liveauth && nohup env \
    ASPNETCORE_URLS='http://0.0.0.0:8081' \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__Default='Data Source=/opt/liveauth/.liveauth.db' \
    LiveAuth__PowHmacSecret='swm3lIZ+arLWaU4Uz9zaUpzUbdV87O7p72Foo6RLtGstLmyeA3bNedhZenBn3H4t8n1IzsToxOVyaL1ILcFOtA==' \
    Resend__ApiKey='re_P4qnqHQM_M5tNYHX3Ar4TfjJLGBc7uxez' \
    Resend__FromEmail='admin@liveauth.app' \
    Resend__FromName='LiveAuth' \
    Lnd__BaseUrl='https://localhost:8080' \
    Lnd__UseMock='false' \
    ./LiveAuthCore > /tmp/liveauth-new.log 2>&1 < /dev/null & disown"

# Wait up to 10s for the new process to bind 8081
NEW_PID=""
for i in 1 2 3 4 5 6 7 8 9 10; do
    sleep 1
    NEW_PID=$(ssh "$SERVER" "ss -tlnp 2>/dev/null | grep ':8081' | grep -oP 'pid=\\K[0-9]+' | head -1" 2>/dev/null || true)
    if [ -n "$NEW_PID" ]; then break; fi
done

if [ -z "$NEW_PID" ]; then
    echo ""
    echo "=== API FAILED TO START — check /tmp/liveauth-new.log on the server ==="
    exit 1
fi

echo "  API running as PID $NEW_PID"

# Verify the running DLL matches the build we just shipped (catches rsync
# issues and any caching weirdness)
REMOTE_HASH=$(ssh "$SERVER" "md5sum /opt/liveauth/LiveAuthCore.dll" 2>/dev/null | awk '{print $1}')
LOCAL_HASH=$(md5sum /tmp/liveauth-x64/LiveAuthCore.dll 2>/dev/null | awk '{print $1}')
if [ "$REMOTE_HASH" = "$LOCAL_HASH" ] && [ -n "$REMOTE_HASH" ]; then
    echo "  DLL hash verified: $REMOTE_HASH"
else
    echo ""
    echo "=== DLL HASH MISMATCH — remote=$REMOTE_HASH local=$LOCAL_HASH ==="
    exit 1
fi

echo ""
echo "=== Full deploy successful (web + API) ==="
