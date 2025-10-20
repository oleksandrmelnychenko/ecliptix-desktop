#!/bin/bash

# Ecliptix Desktop - Linux Installer Creator
# Creates DEB and RPM packages for Linux distributions

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
    echo -e "${BLUE}📦 [INSTALLER]${NC} $1"
}

print_success() {
    echo -e "${GREEN}✅ [INSTALLER]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}⚠️  [INSTALLER]${NC} $1"
}

print_error() {
    echo -e "${RED}❌ [INSTALLER]${NC} $1"
}

# Check if we're on Linux
if [[ "$OSTYPE" != "linux-gnu"* ]]; then
    print_error "This script is designed for Linux only"
    exit 1
fi

# Parse command line arguments
BUILD_PATH=""
OUTPUT_DIR="$PROJECT_ROOT/installers"
CREATE_DEB=true
CREATE_RPM=true
PACKAGE_TYPE=""

show_help() {
    cat << EOF
${CYAN}Ecliptix Desktop - Linux Installer Creator${NC}

${YELLOW}Usage:${NC} $0 [OPTIONS]

${YELLOW}Options:${NC}
  -b, --build-path PATH    Path to built application (default: auto-detect)
  -o, --output DIR         Output directory (default: ./installers)
  --deb-only               Create only DEB package
  --rpm-only               Create only RPM package
  -h, --help               Show this help message

${YELLOW}Examples:${NC}
  $0                                        # Create both DEB and RPM
  $0 --deb-only                             # Create DEB only
  $0 --build-path ../publish/linux-x64/Ecliptix

${YELLOW}Requirements:${NC}
  • ${GREEN}dpkg-deb${NC}: For DEB package creation (usually pre-installed)
  • ${GREEN}rpmbuild${NC}: For RPM package creation
    Install with: ${CYAN}sudo apt install rpm${NC} (Debian/Ubuntu)
                  ${CYAN}sudo dnf install rpm-build${NC} (Fedora/RHEL)

EOF
}

while [[ $# -gt 0 ]]; do
    case $1 in
        -b|--build-path)
            BUILD_PATH="$2"
            shift 2
            ;;
        -o|--output)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --deb-only)
            CREATE_DEB=true
            CREATE_RPM=false
            shift
            ;;
        --rpm-only)
            CREATE_DEB=false
            CREATE_RPM=true
            shift
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

print_status "🚀 Creating Linux Installers..."

# Auto-detect build path
if [ -z "$BUILD_PATH" ]; then
    BUILD_PATH="$PROJECT_ROOT/publish/linux-x64/Ecliptix"
    print_status "Auto-detected build path: $BUILD_PATH"
fi

# Verify build path exists
if [ ! -d "$BUILD_PATH" ]; then
    print_error "Build path not found: $BUILD_PATH"
    print_error "Please build the application first: ./Scripts/build-aot-linux.sh"
    exit 1
fi

