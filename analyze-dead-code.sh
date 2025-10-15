#!/bin/bash

# Comprehensive Dead Code Analysis Script
# Analyzes C# code for unused elements

set -euo pipefail

# Detect project root dynamically
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="${SCRIPT_DIR}"
OUTPUT_FILE="${PROJECT_ROOT}/dead-code-analysis.txt"

# Configuration
ANALYZE_FIELDS=true
ANALYZE_METHODS=true
ANALYZE_COMMENTS=true
ANALYZE_USINGS=true
VERBOSE=false
COLOR_OUTPUT=true

# ANSI color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
RESET='\033[0m'

# Parse command-line arguments
show_help() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Analyze C# code for potential dead code issues.

OPTIONS:
    -h, --help              Show this help message
    -o, --output FILE       Specify output file (default: dead-code-analysis.txt)
    -v, --verbose           Enable verbose output
    --no-color              Disable colored output
    --fields-only           Analyze only unused fields
    --methods-only          Analyze only empty methods
    --comments-only         Analyze only TODO/FIXME comments
    --usings-only           Analyze only unused using statements

EXAMPLES:
    $(basename "$0")                    # Run all analyses
    $(basename "$0") --fields-only      # Check only unused fields
    $(basename "$0") -o report.txt      # Save to custom file

EOF
}

while [[ $# -gt 0 ]]; do
    case $1 in
        -h|--help)
            show_help
            exit 0
            ;;
        -o|--output)
            OUTPUT_FILE="$2"
            shift 2
            ;;
        -v|--verbose)
            VERBOSE=true
            shift
            ;;
        --no-color)
            COLOR_OUTPUT=false
            RED=''
            GREEN=''
            YELLOW=''
            BLUE=''
            CYAN=''
            RESET=''
            shift
            ;;
        --fields-only)
            ANALYZE_METHODS=false
            ANALYZE_COMMENTS=false
            ANALYZE_USINGS=false
            shift
            ;;
        --methods-only)
            ANALYZE_FIELDS=false
            ANALYZE_COMMENTS=false
            ANALYZE_USINGS=false
            shift
            ;;
        --comments-only)
            ANALYZE_FIELDS=false
            ANALYZE_METHODS=false
            ANALYZE_USINGS=false
            shift
            ;;
        --usings-only)
            ANALYZE_FIELDS=false
            ANALYZE_METHODS=false
            ANALYZE_COMMENTS=false
            shift
            ;;
        *)
            echo "Unknown option: $1"
            show_help
            exit 1
            ;;
    esac
done

# Initialize output file
cat > "$OUTPUT_FILE" <<EOF
╔════════════════════════════════════════════════════════════════╗
║     ECLIPTIX DESKTOP - COMPREHENSIVE DEAD CODE ANALYSIS        ║
╚════════════════════════════════════════════════════════════════╝

Analysis Date: $(date "+%Y-%m-%d %H:%M:%S")
Project Root: ${PROJECT_ROOT}

EOF

# Temporary files for results
TMP_FIELDS="${OUTPUT_FILE}.fields.tmp"
TMP_METHODS="${OUTPUT_FILE}.methods.tmp"
TMP_COMMENTS="${OUTPUT_FILE}.comments.tmp"
TMP_USINGS="${OUTPUT_FILE}.usings.tmp"

: > "$TMP_FIELDS"
: > "$TMP_METHODS"
: > "$TMP_COMMENTS"
: > "$TMP_USINGS"

# Logging function
log() {
    if [ "$VERBOSE" = true ]; then
        echo -e "${CYAN}[INFO]${RESET} $1"
    fi
}

