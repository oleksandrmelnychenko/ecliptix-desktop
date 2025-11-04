#!/bin/bash
# Format Code Script
# Auto-fixes code style issues using dotnet format

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

cd "$PROJECT_ROOT"

echo -e "${GREEN}=== Auto-formatting Code ===${NC}"
echo ""

echo -e "${YELLOW}Running dotnet format...${NC}"
dotnet format --verbosity normal

echo ""
echo -e "${GREEN}✓ Code formatting complete${NC}"
