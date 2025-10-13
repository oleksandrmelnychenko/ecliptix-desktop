#!/bin/bash

# macOS Icon Cache Refresh Script
# Use this if the app icon doesn't appear after building

echo "🎨 Refreshing macOS Icon Cache for Ecliptix..."

# Colors
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m'

# Find the app bundle
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
APP_BUNDLE="$PROJECT_ROOT/publish/osx-arm64/Ecliptix.app"

if [ ! -d "$APP_BUNDLE" ]; then
    APP_BUNDLE="$PROJECT_ROOT/publish/osx-x64/Ecliptix.app"
fi

if [ ! -d "$APP_BUNDLE" ]; then
    APP_BUNDLE="$PROJECT_ROOT/publish/universal/Ecliptix.app"
fi

if [ ! -d "$APP_BUNDLE" ]; then
    echo "❌ App bundle not found. Please build the app first."
    exit 1
fi

echo -e "${BLUE}[INFO]${NC} Found app bundle: $APP_BUNDLE"

# Step 1: Verify icon file exists
ICON_FILE="$APP_BUNDLE/Contents/Resources/AppIcon.icns"
if [ ! -f "$ICON_FILE" ]; then
    echo "❌ Icon file not found at: $ICON_FILE"
    echo "   The app was built without an icon. Please rebuild."
    exit 1
fi

echo -e "${GREEN}[OK]${NC} Icon file exists: $ICON_FILE"

# Step 2: Close the app if running
if pgrep -x "Ecliptix" > /dev/null; then
    echo -e "${BLUE}[INFO]${NC} Closing Ecliptix app..."
    osascript -e 'quit app "Ecliptix"' 2>/dev/null || killall Ecliptix 2>/dev/null
    sleep 1
fi

# Step 3: Touch the app bundle to update modification time
echo -e "${BLUE}[INFO]${NC} Updating app bundle timestamp..."
touch "$APP_BUNDLE"

# Step 4: Clear local icon cache
echo -e "${BLUE}[INFO]${NC} Clearing icon cache..."
rm -rf ~/Library/Caches/com.apple.iconservices.store

# Step 5: Clear Finder icon cache
echo -e "${BLUE}[INFO]${NC} Clearing Finder cache..."
sudo find /private/var/folders/ -name com.apple.dock.iconcache -exec rm {} \; 2>/dev/null || true

# Step 6: Restart Dock
echo -e "${BLUE}[INFO]${NC} Restarting Dock (this will close and reopen your Dock)..."
killall Dock

# Step 7: Restart Finder
echo -e "${BLUE}[INFO]${NC} Restarting Finder..."
killall Finder 2>/dev/null || true

echo ""
echo -e "${GREEN}✅ Icon cache refreshed!${NC}"
echo ""
echo "Next steps:"
echo "  1. Wait for Dock to restart (~2 seconds)"
echo "  2. Launch the app: open '$APP_BUNDLE'"
echo "  3. The icon should now appear in the Dock and menu bar"
echo ""
