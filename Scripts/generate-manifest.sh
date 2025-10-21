#!/bin/bash

VERSION=""
ARTIFACTS_DIR=""
BASE_URL=""
OUTPUT=""
RELEASE_NOTES=""
IS_CRITICAL=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --version)
            VERSION="$2"
            shift 2
            ;;
        --artifacts-dir)
            ARTIFACTS_DIR="$2"
            shift 2
            ;;
        --base-url)
            BASE_URL="$2"
            shift 2
            ;;
        --output)
            OUTPUT="$2"
            shift 2
            ;;
        --release-notes)
            RELEASE_NOTES="$2"
            shift 2
            ;;
        --critical)
            IS_CRITICAL=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

if [ -z "$VERSION" ] || [ -z "$ARTIFACTS_DIR" ] || [ -z "$BASE_URL" ] || [ -z "$OUTPUT" ]; then
    echo "Usage: $0 --version <version> --artifacts-dir <dir> --base-url <url> --output <file> [--release-notes <notes>] [--critical]"
    exit 1
fi

echo "Generating update manifest for version $VERSION"

get_file_size() {
    local file="$1"
    if [ -f "$file" ]; then
        if [[ "$OSTYPE" == "darwin"* ]]; then
            stat -f%z "$file"
        else
            stat -c%s "$file"
        fi
    else
        echo "0"
    fi
}

get_sha256() {
    local file="$1"
    local sha_file="${file}.sha256"

    if [ -f "$sha_file" ]; then
        cat "$sha_file"
    elif [ -f "$file" ]; then
        if [[ "$OSTYPE" == "darwin"* ]]; then
            shasum -a 256 "$file" | awk '{print $1}'
        else
            sha256sum "$file" | awk '{print $1}'
        fi
    else
        echo ""
    fi
}

find_installer() {
    local pattern="$1"
    find "$ARTIFACTS_DIR" -type f -name "$pattern" | head -1
}

WIN_X64_EXE=$(find_installer "*win-x64.exe")
WIN_ARM64_EXE=$(find_installer "*win-arm64.exe")
OSX_X64_DMG=$(find_installer "*osx-x64.dmg")
OSX_ARM64_DMG=$(find_installer "*osx-arm64.dmg")
LINUX_X64_DEB=$(find_installer "*linux-x64*.deb")
LINUX_X64_RPM=$(find_installer "*linux-x64*.rpm")
LINUX_ARM64_DEB=$(find_installer "*linux-arm64*.deb")
LINUX_ARM64_RPM=$(find_installer "*linux-arm64*.rpm")

RELEASE_DATE=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

if [ -z "$RELEASE_NOTES" ]; then
    RELEASE_NOTES="Bug fixes and improvements"
fi

mkdir -p "$(dirname "$OUTPUT")"

cat > "$OUTPUT" << EOF
{
  "version": "$VERSION",
  "releaseDate": "$RELEASE_DATE",
  "releaseNotes": "$RELEASE_NOTES",
  "isCritical": $IS_CRITICAL,
  "minimumVersion": null,
  "platforms": {
EOF

PLATFORM_COUNT=0

add_platform() {
    local platform="$1"
    local file="$2"
    local installer_type="$3"

    if [ -z "$file" ] || [ ! -f "$file" ]; then
        return
    fi

    local filename=$(basename "$file")
    local file_size=$(get_file_size "$file")
    local sha256=$(get_sha256 "$file")
    local download_url="${BASE_URL}/${filename}"

    if [ $PLATFORM_COUNT -gt 0 ]; then
        echo "," >> "$OUTPUT"
    fi

    cat >> "$OUTPUT" << EOF
    "$platform": {
      "downloadUrl": "$download_url",
      "fileSize": $file_size,
      "sha256": "$sha256",
      "installerType": "$installer_type"
    }
EOF

    PLATFORM_COUNT=$((PLATFORM_COUNT + 1))
    echo "Added platform: $platform ($filename, $file_size bytes)"
}

add_platform "win-x64" "$WIN_X64_EXE" "exe"
add_platform "win-arm64" "$WIN_ARM64_EXE" "exe"
add_platform "osx-x64" "$OSX_X64_DMG" "dmg"
add_platform "osx-arm64" "$OSX_ARM64_DMG" "dmg"
add_platform "linux-x64-deb" "$LINUX_X64_DEB" "deb"
add_platform "linux-x64-rpm" "$LINUX_X64_RPM" "rpm"
add_platform "linux-arm64-deb" "$LINUX_ARM64_DEB" "deb"
add_platform "linux-arm64-rpm" "$LINUX_ARM64_RPM" "rpm"

cat >> "$OUTPUT" << EOF

  }
}
EOF

echo ""
echo "Manifest generated successfully: $OUTPUT"
echo "Platforms included: $PLATFORM_COUNT"
echo ""
cat "$OUTPUT"
