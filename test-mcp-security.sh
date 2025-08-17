#!/bin/bash

# Test script for security-protocol MCP server
echo "🔐 Testing Security Protocol MCP Server"
echo ""

# Test 1: Create AES-GCM encryption service
echo "1. Creating AES-GCM encryption service:"
echo '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"create_encryption_service","arguments":{"algorithm":"AES-GCM","keySize":256}}}' | \
node .mcp/servers/security-protocol.js 2>/dev/null | \
jq -r '.result.content[0].text' | head -20

echo ""
echo "---"
echo ""

# Test 2: Generate protocol step
echo "2. Creating protocol step for key exchange:"
echo '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"generate_protocol_step","arguments":{"name":"DiffieHellmanStep","stepType":"KeyExchange"}}}' | \
node .mcp/servers/security-protocol.js 2>/dev/null | \
jq -r '.result.content[0].text' | head -20

echo ""
echo "---"
echo ""

# Test 3: Generate gRPC interceptor
echo "3. Creating authentication gRPC interceptor:"
echo '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"generate_grpc_interceptor","arguments":{"name":"AuthenticationInterceptor","purpose":"Authentication"}}}' | \
node .mcp/servers/security-protocol.js 2>/dev/null | \
jq -r '.result.content[0].text' | head -15

echo ""
echo "✅ MCP Security Protocol Server test completed!"