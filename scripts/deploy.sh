#!/bin/bash
# LiveAuth Deploy Script with Smoke Tests
# Usage: ./scripts/deploy.sh

echo "=============================================="
echo "LiveAuth Deploy with Smoke Tests"
echo "=============================================="

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

ERRORS=0

# Function to check a site
check_site() {
    local name=$1
    local url=$2
    local method=${3:-GET}
    local status=$(curl -s -o /dev/null -w "%{http_code}" -X "$method" "$url" 2>/dev/null || echo "000")
    
    if [ "$status" = "200" ]; then
        echo -e "${GREEN}✓ $name: OK ($status)${NC}"
        return 0
    else
        echo -e "${RED}✗ $name: FAILED ($status)${NC}"
        return 1
    fi
}

echo -e "\n${YELLOW}Deploying...${NC}"

cd /opt/liveauth

# Force remove existing containers
docker rm -f liveauth-api liveauth-caddy 2>/dev/null || true

docker compose up -d

echo -e "\n${YELLOW}Waiting for services...${NC}"
sleep 5

echo -e "\n${YELLOW}Running smoke tests...${NC}"

check_site "Main site" "https://liveauth.app/" || ERRORS=$((ERRORS+1))
check_site "Admin site" "https://admin.liveauth.app/" || ERRORS=$((ERRORS+1))
check_site "API Health" "https://api.liveauth.app/api/health" || ERRORS=$((ERRORS+1))
check_site "Demo Auth" "https://api.liveauth.app/api/public/demo/start" "POST" || ERRORS=$((ERRORS+1))

echo -e "\n=============================================="
if [ $ERRORS -eq 0 ]; then
    echo -e "${GREEN}All tests passed!${NC}"
else
    echo -e "${RED}$ERRORS test(s) failed!${NC}"
fi
