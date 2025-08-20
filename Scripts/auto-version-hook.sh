#!/bin/bash
# Ecliptix Auto-Versioning Hook Implementation

set -e

HOOK_TYPE="$1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
CONFIG_FILE="$SCRIPT_DIR/version-config.json"

# Load configuration
if [ ! -f "$CONFIG_FILE" ]; then
    echo "Error: Configuration file not found: $CONFIG_FILE"
    exit 1
fi

# Get current branch
CURRENT_BRANCH=$(git branch --show-current)

# Check if we should process this branch
if ! echo "$CURRENT_BRANCH" | grep -qE "(main|master|develop|release/|hotfix/)"; then
    # Skip versioning for feature branches
    exit 0
fi

# Get the commit message
if [ "$HOOK_TYPE" = "pre-commit" ]; then
    # For pre-commit, we need to get the staged commit message
    # This is tricky because the commit message hasn't been created yet
    # We'll handle version increment in post-commit instead
    exit 0
elif [ "$HOOK_TYPE" = "post-commit" ]; then
    COMMIT_MESSAGE=$(git log -1 --pretty=%B)
else
    echo "Error: Unknown hook type: $HOOK_TYPE"
    exit 1
fi

# Determine version increment type
INCREMENT_TYPE=""
if echo "$COMMIT_MESSAGE" | grep -qE "\[major\]|BREAKING CHANGE:|breaking:"; then
    INCREMENT_TYPE="major"
elif echo "$COMMIT_MESSAGE" | grep -qE "\[minor\]|feat:|feature:"; then
    INCREMENT_TYPE="minor"
elif echo "$COMMIT_MESSAGE" | grep -qE "\[patch\]|fix:|bugfix:|hotfix:"; then
    INCREMENT_TYPE="patch"
elif echo "$COMMIT_MESSAGE" | grep -qE "\[skip-version\]|\[no-version\]|docs:|chore:|style:|refactor:|test:"; then
    INCREMENT_TYPE="skip"
else
    # Default increment type
    INCREMENT_TYPE="patch"
fi

# Skip if requested
if [ "$INCREMENT_TYPE" = "skip" ]; then
    echo "Auto-versioning: Skipping version increment based on commit message"
    exit 0
fi

# Increment version
echo "Auto-versioning: Incrementing $INCREMENT_TYPE version..."
if "$SCRIPT_DIR/version.sh" --action increment --part "$INCREMENT_TYPE"; then
    NEW_VERSION=$("$SCRIPT_DIR/version.sh" --action current | grep "Current version:" | cut -d' ' -f3)
    echo "Auto-versioning: Version incremented to $NEW_VERSION"
    
    # Generate build info
    "$SCRIPT_DIR/version.sh" --action build
    echo "Auto-versioning: Build info updated"
    
    # Stage the updated version files if they were modified
    git add "Ecliptix.Core/Directory.Build.props" 2>/dev/null || true
    git add "build-info.json" 2>/dev/null || true
    
    # Amend the commit with version changes
    git commit --amend --no-edit --no-verify
    
else
    echo "Auto-versioning: Failed to increment version"
    exit 1
fi
