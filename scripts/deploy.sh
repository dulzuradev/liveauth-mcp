#!/bin/bash
# LiveAuth Deploy Script with Smoke Tests
# Usage: ./scripts/deploy.sh

set -e

echo "=============================================="
echo "LiveAuth Deploy with Smoke Tests"
echo "=============================================="

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

ERRORS=0

# Function to check a site
check_site() {
    local name=$1
    local url=$2
    local status=$(curl -s -o /dev/null -w "%{http_code}" "$url")
    
    if [ "$status" = "200" ]; then
        echo -e "${GREEN}✓ $name: OK ($status)${NC}"
        return 0
    else
        echo -e "${RED}✗ $name: FAILED ($status)${NC}"
        return 1
    fi
}

echo -e "\n${YELLOW}Running pre-deploy checks...${NC}"

# Check docker-compose syntax
if docker compose config --quiet < /opt/liveauth/docker-compose.yml 2>/dev/null; then
    echo -e "${GREEN}✓ docker-compose.yml valid${NC}"
else
    echo -e "${RED}✗ docker-compose.yml has errors${NC}"
    exit 1
fi

echo -e "\n${YELLOW}Deploying...${NC}"

# Deploy via docker-compose
cd /opt/liveauth
docker compose up -d --force-recreate

echo -e "\n${YELLOW}Waiting for services to start...${NC}"
sleep 5

echo -e "\n${YELLOW}Running smoke tests...${NC}"

# Test all sites
check_site "Main site" "https://liveauth.app/" || ERRORS=$((ERRORS+1))
check_site "Admin site" "https://admin.liveauth.app/" || ERRORS=$((ERRORS+1))
check_site "API Health" "https://api.liveauth.app/api/health" || ERRORS=$((ERRORS+1))
check_site "Demo Auth" "https://api.liveauth.app/api/public/demo/start" || ERRORS=$((ERRORS+1))

# Summary
echo -e "\n=============================================="
if [ $ERRORS -eq 0 ]; then
    echo -e "${GREEN}All tests passed! Deploy successful.${NC}"
    exit 0
else
    echo -e "${RED}$ERRORS test(s) failed!${NC}"
    echo "Check the errors above and roll back if needed."
    exit 1
fi
