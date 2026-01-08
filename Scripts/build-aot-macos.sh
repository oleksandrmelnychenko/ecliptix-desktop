#!/bin/bash

set -e  

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
DESKTOP_PROJECT="$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core.Desktop/Ecliptix.Core.Desktop.csproj"

echo "🚀 Building Ecliptix Desktop for macOS with AOT compilation..."

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' 

print_status() {
    echo -e "${BLUE}[AOT-INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[AOT-SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[AOT-WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[AOT-ERROR]${NC} $1"
}

if [[ "$OSTYPE" != "darwin"* ]]; then
    print_error "This script is designed for macOS only"
    exit 1
fi

if ! command -v dotnet &> /dev/null; then
    print_error ".NET SDK is required but not found"
    print_error "Please install .NET from: https://dotnet.microsoft.com/download"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
MAJOR_VERSION=$(echo "$DOTNET_VERSION" | cut -d '.' -f 1)
if [ "$MAJOR_VERSION" -lt 8 ]; then
    print_error ".NET 8 or higher is required for AOT compilation (found: $DOTNET_VERSION)"
    exit 1
fi

if [ ! -f "$DESKTOP_PROJECT" ]; then
    print_error "Desktop project not found at: $DESKTOP_PROJECT"
    exit 1
fi

BUILD_CONFIGURATION="Release"
RUNTIME_ID="osx-arm64"
INCREMENT_VERSION=""
SKIP_RESTORE=false
SKIP_TESTS=false
CLEAN_BUILD=false
OPTIMIZATION_LEVEL="aggressive"
CREATE_DMG=false
CODESIGN_IDENTITY=""

while [[ $
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
        --clean)
            CLEAN_BUILD=true
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
        --universal)
            RUNTIME_ID="universal"
            shift
            ;;
        --optimization)
            OPTIMIZATION_LEVEL="$2"
            shift 2
            ;;
        --create-dmg)
            CREATE_DMG=true
            shift
            ;;
        --sign)
            CODESIGN_IDENTITY="$2"
            shift 2
            ;;
        -h|--help)
            echo "AOT Build Script for Ecliptix Desktop macOS"
            echo ""
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -c, --configuration   Build configuration (Debug/Release, default: Release)"
            echo "  -r, --runtime        Runtime identifier (default: osx-arm64)"
            echo "  --intel              Build for Intel Macs (osx-x64)"
            echo "  --arm, --m1          Build for Apple Silicon Macs (osx-arm64)"
            echo "  --universal          Build Universal Binary (both Intel and ARM64)"
            echo "  --increment PART     Increment version (major/minor/patch)"
            echo "  --skip-restore       Skip package restore"
            echo "  --skip-tests         Skip running tests"
            echo "  --clean              Clean build artifacts before building"
            echo "  --optimization LEVEL Optimization level (size/speed/aggressive, default: aggressive)"
            echo "  --create-dmg         Create DMG installer after build"
            echo "  --sign IDENTITY      Code signing identity (for DMG)"
            echo "  -h, --help           Show this help message"
            echo ""
            echo "AOT Features:"
            echo "  • Native code generation for maximum performance"
            echo "  • IL trimming to reduce binary size"
            echo "  • ReadyToRun image generation"
            echo "  • Assembly trimming and dead code elimination"
            echo ""
            echo "Examples:"
            echo "  $0                                # AOT build for Apple Silicon"
            echo "  $0 --intel --clean               # Clean AOT build for Intel Macs"
            echo "  $0 --universal                   # Universal Binary for both architectures"
            echo "  $0 --optimization size            # Size-optimized AOT build"
            echo "  $0 --increment patch              # Increment version and AOT build"
            echo "  $0 --create-dmg                  # Build and create DMG installer"
            echo "  $0 --create-dmg --sign 'Dev ID'  # Build, sign, and create DMG"
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            print_error "Use -h or --help for usage information"
            exit 1
            ;;
    esac
done

print_status "AOT Build Configuration:"
print_status "  • Configuration: $BUILD_CONFIGURATION"
print_status "  • Runtime ID: $RUNTIME_ID"
print_status "  • Optimization: $OPTIMIZATION_LEVEL"
print_status "  • .NET Version: $DOTNET_VERSION"

