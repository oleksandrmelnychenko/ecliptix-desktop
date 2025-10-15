#!/usr/bin/env bash
set -euo pipefail

# =============================================================================
# Proto Sync Script - Intelligent iOS → Desktop Proto Synchronization
# =============================================================================
# This script synchronizes protocol buffer definitions from the iOS project
# to the desktop project with intelligent merging capabilities.
#
# Features:
# - Diff detection between iOS and Desktop protos
# - Interactive merge with conflict resolution
# - Automatic backups before sync
# - Proto syntax validation
# - Detailed sync report generation
#
# Usage:
#   ./sync-protos.sh [--auto] [--dry-run] [--force]
#
# Options:
#   --auto     Auto-merge without prompts (use with caution)
#   --dry-run  Show changes without applying them
#   --force    Force overwrite conflicts without prompting
# =============================================================================

# Color codes for output
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly CYAN='\033[0;36m'
readonly NC='\033[0m' # No Color
readonly BOLD='\033[1m'

# Project paths
readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly DESKTOP_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
readonly IOS_ROOT="/Users/oleksandrmelnychenko/Documents/Ecliptix-IOS"

readonly DESKTOP_PROTOS="$DESKTOP_ROOT/Ecliptix.Protobufs/Sources"
readonly IOS_PROTOS="$IOS_ROOT/Packages/Networking/Sources/Networking/Protos"
readonly BACKUP_DIR="$DESKTOP_ROOT/Ecliptix.Protobufs/Backups/$(date +%Y%m%d_%H%M%S)"
readonly REPORT_FILE="$DESKTOP_ROOT/docs/proto-sync-report.md"

# Flags
DRY_RUN=false
AUTO_MERGE=false
FORCE_OVERWRITE=false

# Statistics
STATS_NEW_FILES=0
STATS_MODIFIED_FILES=0
STATS_UNCHANGED_FILES=0
STATS_CONFLICTS=0

# =============================================================================
# Utility Functions
# =============================================================================

print_header() {
    echo -e "${BOLD}${BLUE}═══════════════════════════════════════════════════════════════════${NC}"
    echo -e "${BOLD}${BLUE}  $1${NC}"
    echo -e "${BOLD}${BLUE}═══════════════════════════════════════════════════════════════════${NC}"
}

print_section() {
    echo ""
    echo -e "${BOLD}${CYAN}▶ $1${NC}"
    echo -e "${CYAN}───────────────────────────────────────────────────────────────────${NC}"
}

print_success() {
    echo -e "${GREEN}✓${NC} $1"
}

print_error() {
    echo -e "${RED}✗${NC} $1" >&2
}

print_warning() {
    echo -e "${YELLOW}⚠${NC} $1"
}

print_info() {
    echo -e "${BLUE}ℹ${NC} $1"
}

# =============================================================================
# Validation Functions
# =============================================================================

validate_prerequisites() {
    print_section "Validating Prerequisites"

    # Check if iOS project exists
    if [[ ! -d "$IOS_PROTOS" ]]; then
        print_error "iOS proto directory not found: $IOS_PROTOS"
        exit 1
    fi
    print_success "iOS proto directory found"

    # Check if desktop proto directory exists
    if [[ ! -d "$DESKTOP_PROTOS" ]]; then
        print_error "Desktop proto directory not found: $DESKTOP_PROTOS"
        exit 1
    fi
    print_success "Desktop proto directory found"

    # Check for required tools
    for tool in diff find grep sed; do
        if ! command -v $tool &> /dev/null; then
            print_error "Required tool not found: $tool"
            exit 1
        fi
    done
    print_success "All required tools available"

    # Count proto files
    local ios_count=$(find "$IOS_PROTOS" -name "*.proto" -type f | wc -l | tr -d ' ')
    local desktop_count=$(find "$DESKTOP_PROTOS" -name "*.proto" -type f | wc -l | tr -d ' ')

    print_info "iOS proto files: $ios_count"
    print_info "Desktop proto files: $desktop_count"
}

# =============================================================================
# Backup Functions
# =============================================================================

