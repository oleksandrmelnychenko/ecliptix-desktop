#!/bin/bash

# Comprehensive Dead Code Analysis Script
# Analyzes C# code for unused elements

PROJECT_ROOT="/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop"
OUTPUT_FILE="${PROJECT_ROOT}/dead-code-analysis.txt"

echo "=== ECLIPTIX DESKTOP - COMPREHENSIVE DEAD CODE ANALYSIS ===" > "$OUTPUT_FILE"
echo "Analysis Date: $(date)" >> "$OUTPUT_FILE"
echo "" >> "$OUTPUT_FILE"

# Function to analyze a single file
analyze_file() {
    local file=$1
    local filename=$(basename "$file")
    local relative_path=${file#${PROJECT_ROOT}/}

    # Skip generated files
    if [[ "$file" =~ obj/ ]] || [[ "$file" =~ bin/ ]] || [[ "$file" =~ AssemblyInfo ]]; then
        return
    fi

    # Unused private fields (never referenced after declaration)
    grep -n "private.*;" "$file" | while read -r line; do
        local line_num=$(echo "$line" | cut -d: -f1)
        local field_name=$(echo "$line" | sed -E 's/.*private[^=;]+([_a-zA-Z][_a-zA-Z0-9]*).*/\1/')
        if [ ! -z "$field_name" ]; then
            local usage_count=$(grep -c "\b$field_name\b" "$file")
            if [ "$usage_count" -eq 1 ]; then
                echo "UNUSED_FIELD|$relative_path|$line_num|$field_name"
            fi
        fi
    done

    # Empty methods
    grep -Pzo "(?s)(private|public|protected|internal).*\{[\s]*\}" "$file" | while read -r method; do
        if [ ! -z "$method" ]; then
            echo "EMPTY_METHOD|$relative_path|unknown|$method"
        fi
    done

    # TODO/FIXME comments
    grep -n "//.*TODO\|//.*FIXME\|//.*HACK" "$file" | while read -r line; do
        local line_num=$(echo "$line" | cut -d: -f1)
        local comment=$(echo "$line" | cut -d: -f2-)
        echo "TODO_COMMENT|$relative_path|$line_num|$comment"
    done
}

# Find all C# files and analyze them
find "$PROJECT_ROOT" -type f -name "*.cs" | while read -r file; do
    analyze_file "$file"
done >> "${OUTPUT_FILE}.tmp"

# Process results
echo "=== STATISTICS ===" >> "$OUTPUT_FILE"
echo "Total unused fields: $(grep -c "^UNUSED_FIELD" "${OUTPUT_FILE}.tmp" 2>/dev/null || echo 0)" >> "$OUTPUT_FILE"
echo "Total empty methods: $(grep -c "^EMPTY_METHOD" "${OUTPUT_FILE}.tmp" 2>/dev/null || echo 0)" >> "$OUTPUT_FILE"
echo "Total TODO comments: $(grep -c "^TODO_COMMENT" "${OUTPUT_FILE}.tmp" 2>/dev/null || echo 0)" >> "$OUTPUT_FILE"
echo "" >> "$OUTPUT_FILE"

# Sort and format results
if [ -f "${OUTPUT_FILE}.tmp" ]; then
    cat "${OUTPUT_FILE}.tmp" >> "$OUTPUT_FILE"
    rm "${OUTPUT_FILE}.tmp"
fi

echo "Analysis complete. Results saved to: $OUTPUT_FILE"
