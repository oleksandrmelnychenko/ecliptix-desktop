#!/bin/bash

# Script to analyze C# member ordering
# This script identifies files that may need member reordering

OUTPUT_FILE="/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/member_order_analysis.txt"

echo "Member Ordering Analysis" > "$OUTPUT_FILE"
echo "========================" >> "$OUTPUT_FILE"
echo "" >> "$OUTPUT_FILE"

BASE_DIR="/Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Core/Ecliptix.Core"

# Find all .cs files excluding obj and bin directories
find "$BASE_DIR" -name "*.cs" -type f ! -path "*/obj/*" ! -path "*/bin/*" | sort | while read -r file; do
    # Skip if file is empty or doesn't exist
    if [ ! -s "$file" ]; then
        continue
    fi

    # Extract relative path for readability
    rel_path="${file#$BASE_DIR/}"

    # Check for member patterns using grep
    has_const=$(grep -c "^\s*public const\|^\s*private const\|^\s*internal const\|^\s*protected const" "$file" || true)
    has_static_field=$(grep -c "^\s*public static\|^\s*private static.*=\|^\s*internal static.*=\|^\s*protected static" "$file" | grep -v "method\|property" || true)
    has_field=$(grep -c "^\s*private .*;\|^\s*protected .*;\|^\s*internal .*;\|^\s*public .*;" "$file" | grep -v "const\|static\|get;\|set;\|=>" || true)
    has_constructor=$(grep -c "^\s*public.*($\|^\s*private.*($\|^\s*internal.*($\|^\s*protected.*($" "$file" || true)
    has_property=$(grep -c "{\s*get;\|{\s*set;\|=> " "$file" || true)
    has_method=$(grep -c "^\s*public.*)\s*$\|^\s*private.*)\s*$\|^\s*internal.*)\s*$\|^\s*protected.*)\s*$" "$file" || true)

    # Simple heuristic: if file has multiple member types, it might need review
    member_types=$((has_const > 0 ? 1 : 0))
    member_types=$((member_types + (has_static_field > 0 ? 1 : 0)))
    member_types=$((member_types + (has_field > 0 ? 1 : 0)))
    member_types=$((member_types + (has_constructor > 0 ? 1 : 0)))
    member_types=$((member_types + (has_property > 0 ? 1 : 0)))
    member_types=$((member_types + (has_method > 0 ? 1 : 0)))

    if [ $member_types -ge 3 ]; then
        echo "FILE: $rel_path" >> "$OUTPUT_FILE"
        echo "  Const: $has_const, Static: $has_static_field, Fields: $has_field, Ctor: $has_constructor, Props: $has_property, Methods: $has_method" >> "$OUTPUT_FILE"
        echo "" >> "$OUTPUT_FILE"
    fi
done

echo "Analysis complete. Results written to: $OUTPUT_FILE"
