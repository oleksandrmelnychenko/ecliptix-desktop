#!/bin/bash

# Ecliptix macOS DMG Installer Creator
# Creates a professional drag-and-drop installer for macOS

set -e  # Exit on error

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${BLUE}[DMG-INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[DMG-SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[DMG-WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[DMG-ERROR]${NC} $1"
}

# Check if we're on macOS
if [[ "$OSTYPE" != "darwin"* ]]; then
    print_error "This script is designed for macOS only"
    exit 1
fi

# Parse command line arguments
APP_BUNDLE=""
OUTPUT_DIR="$PROJECT_ROOT/installers"
DMG_NAME=""
VOLUME_NAME="Ecliptix"
BACKGROUND_IMAGE=""
CODESIGN_IDENTITY=""
NOTARIZE=false

while [[ $# -gt 0 ]]; do
    case $1 in
        -a|--app)
            APP_BUNDLE="$2"
            shift 2
            ;;
        -o|--output)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        -n|--name)
            DMG_NAME="$2"
            shift 2
            ;;
        -v|--volume)
            VOLUME_NAME="$2"
            shift 2
            ;;
        -b|--background)
            BACKGROUND_IMAGE="$2"
            shift 2
            ;;
        -s|--sign)
            CODESIGN_IDENTITY="$2"
            shift 2
            ;;
        --notarize)
            NOTARIZE=true
            shift
            ;;
        -h|--help)
            echo "Ecliptix macOS DMG Installer Creator"
            echo ""
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -a, --app PATH           Path to .app bundle (required)"
            echo "  -o, --output DIR         Output directory (default: ./installers)"
            echo "  -n, --name NAME          DMG filename without extension"
            echo "  -v, --volume NAME        Volume name (default: Ecliptix)"
            echo "  -b, --background PATH    Background image for DMG"
            echo "  -s, --sign IDENTITY      Code signing identity"
            echo "  --notarize               Notarize the DMG (requires signing)"
            echo "  -h, --help               Show this help message"
            echo ""
            echo "Examples:"
            echo "  $0 -a publish/osx-arm64/Ecliptix.app"
            echo "  $0 -a publish/osx-arm64/Ecliptix.app -s 'Developer ID Application: Your Name'"
            echo "  $0 -a publish/osx-arm64/Ecliptix.app -s 'Developer ID' --notarize"
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            print_error "Use -h or --help for usage information"
            exit 1
            ;;
    esac
done

# Auto-detect app bundle if not specified
if [ -z "$APP_BUNDLE" ]; then
    print_status "Auto-detecting app bundle..."

    # Try ARM64 first (most common for new Macs)
    if [ -d "$PROJECT_ROOT/publish/osx-arm64/Ecliptix.app" ]; then
        APP_BUNDLE="$PROJECT_ROOT/publish/osx-arm64/Ecliptix.app"
    elif [ -d "$PROJECT_ROOT/publish/osx-x64/Ecliptix.app" ]; then
        APP_BUNDLE="$PROJECT_ROOT/publish/osx-x64/Ecliptix.app"
    elif [ -d "$PROJECT_ROOT/publish/universal/Ecliptix.app" ]; then
        APP_BUNDLE="$PROJECT_ROOT/publish/universal/Ecliptix.app"
    else
        print_error "No app bundle found. Please build the app first or specify path with -a"
        print_error "Build the app with: ./Scripts/build-aot-macos.sh"
        exit 1
    fi
fi

# Verify app bundle exists
if [ ! -d "$APP_BUNDLE" ]; then
    print_error "App bundle not found: $APP_BUNDLE"
    exit 1
fi

APP_BUNDLE=$(cd "$APP_BUNDLE" && pwd)  # Get absolute path
APP_NAME=$(basename "$APP_BUNDLE" .app)

print_status "Creating DMG installer for: $APP_BUNDLE"

# Get version from app bundle
VERSION=$(defaults read "$APP_BUNDLE/Contents/Info.plist" CFBundleShortVersionString 2>/dev/null || echo "1.0.0")
CLEAN_VERSION=$(echo "$VERSION" | sed 's/-build.*//')

# Determine architecture from app bundle path
ARCH="universal"
if [[ "$APP_BUNDLE" == *"osx-arm64"* ]]; then
    ARCH="arm64"
