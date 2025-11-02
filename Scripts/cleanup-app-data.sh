#!/usr/bin/env bash
set -euo pipefail

SCRIPT_NAME=$(basename "$0")
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly SEPARATOR_LINE="═══════════════════════════════════════════════════════════"

print_banner() {
    echo "╔════════════════════════════════════════════════════════════╗"
    echo "║         Ecliptix Application Data Cleanup Tool            ║"
    echo "║                                                            ║"
    echo "║  Removes all persisted application data for fresh init    ║"
    echo "╚════════════════════════════════════════════════════════════╝"
    echo
    return 0
}

print_usage() {
    cat <<EOF
Usage: $SCRIPT_NAME [OPTIONS]

Removes all persisted Ecliptix application data including:
  - DataProtection encryption keys
  - Secure storage (settings, membership, master keys)
  - Application logs

Options:
  --dry-run           Show what would be deleted without actually deleting
  --keep-logs         Keep log files (only remove keys and settings)
  --force             Skip confirmation prompt
  -h, --help          Display this help message

Examples:
  $SCRIPT_NAME                  # Interactive cleanup with confirmation
  $SCRIPT_NAME --dry-run        # Preview what will be deleted
  $SCRIPT_NAME --force          # Delete without confirmation
  $SCRIPT_NAME --keep-logs      # Delete keys/settings but keep logs

EOF
    return 0
}

detect_platform_appdata() {
    if [[ "$OSTYPE" == "darwin"* ]]; then
        echo "$HOME/Library/Application Support/Ecliptix"
    elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
        echo "$HOME/.local/share/Ecliptix"
    else
        echo "ERROR: Unsupported platform: $OSTYPE" >&2
        exit 1
    fi
}

calculate_directory_size() {
    local dir="$1"
    if [[ -d "$dir" ]]; then
        if [[ "$OSTYPE" == "darwin"* ]]; then
            du -sh "$dir" 2>/dev/null | awk '{print $1}'
        else
            du -sh "$dir" 2>/dev/null | cut -f1
        fi
    else
        echo "0B"
    fi
    return 0
}

count_files_recursive() {
    local dir="$1"
    if [[ -d "$dir" ]]; then
        find "$dir" -type f 2>/dev/null | wc -l | tr -d ' '
    else
        echo "0"
    fi
    return 0
}

print_directory_info() {
    local dir="$1"
    local label="$2"

    if [[ -d "$dir" ]]; then
        local size=$(calculate_directory_size "$dir")
        local file_count=$(count_files_recursive "$dir")
        echo "  ✓ $label"
        echo "    Path: $dir"
        echo "    Size: $size ($file_count files)"
    else
        echo "  ✗ $label (not found)"
    fi
    return 0
}

count_pattern_files() {
    local dir="$1"
    local pattern="$2"
    if [[ -d "$dir" ]]; then
        find "$dir" -maxdepth 1 -name "$pattern" -type f 2>/dev/null | wc -l | tr -d ' '
    else
        echo "0"
    fi
    return 0
}

check_data_exists() {
    local appdata="$1"

    if [[ -d "$appdata/Storage/DataProtection-Keys" ]] || \
       [[ -d "$appdata/Storage/state" ]] || \
       [[ -d "$appdata/Storage/logs" ]] || \
       [[ -d "$appdata/.keychain" ]] || \
       [[ -n "$(find "$appdata" -maxdepth 1 -name "master_*.ecliptix" -type f 2>/dev/null)" ]] || \
       [[ -n "$(find "$appdata" -maxdepth 1 -name "*.ecliptix" -type f 2>/dev/null)" ]]; then
        return 0
    else
        return 1
    fi
}

remove_directory_safe() {
    local dir="$1"
    local dry_run="$2"

    if [[ ! -d "$dir" ]]; then
        return 0
    fi

    if [[ "$dry_run" == "true" ]]; then
        echo "  [DRY-RUN] Would delete: $dir"
        return 0
    else
        echo "  Deleting: $dir"
        rm -rf "$dir"
        if [[ $? -eq 0 ]]; then
            echo "    ✓ Deleted successfully"
            return 0
        else
            echo "    ✗ Failed to delete" >&2
            return 1
        fi
    fi
}

