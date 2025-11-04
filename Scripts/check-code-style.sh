#!/bin/bash
# Check Code Style Issues (Similar to Rider IDE warnings)
# Uses dotnet format analyzers to detect code style violations

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

cd "$PROJECT_ROOT"

echo -e "${GREEN}=== Code Style Analysis ===${NC}"
echo "Analyzing: $PROJECT_ROOT"
echo ""

# Run dotnet format in verify mode (analyze only, don't fix)
echo -e "${YELLOW}Running code style analysis...${NC}"
echo ""

# Capture output
OUTPUT=$(dotnet format --verify-no-changes --verbosity diagnostic 2>&1 || true)

# Check if there are any issues
if echo "$OUTPUT" | grep -q "Formatted code file"; then
    echo -e "${YELLOW}⚠ Code style issues found:${NC}"
    echo ""

    # Extract files with issues
    echo "$OUTPUT" | grep "Formatted code file" | while read -r line; do
        FILE=$(echo "$line" | sed -n "s/.*'\(.*\)'.*/\1/p")
        echo -e "  ${BLUE}→${NC} $FILE"
    done

    echo ""
    echo -e "${YELLOW}Run './Scripts/format-code.sh' to fix these issues${NC}"
    exit 1
elif echo "$OUTPUT" | grep -q "Build FAILED"; then
    echo -e "${RED}✗ Build failed${NC}"
    echo "$OUTPUT" | grep -A 5 "error"
    exit 1
else
    echo -e "${GREEN}✓ No code style issues found${NC}"
    exit 0
fi