elif [[ "$APP_BUNDLE" == *"osx-x64"* ]]; then
    ARCH="x64"
fi

# Set DMG name if not specified
if [ -z "$DMG_NAME" ]; then
    DMG_NAME="${APP_NAME}-${CLEAN_VERSION}-${ARCH}"
fi

DMG_FILE="$OUTPUT_DIR/${DMG_NAME}.dmg"
DMG_TEMP="$OUTPUT_DIR/temp_dmg"
MOUNT_POINT="/Volumes/$VOLUME_NAME"

print_status "DMG Configuration:"
print_status "  • App: $APP_NAME"
print_status "  • Version: $CLEAN_VERSION"
print_status "  • Architecture: $ARCH"
print_status "  • Volume Name: $VOLUME_NAME"
print_status "  • Output: $DMG_FILE"

# Create output directory
mkdir -p "$OUTPUT_DIR"
mkdir -p "$DMG_TEMP"

# Clean up any existing DMG
if [ -f "$DMG_FILE" ]; then
    print_status "Removing existing DMG..."
    rm -f "$DMG_FILE"
fi

# Unmount if already mounted
if [ -d "$MOUNT_POINT" ]; then
    print_status "Unmounting existing volume..."
    hdiutil detach "$MOUNT_POINT" -quiet -force 2>/dev/null || true
fi

# Copy app to temp directory
print_status "Copying app bundle to staging area..."
cp -R "$APP_BUNDLE" "$DMG_TEMP/"

# Create Applications symlink for drag-and-drop installation
print_status "Creating Applications folder symlink..."
ln -s /Applications "$DMG_TEMP/Applications"

# Copy or create background image
DMG_BACKGROUND=""
if [ -n "$BACKGROUND_IMAGE" ] && [ -f "$BACKGROUND_IMAGE" ]; then
    print_status "Using custom background image..."
    mkdir -p "$DMG_TEMP/.background"
    cp "$BACKGROUND_IMAGE" "$DMG_TEMP/.background/background.png"
    DMG_BACKGROUND="background.png"
else
    print_status "No custom background image specified (using default)"
fi

# Create .DS_Store for custom icon positioning
print_status "Configuring DMG layout..."

# Calculate DMG size
APP_SIZE=$(du -sm "$DMG_TEMP" | cut -f1)
DMG_SIZE=$((APP_SIZE + 50))  # Add 50MB buffer

print_status "Creating temporary DMG (${DMG_SIZE}MB)..."
hdiutil create \
    -srcfolder "$DMG_TEMP" \
    -volname "$VOLUME_NAME" \
    -fs HFS+ \
    -fsargs "-c c=64,a=16,e=16" \
    -format UDRW \
    -size ${DMG_SIZE}m \
    "$OUTPUT_DIR/temp.dmg"

# Mount the temporary DMG
print_status "Mounting temporary DMG..."
DEVICE=$(hdiutil attach -readwrite -noverify -noautoopen "$OUTPUT_DIR/temp.dmg" | \
         egrep '^/dev/' | sed 1q | awk '{print $1}')

# Wait for mount
sleep 2

# Verify mount
if [ ! -d "$MOUNT_POINT" ]; then
    print_error "Failed to mount DMG at $MOUNT_POINT"
    exit 1
fi

print_success "DMG mounted at: $MOUNT_POINT"

# Configure DMG appearance with AppleScript
print_status "Customizing DMG appearance..."

osascript << EOF
tell application "Finder"
    tell disk "$VOLUME_NAME"
        open
        set current view of container window to icon view
        set toolbar visible of container window to false
        set statusbar visible of container window to false
        set the bounds of container window to {100, 100, 900, 550}
        set viewOptions to the icon view options of container window
        set arrangement of viewOptions to not arranged
        set icon size of viewOptions to 128
        set text size of viewOptions to 12

        -- Position icons
        set position of item "$APP_NAME.app" to {200, 180}
        set position of item "Applications" to {600, 180}

        -- Set background if provided
        $(if [ -n "$DMG_BACKGROUND" ]; then
            echo "set background picture of viewOptions to file \".background:$DMG_BACKGROUND\""
        fi)

        -- Update and close
        update without registering applications
        delay 2
        close
    end tell
