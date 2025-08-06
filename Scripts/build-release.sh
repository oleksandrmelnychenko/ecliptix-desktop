#!/bin/bash

# Ecliptix Desktop Release Build Script
# This script builds release versions for all supported platforms

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
DESKTOP_PROJECT="$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core.Desktop/Ecliptix.Core.Desktop.csproj"
OUTPUT_DIR="$PROJECT_ROOT/builds"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}Ecliptix Desktop Release Build${NC}"
echo "=================================="

# Parse command line arguments
VERSION_ACTION=""
VERSION_PART=""
BUILD_PLATFORMS="all"
CLEAN_BUILD=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --increment)
            VERSION_ACTION="increment"
            VERSION_PART="$2"
            shift 2
            ;;
        --version)
            VERSION_ACTION="set"
            NEW_VERSION="$2"
            shift 2
            ;;
        --platforms)
            BUILD_PLATFORMS="$2"
            shift 2
            ;;
        --clean)
            CLEAN_BUILD=true
            shift
            ;;
        --help)
            echo "Usage: $0 [options]"
            echo ""
            echo "Version Options:"
            echo "  --increment <part>     Increment version part (major|minor|patch)"
            echo "  --version <version>    Set specific version (e.g., 1.2.3)"
            echo ""
            echo "Build Options:"
            echo "  --platforms <list>     Comma-separated platform list (win,mac,linux) or 'all'"
            echo "  --clean               Clean build artifacts before building"
            echo ""
            echo "Examples:"
            echo "  $0 --increment patch --platforms all"
            echo "  $0 --version 1.0.0 --platforms win,mac --clean"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Update version if requested
if [[ -n "$VERSION_ACTION" ]]; then
    echo -e "${YELLOW}Updating version...${NC}"
    if [[ "$VERSION_ACTION" == "increment" ]]; then
        "$SCRIPT_DIR/version.sh" --action increment --part "$VERSION_PART"
    elif [[ "$VERSION_ACTION" == "set" ]]; then
        "$SCRIPT_DIR/version.sh" --action set --version "$NEW_VERSION"
    fi
fi

# Generate build info
echo -e "${YELLOW}Generating build information...${NC}"
"$SCRIPT_DIR/version.sh" --action build

# Get current version for build naming
CURRENT_VERSION=$("$SCRIPT_DIR/version.sh" --action current | grep "Current version:" | cut -d' ' -f3)
BUILD_NUMBER=$(date +%y%m%d.%H%M)
BUILD_VERSION="${CURRENT_VERSION}-build.${BUILD_NUMBER}"

echo -e "${GREEN}Building version: $BUILD_VERSION${NC}"

# Clean build artifacts if requested
if [[ "$CLEAN_BUILD" == true ]]; then
    echo -e "${YELLOW}Cleaning build artifacts...${NC}"
    dotnet clean "$PROJECT_ROOT" -c Release
    rm -rf "$OUTPUT_DIR"
fi

# Create output directory
mkdir -p "$OUTPUT_DIR"

# Define platform configurations  
get_runtime() {
    case "$1" in
        "win") echo "win-x64" ;;
        "mac-intel") echo "osx-x64" ;;
        "mac-arm") echo "osx-arm64" ;;
        "linux") echo "linux-x64" ;;
        *) echo "" ;;
    esac
}

# Determine which platforms to build
BUILD_LIST=()
if [[ "$BUILD_PLATFORMS" == "all" ]]; then
    BUILD_LIST=("win" "mac-intel" "mac-arm" "linux")
else
    IFS=',' read -ra PLATFORM_ARRAY <<< "$BUILD_PLATFORMS"
    for platform in "${PLATFORM_ARRAY[@]}"; do
        runtime=$(get_runtime "$platform")
        if [[ -n "$runtime" ]]; then
            BUILD_LIST+=("$platform")
        else
            echo -e "${RED}Warning: Unknown platform '$platform'. Supported: win, mac-intel, mac-arm, linux${NC}"
        fi
    done
fi

