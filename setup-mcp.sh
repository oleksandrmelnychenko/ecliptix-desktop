#!/bin/bash

# Setup script for MCP servers with Claude Desktop
# This script configures Claude Desktop to use the Ecliptix MCP servers

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

print_info() {
    echo -e "${GREEN}ℹ️  $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

print_error() {
    echo -e "${RED}❌ $1${NC}"
}

echo "🚀 Setting up MCP servers for Claude Desktop..."
echo ""

# Check if Claude Desktop is installed
if [ ! -d "/Applications/Claude.app" ]; then
    print_warning "Claude Desktop not found in /Applications/"
    print_info "Please install Claude Desktop from https://claude.ai/download"
    echo ""
fi

# Get Claude Desktop config directory
CLAUDE_CONFIG_DIR="$HOME/Library/Application Support/Claude"
CLAUDE_CONFIG_FILE="$CLAUDE_CONFIG_DIR/claude_desktop_config.json"

# Create config directory if it doesn't exist
if [ ! -d "$CLAUDE_CONFIG_DIR" ]; then
    print_info "Creating Claude Desktop config directory..."
    mkdir -p "$CLAUDE_CONFIG_DIR"
fi

# Get current directory (project root)
PROJECT_ROOT="$(pwd)"

# Backup existing config if it exists
if [ -f "$CLAUDE_CONFIG_FILE" ]; then
    print_warning "Backing up existing Claude Desktop config..."
    cp "$CLAUDE_CONFIG_FILE" "$CLAUDE_CONFIG_FILE.backup.$(date +%Y%m%d_%H%M%S)"
fi

# Create the MCP configuration
print_info "Creating MCP server configuration..."
cat > "$CLAUDE_CONFIG_FILE" << EOF
{
  "mcpServers": {
    "ecliptix-dev": {
      "command": "node",
      "args": ["$PROJECT_ROOT/.mcp/servers/ecliptix-dev.js"],
      "env": {
        "WORKSPACE_ROOT": "$PROJECT_ROOT"
      }
    },
    "dotnet-project": {
      "command": "node",
      "args": ["$PROJECT_ROOT/.mcp/servers/dotnet-project.js"],
      "env": {
        "WORKSPACE_ROOT": "$PROJECT_ROOT"
      }
    },
    "security-protocol": {
      "command": "node",
      "args": ["$PROJECT_ROOT/.mcp/servers/security-protocol.js"],
      "env": {
        "WORKSPACE_ROOT": "$PROJECT_ROOT"
      }
    }
  }
}
EOF

print_info "✅ MCP configuration created at: $CLAUDE_CONFIG_FILE"
echo ""

# Test MCP servers
print_info "Testing MCP servers..."

# Test ecliptix-dev server
if echo '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | node "$PROJECT_ROOT/.mcp/servers/ecliptix-dev.js" > /dev/null 2>&1; then
    print_info "✅ ecliptix-dev server: Working"
else
    print_error "❌ ecliptix-dev server: Failed"
fi

# Test dotnet-project server
if echo '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | node "$PROJECT_ROOT/.mcp/servers/dotnet-project.js" > /dev/null 2>&1; then
    print_info "✅ dotnet-project server: Working"
else
    print_error "❌ dotnet-project server: Failed"
fi

# Test security-protocol server
if echo '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | node "$PROJECT_ROOT/.mcp/servers/security-protocol.js" > /dev/null 2>&1; then
    print_info "✅ security-protocol server: Working"
else
    print_error "❌ security-protocol server: Failed"
fi

echo ""
print_info "🎉 Setup complete!"
echo ""
echo "Next steps:"
echo "1. Restart Claude Desktop if it's currently running"
echo "2. Open Claude Desktop and you should see MCP servers connected"
echo "3. You can now use commands like:"
echo "   - 'Generate a new ViewModel for user settings using the ecliptix-dev MCP'"
echo "   - 'Check for vulnerable packages using the dotnet-project MCP'"
echo "   - 'Analyze security issues using the security-protocol MCP'"
echo ""
echo "Configuration file location: $CLAUDE_CONFIG_FILE"