end tell
EOF

# Set custom icon for volume (if available)
ICON_FILE="$APP_BUNDLE/Contents/Resources/AppIcon.icns"
if [ -f "$ICON_FILE" ]; then
    print_status "Setting custom volume icon..."
    cp "$ICON_FILE" "$MOUNT_POINT/.VolumeIcon.icns"
    SetFile -c icnC "$MOUNT_POINT/.VolumeIcon.icns"
    SetFile -a C "$MOUNT_POINT"
fi

# Hide background folder
if [ -d "$MOUNT_POINT/.background" ]; then
    SetFile -a V "$MOUNT_POINT/.background"
fi

# Sync and unmount
print_status "Finalizing DMG..."
sync
sync

hdiutil detach "$DEVICE" -quiet -force

# Convert to compressed read-only DMG
print_status "Compressing DMG..."
hdiutil convert "$OUTPUT_DIR/temp.dmg" \
    -format UDZO \
    -imagekey zlib-level=9 \
    -o "$DMG_FILE"

# Clean up
rm -rf "$DMG_TEMP"
rm -f "$OUTPUT_DIR/temp.dmg"

# Get final DMG size
DMG_SIZE_FINAL=$(du -h "$DMG_FILE" | cut -f1)

print_success "DMG created successfully!"
print_success "  • File: $DMG_FILE"
print_success "  • Size: $DMG_SIZE_FINAL"

# Code signing
if [ -n "$CODESIGN_IDENTITY" ]; then
    print_status "Signing DMG with identity: $CODESIGN_IDENTITY"

    codesign --force --sign "$CODESIGN_IDENTITY" "$DMG_FILE"

    if [ $? -eq 0 ]; then
        print_success "DMG signed successfully"

        # Verify signature
        print_status "Verifying signature..."
        codesign -v -d "$DMG_FILE"
    else
        print_error "Failed to sign DMG"
        exit 1
    fi
fi

# Notarization
if [ "$NOTARIZE" = true ]; then
    if [ -z "$CODESIGN_IDENTITY" ]; then
        print_error "Notarization requires code signing. Please use -s option"
        exit 1
    fi

    print_status "Starting notarization process..."
    print_warning "This requires an Apple Developer account and App-Specific Password"
    print_warning "Configure notarization profile with:"
    print_warning "  xcrun notarytool store-credentials 'notarytool-profile' --apple-id 'your@email.com'"

    read -p "Do you want to continue with notarization? (y/N) " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        print_status "Submitting for notarization..."
        xcrun notarytool submit "$DMG_FILE" --keychain-profile "notarytool-profile" --wait

        if [ $? -eq 0 ]; then
            print_success "Notarization successful"

            print_status "Stapling notarization ticket..."
            xcrun stapler staple "$DMG_FILE"

            print_success "DMG notarized and stapled!"
        else
            print_error "Notarization failed"
            print_error "Check your notarization profile and credentials"
        fi
    fi
fi

echo ""
print_success "🎉 macOS Installer Created Successfully!"
echo ""
echo "📦 Installer Details:"
echo "   • DMG File: $DMG_FILE"
echo "   • Size: $DMG_SIZE_FINAL"
echo "   • Volume: $VOLUME_NAME"
echo "   • Version: $CLEAN_VERSION"
echo "   • Architecture: $ARCH"
echo ""
echo "🧪 To test the installer:"
echo "   open '$DMG_FILE'"
echo ""
echo "📋 Distribution checklist:"
if [ -z "$CODESIGN_IDENTITY" ]; then
    echo "   ⚠️  NOT SIGNED - Sign for distribution:"
    echo "      $0 -a '$APP_BUNDLE' -s 'Developer ID Application: Your Name'"
else
    echo "   ✅ Signed with: $CODESIGN_IDENTITY"
fi

if [ "$NOTARIZE" = false ]; then
    echo "   ⚠️  NOT NOTARIZED - Notarize for Gatekeeper approval:"
    echo "      $0 -a '$APP_BUNDLE' -s 'Developer ID' --notarize"
else
    echo "   ✅ Notarized and stapled"
fi

echo ""
echo "📤 Upload to:"
echo "   • Website / GitHub Releases"
echo "   • Direct download link"
echo "   • Update server"
echo ""
