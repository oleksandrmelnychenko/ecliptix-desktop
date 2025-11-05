#!/usr/bin/env python3
import re
from pathlib import Path

def pascal_to_upper_snake(name):
    s1 = re.sub('(.)([A-Z][a-z]+)', r'\1_\2', name)
    s2 = re.sub('([a-z0-9])([A-Z])', r'\1_\2', s1)
    s3 = re.sub('([a-zA-Z])([0-9])', r'\1_\2', s2)
    return s3.upper()

print("Scanning...")
root = Path.cwd()
mappings = {}

# Build mappings
for file in root.rglob('*.cs'):
    if 'obj' in file.parts or 'bin' in file.parts:
        continue
    try:
        content = file.read_text()
        for match in re.finditer(r'\bconst\s+[\w<>\[\]]+\s+(\w+)\s*=', content):
            old = match.group(1)
            if not re.match(r'^[A-Z_0-9]+$', old):
                new = pascal_to_upper_snake(old)
                if old != new:
                    mappings[old] = new
    except:
        pass

print(f"Found {len(mappings)} constants to convert")

# Apply mappings
files_changed = 0
total_replacements = 0

for file in root.rglob('*.cs'):
    if 'obj' in file.parts or 'bin' in file.parts:
        continue
    try:
        original = file.read_text()
        modified = original

        for old, new in mappings.items():
            modified = re.sub(r'\b' + re.escape(old) + r'\b', new, modified)

        if modified != original:
            file.write_text(modified)
            changes = sum(1 for o, n in mappings.items() if o in original)
            print(f"✓ {file.relative_to(root)} ({changes} constants)")
            files_changed += 1

    except Exception as e:
        print(f"✗ {file}: {e}")

print(f"\nConverted {files_changed} files")
