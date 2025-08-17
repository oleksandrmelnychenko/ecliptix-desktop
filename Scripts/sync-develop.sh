#!/bin/bash

# Script to sync develop branch with upstream
# Usage: ./scripts/sync-develop.sh

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to print colored output
print_info() {
    echo -e "${GREEN}ℹ️  $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

print_error() {
    echo -e "${RED}❌ $1${NC}"
}

# Check if git is available
if ! command -v git &> /dev/null; then
    print_error "Git is not installed or not in PATH"
    exit 1
fi

# Check if we're in a git repository
if ! git rev-parse --git-dir > /dev/null 2>&1; then
    print_error "Not in a git repository"
    exit 1
fi

# Check for uncommitted changes
if ! git diff --quiet || ! git diff --staged --quiet; then
    print_error "You have uncommitted changes. Please commit or stash them first."
    git status --short
    exit 1
fi

# Get current branch
CURRENT_BRANCH=$(git branch --show-current)
print_info "Current branch: $CURRENT_BRANCH"

# Stash current work if not on develop
if [ "$CURRENT_BRANCH" != "develop" ]; then
    print_warning "Not on develop branch. Switching to develop..."
    git checkout develop
fi

# Check if develop branch exists
if ! git show-ref --quiet refs/heads/develop; then
    print_error "Develop branch does not exist locally"
    print_info "Creating develop branch from origin/develop..."
    git checkout -b develop origin/develop
fi

# Fetch latest changes
print_info "Fetching latest changes from origin..."
git fetch origin

# Check if origin/develop exists
if ! git show-ref --quiet refs/remotes/origin/develop; then
    print_error "origin/develop does not exist"
    exit 1
fi

# Get commit hashes for comparison
LOCAL_DEVELOP=$(git rev-parse develop)
REMOTE_DEVELOP=$(git rev-parse origin/develop)

if [ "$LOCAL_DEVELOP" = "$REMOTE_DEVELOP" ]; then
    print_info "✅ Develop branch is already up to date"
else
    print_info "Updating develop branch..."
    git merge --ff-only origin/develop
    print_info "✅ Develop branch updated successfully"
fi

# Switch back to original branch if it wasn't develop
if [ "$CURRENT_BRANCH" != "develop" ]; then
    print_info "Switching back to: $CURRENT_BRANCH"
    git checkout $CURRENT_BRANCH
    
    # Offer to rebase current branch on updated develop
    echo ""
    read -p "Do you want to rebase $CURRENT_BRANCH on updated develop? (y/N): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        print_info "Rebasing $CURRENT_BRANCH on develop..."
        git rebase develop
        print_info "✅ Rebase completed successfully"
    fi
fi

print_info "✅ Sync completed"