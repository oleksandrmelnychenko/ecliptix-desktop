#!/bin/bash

# Ecliptix Desktop - Build All Platforms Script
# This script orchestrates building for Windows, macOS, and Linux

set -e  # Exit on error

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
NC='\033[0m' # No Color

# Function to print colored output
print_header() {
    echo -e "${MAGENTA}========================================${NC}"
    echo -e "${MAGENTA}$1${NC}"
    echo -e "${MAGENTA}========================================${NC}"
}

print_status() {
    echo -e "${BLUE}ℹ️  [INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}✅ [SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}⚠️  [WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}❌ [ERROR]${NC} $1"
}

# Parse command line arguments
BUILD_CONFIGURATION="Release"
BUILD_LINUX=false
BUILD_MACOS=false
BUILD_WINDOWS=false
BUILD_ALL=false
INCREMENT_VERSION=""
SKIP_TESTS=false
CLEAN_BUILD=false
OPTIMIZATION_LEVEL="aggressive"

show_help() {
    cat << EOF
${CYAN}Ecliptix Desktop - Multi-Platform Build Script${NC}

${YELLOW}Usage:${NC} $0 [OPTIONS]

${YELLOW}Options:${NC}
  -a, --all              Build for all platforms
  -l, --linux            Build for Linux (x64)
  -m, --macos            Build for macOS (Universal Binary)
  -w, --windows          Build for Windows (x64)

  -c, --configuration    Build configuration (Debug/Release, default: Release)
  --increment PART       Increment version (major/minor/patch)
  --skip-tests           Skip running tests
  --clean                Clean build artifacts before building
  --optimization LEVEL   Optimization level (size/speed/aggressive, default: aggressive)

  -h, --help             Show this help message

${YELLOW}Examples:${NC}
  $0 --all                          # Build for all platforms
  $0 --linux --macos                # Build for Linux and macOS only
  $0 --all --increment patch        # Increment version and build all
  $0 --windows --clean              # Clean build for Windows only
  $0 --all --optimization size      # Size-optimized builds for all platforms

${YELLOW}Platform-Specific Notes:${NC}
  • ${GREEN}Linux${NC}: Can be built natively on Linux systems
  • ${GREEN}macOS${NC}: Requires macOS system with Xcode Command Line Tools
  • ${GREEN}Windows${NC}: Requires Windows system with PowerShell

  ${CYAN}Cross-compilation is not supported. Each platform should be built on its native OS.${NC}

${YELLOW}Output:${NC}
  All builds will be created in: ${CYAN}publish/${NC}
  - Linux: ${CYAN}publish/linux-x64/${NC}
  - macOS: ${CYAN}publish/universal/${NC} (or osx-arm64/osx-x64)
  - Windows: ${CYAN}publish/win-x64/${NC}

EOF
}

while [[ $# -gt 0 ]]; do
    case $1 in
        -a|--all)
            BUILD_ALL=true
            BUILD_LINUX=true
            BUILD_MACOS=true
            BUILD_WINDOWS=true
            shift
            ;;
        -l|--linux)
            BUILD_LINUX=true
            shift
            ;;
        -m|--macos)
            BUILD_MACOS=true
            shift
            ;;
        -w|--windows)
            BUILD_WINDOWS=true
            shift
            ;;
        -c|--configuration)
            BUILD_CONFIGURATION="$2"
            shift 2
            ;;
        --increment)
            INCREMENT_VERSION="$2"
            shift 2
            ;;
        --skip-tests)
            SKIP_TESTS=true
            shift
            ;;
        --clean)
            CLEAN_BUILD=true
            shift
            ;;
        --optimization)
            OPTIMIZATION_LEVEL="$2"
            shift 2
            ;;
        -h|--help)
            show_help
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            echo "Use -h or --help for usage information"
            exit 1
            ;;
    esac
done

# If no platform specified, show help
if [ "$BUILD_LINUX" = false ] && [ "$BUILD_MACOS" = false ] && [ "$BUILD_WINDOWS" = false ]; then
    print_warning "No platform specified. Use --all to build for all platforms, or specify individual platforms."
    echo ""
    show_help
    exit 1
fi

# Display build configuration
print_header "Ecliptix Desktop - Multi-Platform Build"
echo ""
print_status "Build Configuration:"
print_status "  • Configuration: $BUILD_CONFIGURATION"
print_status "  • Optimization: $OPTIMIZATION_LEVEL"
print_status "  • Platforms:"
[ "$BUILD_LINUX" = true ] && print_status "    - Linux (x64)"
[ "$BUILD_MACOS" = true ] && print_status "    - macOS (Universal Binary)"
[ "$BUILD_WINDOWS" = true ] && print_status "    - Windows (x64)"
echo ""

# Build arguments
BUILD_ARGS=""
[ -n "$INCREMENT_VERSION" ] && BUILD_ARGS="$BUILD_ARGS --increment $INCREMENT_VERSION"
[ "$SKIP_TESTS" = true ] && BUILD_ARGS="$BUILD_ARGS --skip-tests"
[ "$CLEAN_BUILD" = true ] && BUILD_ARGS="$BUILD_ARGS --clean"
BUILD_ARGS="$BUILD_ARGS --configuration $BUILD_CONFIGURATION"
BUILD_ARGS="$BUILD_ARGS --optimization $OPTIMIZATION_LEVEL"

# Track build status
BUILDS_SUCCEEDED=0
BUILDS_FAILED=0
BUILDS_SKIPPED=0
BUILD_RESULTS=()