case $OPTIMIZATION_LEVEL in
    "size")
        TRIM_MODE="link"
        IL_LINK_MODE="copyused"
        AOT_MODE="partial"
        ;;
    "speed")
        TRIM_MODE="copyused"
        IL_LINK_MODE="copyused"
        AOT_MODE="full"
        ;;
    "aggressive")
        TRIM_MODE="link"
        IL_LINK_MODE="link"
        AOT_MODE="full"
        ;;
    *)
        print_error "Unknown optimization level: $OPTIMIZATION_LEVEL"
        print_error "Supported levels: size, speed, aggressive"
        exit 1
        ;;
esac

print_status "AOT Optimization settings:"
print_status "  • Trim Mode: $TRIM_MODE"
print_status "  • IL Link Mode: $IL_LINK_MODE"
print_status "  • AOT Mode: $AOT_MODE"

if [ -n "$INCREMENT_VERSION" ]; then
    print_status "Incrementing $INCREMENT_VERSION version..."
    if "$SCRIPT_DIR/version.sh" --action increment --part "$INCREMENT_VERSION"; then
        NEW_VERSION=$("$SCRIPT_DIR/version.sh" --action current | grep "Current version:" | cut -d' ' -f3)
        print_success "Version incremented to: $NEW_VERSION"
    else
        print_error "Failed to increment version"
        exit 1
    fi
fi

print_status "Generating build information..."
if "$SCRIPT_DIR/version.sh" --action build; then
    print_success "Build information generated"
else
    print_warning "Could not generate build information (continuing anyway)"
fi

cd "$PROJECT_ROOT"

if [ "$CLEAN_BUILD" = true ]; then
    print_status "Cleaning previous builds..."
    dotnet clean "$DESKTOP_PROJECT" -c "$BUILD_CONFIGURATION" --verbosity minimal
    rm -rf "$PROJECT_ROOT/build" "$PROJECT_ROOT/publish"
    print_success "Build artifacts cleaned"
fi

if [ "$SKIP_RESTORE" = false ]; then
    print_status "Restoring NuGet packages for AOT..."
    dotnet restore "$DESKTOP_PROJECT" --verbosity minimal
    if [ $? -eq 0 ]; then
        print_success "Packages restored successfully"
    else
        print_error "Failed to restore packages"
        exit 1
    fi
fi

if [ "$SKIP_TESTS" = false ]; then
    print_status "Running tests..."
    if dotnet test --verbosity minimal --nologo; then
        print_success "All tests passed"
    else
        print_warning "Some tests failed, but continuing with build..."
    fi
fi

CURRENT_VERSION=$("$SCRIPT_DIR/version.sh" --action current | grep "Current version:" | cut -d' ' -f3 2>/dev/null || echo "1.0.0")

CLEAN_VERSION=$(echo "$CURRENT_VERSION" | sed 's/-build.*//' | sed 's/^v//')