main() {
    local args=("$@")
    local dry_run=false
    local keep_logs=false
    local force=false

    while [[ ${#args[@]} -gt 0 ]]; do
        local current_arg="${args[0]}"
        case $current_arg in
            --dry-run)
                dry_run=true
                args=("${args[@]:1}")
                ;;
            --keep-logs)
                keep_logs=true
                args=("${args[@]:1}")
                ;;
            --force)
                force=true
                args=("${args[@]:1}")
                ;;
            -h|--help)
                print_usage
                exit 0
                ;;
            *)
                echo "ERROR: Unknown option: $current_arg" >&2
                print_usage
                exit 1
                ;;
        esac
    done

    print_banner

    local appdata
    appdata=$(detect_platform_appdata)
    APPDATA="$appdata"
    echo "Platform: $(uname -s)"
    echo "Application data directory: $APPDATA"
    echo

    if ! check_data_exists "$APPDATA"; then
        echo "✓ No application data found. Nothing to clean up."
        exit 0
    fi

    echo "Found application data:"
    echo
    print_directory_info "$APPDATA/Storage/DataProtection-Keys" "DataProtection Keys"
    print_directory_info "$APPDATA/Storage/state" "Secure Storage (settings, keys, membership)"
    print_directory_info "$APPDATA/.keychain" "Keychain (encryption keys, machine key)"

    local master_count=$(count_pattern_files "$APPDATA" "master_*.ecliptix")
    local ecliptix_count=$(count_pattern_files "$APPDATA" "*.ecliptix")
    if [[ $master_count -gt 0 ]]; then
        echo "  ✓ Master Key Files"
        echo "    Path: $APPDATA/master_*.ecliptix"
        echo "    Count: $master_count files"
    else
        echo "  ✗ Master Key Files (not found)"
    fi

    if [[ $ecliptix_count -gt $master_count ]]; then
        local other_count=$((ecliptix_count - master_count))
        echo "  ✓ Other Ecliptix Files"
        echo "    Path: $APPDATA/*.ecliptix"
        echo "    Count: $other_count files"
    fi

    print_directory_info "$APPDATA/Storage/logs" "Application Logs"
    echo

    if [[ "$dry_run" == "true" ]]; then
        echo "$SEPARATOR_LINE"
        echo "DRY-RUN MODE: No files will be deleted"
        echo "$SEPARATOR_LINE"
        echo
    fi

    if [[ "$force" == "false" && "$dry_run" == "false" ]]; then
        echo "⚠️  WARNING: This will permanently delete all application data!"
        echo "   This includes:"
        echo "   - Encryption keys"
        echo "   - User settings and preferences"
        echo "   - Membership information"
        echo "   - Device identifiers"
        if [[ "$keep_logs" == "false" ]]; then
            echo "   - Application logs"
        fi
        echo
        read -p "Are you sure you want to continue? (yes/no): " -r
        echo
        if [[ ! $REPLY =~ ^[Yy][Ee][Ss]$ ]]; then
            echo "Cleanup cancelled."
            exit 0
        fi
    fi

    echo "Starting cleanup..."
    echo

    local errors=0

    remove_directory_safe "$APPDATA/Storage/DataProtection-Keys" "$dry_run" || ((errors++))
    remove_directory_safe "$APPDATA/Storage/state" "$dry_run" || ((errors++))
    remove_directory_safe "$APPDATA/.keychain" "$dry_run" || ((errors++))

    # Remove master key files
    if compgen -G "$APPDATA/master_*.ecliptix" > /dev/null 2>&1; then
        if [[ "$dry_run" == "true" ]]; then
            echo "  [DRY-RUN] Would delete: $APPDATA/master_*.ecliptix"
        else
            echo "  Deleting: $APPDATA/master_*.ecliptix"
            rm -f "$APPDATA/master_"*.ecliptix
            if [[ $? -eq 0 ]]; then
                echo "    ✓ Deleted successfully"
            else
                echo "    ✗ Failed to delete" >&2
                ((errors++))
            fi
        fi
    fi

    # Remove numbered ecliptix files
    if compgen -G "$APPDATA/[0-9]*.ecliptix" > /dev/null 2>&1; then
        if [[ "$dry_run" == "true" ]]; then
            echo "  [DRY-RUN] Would delete: $APPDATA/*.ecliptix (numbered files)"
        else
            echo "  Deleting: $APPDATA/*.ecliptix (numbered files)"
            rm -f "$APPDATA/"[0-9]*.ecliptix
            if [[ $? -eq 0 ]]; then
                echo "    ✓ Deleted successfully"
            else
                echo "    ✗ Failed to delete" >&2
                ((errors++))
            fi
        fi
    fi

    if [[ "$keep_logs" == "false" ]]; then
        remove_directory_safe "$APPDATA/Storage/logs" "$dry_run" || ((errors++))
    else
        echo "  [SKIPPED] Keeping logs: $APPDATA/Storage/logs"
    fi

    if [[ -d "$APPDATA/Storage" ]]; then
        local remaining=$(find "$APPDATA/Storage" -mindepth 1 2>/dev/null | wc -l | tr -d ' ')
        if [[ "$remaining" -eq 0 && "$dry_run" == "false" ]]; then
            echo
            echo "  Removing empty Storage directory"
            rmdir "$APPDATA/Storage" 2>/dev/null || true
        fi
    fi

    echo
    echo "$SEPARATOR_LINE"
    if [[ "$dry_run" == "true" ]]; then
        echo "✓ Dry-run completed. No files were deleted."
    elif [[ $errors -eq 0 ]]; then
        echo "✓ Cleanup completed successfully!"
        echo
        echo "The application will initialize with fresh settings on next launch."
    else
        echo "⚠️  Cleanup completed with $errors error(s)." >&2
        exit 1
    fi
    echo "$SEPARATOR_LINE"
}

main "$@"
