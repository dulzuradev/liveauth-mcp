#!/bin/bash
# Post-deploy verification script
# Verifies the deployed sites are responding correctly

set -e

echo "Running post-deploy verification..."

# Check main site
echo "Checking liveauth.app..."
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" https://liveauth.app)
if [ "$HTTP_CODE" != "200" ]; then
    echo "ERROR: liveauth.app returned $HTTP_CODE"
    exit 1
fi
echo "✓ liveauth.app OK (200)"

# Check admin site
echo "Checking admin.liveauth.app..."
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" https://admin.liveauth.app)
if [ "$HTTP_CODE" != "200" ]; then
    echo "ERROR: admin.liveauth.app returned $HTTP_CODE"
    exit 1
fi
echo "✓ admin.liveauth.app OK (200)"

# Check docs site
echo "Checking docs.liveauth.app..."
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" https://docs.liveauth.app)
if [ "$HTTP_CODE" != "200" ]; then
    echo "ERROR: docs.liveauth.app returned $HTTP_CODE"
    exit 1
fi
echo "✓ docs.liveauth.app OK (200)"

# Check API health
echo "Checking API health..."
API_RESP=$(curl -s https://api.liveauth.app/api/health)
if echo "$API_RESP" | grep -q '"status":"healthy"'; then
    echo "✓ API health OK"
else
    echo "ERROR: API health check failed"
    echo "Response: $API_RESP"
    exit 1
fi

# Check GitHub OAuth status
echo "Checking GitHub OAuth..."
GITHUB_RESP=$(curl -s https://api.liveauth.app/api/dev/auth/github/status)
if echo "$GITHUB_RESP" | grep -q '"enabled":true'; then
    echo "✓ GitHub OAuth enabled"
else
    echo "WARNING: GitHub OAuth may not be configured"
fi

echo ""
echo "✓ All post-deploy checks passed!"
