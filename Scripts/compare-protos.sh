#!/usr/bin/env bash
set -euo pipefail

# =============================================================================
# Proto Comparison Script - Detailed iOS vs Desktop Proto Analysis
# =============================================================================
# This script provides detailed comparison between iOS and Desktop proto files.
#
# Features:
# - Side-by-side file comparison
# - Line-by-line diff viewing
# - Missing file detection
# - Package/namespace comparison
# - Message and service inventory
#
# Usage:
#   ./compare-protos.sh [category] [filename]
#   ./compare-protos.sh                     # Compare all
#   ./compare-protos.sh common             # Compare category
#   ./compare-protos.sh common types.proto # Compare specific file
# =============================================================================

readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly CYAN='\033[0;36m'
readonly NC='\033[0m'
readonly BOLD='\033[1m'

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly DESKTOP_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
readonly IOS_ROOT="/Users/oleksandrmelnychenko/Documents/Ecliptix-IOS"

readonly DESKTOP_PROTOS="$DESKTOP_ROOT/Ecliptix.Protobufs/Sources"
readonly IOS_PROTOS="$IOS_ROOT/Packages/Networking/Sources/Networking/Protos"

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

print_file_header() {
    echo ""
    echo -e "${BOLD}${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo -e "${BOLD}${YELLOW}  $1${NC}"
    echo -e "${BOLD}${YELLOW}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
}

compare_file_metadata() {
    local category="$1"
    local filename="$2"
    local ios_file="$IOS_PROTOS/$category/$filename"
    local desktop_file="$DESKTOP_PROTOS/$category/$filename"

    print_file_header "$category/$filename"

    # Check existence
    if [[ ! -f "$ios_file" ]]; then
        echo -e "${RED}iOS file not found${NC}"
        return 1
    fi

    if [[ ! -f "$desktop_file" ]]; then
        echo -e "${YELLOW}Desktop file not found (NEW in iOS)${NC}"
        echo -e "\n${BOLD}iOS File Content:${NC}"
        cat "$ios_file" | head -30
        return 0
    fi

    # Extract metadata
    local ios_package=$(grep "^package" "$ios_file" | head -1 || echo "N/A")
    local desktop_package=$(grep "^package" "$desktop_file" | head -1 || echo "N/A")

    local ios_namespace=$(grep "csharp_namespace" "$ios_file" | head -1 || echo "N/A")
    local desktop_namespace=$(grep "csharp_namespace" "$desktop_file" | head -1 || echo "N/A")

    local ios_lines=$(wc -l < "$ios_file" | tr -d ' ')
    local desktop_lines=$(wc -l < "$desktop_file" | tr -d ' ')

    local ios_messages=$(grep -c "^message\s" "$ios_file" || echo "0")
    local desktop_messages=$(grep -c "^message\s" "$desktop_file" || echo "0")

    local ios_services=$(grep -c "^service\s" "$ios_file" || echo "0")
    local desktop_services=$(grep -c "^service\s" "$desktop_file" || echo "0")

    local ios_enums=$(grep -c "^enum\s" "$ios_file" || echo "0")
    local desktop_enums=$(grep -c "^enum\s" "$desktop_file" || echo "0")

    # Display comparison table
    echo -e "\n${BOLD}Metadata Comparison:${NC}"
    printf "%-25s | %-35s | %-35s\n" "Property" "iOS" "Desktop"
    printf "%s\n" "─────────────────────────┼─────────────────────────────────────┼─────────────────────────────────────"
    printf "%-25s | %-35s | %-35s\n" "Package" "${ios_package:0:35}" "${desktop_package:0:35}"
    printf "%-25s | %-35s | %-35s\n" "Namespace" "${ios_namespace:0:35}" "${desktop_namespace:0:35}"
    printf "%-25s | %-35s | %-35s\n" "Lines" "$ios_lines" "$desktop_lines"
    printf "%-25s | %-35s | %-35s\n" "Messages" "$ios_messages" "$desktop_messages"
    printf "%-25s | %-35s | %-35s\n" "Services" "$ios_services" "$desktop_services"
    printf "%-25s | %-35s | %-35s\n" "Enums" "$ios_enums" "$desktop_enums"

    # Check if files are identical
    if diff -q "$ios_file" "$desktop_file" &>/dev/null; then
        echo -e "\n${GREEN}✓ Files are identical${NC}"
        return 0
    fi

    # Show diff
    echo -e "\n${BOLD}Differences:${NC}"
    diff -u --color=always "$desktop_file" "$ios_file" | tail -n +3 || true
}

compare_category() {
    local category="$1"

    print_section "Category: $category"

    # Check if category exists in both
    local ios_has_category=false
    local desktop_has_category=false

    [[ -d "$IOS_PROTOS/$category" ]] && ios_has_category=true
    [[ -d "$DESKTOP_PROTOS/$category" ]] && desktop_has_category=true

    if [[ "$ios_has_category" == false ]]; then
        echo -e "${RED}Category not found in iOS${NC}"
        return 1
    fi

    if [[ "$desktop_has_category" == false ]]; then
        echo -e "${YELLOW}Category not found in Desktop (NEW in iOS)${NC}"
        echo -e "Files in iOS:"
        find "$IOS_PROTOS/$category" -name "*.proto" -type f -exec basename {} \; | sort
        return 0
    fi

    # Get all unique proto files
    local all_files=$(mktemp)
    find "$IOS_PROTOS/$category" -name "*.proto" -type f -exec basename {} \; | sort > "$all_files"
    find "$DESKTOP_PROTOS/$category" -name "*.proto" -type f -exec basename {} \; | sort >> "$all_files"
    cat "$all_files" | sort -u > "${all_files}.unique"
    mv "${all_files}.unique" "$all_files"

    # Compare each file
    while IFS= read -r filename; do
        compare_file_metadata "$category" "$filename"
    done < "$all_files"

    rm "$all_files"
}

generate_inventory_report() {
    print_section "Generating Inventory Report"

    local report_file="$DESKTOP_ROOT/docs/proto-inventory-report.md"
    mkdir -p "$(dirname "$report_file")"

    cat > "$report_file" <<EOF
# Proto Inventory Report

Generated: $(date)

## iOS Proto Files

EOF

    echo "### File Count by Category" >> "$report_file"
    echo "" >> "$report_file"
    for category in $(find "$IOS_PROTOS" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; | sort); do
        local count=$(find "$IOS_PROTOS/$category" -name "*.proto" -type f | wc -l | tr -d ' ')
        echo "- **$category**: $count files" >> "$report_file"
    done

    echo "" >> "$report_file"
    echo "## Desktop Proto Files" >> "$report_file"
    echo "" >> "$report_file"
    echo "### File Count by Category" >> "$report_file"
    echo "" >> "$report_file"
    for category in $(find "$DESKTOP_PROTOS" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; | sort); do
        local count=$(find "$DESKTOP_PROTOS/$category" -name "*.proto" -type f | wc -l | tr -d ' ')
        echo "- **$category**: $count files" >> "$report_file"
    done

    echo -e "\n${GREEN}✓${NC} Report saved to: $report_file"
}

main() {
    local category="${1:-}"
    local filename="${2:-}"

    print_header "Proto Comparison Tool"

    if [[ -n "$filename" ]]; then
        # Compare specific file
        compare_file_metadata "$category" "$filename"
    elif [[ -n "$category" ]]; then
        # Compare specific category
        compare_category "$category"
    else
        # Compare all categories
        for cat in $(find "$IOS_PROTOS" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; | sort); do
            compare_category "$cat"
        done

        generate_inventory_report
    fi
}

main "$@"
