#!/usr/bin/env python3
"""
Enhanced script to convert const field names to UPPER_CASE with underscores.
This version handles qualified references properly.
"""

import os
import re
import sys
from pathlib import Path
from typing import Dict, Set, Tuple

def pascal_to_upper_snake(name: str) -> str:
    """Convert PascalCase to UPPER_SNAKE_CASE."""
    # Insert underscore before uppercase letters
    s1 = re.sub('(.)([A-Z][a-z]+)', r'\1_\2', name)
    # Insert underscore before uppercase letters followed by lowercase
    s2 = re.sub('([a-z0-9])([A-Z])', r'\1_\2', s1)
    # Handle numbers
    s3 = re.sub('([a-zA-Z])([0-9])', r'\1_\2', s2)
    return s3.upper()

def find_all_const_declarations(root_path: Path) -> Dict[str, str]:
    """
    Scan all C# files and build a complete mapping of const names to their UPPER_CASE equivalents.
    Returns: {old_name: new_name}
    """
    print("Phase 1: Scanning for const declarations...")

    mappings = {}
    pattern = r'\bconst\s+[a-zA-Z<>\[\]]+\s+([A-Za-z][A-Za-z0-9]*)\s*='

    cs_files = []
    for file_path in root_path.rglob('*.cs'):
        if 'obj' in file_path.parts or 'bin' in file_path.parts:
            continue
        cs_files.append(file_path)

    for file_path in cs_files:
        try:
            with open(file_path, 'r', encoding='utf-8') as f:
                content = f.read()

            matches = re.finditer(pattern, content)
            for match in matches:
                old_name = match.group(1)

                # Skip if already UPPER_CASE
                if re.match(r'^[A-Z_0-9]+$', old_name):
                    continue

                new_name = pascal_to_upper_snake(old_name)

                if old_name != new_name:
                    mappings[old_name] = new_name

        except Exception as e:
            print(f"  Warning: Error reading {file_path}: {e}", file=sys.stderr)

    print(f"  Found {len(mappings)} const fields to convert\\n")
    return mappings

def apply_mappings_to_all_files(root_path: Path, mappings: Dict[str, str]) -> Tuple[int, int]:
    """
    Apply the const name mappings to ALL C# files in the solution.
    Returns: (files_changed, total_replacements)
    """
    print("Phase 2: Applying conversions to all files...")

    files_changed = 0
    total_replacements = 0

    cs_files = []
    for file_path in root_path.rglob('*.cs'):
        if 'obj' in file_path.parts or 'bin' in file_path.parts:
            continue
        cs_files.append(file_path)

    for file_path in cs_files:
        try:
            with open(file_path, 'r', encoding='utf-8') as f:
                original_content = f.read()

            modified_content = original_content
            file_replacements = 0

            # Apply each mapping with word boundaries
            for old_name, new_name in mappings.items():
                pattern = r'\\b' + re.escape(old_name) + r'\\b'
                new_modified, count = re.subn(pattern, new_name, modified_content)
                if count > 0:
                    modified_content = new_modified
                    file_replacements += count

            # Only write if changed
            if modified_content != original_content:
                # Create backup
                backup_path = file_path.with_suffix('.cs.bak')
                with open(backup_path, 'w', encoding='utf-8') as f:
                    f.write(original_content)

                # Write new content
                with open(file_path, 'w', encoding='utf-8') as f:
                    f.write(modified_content)

                files_changed += 1
                total_replacements += file_replacements
                print(f"  ✓ {file_path.relative_to(root_path)} ({file_replacements} replacements)")

        except Exception as e:
            print(f"  ✗ Error processing {file_path}: {e}", file=sys.stderr)

    return files_changed, total_replacements

def main():
    """Main entry point."""
    print("=" * 70)
    print("Converting ALL const field names to UPPER_CASE...")
    print("=" * 70)
    print()

    root_path = Path.cwd()

    # Phase 1: Build complete mapping
    mappings = find_all_const_declarations(root_path)

    if not mappings:
        print("No const fields found that need conversion.")
        return

    print(f"Sample conversions:")
    for i, (old_name, new_name) in enumerate(list(mappings.items())[:10]):
        print(f"  {old_name} → {new_name}")
    if len(mappings) > 10:
        print(f"  ... and {len(mappings) - 10} more")
    print()

    # Phase 2: Apply to all files
    files_changed, total_replacements = apply_mappings_to_all_files(root_path, mappings)

    print()
    print("=" * 70)
    print("Conversion complete!")
    print("=" * 70)
    print(f"  Files changed: {files_changed}")
    print(f"  Total replacements: {total_replacements}")
    print(f"  Const declarations converted: {len(mappings)}")
    print()
    print("Next steps:")
    print("  1. Review changes: git diff")
    print("  2. Build solution: dotnet build")
    print("  3. Remove backups if satisfied: find . -name '*.cs.bak' -delete")
    print()

if __name__ == '__main__':
    main()
