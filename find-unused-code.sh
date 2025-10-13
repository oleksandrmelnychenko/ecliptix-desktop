#!/bin/bash

# Script to find unused code in the codebase
# This will search for private members that are defined but never referenced

echo "=== Finding Unused Code in Ecliptix Desktop ==="
echo ""

# Create output file
OUTPUT_FILE="unused-code-report.txt"
> "$OUTPUT_FILE"

echo "Searching for unused code..." >> "$OUTPUT_FILE"
echo "Generated: $(date)" >> "$OUTPUT_FILE"
echo "======================================" >> "$OUTPUT_FILE"
echo "" >> "$OUTPUT_FILE"

# Find all C# files
CS_FILES=$(find Ecliptix.Core Ecliptix.Protocol.System Ecliptix.Opaque.Protocol Ecliptix.Utilities -name "*.cs" 2>/dev/null)

# Counter
UNUSED_COUNT=0

# Search for private const fields
echo "=== Checking Private Constants ===" >> "$OUTPUT_FILE"
for file in $CS_FILES; do
    # Extract private const names
    grep -n "private const" "$file" 2>/dev/null | while IFS=: read -r line_num line_content; do
        # Extract the constant name
        const_name=$(echo "$line_content" | sed -E 's/.*private const .* ([A-Za-z_][A-Za-z0-9_]*) .*/\1/')
        if [ -n "$const_name" ]; then
            # Count occurrences (should be > 1 if used)
            count=$(grep -c "\b$const_name\b" "$file" 2>/dev/null || echo "0")
            if [ "$count" -eq "1" ]; then
                echo "  UNUSED: $file:$line_num - private const $const_name" >> "$OUTPUT_FILE"
                ((UNUSED_COUNT++))
            fi
        fi
    done
done

echo "" >> "$OUTPUT_FILE"

# Search for private readonly fields
echo "=== Checking Private Readonly Fields ===" >> "$OUTPUT_FILE"
for file in $CS_FILES; do
    grep -n "private readonly" "$file" 2>/dev/null | while IFS=: read -r line_num line_content; do
        # Extract field name
        field_name=$(echo "$line_content" | sed -E 's/.*private readonly .* _([A-Za-z0-9_]*).*/\1/')
        if [ -n "$field_name" ]; then
            # Count occurrences
            count=$(grep -c "_$field_name" "$file" 2>/dev/null || echo "0")
            if [ "$count" -eq "1" ]; then
                echo "  UNUSED: $file:$line_num - private readonly _$field_name" >> "$OUTPUT_FILE"
                ((UNUSED_COUNT++))
            fi
        fi
    done
done

echo "" >> "$OUTPUT_FILE"

# Search for private methods
echo "=== Checking Private Methods ===" >> "$OUTPUT_FILE"
for file in $CS_FILES; do
    grep -n "private .* [A-Za-z_][A-Za-z0-9_]*(" "$file" 2>/dev/null | while IFS=: read -r line_num line_content; do
        # Skip lines with comments
        if echo "$line_content" | grep -q "//"; then
            continue
        fi
        # Extract method name
        method_name=$(echo "$line_content" | sed -E 's/.*private .* ([A-Za-z_][A-Za-z0-9_]*)\(.*/\1/')
        if [ -n "$method_name" ] && [ "$method_name" != "private" ]; then
            # Count occurrences
            count=$(grep -c "\b$method_name\b" "$file" 2>/dev/null || echo "0")
            if [ "$count" -eq "1" ]; then
                echo "  SUSPICIOUS: $file:$line_num - private method $method_name()" >> "$OUTPUT_FILE"
                ((UNUSED_COUNT++))
            fi
        fi
    done
done

echo "" >> "$OUTPUT_FILE"
echo "======================================" >> "$OUTPUT_FILE"
echo "Total suspicious items found: $UNUSED_COUNT" >> "$OUTPUT_FILE"
echo "" >> "$OUTPUT_FILE"

# Display results
cat "$OUTPUT_FILE"

echo ""
echo "Report saved to: $OUTPUT_FILE"
