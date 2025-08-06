#!/bin/bash

# Ecliptix Desktop Version Helper Script
# This script provides easy access to version management functionality

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PYTHON_SCRIPT="$SCRIPT_DIR/version-helper.py"

# Check if Python is available
if ! command -v python3 &> /dev/null; then
    echo "Error: Python 3 is required but not found"
    exit 1
fi

# Check if the Python script exists
if [ ! -f "$PYTHON_SCRIPT" ]; then
    echo "Error: version-helper.py not found at $PYTHON_SCRIPT"
    exit 1
fi

# Pass all arguments to the Python script
python3 "$PYTHON_SCRIPT" "$@"