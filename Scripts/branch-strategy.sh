#!/bin/bash

# Ecliptix Desktop Branch Strategy Helper
# Helps create and manage the proper branch structure

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

print_info() {
    echo -e "${BLUE}ℹ️  [BRANCH]${NC} $1"
}

print_success() {
    echo -e "${GREEN}✅ [BRANCH]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}⚠️  [BRANCH]${NC} $1"
}

print_error() {
    echo -e "${RED}❌ [BRANCH]${NC} $1"
}

print_header() {
    echo -e "${CYAN}🌳 Ecliptix Branch Strategy${NC}"
    echo "=========================="
    echo ""
}

# Parse command line arguments
ACTION=""
BRANCH_NAME=""
BRANCH_TYPE=""

while [[ $# -gt 0 ]]; do
    case $1 in
        init|setup)
            ACTION="init"
            shift
            ;;
        create)
            ACTION="create"
            BRANCH_TYPE="$2"
            BRANCH_NAME="$3"
            shift 3
            ;;
        status|info)
            ACTION="status"
            shift
            ;;
        -h|--help)
            echo "Branch Strategy Helper for Ecliptix Desktop"
            echo ""
            echo "Usage: $0 [COMMAND] [OPTIONS]"
            echo ""
            echo "Commands:"
            echo "  init, setup              Initialize branch structure"
            echo "  create <type> <name>     Create new branch"
            echo "  status, info             Show branch strategy status"
            echo ""
            echo "Branch Types:"
            echo "  feature <name>           Feature branch (feature/name)"
            echo "  task <name>              Task branch (task/name)"
            echo "  bugfix <name>            Bug fix branch (bugfix/name)"
            echo "  hotfix <name>            Hotfix branch (hotfix/name)"
            echo ""
            echo "Branch Strategy:"
            echo "  main                     ✅ Production releases (auto-versioned)"
            echo "  develop                  🔄 Development integration"
            echo "  feature/*                🚀 Feature development"
            echo "  task/*                   📋 Task implementation"
            echo "  bugfix/*                 🐛 Bug fixes"
            echo "  hotfix/*                 🔥 Critical fixes"
            echo ""
            echo "Examples:"
            echo "  $0 init                           # Setup branch structure"
            echo "  $0 create feature user-auth       # Create feature/user-auth"
            echo "  $0 create task setup-ci          # Create task/setup-ci"
            echo "  $0 status                         # Show current status"
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            print_error "Use -h or --help for usage information"
            exit 1
            ;;
    esac
done

# Default action if none specified
if [ -z "$ACTION" ]; then
    ACTION="status"
fi

print_header

# Check if we're in a git repository
if ! git rev-parse --git-dir > /dev/null 2>&1; then
    print_error "Not in a git repository"
    exit 1
fi

# Function to check if branch exists
branch_exists() {
    git show-ref --verify --quiet refs/heads/"$1"
}

# Function to check if remote branch exists
remote_branch_exists() {
    git show-ref --verify --quiet refs/remotes/origin/"$1"
}

# Initialize branch structure
init_branches() {
    print_info "Initializing Ecliptix branch structure..."
    
    # Check current branch
    CURRENT_BRANCH=$(git branch --show-current)
    print_info "Current branch: $CURRENT_BRANCH"
    
    # Create develop branch if it doesn't exist
    if ! branch_exists "develop"; then
        print_info "Creating develop branch from main..."
        
        # Ensure we're on main first
        if [ "$CURRENT_BRANCH" != "main" ]; then
            git checkout main
        fi
        
        # Create develop branch
        git checkout -b develop
        print_success "Created develop branch"
        
        # Switch back to main
        git checkout main
        print_info "Switched back to main branch"
    else
        print_warning "develop branch already exists"
    fi
    
    # Show branch structure
    print_success "Branch structure initialized!"
    show_branch_status
}