if [[ ! "$CLEAN_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    CLEAN_VERSION="1.0.0"
fi
BUILD_NUMBER=$(date +%H%M)

print_status "Building application with AOT compilation..."
print_status "This may take several minutes for native code generation..."

build_for_runtime() {
    local runtime="$1"
    local output_dir="$PROJECT_ROOT/publish/$runtime"

    print_status "Building for $runtime..."

    dotnet publish "$DESKTOP_PROJECT" \
        -c "$BUILD_CONFIGURATION" \
        -r "$runtime" \
        --self-contained true \
        --output "$output_dir" \
        --verbosity minimal \
        -p:PublishAot=true \
        -p:TrimMode="$TRIM_MODE" \
        -p:PublishTrimmed=true \
        -p:PublishSingleFile=false \
        -p:EnableCompressionInSingleFile=true \
        -p:DebugType=None \
        -p:DebugSymbols=false \
        -p:StripSymbols=true \
        -p:OptimizationPreference=Speed \
        -p:IlcOptimizationPreference=Speed \
        -p:IlcFoldIdenticalMethodBodies=true

    return $?
}

if [ "$RUNTIME_ID" = "universal" ]; then
    print_status "Building Universal Binary for both Intel and Apple Silicon..."

    build_for_runtime "osx-x64"
    if [ $? -ne 0 ]; then
        print_error "Failed to build for Intel (osx-x64)"
        exit 1
    fi

    build_for_runtime "osx-arm64"
    if [ $? -ne 0 ]; then
        print_error "Failed to build for Apple Silicon (osx-arm64)"
        exit 1
    fi

    INTEL_DIR="$PROJECT_ROOT/publish/osx-x64"
    ARM64_DIR="$PROJECT_ROOT/publish/osx-arm64"
    BUILD_OUTPUT_DIR="$PROJECT_ROOT/publish/universal"

    print_status "Creating universal binary..."
    mkdir -p "$BUILD_OUTPUT_DIR"

    cp -r "$ARM64_DIR"/* "$BUILD_OUTPUT_DIR/"

    if [ -f "$INTEL_DIR/Ecliptix" ] && [ -f "$ARM64_DIR/Ecliptix" ]; then
        lipo -create "$INTEL_DIR/Ecliptix" "$ARM64_DIR/Ecliptix" -output "$BUILD_OUTPUT_DIR/Ecliptix"
        print_success "Universal binary created successfully"
    else
        print_error "Could not find executables to create universal binary"
        exit 1
    fi

    shopt -s nullglob
    for intel_lib in "$INTEL_DIR"/*.dylib; do
        if [ -f "$intel_lib" ]; then
            lib_name=$(basename "$intel_lib")
            arm64_lib="$ARM64_DIR/$lib_name"
            if [ -f "$arm64_lib" ]; then
                lipo -create "$intel_lib" "$arm64_lib" -output "$BUILD_OUTPUT_DIR/$lib_name"
                print_status "Created universal binary for $lib_name"
            fi
        fi
    done
    shopt -u nullglob

    RUNTIME_ID="universal"
else

    build_for_runtime "$RUNTIME_ID"
    BUILD_OUTPUT_DIR="$PROJECT_ROOT/publish/$RUNTIME_ID"
fi

if [ $? -eq 0 ]; then
    print_success "AOT compilation completed successfully"
    print_success "Output directory: $BUILD_OUTPUT_DIR"
else
    print_error "AOT build failed"
    exit 1
fi

APP_NAME="Ecliptix"
APP_BUNDLE="$BUILD_OUTPUT_DIR/$APP_NAME.app"
CONTENTS_DIR="$APP_BUNDLE/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"

print_status "Creating optimized macOS app bundle..."

mkdir -p "$MACOS_DIR"
mkdir -p "$RESOURCES_DIR"

if [ -f "$BUILD_OUTPUT_DIR/Ecliptix" ]; then
    mv "$BUILD_OUTPUT_DIR/Ecliptix" "$MACOS_DIR/"
else
    print_error "AOT executable not found: $BUILD_OUTPUT_DIR/Ecliptix"
    exit 1
fi

mv "$BUILD_OUTPUT_DIR"/*.dylib "$MACOS_DIR/" 2>/dev/null || true
mv "$BUILD_OUTPUT_DIR"/*.json "$MACOS_DIR/" 2>/dev/null || true
mv "$BUILD_OUTPUT_DIR"/*.pdb "$MACOS_DIR/" 2>/dev/null || true

ICON_SOURCE="$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core/Assets/Branding/Platform/macOS/EcliptixLogo.icns"
if [ -f "$ICON_SOURCE" ]; then
    cp "$ICON_SOURCE" "$RESOURCES_DIR/AppIcon.icns"
    chmod 644 "$RESOURCES_DIR/AppIcon.icns"
    print_success "Icon copied to app bundle: $RESOURCES_DIR/AppIcon.icns"
else
    print_warning "Icon file not found at $ICON_SOURCE, app bundle will use default icon"

    ALT_ICON_SOURCE="$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core/Assets/EcliptixLogo.icns"
    if [ -f "$ALT_ICON_SOURCE" ]; then
        cp "$ALT_ICON_SOURCE" "$RESOURCES_DIR/AppIcon.icns"
        chmod 644 "$RESOURCES_DIR/AppIcon.icns"
        print_success "Icon copied from alternative location"
    fi
fi

print_status "Creating Info.plist for AOT build..."
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
    <string>$CLEAN_VERSION</string>
    <key>CFBundleVersion</key>
    <string>$BUILD_NUMBER</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
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
    <key>LSArchitecturePriority</key>
    <array>
        $(if [[ "$RUNTIME_ID" == "osx-arm64" ]]; then echo "<string>arm64</string>"; else echo "<string>x86_64</string>"; fi)
    </array>
    <key>LSMultipleInstancesProhibited</key>
    <true/>
    <key>NSAppTransportSecurity</key>
    <dict>
        <key>NSAllowsArbitraryLoads</key>
        <false/>
        <key>NSExceptionDomains</key>
        <dict/>
    </dict>
</dict>
</plist>
EOF

find "$APP_BUNDLE" -type f -exec chmod 644 {} \;
find "$APP_BUNDLE" -type d -exec chmod 755 {} \;
chmod +x "$MACOS_DIR/Ecliptix"

print_success "AOT macOS app bundle created: $APP_BUNDLE"

touch "$APP_BUNDLE"

print_status "Clearing local icon cache..."
rm -rf ~/Library/Caches/com.apple.iconservices.store 2>/dev/null || true
killall Dock 2>/dev/null || true
print_success "Icon cache cleared (Dock will restart)"

if command -v du &> /dev/null; then
    BUNDLE_SIZE=$(du -sh "$APP_BUNDLE" | cut -f1)
    EXECUTABLE_SIZE=$(du -sh "$MACOS_DIR/Ecliptix" | cut -f1)
    print_status "Bundle size: $BUNDLE_SIZE"
    print_status "Executable size: $EXECUTABLE_SIZE"
fi

print_status "Creating distributable archive..."
cd "$BUILD_OUTPUT_DIR"
ARCHIVE_NAME="Ecliptix-$CLEAN_VERSION-$RUNTIME_ID-AOT.tar.gz"
tar -czf "$ARCHIVE_NAME" "$APP_NAME.app"
cd - >/dev/null

if [ -f "$BUILD_OUTPUT_DIR/$ARCHIVE_NAME" ]; then
    ARCHIVE_SIZE=$(du -sh "$BUILD_OUTPUT_DIR/$ARCHIVE_NAME" | cut -f1)
    print_success "Archive created: $ARCHIVE_NAME ($ARCHIVE_SIZE)"
fi

echo ""
print_success "🎉 AOT Build completed successfully!"
echo ""
echo "📦 AOT Build Summary:"
echo "   Version: $CLEAN_VERSION"
echo "   Build Number: $BUILD_NUMBER"
echo "   Configuration: $BUILD_CONFIGURATION"
echo "   Runtime: $RUNTIME_ID"
echo "   Optimization: $OPTIMIZATION_LEVEL"
echo "   Trim Mode: $TRIM_MODE"
echo "   App Bundle: $APP_BUNDLE"
if [ -n "$BUNDLE_SIZE" ]; then
    echo "   Bundle Size: $BUNDLE_SIZE"
    echo "   Executable Size: $EXECUTABLE_SIZE"
fi
echo ""
echo "🚀 AOT Benefits:"
echo "   ✓ Native machine code compilation"
echo "   ✓ Faster startup time"
echo "   ✓ Reduced memory footprint"
echo "   ✓ Self-contained deployment"
echo "   ✓ IL trimming and dead code elimination"
echo ""
echo "🧪 To test the AOT application:"
echo "   open '$APP_BUNDLE'"
echo ""
echo "🎨 If the icon doesn't appear in the Dock/Toolbar:"
echo "   1. Quit the app completely"
echo "   2. Clear icon cache: rm -rf ~/Library/Caches/com.apple.iconservices.store"
echo "   3. Restart Dock: killall Dock"
echo "   4. Relaunch the app: open '$APP_BUNDLE'"
echo ""
echo "📋 Next steps for distribution:"
echo "   1. Test thoroughly: open '$APP_BUNDLE'"
echo "   2. Sign for distribution: codesign -s 'Developer ID Application: Your Name' '$APP_BUNDLE'"
echo "   3. Verify signature: codesign -v -d '$APP_BUNDLE'"
echo "   4. Notarize: xcrun notarytool submit '$BUILD_OUTPUT_DIR/$ARCHIVE_NAME' --keychain-profile 'notarytool-profile'"
echo "   5. Staple notarization: xcrun stapler staple '$APP_BUNDLE'"
echo ""

if [ "$CREATE_DMG" = true ]; then
    echo ""
    print_status "Creating DMG installer..."

    DMG_SCRIPT="$SCRIPT_DIR/create-macos-installer.sh"

    if [ ! -f "$DMG_SCRIPT" ]; then
        print_error "DMG creation script not found: $DMG_SCRIPT"
        exit 1
    fi

    if [ -n "$CODESIGN_IDENTITY" ]; then
        "$DMG_SCRIPT" -a "$APP_BUNDLE" -s "$CODESIGN_IDENTITY"
    else
        "$DMG_SCRIPT" -a "$APP_BUNDLE"
    fi

    if [ $? -eq 0 ]; then
        print_success "DMG installer created successfully!"
    else
        print_error "Failed to create DMG installer"
        exit 1
    fi
fi