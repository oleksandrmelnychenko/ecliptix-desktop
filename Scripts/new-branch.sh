#!/bin/bash

# Script to create a new branch with proper naming convention
# Usage: ./scripts/new-branch.sh <type> <description>
# Example: ./scripts/new-branch.sh feature mobile-verification
# Example: ./scripts/new-branch.sh fix memory-leak-auth

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

# Check arguments
if [ $# -ne 2 ]; then
    print_error "Usage: $0 <type> <description>"
    echo "Types: feature, fix, hotfix, docs, refactor, test, chore"
    echo "Example: $0 feature mobile-verification"
    exit 1
fi

TYPE=$1
DESCRIPTION=$2

# Validate branch type
case $TYPE in
    feature|fix|hotfix|docs|refactor|test|chore)
        ;;
    *)
        print_error "Invalid branch type: $TYPE"
        echo "Valid types: feature, fix, hotfix, docs, refactor, test, chore"
        exit 1
        ;;
esac

# Validate description
if [[ ! $DESCRIPTION =~ ^[a-z0-9-]+$ ]]; then
    print_error "Description must contain only lowercase letters, numbers, and hyphens"
    exit 1
fi

# Create branch name
BRANCH_NAME="$TYPE/$DESCRIPTION"

# Check if branch already exists
if git show-ref --quiet refs/heads/$BRANCH_NAME; then
    print_error "Branch '$BRANCH_NAME' already exists"
    exit 1
fi

if git show-ref --quiet refs/remotes/origin/$BRANCH_NAME; then
    print_error "Branch '$BRANCH_NAME' already exists on remote"
    exit 1
fi

# Ensure we're on develop branch
CURRENT_BRANCH=$(git branch --show-current)
if [ "$CURRENT_BRANCH" != "develop" ]; then
    print_warning "Not on develop branch. Switching to develop..."
    git checkout develop
fi

# Update develop branch
print_info "Updating develop branch..."
git pull origin develop

# Create and switch to new branch
print_info "Creating branch: $BRANCH_NAME"
git checkout -b $BRANCH_NAME

print_info "✅ Successfully created and switched to branch: $BRANCH_NAME"
print_info "You can now start working on your changes."
print_info ""
print_info "Next steps:"
echo "  1. Make your changes"
echo "  2. git add ."
echo "  3. git commit -m \"$TYPE: <commit message>\""
echo "  4. git push -u origin $BRANCH_NAME"
echo "  5. Create a pull request on GitHub"