# Get version
CURRENT_VERSION=$("$SCRIPT_DIR/version.sh" --action current | grep "Current version:" | cut -d' ' -f3 2>/dev/null || echo "1.0.0")
CLEAN_VERSION=$(echo "$CURRENT_VERSION" | sed 's/-build.*//' | sed 's/^v//')
if [[ ! "$CLEAN_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    CLEAN_VERSION="1.0.0"
fi

print_status "Version: $CLEAN_VERSION"

# Create output directory
mkdir -p "$OUTPUT_DIR"

APP_NAME="ecliptix"
MAINTAINER="Ecliptix <support@ecliptix.com>"
DESCRIPTION="Secure cross-platform desktop application"
HOMEPAGE="https://ecliptix.com"

# Create DEB package
if [ "$CREATE_DEB" = true ]; then
    print_status "Creating DEB package..."

    # Check for dpkg-deb
    if ! command -v dpkg-deb &> /dev/null; then
        print_error "dpkg-deb not found. Install with: sudo apt install dpkg-dev"
        exit 1
    fi

    DEB_DIR="$OUTPUT_DIR/deb-build"
    rm -rf "$DEB_DIR"
    mkdir -p "$DEB_DIR"

    # Create directory structure
    mkdir -p "$DEB_DIR/DEBIAN"
    mkdir -p "$DEB_DIR/usr/bin"
    mkdir -p "$DEB_DIR/usr/share/applications"
    mkdir -p "$DEB_DIR/usr/share/icons/hicolor/256x256/apps"
    mkdir -p "$DEB_DIR/usr/share/$APP_NAME"
    mkdir -p "$DEB_DIR/usr/share/doc/$APP_NAME"

    # Copy application files
    cp -r "$BUILD_PATH"/* "$DEB_DIR/usr/share/$APP_NAME/"

    # Create launcher script
    cat > "$DEB_DIR/usr/bin/$APP_NAME" << 'EOF'
#!/bin/bash
INSTALL_DIR="/usr/share/ecliptix"
exec "$INSTALL_DIR/Ecliptix" "$@"
EOF
    chmod +x "$DEB_DIR/usr/bin/$APP_NAME"

    # Copy icon
    ICON_SOURCE="$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core/Assets/Branding/Logos/logo_256x256.png"
    if [ -f "$ICON_SOURCE" ]; then
        cp "$ICON_SOURCE" "$DEB_DIR/usr/share/icons/hicolor/256x256/apps/$APP_NAME.png"
    fi

    # Create desktop file
    cat > "$DEB_DIR/usr/share/applications/$APP_NAME.desktop" << EOF
[Desktop Entry]
Name=Ecliptix
Comment=Secure cross-platform desktop application
Exec=/usr/bin/$APP_NAME
Icon=$APP_NAME
Type=Application
Categories=Office;Network;Security;
Terminal=false
StartupNotify=true
Version=$CLEAN_VERSION
EOF

    # Create copyright file
    cat > "$DEB_DIR/usr/share/doc/$APP_NAME/copyright" << EOF
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: Ecliptix
Source: $HOMEPAGE

Files: *
Copyright: $(date +%Y) Ecliptix
License: Proprietary
 All rights reserved.
EOF

    # Calculate installed size
    INSTALLED_SIZE=$(du -sk "$DEB_DIR" | cut -f1)

    # Create control file
    cat > "$DEB_DIR/DEBIAN/control" << EOF
Package: $APP_NAME
Version: $CLEAN_VERSION
Section: net
Priority: optional
Architecture: amd64
Installed-Size: $INSTALLED_SIZE
Maintainer: $MAINTAINER
Description: $DESCRIPTION
 Ecliptix is a secure, cross-platform desktop application
 providing end-to-end encrypted communications and file management.
Homepage: $HOMEPAGE
EOF

    # Create postinst script
    cat > "$DEB_DIR/DEBIAN/postinst" << 'EOF'
#!/bin/bash
set -e

# Update desktop database
if command -v update-desktop-database &> /dev/null; then
    update-desktop-database -q
fi

# Update icon cache
if command -v gtk-update-icon-cache &> /dev/null; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor
fi

exit 0
EOF
    chmod +x "$DEB_DIR/DEBIAN/postinst"

    # Create postrm script
    cat > "$DEB_DIR/DEBIAN/postrm" << 'EOF'
#!/bin/bash
set -e

if [ "$1" = "remove" ]; then
    # Update desktop database
    if command -v update-desktop-database &> /dev/null; then
        update-desktop-database -q
    fi

    # Update icon cache
    if command -v gtk-update-icon-cache &> /dev/null; then
        gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor
    fi
fi

exit 0
EOF
    chmod +x "$DEB_DIR/DEBIAN/postrm"

    # Build DEB package
    DEB_FILE="$OUTPUT_DIR/${APP_NAME}_${CLEAN_VERSION}_amd64.deb"
    dpkg-deb --build "$DEB_DIR" "$DEB_FILE"

    if [ -f "$DEB_FILE" ]; then
        DEB_SIZE=$(du -h "$DEB_FILE" | cut -f1)
        print_success "DEB package created: $DEB_FILE ($DEB_SIZE)"
    else
        print_error "Failed to create DEB package"
    fi

    # Clean up
    rm -rf "$DEB_DIR"
fi

# Create RPM package
if [ "$CREATE_RPM" = true ]; then
    print_status "Creating RPM package..."

    # Check for rpmbuild
    if ! command -v rpmbuild &> /dev/null; then
        print_warning "rpmbuild not found. Install with:"
        print_warning "  Debian/Ubuntu: sudo apt install rpm"
        print_warning "  Fedora/RHEL:   sudo dnf install rpm-build"
        print_warning "Skipping RPM creation..."
    else
        RPM_BUILD_DIR="$OUTPUT_DIR/rpm-build"
        rm -rf "$RPM_BUILD_DIR"

        # Create RPM build structure
        mkdir -p "$RPM_BUILD_DIR"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}
        mkdir -p "$RPM_BUILD_DIR/BUILD/usr/bin"
        mkdir -p "$RPM_BUILD_DIR/BUILD/usr/share/applications"
        mkdir -p "$RPM_BUILD_DIR/BUILD/usr/share/icons/hicolor/256x256/apps"
        mkdir -p "$RPM_BUILD_DIR/BUILD/usr/share/$APP_NAME"

        # Copy application files
        cp -r "$BUILD_PATH"/* "$RPM_BUILD_DIR/BUILD/usr/share/$APP_NAME/"

        # Create launcher script
        cat > "$RPM_BUILD_DIR/BUILD/usr/bin/$APP_NAME" << 'EOF'
#!/bin/bash
INSTALL_DIR="/usr/share/ecliptix"
exec "$INSTALL_DIR/Ecliptix" "$@"
EOF
        chmod +x "$RPM_BUILD_DIR/BUILD/usr/bin/$APP_NAME"

        # Copy icon
        ICON_SOURCE="$PROJECT_ROOT/Ecliptix.Core/Ecliptix.Core/Assets/Branding/Logos/logo_256x256.png"
        if [ -f "$ICON_SOURCE" ]; then
            cp "$ICON_SOURCE" "$RPM_BUILD_DIR/BUILD/usr/share/icons/hicolor/256x256/apps/$APP_NAME.png"
        fi

        # Create desktop file
        cat > "$RPM_BUILD_DIR/BUILD/usr/share/applications/$APP_NAME.desktop" << EOF
[Desktop Entry]
Name=Ecliptix
Comment=Secure cross-platform desktop application
Exec=/usr/bin/$APP_NAME
Icon=$APP_NAME
Type=Application
Categories=Office;Network;Security;
Terminal=false
StartupNotify=true
Version=$CLEAN_VERSION
EOF

        # Create spec file
        cat > "$RPM_BUILD_DIR/SPECS/$APP_NAME.spec" << EOF
Name:           $APP_NAME
Version:        $CLEAN_VERSION
Release:        1%{?dist}
Summary:        $DESCRIPTION
License:        Proprietary
URL:            $HOMEPAGE
BuildArch:      x86_64
AutoReqProv:    no

%description
Ecliptix is a secure, cross-platform desktop application
providing end-to-end encrypted communications and file management.

%install
rm -rf %{buildroot}
mkdir -p %{buildroot}
cp -r $RPM_BUILD_DIR/BUILD/* %{buildroot}/

%files
%defattr(-,root,root,-)
/usr/bin/$APP_NAME
/usr/share/$APP_NAME/*
/usr/share/applications/$APP_NAME.desktop
/usr/share/icons/hicolor/256x256/apps/$APP_NAME.png

%post
if [ -x /usr/bin/update-desktop-database ]; then
    /usr/bin/update-desktop-database -q
fi
if [ -x /usr/bin/gtk-update-icon-cache ]; then
    /usr/bin/gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor
fi

%postun
if [ -x /usr/bin/update-desktop-database ]; then
    /usr/bin/update-desktop-database -q
fi
if [ -x /usr/bin/gtk-update-icon-cache ]; then
    /usr/bin/gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor
fi

%changelog
* $(date "+%a %b %d %Y") Ecliptix <support@ecliptix.com> - $CLEAN_VERSION-1
- Release version $CLEAN_VERSION
EOF

        # Build RPM
        RPM_FILE="$OUTPUT_DIR/${APP_NAME}-${CLEAN_VERSION}-1.x86_64.rpm"
        rpmbuild --define "_topdir $RPM_BUILD_DIR" -bb "$RPM_BUILD_DIR/SPECS/$APP_NAME.spec" 2>&1 | grep -v "warning:"

        # Move RPM to output directory
        if [ -f "$RPM_BUILD_DIR/RPMS/x86_64/${APP_NAME}-${CLEAN_VERSION}-1.x86_64.rpm" ]; then
            mv "$RPM_BUILD_DIR/RPMS/x86_64/${APP_NAME}-${CLEAN_VERSION}-1.x86_64.rpm" "$RPM_FILE"
            RPM_SIZE=$(du -h "$RPM_FILE" | cut -f1)
            print_success "RPM package created: $RPM_FILE ($RPM_SIZE)"
        else
            print_error "Failed to create RPM package"
        fi

        # Clean up
        rm -rf "$RPM_BUILD_DIR"
    fi
fi

# Display summary
echo ""
print_success "🎉 Linux Installers Created Successfully!"
echo ""
echo -e "${CYAN}📦 Installer Details:${NC}"

if [ "$CREATE_DEB" = true ] && [ -f "$DEB_FILE" ]; then
    echo "   • DEB: $DEB_FILE"
fi

if [ "$CREATE_RPM" = true ] && [ -f "$RPM_FILE" ]; then
    echo "   • RPM: $RPM_FILE"
fi

echo ""
echo -e "${YELLOW}🧪 To test the installers:${NC}"

if [ "$CREATE_DEB" = true ] && [ -f "$DEB_FILE" ]; then
    echo "   DEB: sudo dpkg -i '$DEB_FILE'"
fi

if [ "$CREATE_RPM" = true ] && [ -f "$RPM_FILE" ]; then
    echo "   RPM: sudo rpm -i '$RPM_FILE'"
fi

echo ""
echo -e "${YELLOW}📋 Distribution checklist:${NC}"
echo "   1. Test installation on target distributions"
echo "   2. Test application launch after installation"
echo "   3. Verify icon and menu entry creation"
echo "   4. Test uninstallation"
echo "   5. Upload to package repositories or distribution server"
echo ""
