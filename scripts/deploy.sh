#!/bin/bash
# Full bare-metal deployment for LiveAuth.
# Production secrets are loaded on the server from /opt/liveauth/liveauth.env.

set -euo pipefail

SERVER="${LIVEAUTH_DEPLOY_TARGET:-liveauth@64.225.32.102}"
REMOTE_WEB_DIR="/srv"
REMOTE_APP_DIR="/opt/liveauth"
REMOTE_ENV_FILE="${LIVEAUTH_REMOTE_ENV_FILE:-/opt/liveauth/liveauth.env}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOCAL_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
BACKEND_DIR="$(mktemp -d /tmp/liveauth-backend.XXXXXX)"
FLAT_DIR="$(mktemp -d /tmp/liveauth-web.XXXXXX)"

cleanup() {
    rm -rf "$BACKEND_DIR" "$FLAT_DIR"
}
trap cleanup EXIT

echo "=== LiveAuth Deploy Script ==="

if [[ ! -d "$LOCAL_DIR/LiveAuthCore" ]]; then
    echo "Error: LiveAuthCore was not found under $LOCAL_DIR"
    exit 1
fi

echo "Checking remote production environment..."
ssh "$SERVER" "test -r '$REMOTE_ENV_FILE'"

echo "Building backend API..."
dotnet publish "$LOCAL_DIR/LiveAuthCore/LiveAuthCore.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o "$BACKEND_DIR"

echo "Building frontend..."
npm --prefix "$LOCAL_DIR/LiveAuthWeb" run build

echo "Building admin panel..."
npm --prefix "$LOCAL_DIR/liveauth-admin" run build

for file in "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/browser/"*; do
    [[ -f "$file" ]] && cp -p "$file" "$FLAT_DIR/"
    [[ -d "$file" ]] && cp -R "$file" "$FLAT_DIR/"
done

if [[ -f "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/demo.html" ]]; then
    cp "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/demo.html" "$FLAT_DIR/"
fi

mkdir -p "$FLAT_DIR/liveauth-admin"
for file in "$LOCAL_DIR/liveauth-admin/dist/liveauth-admin/browser/"*; do
    [[ -f "$file" ]] && cp -p "$file" "$FLAT_DIR/liveauth-admin/"
    [[ -d "$file" ]] && cp -R "$file" "$FLAT_DIR/liveauth-admin/"
done

mkdir -p "$FLAT_DIR/docs"
if [[ -d "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/browser/docs" ]]; then
    cp -R "$LOCAL_DIR/LiveAuthWeb/dist/liveauth-web/browser/docs/." \
        "$FLAT_DIR/docs/"
fi

echo "Syncing web files..."
ssh "$SERVER" "sudo rm -rf /tmp/srv-new"
rsync -avz --delete "$FLAT_DIR/" "$SERVER:/tmp/srv-new/"
ssh "$SERVER" "
    sudo rm -rf /srv-old
    sudo mv '$REMOTE_WEB_DIR' /srv-old
    sudo mv /tmp/srv-new '$REMOTE_WEB_DIR'
    sudo chown -R root:root '$REMOTE_WEB_DIR'
    sudo chmod -R 755 '$REMOTE_WEB_DIR'
"

echo "Syncing Caddy configuration..."
rsync -avz "$LOCAL_DIR/caddy/Caddyfile" "$SERVER:/tmp/Caddyfile"
ssh "$SERVER" "sudo cp /tmp/Caddyfile /etc/caddy/Caddyfile"
ssh "$SERVER" "sudo systemctl restart caddy"

echo "Verifying web sites..."
HTTP_LIVE="$(curl -s -o /dev/null -w "%{http_code}" https://liveauth.app/)"
HTTP_ADMIN="$(curl -s -o /dev/null -w "%{http_code}" https://admin.liveauth.app/)"
if [[ "$HTTP_LIVE" != "200" || "$HTTP_ADMIN" != "200" ]]; then
    echo "Web verification failed: live=$HTTP_LIVE admin=$HTTP_ADMIN"
    exit 1
fi

echo "Syncing backend..."
rsync -avz "$BACKEND_DIR/" "$SERVER:$REMOTE_APP_DIR/"

echo "Restarting API..."
ssh "$SERVER" "bash -s -- '$REMOTE_APP_DIR' '$REMOTE_ENV_FILE'" <<'REMOTE'
set -euo pipefail

APP_DIR="$1"
ENV_FILE="$2"

if [[ ! -r "$ENV_FILE" ]]; then
    echo "Missing or unreadable production environment: $ENV_FILE" >&2
    exit 1
fi

set -a
# shellcheck disable=SC1090
. "$ENV_FILE"
set +a

required_variables=(
    ConnectionStrings__Default
    Jwt__SigningKey
    LiveAuth__PowHmacSecret
    LiveAuth__DemoProjectId
    CostShield__SigningPrivateKeyPemBase64
    GitHub__ClientId
    GitHub__ClientSecret
)
for variable in "${required_variables[@]}"; do
    if [[ -z "${!variable:-}" ]]; then
        echo "Required variable $variable is missing from $ENV_FILE" >&2
        exit 1
    fi
done

if [[ "${Lnd__UseMock:-false}" != "true" &&
      -z "${Lnd__Macaroon:-}" ]]; then
    echo "Lnd__Macaroon is required when Lnd__UseMock is false" >&2
    exit 1
fi

old_pid="$(ss -tlnp 2>/dev/null | grep ':8081' | grep -oP 'pid=\K[0-9]+' | head -1 || true)"
if [[ -n "$old_pid" ]]; then
    kill "$old_pid"
    for _ in 1 2 3 4 5; do
        if ! kill -0 "$old_pid" 2>/dev/null; then
            break
        fi
        sleep 1
    done
fi

cd "$APP_DIR"
export ASPNETCORE_URLS="http://0.0.0.0:8081"
export ASPNETCORE_ENVIRONMENT="Production"
nohup setsid ./LiveAuthCore > /tmp/liveauth-new.log 2>&1 < /dev/null &
disown
REMOTE

echo "Waiting for API..."
API_READY=false
for _ in 1 2 3 4 5 6 7 8 9 10; do
    if ssh "$SERVER" "ss -tln 2>/dev/null | grep -q ':8081'"; then
        API_READY=true
        break
    fi
    sleep 1
done

if [[ "$API_READY" != "true" ]]; then
    echo "API failed to start; inspect /tmp/liveauth-new.log on the server."
    exit 1
fi

echo "Running post-deploy verification..."
bash "$LOCAL_DIR/scripts/post-deploy-check.sh"

echo "=== Full deploy successful ==="
