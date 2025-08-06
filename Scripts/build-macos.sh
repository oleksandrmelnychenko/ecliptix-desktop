#!/bin/bash

# macOS Build Script for Ecliptix Desktop
# This script builds the application for macOS with proper versioning and creates an app bundle

set -e  # Exit on error

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
DESKTOP_PROJECT="$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core.Desktop/Ecliptix.Core.Desktop.csproj"

echo "🚀 Building Ecliptix Desktop for macOS..."

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if we're on macOS
if [[ "$OSTYPE" != "darwin"* ]]; then
    print_error "This script is designed for macOS only"
    exit 1
fi

# Check if .NET is installed
if ! command -v dotnet &> /dev/null; then
    print_error ".NET SDK is required but not found"
    print_error "Please install .NET from: https://dotnet.microsoft.com/download"
    exit 1
fi

# Check if project file exists
if [ ! -f "$DESKTOP_PROJECT" ]; then
    print_error "Desktop project not found at: $DESKTOP_PROJECT"
    exit 1
fi

# Parse command line arguments
BUILD_CONFIGURATION="Release"
RUNTIME_ID="osx-arm64"
INCREMENT_VERSION=""
SKIP_RESTORE=false
SKIP_TESTS=false

while [[ $# -gt 0 ]]; do
    case $1 in
        -c|--configuration)
            BUILD_CONFIGURATION="$2"
            shift 2
            ;;
        -r|--runtime)
            RUNTIME_ID="$2"
            shift 2
            ;;
        --increment)
            INCREMENT_VERSION="$2"
            shift 2
            ;;
        --skip-restore)
            SKIP_RESTORE=true
            shift
            ;;
        --skip-tests)
            SKIP_TESTS=true
            shift
            ;;
        --intel)
            RUNTIME_ID="osx-x64"
            shift
            ;;
        --arm|--m1)
            RUNTIME_ID="osx-arm64"
            shift
            ;;
        -h|--help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -c, --configuration   Build configuration (Debug/Release, default: Release)"
            echo "  -r, --runtime        Runtime identifier (default: osx-arm64)"
            echo "  --intel              Build for Intel Macs (osx-x64)"
            echo "  --arm, --m1          Build for Apple Silicon Macs (osx-arm64)"
            echo "  --increment PART     Increment version (major/minor/patch)"
            echo "  --skip-restore       Skip package restore"
            echo "  --skip-tests         Skip running tests"
            echo "  -h, --help           Show this help message"
            echo ""
            echo "Examples:"
            echo "  $0                                # Build for Apple Silicon (ARM64)"
            echo "  $0 --intel                        # Build for Intel Macs"
            echo "  $0 --increment patch              # Increment patch version and build"
            echo "  $0 -c Debug --skip-tests          # Debug build without tests"
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            print_error "Use -h or --help for usage information"
            exit 1
            ;;
    esac
done

print_status "Build Configuration: $BUILD_CONFIGURATION"
print_status "Runtime ID: $RUNTIME_ID"

# Increment version if requested
if [ -n "$INCREMENT_VERSION" ]; then
    print_status "Incrementing $INCREMENT_VERSION version..."
    if python3 "$SCRIPT_DIR/version-helper.py" --action increment --part "$INCREMENT_VERSION"; then
        NEW_VERSION=$(python3 "$SCRIPT_DIR/version-helper.py" --action current)
        print_success "Version incremented to: $NEW_VERSION"
    else
        print_error "Failed to increment version"
        exit 1
    fi
fi

# Generate build info
print_status "Generating build information..."
if python3 "$SCRIPT_DIR/version-helper.py" --action build; then
    print_success "Build information generated"
else
    print_warning "Could not generate build information (continuing anyway)"
fi

# Navigate to project directory
cd "$PROJECT_ROOT"

# Clean previous builds
print_status "Cleaning previous builds..."
dotnet clean "$DESKTOP_PROJECT" -c "$BUILD_CONFIGURATION" --verbosity minimal

# Restore packages
if [ "$SKIP_RESTORE" = false ]; then
    print_status "Restoring NuGet packages..."
    dotnet restore "$DESKTOP_PROJECT" --verbosity minimal
    if [ $? -eq 0 ]; then
        print_success "Packages restored successfully"
    else
        print_error "Failed to restore packages"
        exit 1
    fi
fi

# Run tests
if [ "$SKIP_TESTS" = false ]; then
    print_status "Running tests..."
    if dotnet test --verbosity minimal --nologo; then
        print_success "All tests passed"
    else
        print_warning "Some tests failed, but continuing with build..."
    fi
fi

# Build the application
print_status "Building application..."
BUILD_OUTPUT_DIR="$PROJECT_ROOT/build/macos/$RUNTIME_ID"

