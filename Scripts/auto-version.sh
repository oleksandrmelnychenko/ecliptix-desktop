#!/bin/bash

# Ecliptix Desktop Auto-Versioning Setup Script
# This script sets up automatic version management with git hooks

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
HOOKS_DIR="$PROJECT_ROOT/.githooks"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

print_info() {
    echo -e "${BLUE}ℹ️  [AUTO-VERSION]${NC} $1"
}

print_success() {
    echo -e "${GREEN}✅ [AUTO-VERSION]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}⚠️  [AUTO-VERSION]${NC} $1"
}

print_error() {
    echo -e "${RED}❌ [AUTO-VERSION]${NC} $1"
}

print_header() {
    echo -e "${CYAN}🔧 Ecliptix Desktop Auto-Versioning Setup${NC}"
    echo "============================================="
    echo ""
}

# Parse command line arguments
ACTION=""
FORCE=false

while [[ $# -gt 0 ]]; do
    case $1 in
        install|setup)
            ACTION="install"
            shift
            ;;
        uninstall|remove)
            ACTION="uninstall"
            shift
            ;;
        status|check)
            ACTION="status"
            shift
            ;;
        --force)
            FORCE=true
            shift
            ;;
        -h|--help)
            echo "Auto-Versioning Setup Script for Ecliptix Desktop"
            echo ""
            echo "Usage: $0 [COMMAND] [OPTIONS]"
            echo ""
            echo "Commands:"
            echo "  install, setup     Install auto-versioning git hooks"
            echo "  uninstall, remove  Remove auto-versioning git hooks"
            echo "  status, check      Check auto-versioning setup status"
            echo ""
            echo "Options:"
            echo "  --force           Force installation even if hooks exist"
            echo "  -h, --help        Show this help message"
            echo ""
            echo "Auto-Versioning Features:"
            echo "  • Automatic version increment on commit based on commit message patterns"
            echo "  • Build info generation with git commit hash and timestamp"
            echo "  • Configurable increment rules (major/minor/patch/skip)"
            echo "  • Integration with Directory.Build.props for .NET projects"
            echo ""
            echo "Commit Message Patterns:"
            echo "  [major]           - Increment major version (1.0.0 → 2.0.0)"
            echo "  [minor], feat:    - Increment minor version (1.0.0 → 1.1.0)"
            echo "  [patch], fix:     - Increment patch version (1.0.0 → 1.0.1)"
            echo "  [skip-version]    - Skip version increment"
            echo ""
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
    ACTION="install"
fi

print_header

# Check if we're in a git repository
if ! git rev-parse --git-dir > /dev/null 2>&1; then
    print_error "Not in a git repository"
    exit 1
fi

# Check if version-config.json exists
CONFIG_FILE="$SCRIPT_DIR/version-config.json"
if [ ! -f "$CONFIG_FILE" ]; then
    print_error "Configuration file not found: $CONFIG_FILE"
    print_error "Please ensure version-config.json exists in the Scripts directory"
    exit 1
fi

# Function to check hook status
check_hook_status() {
    local hook_name="$1"
    local git_hooks_dir="$PROJECT_ROOT/.git/hooks"
    local hook_file="$git_hooks_dir/$hook_name"
    
    if [ -f "$hook_file" ]; then
        if grep -q "Ecliptix Auto-Versioning" "$hook_file" 2>/dev/null; then
            echo "installed"
        else
            echo "conflict"
        fi
    else
        echo "not-installed"
    fi
}