# Detect current OS
CURRENT_OS="unknown"
case "$OSTYPE" in
    linux-gnu*)   CURRENT_OS="linux" ;;
    darwin*)      CURRENT_OS="macos" ;;
    msys*|cygwin*|win32) CURRENT_OS="windows" ;;
esac

print_status "Detected OS: $CURRENT_OS"
echo ""

# Build for Linux
if [ "$BUILD_LINUX" = true ]; then
    print_header "Building for Linux (x64)"

    if [ "$CURRENT_OS" = "linux" ]; then
        if bash "$SCRIPT_DIR/build-aot-linux.sh" $BUILD_ARGS --x64; then
            print_success "Linux build completed successfully"
            BUILD_RESULTS+=("✅ Linux (x64): SUCCESS")
            ((BUILDS_SUCCEEDED++))
        else
            print_error "Linux build failed"
            BUILD_RESULTS+=("❌ Linux (x64): FAILED")
            ((BUILDS_FAILED++))
        fi
    else
        print_warning "Cannot build Linux binaries on $CURRENT_OS"
        print_warning "Please run this script on a Linux system to build for Linux"
        BUILD_RESULTS+=("⚠️  Linux (x64): SKIPPED (not on Linux)")
        ((BUILDS_SKIPPED++))
    fi
    echo ""
fi

# Build for macOS
if [ "$BUILD_MACOS" = true ]; then
    print_header "Building for macOS (Universal Binary)"

    if [ "$CURRENT_OS" = "macos" ]; then
        if bash "$SCRIPT_DIR/build-aot-macos.sh" $BUILD_ARGS --universal; then
            print_success "macOS build completed successfully"
            BUILD_RESULTS+=("✅ macOS (Universal): SUCCESS")
            ((BUILDS_SUCCEEDED++))
        else
            print_error "macOS build failed"
            BUILD_RESULTS+=("❌ macOS (Universal): FAILED")
            ((BUILDS_FAILED++))
        fi
    else
        print_warning "Cannot build macOS binaries on $CURRENT_OS"
        print_warning "Please run this script on a macOS system to build for macOS"
        BUILD_RESULTS+=("⚠️  macOS (Universal): SKIPPED (not on macOS)")
        ((BUILDS_SKIPPED++))
    fi
    echo ""
fi

# Build for Windows
if [ "$BUILD_WINDOWS" = true ]; then
    print_header "Building for Windows (x64)"

    if [ "$CURRENT_OS" = "windows" ]; then
        if pwsh "$SCRIPT_DIR/build-aot-windows.ps1" -Configuration $BUILD_CONFIGURATION -Runtime win-x64 -Optimization $OPTIMIZATION_LEVEL; then
            print_success "Windows build completed successfully"
            BUILD_RESULTS+=("✅ Windows (x64): SUCCESS")
            ((BUILDS_SUCCEEDED++))
        else
            print_error "Windows build failed"
            BUILD_RESULTS+=("❌ Windows (x64): FAILED")
            ((BUILDS_FAILED++))
        fi
    else
        print_warning "Cannot build Windows binaries on $CURRENT_OS"
        print_warning "Please run the build script on a Windows system:"
        print_warning "  PowerShell: .\\Scripts\\build-aot-windows.ps1"
        BUILD_RESULTS+=("⚠️  Windows (x64): SKIPPED (not on Windows)")
        ((BUILDS_SKIPPED++))
    fi
    echo ""
fi

# Display final summary
print_header "Build Summary"
echo ""

for result in "${BUILD_RESULTS[@]}"; do
    echo "$result"
done

echo ""
print_status "Total: $((BUILDS_SUCCEEDED + BUILDS_FAILED + BUILDS_SKIPPED)) builds"
print_success "Succeeded: $BUILDS_SUCCEEDED"
[ $BUILDS_FAILED -gt 0 ] && print_error "Failed: $BUILDS_FAILED"
[ $BUILDS_SKIPPED -gt 0 ] && print_warning "Skipped: $BUILDS_SKIPPED"

echo ""

# Exit with appropriate code
if [ $BUILDS_FAILED -gt 0 ]; then
    print_error "Some builds failed. Please check the output above for details."
    exit 1
elif [ $BUILDS_SKIPPED -gt 0 ] && [ $BUILDS_SUCCEEDED -eq 0 ]; then
    print_warning "All builds were skipped. Run on the appropriate OS for each platform."
    echo ""
    print_status "To build for all platforms:"
    print_status "  • On Linux: ./Scripts/build-all.sh --linux"
    print_status "  • On macOS: ./Scripts/build-all.sh --macos"
    print_status "  • On Windows: .\\Scripts\\build-aot-windows.ps1"
    exit 0
else
    print_success "All requested builds completed successfully!"
    echo ""
    print_status "Build artifacts can be found in:"
    [ "$BUILD_LINUX" = true ] && [ "$CURRENT_OS" = "linux" ] && print_status "  • Linux: $PROJECT_ROOT/publish/linux-x64/"
    [ "$BUILD_MACOS" = true ] && [ "$CURRENT_OS" = "macos" ] && print_status "  • macOS: $PROJECT_ROOT/publish/universal/"
    [ "$BUILD_WINDOWS" = true ] && [ "$CURRENT_OS" = "windows" ] && print_status "  • Windows: $PROJECT_ROOT/publish/win-x64/"
    exit 0
fi