# Function to analyze a single file
analyze_file() {
    local file=$1
    local relative_path="${file#${PROJECT_ROOT}/}"

    # Skip generated and build files
    if [[ "$file" =~ (obj|bin)/|AssemblyInfo\.cs$|\.Designer\.cs$|\.g\.cs$ ]]; then
        return
    fi

    log "Analyzing: ${relative_path}"

    # Analyze unused private fields
    if [ "$ANALYZE_FIELDS" = true ]; then
        # Match private field declarations with improved regex
        grep -nE "^\s*private\s+(readonly\s+)?(static\s+)?[a-zA-Z0-9_<>,\[\]]+\s+[_a-zA-Z][_a-zA-Z0-9]*\s*[=;]" "$file" 2>/dev/null | while IFS=: read -r line_num line_content; do
            # Extract field name more accurately
            local field_name
            field_name=$(echo "$line_content" | sed -E 's/.*\s([_a-zA-Z][_a-zA-Z0-9]*)\s*[=;].*/\1/')

            if [ -n "$field_name" ]; then
                # Count references (excluding the declaration line)
                local usage_count
                usage_count=$(grep -nw "$field_name" "$file" 2>/dev/null | grep -v "^${line_num}:" | wc -l | tr -d ' ')

                if [ "$usage_count" -eq 0 ]; then
                    echo "${relative_path}|${line_num}|${field_name}" >> "$TMP_FIELDS"
                fi
            fi
        done || true
    fi

    # Analyze empty methods
    if [ "$ANALYZE_METHODS" = true ] && command -v rg >/dev/null 2>&1; then
        # Find methods with empty bodies (including whitespace)
        rg --line-number --pcre2 '^\s*(?:public|private|protected|internal)(?:\s+(?:static|virtual|override|abstract|sealed|async|extern))?\s+[\w\<\>\[\]]+\s+\w+\([^)]*\)\s*\{\s*\}' "$file" 2>/dev/null | while IFS=: read -r line_num match; do
            if [ -n "$match" ]; then
                local method_sig
                method_sig=$(echo "$match" | sed -E 's/^\s+//;s/\s+/ /g' | cut -c1-80)
                echo "${relative_path}|${line_num}|${method_sig}" >> "$TMP_METHODS"
            fi
        done || true
    fi

    # Analyze TODO/FIXME/HACK comments
    if [ "$ANALYZE_COMMENTS" = true ]; then
        grep -niE '//\s*(TODO|FIXME|HACK|XXX|BUG|OPTIMIZE)' "$file" 2>/dev/null | while IFS=: read -r line_num comment; do
            local clean_comment
            clean_comment=$(echo "$comment" | sed -E 's/^\s+//;s/\s+/ /g' | cut -c1-100)
            echo "${relative_path}|${line_num}|${clean_comment}" >> "$TMP_COMMENTS"
        done || true
    fi

    # Analyze potentially unused using statements
    if [ "$ANALYZE_USINGS" = true ]; then
        # Common namespaces that are often implicitly used (whitelist)
        local common_namespaces=(
            "System"
            "System.Collections.Generic"
            "System.Linq"
            "System.Threading.Tasks"
            "System.Text"
            "Microsoft.Extensions.DependencyInjection"
        )

        grep -nE "^\s*using\s+[a-zA-Z0-9_.]+;" "$file" 2>/dev/null | while IFS=: read -r line_num using_stmt; do
            local namespace
            namespace=$(echo "$using_stmt" | sed -E 's/.*using\s+([a-zA-Z0-9_.]+);.*/\1/')

            if [ -n "$namespace" ]; then
                # Skip common namespaces
                local skip=false
                for common in "${common_namespaces[@]}"; do
                    if [ "$namespace" = "$common" ]; then
                        skip=true
                        break
                    fi
                done

                if [ "$skip" = true ]; then
                    continue
                fi

                # Extract the last part of the namespace (most specific)
                local ns_last
                ns_last=$(echo "$namespace" | awk -F. '{print $NF}')

                # Check if it's used anywhere in the file (improved heuristic)
                local usage_count
                # Check for the namespace identifier in code (not in comments or strings)
                usage_count=$(grep -v "^\s*//" "$file" 2>/dev/null | grep -c "\b${ns_last}\b" 2>/dev/null || echo 0)

                # If appears 2 or fewer times (using statement + maybe one false match), might be unused
                if [ "$usage_count" -le 2 ]; then
                    echo "${relative_path}|${line_num}|${namespace}" >> "$TMP_USINGS"
                fi
            fi
        done || true
    fi
}

# Main analysis
echo -e "${BLUE}Starting analysis...${RESET}"

# Find all C# files and analyze them
file_count=0
while IFS= read -r file || [ -n "$file" ]; do
    if [ -n "$file" ]; then
        analyze_file "$file"
        file_count=$((file_count + 1))
    fi
done < <(find "$PROJECT_ROOT" -type f -name "*.cs")

echo -e "${GREEN}Analyzed ${file_count} C# files${RESET}"