# Function to install hooks
install_hooks() {
    local git_hooks_dir="$PROJECT_ROOT/.git/hooks"
    
    print_info "Installing auto-versioning git hooks..."
    
    # Create hooks directory if it doesn't exist
    mkdir -p "$HOOKS_DIR"
    mkdir -p "$git_hooks_dir"
    
    # Install pre-commit hook
    local pre_commit_status=$(check_hook_status "pre-commit")
    if [ "$pre_commit_status" = "conflict" ] && [ "$FORCE" = false ]; then
        print_warning "Pre-commit hook already exists and is not managed by auto-versioning"
        print_warning "Use --force to overwrite, or manually integrate with existing hook"
    else
        print_info "Installing pre-commit hook..."
        cat > "$git_hooks_dir/pre-commit" << 'EOF'
#!/bin/bash
# Ecliptix Auto-Versioning Pre-Commit Hook

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
AUTO_VERSION_SCRIPT="$PROJECT_ROOT/Scripts/auto-version-hook.sh"

if [ -f "$AUTO_VERSION_SCRIPT" ]; then
    "$AUTO_VERSION_SCRIPT" pre-commit
else
    echo "Warning: Auto-versioning script not found at $AUTO_VERSION_SCRIPT"
fi
EOF
        chmod +x "$git_hooks_dir/pre-commit"
        print_success "Pre-commit hook installed"
    fi
    
    # Install post-commit hook
    local post_commit_status=$(check_hook_status "post-commit")
    if [ "$post_commit_status" = "conflict" ] && [ "$FORCE" = false ]; then
        print_warning "Post-commit hook already exists and is not managed by auto-versioning"
        print_warning "Use --force to overwrite, or manually integrate with existing hook"
    else
        print_info "Installing post-commit hook..."
        cat > "$git_hooks_dir/post-commit" << 'EOF'
#!/bin/bash
# Ecliptix Auto-Versioning Post-Commit Hook

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
AUTO_VERSION_SCRIPT="$PROJECT_ROOT/Scripts/auto-version-hook.sh"

if [ -f "$AUTO_VERSION_SCRIPT" ]; then
    "$AUTO_VERSION_SCRIPT" post-commit
else
    echo "Warning: Auto-versioning script not found at $AUTO_VERSION_SCRIPT"
fi
EOF
        chmod +x "$git_hooks_dir/post-commit"
        print_success "Post-commit hook installed"
    fi
    
    # Create the hook implementation script
    print_info "Creating hook implementation script..."
    cat > "$SCRIPT_DIR/auto-version-hook.sh" << 'EOF'
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
EOF
    chmod +x "$SCRIPT_DIR/auto-version-hook.sh"
    print_success "Hook implementation script created"
    
    print_success "Auto-versioning setup completed!"
    echo ""
    print_info "Configuration loaded from: $CONFIG_FILE"
    print_info "Hooks installed in: $git_hooks_dir"
    echo ""
    print_info "Next commit will automatically increment version based on commit message patterns:"
    echo "  • [major] or BREAKING CHANGE: → Major version increment"
    echo "  • [minor] or feat: → Minor version increment" 
    echo "  • [patch] or fix: → Patch version increment"
    echo "  • [skip-version] or docs:/chore: → No version increment"
}

# Function to uninstall hooks
uninstall_hooks() {
    local git_hooks_dir="$PROJECT_ROOT/.git/hooks"
    
    print_info "Uninstalling auto-versioning git hooks..."
    
    # Remove hooks if they are ours
    for hook in "pre-commit" "post-commit"; do
        local hook_file="$git_hooks_dir/$hook"
        if [ -f "$hook_file" ]; then
            if grep -q "Ecliptix Auto-Versioning" "$hook_file" 2>/dev/null; then
                rm "$hook_file"
                print_success "Removed $hook hook"
            else
                print_warning "$hook hook exists but is not managed by auto-versioning"
            fi
        fi
    done
    
    # Remove hook implementation script
    if [ -f "$SCRIPT_DIR/auto-version-hook.sh" ]; then
        rm "$SCRIPT_DIR/auto-version-hook.sh"
        print_success "Removed hook implementation script"
    fi
    
    print_success "Auto-versioning uninstalled"
}

# Function to show status
show_status() {
    print_info "Auto-versioning status:"
    echo ""
    
    # Check configuration
    if [ -f "$CONFIG_FILE" ]; then
        print_success "Configuration file exists: $CONFIG_FILE"
    else
        print_error "Configuration file missing: $CONFIG_FILE"
    fi
    
    # Check hooks
    local pre_commit_status=$(check_hook_status "pre-commit")
    local post_commit_status=$(check_hook_status "post-commit")
    
    case $pre_commit_status in
        "installed")
            print_success "Pre-commit hook: Installed and managed by auto-versioning"
            ;;
        "conflict")
            print_warning "Pre-commit hook: Exists but not managed by auto-versioning"
            ;;
        "not-installed")
            print_warning "Pre-commit hook: Not installed"
            ;;
    esac
    
    case $post_commit_status in
        "installed")
            print_success "Post-commit hook: Installed and managed by auto-versioning"
            ;;
        "conflict")
            print_warning "Post-commit hook: Exists but not managed by auto-versioning"
            ;;
        "not-installed")
            print_warning "Post-commit hook: Not installed"
            ;;
    esac
    
    # Check hook implementation
    if [ -f "$SCRIPT_DIR/auto-version-hook.sh" ]; then
        print_success "Hook implementation: Available"
    else
        print_warning "Hook implementation: Missing"
    fi
    
    # Show current version
    if [ -f "$SCRIPT_DIR/version.sh" ]; then
        CURRENT_VERSION=$("$SCRIPT_DIR/version.sh" --action current | grep "Current version:" | cut -d' ' -f3 2>/dev/null || echo "Unknown")
        echo ""
        print_info "Current version: $CURRENT_VERSION"
    fi
    
    # Overall status
    echo ""
    if [ "$pre_commit_status" = "installed" ] && [ "$post_commit_status" = "installed" ]; then
        print_success "Auto-versioning is fully configured and active"
    else
        print_warning "Auto-versioning is not fully configured"
        echo "Run: $0 install"
    fi
}

# Execute the requested action
case $ACTION in
    "install")
        install_hooks
        ;;
    "uninstall")
        uninstall_hooks
        ;;
    "status")
        show_status
        ;;
    *)
        print_error "Unknown action: $ACTION"
        exit 1
        ;;
esac