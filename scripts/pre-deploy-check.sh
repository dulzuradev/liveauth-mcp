#!/bin/bash
# Pre-deploy health check script
# Verifies the build is valid before deploying

set -e

echo "Running pre-deploy checks..."

# Check that critical files exist in the build
BROWSER_DIR="${1:-./dist/liveauth-web/browser}"

if [ ! -d "$BROWSER_DIR" ]; then
    echo "ERROR: Build directory $BROWSER_DIR does not exist"
    exit 1
fi

# Check index.html exists
if [ ! -f "$BROWSER_DIR/index.html" ]; then
    echo "ERROR: index.html not found in build"
    exit 1
fi

# Check main JS exists (glob pattern)
if ! ls "$BROWSER_DIR"/main-*.js >/dev/null 2>&1; then
    echo "ERROR: main JS bundle not found in build"
    exit 1
fi

# Check CSS exists
if ! ls "$BROWSER_DIR"/styles-*.css >/dev/null 2>&1; then
    echo "ERROR: CSS bundle not found in build"
    exit 1
fi

# Check admin build if it exists
ADMIN_DIR="${2:-../liveauth-admin/dist/liveauth-admin/browser}"
if [ -d "$ADMIN_DIR" ]; then
    if [ ! -f "$ADMIN_DIR/index.html" ]; then
        echo "ERROR: admin index.html not found"
        exit 1
    fi
    echo "✓ Admin build OK"
fi

echo "✓ All pre-deploy checks passed"
echo ""
echo "Build contents:"
ls -la "$BROWSER_DIR/"
