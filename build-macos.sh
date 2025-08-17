#!/bin/bash

echo "Building Ecliptix for macOS with AOT..."

# Clean previous builds
echo "Cleaning previous builds..."
dotnet clean Ecliptix.Core/Ecliptix.Core.Desktop/Ecliptix.Core.Desktop.csproj -c Release

# Publish for macOS ARM64 with AOT
echo "Publishing for macOS ARM64..."
dotnet publish Ecliptix.Core/Ecliptix.Core.Desktop/Ecliptix.Core.Desktop.csproj \
    -c Release \
    -r osx-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o publish/osx-arm64

# Create macOS app bundle
echo "Creating macOS app bundle..."
APP_NAME="Ecliptix.app"
BUNDLE_DIR="publish/osx-arm64/$APP_NAME"

# Create app bundle structure
mkdir -p "$BUNDLE_DIR/Contents/MacOS"
mkdir -p "$BUNDLE_DIR/Contents/Resources"

# Move the executable
mv "publish/osx-arm64/Ecliptix" "$BUNDLE_DIR/Contents/MacOS/"

# Copy Info.plist
cp "Ecliptix.Core/Ecliptix.Core.Desktop/Info.plist" "$BUNDLE_DIR/Contents/"

# Copy icon
cp "Ecliptix.Core/Ecliptix.Core/Assets/EcliptixLogo.icns" "$BUNDLE_DIR/Contents/Resources/"

# Copy configuration files
cp publish/osx-arm64/*.json "$BUNDLE_DIR/Contents/MacOS/" 2>/dev/null || true
# Ensure all required configuration files are present
for config_file in "appsettings.json" "appsettings.Development.json" "appsettings.Production.json" "build-info.json"; do
    if [ ! -f "$BUNDLE_DIR/Contents/MacOS/$config_file" ] && [ -f "publish/osx-arm64/$config_file" ]; then
        cp "publish/osx-arm64/$config_file" "$BUNDLE_DIR/Contents/MacOS/"
        echo "Copied missing config file: $config_file"
    fi
done

# Copy native libraries
cp publish/osx-arm64/lib*.dylib "$BUNDLE_DIR/Contents/MacOS/" 2>/dev/null || true

# Set executable permissions
chmod +x "$BUNDLE_DIR/Contents/MacOS/Ecliptix"

# Create symlink for easier access
ln -sf "$PWD/$BUNDLE_DIR" "$PWD/Ecliptix.app"

echo "✅ macOS build completed!"
echo "📱 App bundle created at: $BUNDLE_DIR"
echo "🔗 Symlink created at: ./Ecliptix.app"
echo ""
echo "To run the app:"
echo "  open ./Ecliptix.app"
echo ""
echo "To install:"
echo "  cp -r '$BUNDLE_DIR' /Applications/"