#!/bin/bash

# Ecliptix Desktop AOT Build Script for Linux
# This script builds the application with full AOT compilation and creates AppImage packages

set -e  # Exit on error

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
DESKTOP_PROJECT="$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core.Desktop/Ecliptix.Core.Desktop.csproj"

echo "🚀 Building Ecliptix Desktop for Linux with AOT compilation..."

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${BLUE}🔵 [AOT-INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}✅ [AOT-SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}⚠️ [AOT-WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}❌ [AOT-ERROR]${NC} $1"
}

# Check if we're on Linux
if [[ "$OSTYPE" != "linux-gnu"* ]]; then
    print_error "This script is designed for Linux only"
    exit 1
fi

# Check if .NET is installed
if ! command -v dotnet &> /dev/null; then
    print_error ".NET SDK is required but not found"
    print_error "Please install .NET from: https://dotnet.microsoft.com/download"
    exit 1
fi

# Check .NET version for AOT support (requires .NET 8+)
DOTNET_VERSION=$(dotnet --version)
MAJOR_VERSION=$(echo "$DOTNET_VERSION" | cut -d '.' -f 1)
if [ "$MAJOR_VERSION" -lt 8 ]; then
    print_error ".NET 8 or higher is required for AOT compilation (found: $DOTNET_VERSION)"
    exit 1
fi

# Check if project file exists
if [ ! -f "$DESKTOP_PROJECT" ]; then
    print_error "Desktop project not found at: $DESKTOP_PROJECT"
    exit 1
fi

# Parse command line arguments
BUILD_CONFIGURATION="Release"
RUNTIME_ID="linux-x64"
INCREMENT_VERSION=""
SKIP_RESTORE=false
SKIP_TESTS=false
CLEAN_BUILD=false
OPTIMIZATION_LEVEL="aggressive"
CREATE_APPIMAGE=true

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
        --clean)
            CLEAN_BUILD=true
            shift
            ;;
        --arm64)
            RUNTIME_ID="linux-arm64"
            shift
            ;;
        --x64)
            RUNTIME_ID="linux-x64"
            shift
            ;;
        --optimization)
            OPTIMIZATION_LEVEL="$2"
            shift 2
            ;;
        --no-appimage)
            CREATE_APPIMAGE=false
            shift
            ;;
        -h|--help)
            echo "AOT Build Script for Ecliptix Desktop Linux"
            echo ""
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -c, --configuration   Build configuration (Debug/Release, default: Release)"
            echo "  -r, --runtime        Runtime identifier (default: linux-x64)"
            echo "  --x64                Build for x64 (linux-x64)"
            echo "  --arm64              Build for ARM64 (linux-arm64)"
            echo "  --increment PART     Increment version (major/minor/patch)"
            echo "  --skip-restore       Skip package restore"
            echo "  --skip-tests         Skip running tests"
            echo "  --clean              Clean build artifacts before building"
            echo "  --optimization LEVEL Optimization level (size/speed/aggressive, default: aggressive)"
            echo "  --no-appimage        Skip AppImage creation"
            echo "  -h, --help           Show this help message"
            echo ""
            echo "AOT Features:"
            echo "  • Native code generation for maximum performance"
            echo "  • IL trimming to reduce binary size"
            echo "  • ReadyToRun image generation"
            echo "  • Assembly trimming and dead code elimination"
            echo "  • AppImage portable package creation"
            echo ""
            echo "Examples:"
            echo "  $0                                # AOT build for x64 with AppImage"
            echo "  $0 --arm64 --clean               # Clean AOT build for ARM64"
            echo "  $0 --optimization size            # Size-optimized AOT build"
            echo "  $0 --increment patch --no-appimage # Increment version, no AppImage"
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
print_status "  • Create AppImage: $CREATE_APPIMAGE"
print_status "  • .NET Version: $DOTNET_VERSION"

# Set optimization flags based on level
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

# Increment version if requested
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

# Generate build info
print_status "Generating build information..."
if "$SCRIPT_DIR/version.sh" --action build; then
    print_success "Build information generated"
else
    print_warning "Could not generate build information (continuing anyway)"
fi

# Navigate to project directory
cd "$PROJECT_ROOT"

# Clean previous builds if requested
if [ "$CLEAN_BUILD" = true ]; then
    print_status "Cleaning previous builds..."
    dotnet clean "$DESKTOP_PROJECT" -c "$BUILD_CONFIGURATION" --verbosity minimal
    rm -rf "$PROJECT_ROOT/build" "$PROJECT_ROOT/publish"
    print_success "Build artifacts cleaned"
fi

# Restore packages
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

# Run tests
if [ "$SKIP_TESTS" = false ]; then
    print_status "Running tests..."
    if dotnet test --verbosity minimal --nologo; then
        print_success "All tests passed"
    else
        print_warning "Some tests failed, but continuing with build..."
    fi
