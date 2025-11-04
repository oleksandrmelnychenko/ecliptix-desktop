#!/bin/bash
# Rider Code Inspection Script
# Runs Rider's code inspections from command line

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RIDER_BIN="/Applications/Rider.app/Contents/bin/inspect.sh"
OUTPUT_FILE="$PROJECT_ROOT/rider-inspection-results.xml"
SOLUTION_FILE="$PROJECT_ROOT/Ecliptix-Desktop.sln"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}=== Rider Code Inspection ===${NC}"
echo "Solution: $SOLUTION_FILE"
echo "Output: $OUTPUT_FILE"
echo ""

# Check if Rider is installed
if [ ! -f "$RIDER_BIN" ]; then
    echo -e "${RED}Error: Rider not found at $RIDER_BIN${NC}"
    echo "Please install JetBrains Rider or update the RIDER_BIN path"
    exit 1
fi

# Check if solution exists
if [ ! -f "$SOLUTION_FILE" ]; then
    echo -e "${RED}Error: Solution file not found: $SOLUTION_FILE${NC}"
    exit 1
fi

# Run inspection
echo -e "${YELLOW}Running Rider code inspection...${NC}"
"$RIDER_BIN" "$SOLUTION_FILE" \
    --output="$OUTPUT_FILE" \
    --severity=WARNING \
    --format=Xml \
    --properties:Configuration=Debug

if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ Inspection completed successfully${NC}"
    echo ""

    # Parse and display summary
    if [ -f "$OUTPUT_FILE" ]; then
        echo -e "${YELLOW}Summary of issues found:${NC}"

        # Count issues by severity
        ERROR_COUNT=$(grep -c 'Severity="ERROR"' "$OUTPUT_FILE" || echo "0")
        WARNING_COUNT=$(grep -c 'Severity="WARNING"' "$OUTPUT_FILE" || echo "0")
        SUGGESTION_COUNT=$(grep -c 'Severity="SUGGESTION"' "$OUTPUT_FILE" || echo "0")

        echo "  Errors: $ERROR_COUNT"
        echo "  Warnings: $WARNING_COUNT"
        echo "  Suggestions: $SUGGESTION_COUNT"
        echo ""
        echo "View full report: $OUTPUT_FILE"
    fi
else
    echo -e "${RED}✗ Inspection failed${NC}"
    exit 1
fi