# Build each platform
for platform in "${BUILD_LIST[@]}"; do
    runtime=$(get_runtime "$platform")
    platform_output="$OUTPUT_DIR/ecliptix-$platform-$CURRENT_VERSION"
    
    echo -e "${BLUE}Building for $platform ($runtime)...${NC}"
    
    # Build command with optimizations for each platform
    dotnet publish "$DESKTOP_PROJECT" \
        -c Release \
        -r "$runtime" \
        --self-contained true \
        -p:PublishAot=true \
        -p:TrimMode=link \
        -p:PublishTrimmed=true \
        -p:PublishSingleFile=false \
        -p:BuildNumber="$BUILD_NUMBER" \
        -o "$platform_output" \
        --verbosity minimal
    
    if [[ $? -eq 0 ]]; then
        # Copy build info to output
        cp "$PROJECT_ROOT/build-info.json" "$platform_output/" 2>/dev/null || true
        
        # Platform-specific post-build steps
        case $platform in
            "mac-intel"|"mac-arm")
                echo -e "${YELLOW}Creating macOS application bundle...${NC}"
                create_mac_app "$platform_output" "$CURRENT_VERSION"
                ;;
            "linux")
                echo -e "${YELLOW}Setting Linux executable permissions...${NC}"
                chmod +x "$platform_output/Ecliptix"
                ;;
        esac
        
        # Create archive
        echo -e "${YELLOW}Creating archive for $platform...${NC}"
        cd "$OUTPUT_DIR"
        case $platform in
            "win")
                zip -r "ecliptix-$platform-$CURRENT_VERSION.zip" "ecliptix-$platform-$CURRENT_VERSION/" >/dev/null
                ;;
            *)
                tar -czf "ecliptix-$platform-$CURRENT_VERSION.tar.gz" "ecliptix-$platform-$CURRENT_VERSION/" >/dev/null
                ;;
        esac
        cd - >/dev/null
        
        echo -e "${GREEN}✓ $platform build completed${NC}"
    else
        echo -e "${RED}✗ $platform build failed${NC}"
    fi
done

# Function to create macOS app bundle
create_mac_app() {
    local build_path="$1"
    local version="$2"
    local app_name="Ecliptix.app"
    local app_path="$build_path/$app_name"
    
    # Create app bundle structure
    mkdir -p "$app_path/Contents/MacOS"
    mkdir -p "$app_path/Contents/Resources"
    
    # Move executable and dependencies
    mv "$build_path/Ecliptix" "$app_path/Contents/MacOS/"
    mv "$build_path"/*.dll "$app_path/Contents/MacOS/" 2>/dev/null || true
    mv "$build_path"/*.dylib "$app_path/Contents/MacOS/" 2>/dev/null || true
    mv "$build_path"/*.json "$app_path/Contents/MacOS/" 2>/dev/null || true
    
    # Copy icon
    if [[ -f "$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core/Assets/logo.icns" ]]; then
        cp "$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core/Assets/logo.icns" "$app_path/Contents/Resources/"
    fi
    
    # Create Info.plist
    cat > "$app_path/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDisplayName</key>
    <string>Ecliptix</string>
    <key>CFBundleExecutable</key>
    <string>Ecliptix</string>
    <key>CFBundleIconFile</key>
    <string>logo</string>
    <key>CFBundleIdentifier</key>
    <string>com.ecliptix.desktop</string>
    <key>CFBundleName</key>
    <string>Ecliptix</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$version</string>
    <key>CFBundleVersion</key>
    <string>$version</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
EOF
    
    # Make executable
    chmod +x "$app_path/Contents/MacOS/Ecliptix"
}

echo -e "${GREEN}Build completed successfully!${NC}"
echo -e "${BLUE}Build artifacts are available in: $OUTPUT_DIR${NC}"

# Show build summary
echo ""
echo -e "${BLUE}Build Summary:${NC}"
echo "Version: $BUILD_VERSION"
echo "Platforms built: ${BUILD_LIST[*]}"
echo "Output directory: $OUTPUT_DIR"

# List created files
echo ""
echo -e "${BLUE}Created files:${NC}"
for file in "$OUTPUT_DIR"/*.{zip,tar.gz} 2>/dev/null; do
    if [[ -f "$file" ]]; then
        size=$(du -h "$file" | cut -f1)
        echo "  $(basename "$file") ($size)"
    fi
done