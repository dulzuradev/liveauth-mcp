#!/bin/bash
# LiveAuth Deployment Checklist
# Run this before deploying to catch config issues early

set -e

echo "=============================================="
echo "LiveAuth Deployment Checklist"
echo "=============================================="

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

ERRORS=0

# Check required env vars
echo -e "\n${YELLOW}Checking required environment variables...${NC}"

REQUIRED_VARS=(
  "LiveAuth__PowHmacSecret"
  "LiveAuth__DemoProjectId"
  "Jwt__SigningKey"
)

for var in "${REQUIRED_VARS[@]}"; do
  VALUE=$(grep -E "^[[:space:]]*$var:" docker-compose.yml | cut -d'"' -f2)
  if [ -z "$VALUE" ]; then
    echo -e "${RED}✗ $var is not set${NC}"
    ERRORS=$((ERRORS+1))
  else
    echo -e "${GREEN}✓ $var is configured${NC}"
  fi
done

# Check demo project in database
echo -e "\n${YELLOW}Checking demo project configuration...${NC}"

# Try to get DB from container or volume
DB_PATH=""
if docker volume inspect liveauth_sqlite_data &>/dev/null; then
  DB_PATH="/tmp/check_liveauth.db"
  docker run --rm -v liveauth_sqlite_data:/data alpine cat /data/liveauth.db > "$DB_PATH" 2>/dev/null || true
fi

if [ -f "$DB_PATH" ]; then
  DEMO_ENV=$(sqlite3 "$DB_PATH" "SELECT Environment FROM Projects WHERE Id = '00000000-0000-0000-0000-000000000002';" 2>/dev/null || echo "")
  DEMO_SATS=$(sqlite3 "$DB_PATH" "SELECT SatsPerLogin FROM Projects WHERE Id = '00000000-0000-0000-0000-000000000002';" 2>/dev/null || echo "420")
  
  if [ "$DEMO_ENV" = "TEST" ]; then
    echo -e "${GREEN}✓ Demo project Environment = TEST${NC}"
  else
    echo -e "${RED}✗ Demo project Environment should be TEST, found: $DEMO_ENV${NC}"
    ERRORS=$((ERRORS+1))
  fi
  
  if [ "$DEMO_SATS" = "0" ]; then
    echo -e "${GREEN}✓ Demo project SatsPerLogin = 0 (no real Lightning for demo)${NC}"
  else
    echo -e "${YELLOW}⚠ Demo project SatsPerLogin = $DEMO_SATS (will attempt real Lightning)${NC}"
  fi
  
  rm -f "$DB_PATH"
else
  echo -e "${YELLOW}⚠ Could not check database (not running or no volume)${NC}"
fi

# Check LND if not using mock
LND_MOCK=$(grep -E "^[[:space:]]*Lnd__UseMock:" docker-compose.yml | cut -d'"' -f2)
if [ "$LND_MOCK" != "true" ]; then
  echo -e "\n${YELLOW}LND is in LIVE mode - checking LND connectivity...${NC}"
  
  LND_URL=$(grep -E "^[[:space:]]*Lnd__BaseUrl:" docker-compose.yml | cut -d'"' -f2)
  LND_MACAROON=$(grep -E "^[[:space:]]*Lnd__Macaroon:" docker-compose.yml | cut -d'"' -f2)
  
  if [ -z "$LND_URL" ]; then
    echo -e "${RED}✗ Lnd__BaseUrl not configured but Lnd__UseMock is false${NC}"
    ERRORS=$((ERRORS+1))
  else
    echo -e "${GREEN}✓ Lnd__BaseUrl: $LND_URL${NC}"
  fi
  
  if [ -z "$LND_MACAROON" ]; then
    echo -e "${RED}✗ Lnd__Macaroon not configured but Lnd__UseMock is false${NC}"
    ERRORS=$((ERRORS+1))
  else
    echo -e "${GREEN}✓ Lnd__Macaroon is configured${NC}"
  fi
else
  echo -e "\n${GREEN}✓ LND is in MOCK mode (safe for testing)${NC}"
fi

# Check docker-compose syntax
echo -e "\n${YELLOW}Validating docker-compose.yml...${NC}"
if docker compose config --quiet < docker-compose.yml 2>/dev/null; then
  echo -e "${GREEN}✓ docker-compose.yml is valid${NC}"
else
  echo -e "${RED}✗ docker-compose.yml has syntax errors${NC}"
  ERRORS=$((ERRORS+1))
fi

# Summary
echo -e "\n=============================================="
if [ $ERRORS -eq 0 ]; then
  echo -e "${GREEN}All checks passed! Safe to deploy.${NC}"
  exit 0
else
  echo -e "${RED}Found $ERRORS issue(s). Fix before deploying!${NC}"
  exit 1
fi
