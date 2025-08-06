#!/bin/bash

# Ecliptix Desktop Development Build Script
# Quick build for development and testing

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
DESKTOP_PROJECT="$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core.Desktop/Ecliptix.Core.Desktop.csproj"

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}Ecliptix Desktop Development Build${NC}"
echo "=================================="

# Generate build info for development
echo -e "${YELLOW}Generating build information...${NC}"
"$SCRIPT_DIR/version.sh" --action build

# Build for current platform
echo -e "${YELLOW}Building for development...${NC}"
dotnet build "$DESKTOP_PROJECT" -c Debug --verbosity minimal

if [[ $? -eq 0 ]]; then
    echo -e "${GREEN}✓ Development build completed successfully${NC}"
    
    # Get current version
    CURRENT_VERSION=$("$SCRIPT_DIR/version.sh" --action current | grep "Current version:" | cut -d' ' -f3)
    echo -e "${BLUE}Version: $CURRENT_VERSION${NC}"
    
    echo ""
    echo -e "${BLUE}To run the application:${NC}"
    echo "  dotnet run --project \"$DESKTOP_PROJECT\""
else
    echo -e "${RED}✗ Development build failed${NC}"
    exit 1
fi