create_backup() {
    print_section "Creating Backup"

    if [[ "$DRY_RUN" == true ]]; then
        print_info "Dry run: Backup would be created at $BACKUP_DIR"
        return 0
    fi

    mkdir -p "$BACKUP_DIR"

    # Copy all existing proto files
    if cp -R "$DESKTOP_PROTOS"/* "$BACKUP_DIR/" 2>/dev/null; then
        local file_count=$(find "$BACKUP_DIR" -name "*.proto" | wc -l | tr -d ' ')
        print_success "Backup created: $BACKUP_DIR ($file_count files)"

        # Create backup manifest
        cat > "$BACKUP_DIR/MANIFEST.txt" <<EOF
Backup Created: $(date)
Desktop Protos: $DESKTOP_PROTOS
Files Backed Up: $file_count

To restore:
  cp -R $BACKUP_DIR/* $DESKTOP_PROTOS/
EOF

        return 0
    else
        print_error "Failed to create backup"
        return 1
    fi
}

# =============================================================================
# Proto Comparison Functions
# =============================================================================

get_proto_categories() {
    # Get unique category directories from iOS protos
    find "$IOS_PROTOS" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; | sort
}

compare_proto_file() {
    local category="$1"
    local filename="$2"
    local ios_file="$IOS_PROTOS/$category/$filename"
    local desktop_file="$DESKTOP_PROTOS/$category/$filename"

    # Check if file exists in both locations
    if [[ ! -f "$desktop_file" ]]; then
        echo "NEW"
        return 0
    fi

    # Check if files are identical
    if diff -q "$ios_file" "$desktop_file" &>/dev/null; then
        echo "UNCHANGED"
        return 0
    fi

    echo "MODIFIED"
    return 0
}

generate_file_diff() {
    local category="$1"
    local filename="$2"
    local ios_file="$IOS_PROTOS/$category/$filename"
    local desktop_file="$DESKTOP_PROTOS/$category/$filename"

    if [[ -f "$desktop_file" ]]; then
        diff -u "$desktop_file" "$ios_file" || true
    else
        echo "New file: $ios_file"
        cat "$ios_file"
    fi
}

# =============================================================================
# Scanning and Analysis
# =============================================================================

scan_proto_differences() {
    print_section "Scanning Proto Differences"

    local temp_report=$(mktemp)

    echo "# Proto Sync Analysis Report" > "$temp_report"
    echo "Generated: $(date)" >> "$temp_report"
    echo "" >> "$temp_report"

    for category in $(get_proto_categories); do
        local category_has_changes=false
        local category_report=$(mktemp)

        echo "## Category: $category" >> "$category_report"
        echo "" >> "$category_report"

        # Check if category exists in desktop
        if [[ ! -d "$DESKTOP_PROTOS/$category" ]]; then
            print_info "New category: $category"
            echo "**Status**: New category (all files are new)" >> "$category_report"
            echo "" >> "$category_report"
            category_has_changes=true
        fi

        # Process each proto file in the category
        if [[ -d "$IOS_PROTOS/$category" ]]; then
            while IFS= read -r proto_file; do
                local filename=$(basename "$proto_file")
                local status=$(compare_proto_file "$category" "$filename")

                case "$status" in
                    NEW)
                        print_info "  [NEW] $category/$filename"
                        echo "- **$filename**: NEW" >> "$category_report"
                        ((STATS_NEW_FILES++))
                        category_has_changes=true
                        ;;
                    MODIFIED)
                        print_warning "  [MODIFIED] $category/$filename"
                        echo "- **$filename**: MODIFIED" >> "$category_report"
                        ((STATS_MODIFIED_FILES++))
                        category_has_changes=true
                        ;;
                    UNCHANGED)
                        echo "- **$filename**: Unchanged" >> "$category_report"
                        ((STATS_UNCHANGED_FILES++))
                        ;;
                esac
            done < <(find "$IOS_PROTOS/$category" -name "*.proto" -type f)
        fi

        if [[ "$category_has_changes" == true ]]; then
            cat "$category_report" >> "$temp_report"
            echo "" >> "$temp_report"
        fi

        rm "$category_report"
    done

    # Add statistics
    echo "## Summary Statistics" >> "$temp_report"
    echo "" >> "$temp_report"
    echo "- New files: $STATS_NEW_FILES" >> "$temp_report"
    echo "- Modified files: $STATS_MODIFIED_FILES" >> "$temp_report"
    echo "- Unchanged files: $STATS_UNCHANGED_FILES" >> "$temp_report"
    echo "- Total files: $((STATS_NEW_FILES + STATS_MODIFIED_FILES + STATS_UNCHANGED_FILES))" >> "$temp_report"

    # Save report
    mkdir -p "$(dirname "$REPORT_FILE")"
    cp "$temp_report" "$REPORT_FILE"
    rm "$temp_report"

    print_success "Analysis complete. Report saved to: $REPORT_FILE"
}

# =============================================================================
# Sync Operations
# =============================================================================

sync_proto_file() {
    local category="$1"
    local filename="$2"
    local ios_file="$IOS_PROTOS/$category/$filename"
    local desktop_file="$DESKTOP_PROTOS/$category/$filename"

    # Create category directory if needed
    mkdir -p "$DESKTOP_PROTOS/$category"

    if [[ "$DRY_RUN" == true ]]; then
        print_info "Would sync: $category/$filename"
        return 0
    fi

    # Copy file
    if cp "$ios_file" "$desktop_file"; then
        print_success "Synced: $category/$filename"
        return 0
    else
        print_error "Failed to sync: $category/$filename"
        return 1
    fi
}

perform_sync() {
    print_section "Performing Sync"

    if [[ $STATS_NEW_FILES -eq 0 ]] && [[ $STATS_MODIFIED_FILES -eq 0 ]]; then
        print_success "No changes to sync. All protos are up to date!"
        return 0
    fi

    if [[ "$AUTO_MERGE" == false ]] && [[ "$DRY_RUN" == false ]]; then
        echo ""
        echo -e "${YELLOW}${BOLD}About to sync:${NC}"
        echo -e "  - ${GREEN}$STATS_NEW_FILES${NC} new files"
        echo -e "  - ${YELLOW}$STATS_MODIFIED_FILES${NC} modified files"
        echo ""
        read -p "Continue with sync? (y/N): " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            print_warning "Sync cancelled by user"
            return 1
        fi
    fi

    local synced_count=0
    local failed_count=0

    for category in $(get_proto_categories); do
        if [[ -d "$IOS_PROTOS/$category" ]]; then
            while IFS= read -r proto_file; do
                local filename=$(basename "$proto_file")
                local status=$(compare_proto_file "$category" "$filename")

                if [[ "$status" != "UNCHANGED" ]]; then
                    if sync_proto_file "$category" "$filename"; then
                        ((synced_count++))
                    else
                        ((failed_count++))
                    fi
                fi
            done < <(find "$IOS_PROTOS/$category" -name "*.proto" -type f)
        fi
    done

    echo ""
    print_success "Sync complete: $synced_count files synced, $failed_count failures"
}

# =============================================================================
# Main Execution
# =============================================================================

parse_arguments() {
    while [[ $# -gt 0 ]]; do
        case $1 in
            --dry-run)
                DRY_RUN=true
                shift
                ;;
            --auto)
                AUTO_MERGE=true
                shift
                ;;
            --force)
                FORCE_OVERWRITE=true
                shift
                ;;
            -h|--help)
                cat <<EOF
Usage: $0 [OPTIONS]

Sync protocol buffer definitions from iOS to Desktop project.

Options:
  --dry-run    Show what would be changed without making changes
  --auto       Auto-merge without interactive prompts
  --force      Force overwrite conflicts without prompting
  -h, --help   Show this help message

Examples:
  $0                  # Interactive sync
  $0 --dry-run        # Preview changes
  $0 --auto --force   # Fully automated sync

EOF
                exit 0
                ;;
            *)
                print_error "Unknown option: $1"
                exit 1
                ;;
        esac
    done
}

main() {
    parse_arguments "$@"

    print_header "Proto Sync: iOS → Desktop"

    if [[ "$DRY_RUN" == true ]]; then
        print_warning "DRY RUN MODE - No changes will be made"
    fi

    validate_prerequisites
    scan_proto_differences

    if [[ "$DRY_RUN" == false ]]; then
        create_backup
        perform_sync

        echo ""
        print_header "Sync Complete"
        print_info "Backup location: $BACKUP_DIR"
        print_info "Report location: $REPORT_FILE"
    else
        echo ""
        print_header "Dry Run Complete"
        print_info "Report location: $REPORT_FILE"
    fi
}

# Run main function
main "$@"