dotnet publish "$DESKTOP_PROJECT" \
    -c "$BUILD_CONFIGURATION" \
    -r "$RUNTIME_ID" \
    --self-contained true \
    --output "$BUILD_OUTPUT_DIR" \
    --verbosity minimal \
    -p:PublishSingleFile=true \
    -p:DebugType=None \
    -p:DebugSymbols=false

if [ $? -eq 0 ]; then
    print_success "Build completed successfully"
    print_success "Output directory: $BUILD_OUTPUT_DIR"
else
    print_error "Build failed"
    exit 1
fi

# Create app bundle structure for macOS
APP_NAME="Ecliptix"
APP_BUNDLE="$BUILD_OUTPUT_DIR/$APP_NAME.app"
CONTENTS_DIR="$APP_BUNDLE/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"

print_status "Creating macOS app bundle..."

# Create app bundle directories
mkdir -p "$MACOS_DIR"
mkdir -p "$RESOURCES_DIR"

# Move the executable to the MacOS directory
mv "$BUILD_OUTPUT_DIR/Ecliptix.Core.Desktop" "$MACOS_DIR/Ecliptix"

# Copy the icon file
if [ -f "$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core/Assets/logo.icns" ]; then
    cp "$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core/Assets/logo.icns" "$RESOURCES_DIR/AppIcon.icns"
    print_success "Icon copied to app bundle"
else
    print_warning "Icon file not found, app bundle will use default icon"
fi

# Get current version
CURRENT_VERSION=$(python3 "$SCRIPT_DIR/version-helper.py" --action current 2>/dev/null || echo "1.0.0")
BUILD_NUMBER=$(date +%Y%m%d.%H%M)

# Create Info.plist
print_status "Creating Info.plist..."
cat > "$CONTENTS_DIR/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDisplayName</key>
    <string>Ecliptix</string>
    <key>CFBundleExecutable</key>
    <string>Ecliptix</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundleIdentifier</key>
    <string>com.ecliptix.desktop</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>Ecliptix</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$CURRENT_VERSION</string>
    <key>CFBundleVersion</key>
    <string>$BUILD_NUMBER</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSSupportsAutomaticGraphicsSwitching</key>
    <true/>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.productivity</string>
    <key>NSHumanReadableCopyright</key>
    <string>© $(date +%Y) Ecliptix. All rights reserved.</string>
    <key>NSRequiresAquaSystemAppearance</key>
    <false/>
</dict>
</plist>
EOF

# Make the executable file executable
chmod +x "$MACOS_DIR/Ecliptix"

# Set proper permissions for the app bundle
find "$APP_BUNDLE" -type f -exec chmod 644 {} \;
find "$APP_BUNDLE" -type d -exec chmod 755 {} \;
chmod +x "$MACOS_DIR/Ecliptix"

print_success "macOS app bundle created: $APP_BUNDLE"

# Create a DMG (optional, requires create-dmg)
if command -v create-dmg &> /dev/null; then
    print_status "Creating DMG installer..."
    DMG_OUTPUT="$BUILD_OUTPUT_DIR/Ecliptix-$CURRENT_VERSION-$RUNTIME_ID.dmg"
    
    # Remove existing DMG
    rm -f "$DMG_OUTPUT"
    
    create-dmg \
        --volname "Ecliptix $CURRENT_VERSION" \
        --volicon "$RESOURCES_DIR/AppIcon.icns" \
        --window-pos 200 120 \
        --window-size 600 300 \
        --icon-size 100 \
        --icon "$APP_NAME.app" 175 120 \
        --hide-extension "$APP_NAME.app" \
        --app-drop-link 425 120 \
        "$DMG_OUTPUT" \
        "$BUILD_OUTPUT_DIR"
    
    if [ $? -eq 0 ]; then
        print_success "DMG created: $DMG_OUTPUT"
    else
        print_warning "DMG creation failed (install create-dmg for DMG support)"
    fi
else
    print_warning "create-dmg not found. Install with: brew install create-dmg"
fi

# Display build summary
echo ""
print_success "🎉 Build completed successfully!"
echo ""
echo "📦 Build Summary:"
echo "   Version: $CURRENT_VERSION"
echo "   Configuration: $BUILD_CONFIGURATION"
echo "   Runtime: $RUNTIME_ID"
echo "   App Bundle: $APP_BUNDLE"
echo ""
echo "🚀 To run the application:"
echo "   open '$APP_BUNDLE'"
echo ""
echo "📋 Next steps:"
echo "   1. Test the application: open '$APP_BUNDLE'"
echo "   2. Sign the app bundle if distributing: codesign -s 'Developer ID' '$APP_BUNDLE'"
echo "   3. Notarize for distribution: xcrun notarytool submit ..."
echo ""