# Count results
unused_fields_count=$(wc -l < "$TMP_FIELDS" | tr -d ' ')
empty_methods_count=$(wc -l < "$TMP_METHODS" | tr -d ' ')
todo_comments_count=$(wc -l < "$TMP_COMMENTS" | tr -d ' ')
unused_usings_count=$(wc -l < "$TMP_USINGS" | tr -d ' ')
total_issues=$((unused_fields_count + empty_methods_count + todo_comments_count + unused_usings_count))

# Generate formatted report
cat >> "$OUTPUT_FILE" <<EOF
┌────────────────────────────────────────────────────────────────┐
│ SUMMARY STATISTICS                                             │
└────────────────────────────────────────────────────────────────┘

  Files Analyzed:           ${file_count}
  Total Issues Found:       ${total_issues}

  • Unused Private Fields:  ${unused_fields_count}
  • Empty Methods:          ${empty_methods_count}
  • TODO Comments:          ${todo_comments_count}
  • Potentially Unused Using Statements: ${unused_usings_count}

EOF

# Output detailed findings
if [ "$unused_fields_count" -gt 0 ]; then
    cat >> "$OUTPUT_FILE" <<EOF

┌────────────────────────────────────────────────────────────────┐
│ UNUSED PRIVATE FIELDS (${unused_fields_count})                                      │
└────────────────────────────────────────────────────────────────┘

EOF
    while IFS='|' read -r file line field; do
        printf "  %-60s %s:%s\n" "$field" "$file" "$line" >> "$OUTPUT_FILE"
    done < "$TMP_FIELDS"
fi

if [ "$empty_methods_count" -gt 0 ]; then
    cat >> "$OUTPUT_FILE" <<EOF


┌────────────────────────────────────────────────────────────────┐
│ EMPTY METHODS (${empty_methods_count})                                             │
└────────────────────────────────────────────────────────────────┘

EOF
    while IFS='|' read -r file line method; do
        printf "  %s:%s\n    %s\n\n" "$file" "$line" "$method" >> "$OUTPUT_FILE"
    done < "$TMP_METHODS"
fi

if [ "$todo_comments_count" -gt 0 ]; then
    cat >> "$OUTPUT_FILE" <<EOF

┌────────────────────────────────────────────────────────────────┐
│ TODO/FIXME COMMENTS (${todo_comments_count})                                       │
└────────────────────────────────────────────────────────────────┘

EOF
    while IFS='|' read -r file line comment; do
        printf "  %s:%s\n    %s\n\n" "$file" "$line" "$comment" >> "$OUTPUT_FILE"
    done < "$TMP_COMMENTS"
fi

if [ "$unused_usings_count" -gt 0 ]; then
    cat >> "$OUTPUT_FILE" <<EOF

┌────────────────────────────────────────────────────────────────┐
│ POTENTIALLY UNUSED USING STATEMENTS (${unused_usings_count})                        │
└────────────────────────────────────────────────────────────────┘

Note: This is a simple heuristic check. Verify before removing.

EOF
    while IFS='|' read -r file line namespace; do
        printf "  %-60s %s:%s\n" "$namespace" "$file" "$line" >> "$OUTPUT_FILE"
    done < "$TMP_USINGS"
fi

# Recommendations
cat >> "$OUTPUT_FILE" <<EOF


┌────────────────────────────────────────────────────────────────┐
│ RECOMMENDATIONS                                                │
└────────────────────────────────────────────────────────────────┘

For more comprehensive analysis, consider:

  1. Use .NET native analyzers:
     dotnet format analyzers --verify-no-changes

  2. Run with code analysis enabled:
     dotnet build -p:RunAnalyzers=true

  3. Check for unused code with IDE0051, IDE0052 analyzers
     (already configured in Directory.Build.props)

  4. Review TODO comments and convert to issues/tasks

EOF

# Cleanup
rm -f "$TMP_FIELDS" "$TMP_METHODS" "$TMP_COMMENTS" "$TMP_USINGS"

echo -e "${GREEN}✓ Analysis complete!${RESET}"
echo -e "Results saved to: ${YELLOW}${OUTPUT_FILE}${RESET}"

if [ "$total_issues" -eq 0 ]; then
    echo -e "${GREEN}✓ No issues found!${RESET}"
else
    echo -e "${YELLOW}⚠ Found ${total_issues} potential issues${RESET}"
fi