# Create new branch
create_branch() {
    local type="$1"
    local name="$2"
    
    if [ -z "$type" ] || [ -z "$name" ]; then
        print_error "Both branch type and name are required"
        print_error "Usage: $0 create <type> <name>"
        exit 1
    fi
    
    # Validate branch type
    case $type in
        feature|task|bugfix|hotfix)
            ;;
        *)
            print_error "Invalid branch type: $type"
            print_error "Valid types: feature, task, bugfix, hotfix"
            exit 1
            ;;
    esac
    
    local full_branch_name="$type/$name"
    
    # Check if branch already exists
    if branch_exists "$full_branch_name"; then
        print_error "Branch already exists: $full_branch_name"
        exit 1
    fi
    
    print_info "Creating branch: $full_branch_name"
    
    # Determine base branch
    local base_branch="develop"
    if [ "$type" = "hotfix" ]; then
        base_branch="main"
    fi
    
    # Ensure base branch exists and is up to date
    if ! branch_exists "$base_branch"; then
        print_error "Base branch does not exist: $base_branch"
        print_error "Run: $0 init"
        exit 1
    fi
    
    # Update base branch
    print_info "Updating $base_branch branch..."
    git checkout "$base_branch"
    if remote_branch_exists "$base_branch"; then
        git pull origin "$base_branch"
    fi
    
    # Create new branch
    git checkout -b "$full_branch_name"
    print_success "Created and switched to branch: $full_branch_name"
    
    print_info "Branch ready for development!"
    print_info "When ready to merge:"
    echo "  1. git push origin $full_branch_name"
    echo "  2. Create PR to $base_branch"
    echo "  3. After merge to $base_branch, merge $base_branch to main for release"
}

# Show branch status
show_branch_status() {
    print_info "Current branch structure:"
    echo ""
    
    # Show all branches
    echo -e "${CYAN}Local branches:${NC}"
    git branch -vv | sed 's/^/  /'
    echo ""
    
    if git remote | grep -q origin; then
        echo -e "${CYAN}Remote branches:${NC}"
        git branch -r | grep -v HEAD | sed 's/^/  /'
        echo ""
    fi
    
    # Show branch strategy
    echo -e "${CYAN}Branch Strategy:${NC}"
    if branch_exists "main"; then
        echo -e "  ${GREEN}✅ main${NC}     - Production releases (auto-versioned on commit)"
    else
        echo -e "  ${RED}❌ main${NC}     - MISSING: Production branch"
    fi
    
    if branch_exists "develop"; then
        echo -e "  ${GREEN}✅ develop${NC}  - Development integration"
    else
        echo -e "  ${YELLOW}⚠️  develop${NC}  - Development integration (run: $0 init)"
    fi
    
    # Show feature branches
    local feature_branches=$(git branch | grep 'feature/' | wc -l | xargs)
    local task_branches=$(git branch | grep 'task/' | wc -l | xargs)
    local bugfix_branches=$(git branch | grep 'bugfix/' | wc -l | xargs)
    local hotfix_branches=$(git branch | grep 'hotfix/' | wc -l | xargs)
    
    echo -e "  ${BLUE}📋 task/*${NC}    - Task branches ($task_branches active)"
    echo -e "  ${BLUE}🚀 feature/*${NC} - Feature branches ($feature_branches active)"
    echo -e "  ${BLUE}🐛 bugfix/*${NC}  - Bug fix branches ($bugfix_branches active)"
    echo -e "  ${BLUE}🔥 hotfix/*${NC}  - Hotfix branches ($hotfix_branches active)"
    
    echo ""
    echo -e "${CYAN}Version Strategy:${NC}"
    echo "  • Only main branch gets auto-versioned"
    echo "  • Merge develop → main triggers release build"
    echo "  • GitHub releases built automatically on main"
    
    # Show current version
    if [ -f "$SCRIPT_DIR/version.sh" ]; then
        CURRENT_VERSION=$("$SCRIPT_DIR/version.sh" --action current | grep "Current version:" | cut -d' ' -f3 2>/dev/null || echo "Unknown")
        echo ""
        print_info "Current version: $CURRENT_VERSION"
    fi
}

# Execute the requested action
case $ACTION in
    "init")
        init_branches
        ;;
    "create")
        create_branch "$BRANCH_TYPE" "$BRANCH_NAME"
        ;;
    "status")
        show_branch_status
        ;;
    *)
        print_error "Unknown action: $ACTION"
        exit 1
        ;;
esac