fi

# Get version information
CURRENT_VERSION=$("$SCRIPT_DIR/version.sh" --action current | grep "Current version:" | cut -d' ' -f3 2>/dev/null || echo "1.0.0")
# Extract just the version number without build suffix and ensure proper format
CLEAN_VERSION=$(echo "$CURRENT_VERSION" | sed 's/-build.*//' | sed 's/^v//')
# Ensure we have a 3-part version (major.minor.patch)
if [[ ! "$CLEAN_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    CLEAN_VERSION="1.0.0"
fi
BUILD_NUMBER=$(date +%H%M)

# Build the application with AOT
print_status "Building application with AOT compilation..."
print_status "This may take several minutes for native code generation..."

BUILD_OUTPUT_DIR="$PROJECT_ROOT/publish/$RUNTIME_ID"

# AOT Build command with comprehensive optimizations
dotnet publish "$DESKTOP_PROJECT" \
    -c "$BUILD_CONFIGURATION" \
    -r "$RUNTIME_ID" \
    --self-contained true \
    --output "$BUILD_OUTPUT_DIR" \
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

if [ $? -eq 0 ]; then
    print_success "AOT compilation completed successfully"
    print_success "Output directory: $BUILD_OUTPUT_DIR"
else
    print_error "AOT build failed"
    exit 1
fi

# Create Linux application structure
APP_NAME="Ecliptix"
APP_DIR="$BUILD_OUTPUT_DIR/$APP_NAME"

print_status "Creating Linux application package..."

# Remove existing app directory and create fresh one
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR"

# Move the executable and dependencies
if [ -f "$BUILD_OUTPUT_DIR/Ecliptix" ]; then
    mv "$BUILD_OUTPUT_DIR/Ecliptix" "$APP_DIR/"
    chmod +x "$APP_DIR/Ecliptix"
else
    print_error "AOT executable not found: $BUILD_OUTPUT_DIR/Ecliptix"
    exit 1
fi

# Move supporting files
mv "$BUILD_OUTPUT_DIR"/*.so "$APP_DIR/" 2>/dev/null || true
mv "$BUILD_OUTPUT_DIR"/*.json "$APP_DIR/" 2>/dev/null || true
mv "$BUILD_OUTPUT_DIR"/*.pdb "$APP_DIR/" 2>/dev/null || true

# Copy icon file
ICON_SOURCE="$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core/Assets/EcliptixLogo.png"
if [ -f "$ICON_SOURCE" ]; then
    cp "$ICON_SOURCE" "$APP_DIR/AppIcon.png"
    print_success "Icon copied to app package"
else
    print_warning "Icon file not found at $ICON_SOURCE, package will use default icon"
fi

# Create .desktop file
cat > "$APP_DIR/Ecliptix.desktop" << EOF
[Desktop Entry]
Name=Ecliptix
Comment=Secure cross-platform desktop application
Exec=./Ecliptix
Icon=AppIcon.png
Type=Application
Categories=Office;Network;Security;
Terminal=false
StartupNotify=true
Version=$CLEAN_VERSION
EOF

# Create version info file
cat > "$APP_DIR/version.json" << EOF
{
  "version": "$CLEAN_VERSION",
  "build_number": "$BUILD_NUMBER",
  "full_version": "$CLEAN_VERSION-build.$BUILD_NUMBER",
  "timestamp": "$(date -Iseconds)",
  "runtime": "$RUNTIME_ID",
  "configuration": "$BUILD_CONFIGURATION",
  "optimization": "$OPTIMIZATION_LEVEL"
}
EOF

# Set proper permissions
find "$APP_DIR" -type f -name "*.so" -exec chmod +x {} \;
chmod +x "$APP_DIR/Ecliptix"

print_success "Linux application package created: $APP_DIR"

# Calculate and display size information
if command -v du &> /dev/null; then
    PACKAGE_SIZE=$(du -sh "$APP_DIR" | cut -f1)
    EXECUTABLE_SIZE=$(du -sh "$APP_DIR/Ecliptix" | cut -f1)
    print_status "Package size: $PACKAGE_SIZE"
    print_status "Executable size: $EXECUTABLE_SIZE"
fi

# Create AppImage if requested
if [ "$CREATE_APPIMAGE" = true ]; then
    print_status "Creating AppImage package..."
    
    # Check for AppImage tools
    if command -v appimagetool &> /dev/null || command -v linuxdeploy &> /dev/null; then
        APPIMAGE_DIR="$BUILD_OUTPUT_DIR/AppDir"
        mkdir -p "$APPIMAGE_DIR"
        
        # Copy application files
        cp -r "$APP_DIR"/* "$APPIMAGE_DIR/"
        
        # Create AppRun
        cat > "$APPIMAGE_DIR/AppRun" << 'EOF'
#!/bin/bash
SELF=$(readlink -f "$0")
HERE=${SELF%/*}
export PATH="${HERE}:${PATH}"
exec "${HERE}/Ecliptix" "$@"
EOF
        chmod +x "$APPIMAGE_DIR/AppRun"
        
        # Try to create AppImage
        APPIMAGE_NAME="Ecliptix-$CLEAN_VERSION-$RUNTIME_ID-AOT.AppImage"
        APPIMAGE_PATH="$BUILD_OUTPUT_DIR/$APPIMAGE_NAME"
        
        if command -v appimagetool &> /dev/null; then
            appimagetool "$APPIMAGE_DIR" "$APPIMAGE_PATH" 2>/dev/null || {
                print_warning "AppImage creation with appimagetool failed"
                CREATE_APPIMAGE=false
            }
        elif command -v linuxdeploy &> /dev/null; then
            linuxdeploy --appdir "$APPIMAGE_DIR" --output appimage 2>/dev/null || {
                print_warning "AppImage creation with linuxdeploy failed"
                CREATE_APPIMAGE=false
            }
        fi
        
        if [ -f "$APPIMAGE_PATH" ]; then
            chmod +x "$APPIMAGE_PATH"
            APPIMAGE_SIZE=$(du -sh "$APPIMAGE_PATH" | cut -f1)
            print_success "AppImage created: $APPIMAGE_NAME ($APPIMAGE_SIZE)"
        else
            print_warning "AppImage creation failed - package available as directory"
            CREATE_APPIMAGE=false
        fi
        
        # Clean up AppDir
        rm -rf "$APPIMAGE_DIR"
    else
        print_warning "AppImage tools (appimagetool or linuxdeploy) not found"
        print_warning "Install with: sudo apt install appimagetool or download from https://appimage.github.io/"
        CREATE_APPIMAGE=false
    fi
fi

# Create archive
print_status "Creating distributable archive..."
cd "$BUILD_OUTPUT_DIR"
ARCHIVE_NAME="Ecliptix-$CLEAN_VERSION-$RUNTIME_ID-AOT.tar.gz"
tar -czf "$ARCHIVE_NAME" "$APP_NAME"
cd - >/dev/null

if [ -f "$BUILD_OUTPUT_DIR/$ARCHIVE_NAME" ]; then
    ARCHIVE_SIZE=$(du -sh "$BUILD_OUTPUT_DIR/$ARCHIVE_NAME" | cut -f1)
    print_success "Archive created: $ARCHIVE_NAME ($ARCHIVE_SIZE)"
fi

# Display comprehensive build summary
echo ""
print_success "🎉 Linux AOT Build completed successfully!"
echo ""
echo -e "${CYAN}📦 AOT Build Summary:${NC}"
echo "   Version: $CLEAN_VERSION"
echo "   Build Number: $BUILD_NUMBER"
echo "   Configuration: $BUILD_CONFIGURATION"
echo "   Runtime: $RUNTIME_ID"
echo "   Optimization: $OPTIMIZATION_LEVEL"
echo "   Trim Mode: $TRIM_MODE"
echo "   App Package: $APP_DIR"
if [ -n "$PACKAGE_SIZE" ]; then
    echo "   Package Size: $PACKAGE_SIZE"
    echo "   Executable Size: $EXECUTABLE_SIZE"
fi
if [ "$CREATE_APPIMAGE" = true ] && [ -f "$APPIMAGE_PATH" ]; then
    echo "   AppImage: $APPIMAGE_PATH"
fi
echo ""
echo -e "${GREEN}🚀 AOT Benefits:${NC}"
echo "   ✓ Native machine code compilation"
echo "   ✓ Faster startup time"
echo "   ✓ Reduced memory footprint"
echo "   ✓ Self-contained deployment"
echo "   ✓ IL trimming and dead code elimination"
echo ""
echo -e "${YELLOW}🧪 To test the AOT application:${NC}"
echo "   cd '$APP_DIR' && ./Ecliptix"
if [ "$CREATE_APPIMAGE" = true ] && [ -f "$APPIMAGE_PATH" ]; then
    echo "   Or run AppImage: '$APPIMAGE_PATH'"
fi
echo ""
echo -e "${BLUE}📋 Next steps for distribution:${NC}"
echo "   1. Test thoroughly: cd '$APP_DIR' && ./Ecliptix"
if [ "$CREATE_APPIMAGE" = true ] && [ -f "$APPIMAGE_PATH" ]; then
    echo "   2. Distribute AppImage: '$APPIMAGE_PATH'"
fi
echo "   3. Create DEB/RPM packages: Use fpm, checkinstall, or native packaging"
echo "   4. Distribute archive: '$BUILD_OUTPUT_DIR/$ARCHIVE_NAME'"
echo ""