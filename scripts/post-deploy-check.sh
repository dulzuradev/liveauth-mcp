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

# Check demo page
echo "Checking demo page..."
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" https://liveauth.app/demo)
if [ "$HTTP_CODE" != "200" ]; then
    echo "ERROR: liveauth.app/demo returned $HTTP_CODE"
    exit 1
fi
echo "✓ demo page OK (200)"

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

echo ""
echo "✓ All post-deploy checks passed!"
