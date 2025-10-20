#!/bin/bash

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT_DIR="$( cd "$SCRIPT_DIR/.." && pwd )"

show_help() {
    cat << EOF
Ecliptix Release Creation Script

Usage: ./Scripts/create-release.sh <version> [options]

Arguments:
    version         Version number (e.g., 1.0.0 or v1.0.0)

Options:
    --prerelease    Mark this as a pre-release
    --push          Automatically push tag to origin
    --help          Show this help message

Examples:
    ./Scripts/create-release.sh 1.0.0
    ./Scripts/create-release.sh v1.2.3 --push
    ./Scripts/create-release.sh 2.0.0-beta.1 --prerelease --push

This script will:
1. Validate the version format
2. Create a git tag
3. Optionally push to GitHub (triggers automated build)

The GitHub Actions workflow will then:
- Build for all platforms (Windows, macOS, Linux)
- Create installers (EXE, DMG, DEB, RPM)
- Sign binaries (if secrets configured)
- Generate update manifest
- Create GitHub release with all files
- Deploy update server

For more details, see: RELEASE-AUTOMATION.md
EOF
}

if [ "$1" = "--help" ] || [ "$1" = "-h" ] || [ -z "$1" ]; then
    show_help
    exit 0
fi

VERSION="$1"
shift

PRERELEASE=false
PUSH=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --prerelease)
            PRERELEASE=true
            shift
            ;;
        --push)
            PUSH=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            show_help
            exit 1
            ;;
    esac
done

VERSION="${VERSION#v}"

if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$ ]]; then
    echo "Error: Invalid version format: $VERSION"
    echo "Expected: MAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH-prerelease"
    echo "Examples: 1.0.0, 2.1.3, 1.0.0-beta.1"
    exit 1
fi

TAG="v${VERSION}"

if git rev-parse "$TAG" >/dev/null 2>&1; then
    echo "Error: Tag $TAG already exists"
    echo ""
    echo "Existing tag info:"
    git show "$TAG" --no-patch
    exit 1
fi

if [ "$(git status --porcelain)" ]; then
    echo "Warning: You have uncommitted changes:"
    git status --short
    echo ""
    read -p "Continue anyway? (y/N) " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        exit 1
    fi
fi

echo "=================================================="
echo "Creating Ecliptix Release"
echo "=================================================="
echo "Version:     $VERSION"
echo "Tag:         $TAG"
echo "Pre-release: $PRERELEASE"
echo "Auto-push:   $PUSH"
echo "Branch:      $(git branch --show-current)"
echo "Commit:      $(git rev-parse --short HEAD)"
echo "=================================================="
echo ""

read -p "Create this release? (y/N) " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "Cancelled"
    exit 1
fi

echo ""
echo "Creating git tag: $TAG"

TAG_MESSAGE="Release $VERSION"
if [ "$PRERELEASE" = true ]; then
    TAG_MESSAGE="Pre-release $VERSION"
fi

git tag -a "$TAG" -m "$TAG_MESSAGE"

echo "✅ Tag created successfully"
echo ""

if [ "$PUSH" = true ]; then
    echo "Pushing tag to origin..."
    git push origin "$TAG"
    echo ""
    echo "✅ Tag pushed to GitHub"
    echo ""
    echo "🚀 Automated build started!"
    echo ""
    echo "Monitor progress:"
    REPO_URL=$(git config --get remote.origin.url | sed 's/\.git$//' | sed 's/git@github.com:/https:\/\/github.com\//')
    echo "   $REPO_URL/actions"
    echo ""
    echo "Release will be published at:"
    echo "   $REPO_URL/releases/tag/$TAG"
else
    echo "Tag created locally. To trigger automated build:"
    echo ""
    echo "   git push origin $TAG"
    echo ""
    echo "Or push all tags:"
    echo ""
    echo "   git push --tags"
fi

echo ""
echo "=================================================="
echo "What happens next:"
echo "=================================================="
echo ""
echo "1. GitHub Actions detects the tag"
echo "2. Builds for all platforms (Windows, macOS, Linux)"
echo "3. Creates installers (EXE, DMG, DEB, RPM)"
echo "4. Generates SHA-256 checksums"
echo "5. Signs binaries (if secrets configured)"
echo "6. Creates update manifest"
echo "7. Publishes GitHub release"
echo "8. Deploys update server"
echo ""
echo "⏱️  Total time: ~15-30 minutes"
echo ""
echo "For more details: cat RELEASE-AUTOMATION.md"
echo ""
