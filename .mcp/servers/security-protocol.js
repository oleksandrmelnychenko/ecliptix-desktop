#!/usr/bin/env node

/**
 * Security Protocol MCP Server
 * Provides tools for cryptographic operations and security protocol implementation
 */

import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ErrorCode,
  ListResourcesRequestSchema,
  ListToolsRequestSchema,
  McpError,
  ReadResourceRequestSchema,
} from '@modelcontextprotocol/sdk/types.js';
import fs from 'fs/promises';
import path from 'path';

const WORKSPACE_ROOT = process.env.WORKSPACE_ROOT || process.cwd();

class SecurityProtocolServer {
  constructor() {
    this.server = new Server(
      {
        name: 'security-protocol',
        version: '0.1.0',
      },
      {
        capabilities: {
          resources: {},
          tools: {},
        },
      }
    );

    this.setupToolHandlers();
    this.setupResourceHandlers();
  }

  setupToolHandlers() {
    this.server.setRequestHandler(ListToolsRequestSchema, async () => ({
      tools: [
        {
          name: 'generate_protocol_step',
          description: 'Generate a new protocol chain step class',
          inputSchema: {
            type: 'object',
            properties: {
              name: {
                type: 'string',
                description: 'Name of the protocol step'
              },
              stepType: {
                type: 'string',
                description: 'Type of protocol step',
                enum: ['KeyExchange', 'Authentication', 'Encryption', 'Verification']
              }
            },
            required: ['name', 'stepType']
          }
        },
        {
          name: 'create_encryption_service',
          description: 'Create a new encryption service implementation',
          inputSchema: {
            type: 'object',
            properties: {
              algorithm: {
                type: 'string',
                description: 'Encryption algorithm',
                enum: ['AES-GCM', 'ChaCha20-Poly1305', 'XSalsa20-Poly1305']
              },
              keySize: {
                type: 'number',
                description: 'Key size in bits',
                enum: [128, 256]
              }
            },
            required: ['algorithm']
          }
        },
        {
          name: 'generate_grpc_interceptor',
          description: 'Generate a gRPC interceptor for security operations',
          inputSchema: {
            type: 'object',
            properties: {
              name: {
                type: 'string',
                description: 'Interceptor name'
              },
              purpose: {
                type: 'string',
                description: 'Purpose of the interceptor',
                enum: ['Authentication', 'Encryption', 'Metadata', 'Logging']
              }
            },
            required: ['name', 'purpose']
          }
        },
        {
          name: 'create_failure_type',
          description: 'Create a new failure type for error handling',
          inputSchema: {
            type: 'object',
            properties: {
              domain: {
                type: 'string',
                description: 'Failure domain (e.g., Network, Crypto, Auth)'
              },
              failures: {
                type: 'array',
                items: { type: 'string' },
                description: 'List of failure types'
              }
            },
            required: ['domain', 'failures']
          }
        },
        {
          name: 'validate_security_pattern',
          description: 'Validate security implementation against best practices',
          inputSchema: {
            type: 'object',
            properties: {
              filePath: {
                type: 'string',
                description: 'Path to file to validate'
              }
            },
            required: ['filePath']
          }
        }
      ]
    }));

    this.server.setRequestHandler(CallToolRequestSchema, async (request) => {
      switch (request.params.name) {
        case 'generate_protocol_step':
          return this.generateProtocolStep(request.params.arguments);
        case 'create_encryption_service':
          return this.createEncryptionService(request.params.arguments);
        case 'generate_grpc_interceptor':
          return this.generateGrpcInterceptor(request.params.arguments);
        case 'create_failure_type':
          return this.createFailureType(request.params.arguments);
        case 'validate_security_pattern':
          return this.validateSecurityPattern(request.params.arguments);
        default:
          throw new McpError(ErrorCode.MethodNotFound, `Unknown tool: ${request.params.name}`);
      }
    });
  }

  setupResourceHandlers() {
    this.server.setRequestHandler(ListResourcesRequestSchema, async () => ({
      resources: [
        {
          uri: 'security://protocols/opaque',
          name: 'OPAQUE Protocol',
          description: 'OPAQUE password-authenticated key exchange implementation'
        },
        {
          uri: 'security://encryption/aes-gcm',
          name: 'AES-GCM Encryption',
          description: 'AES-GCM encryption service patterns'
        },
        {
          uri: 'security://sodium/interop',
          name: 'Sodium Interop',
          description: 'Sodium cryptographic library integration'
        },
        {
          uri: 'security://patterns/secure-memory',
          name: 'Secure Memory',
          description: 'Secure memory management patterns'
        }
      ]
    }));

    this.server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
      const uri = request.params.uri;
      
      switch (uri) {
        case 'security://protocols/opaque':
          return {
            contents: [{
              uri,
              mimeType: 'text/markdown',
              text: await this.getOpaqueProtocolInfo()
            }]
          };
        case 'security://encryption/aes-gcm':
          return {
            contents: [{
              uri,
              mimeType: 'text/markdown',
              text: await this.getAesGcmInfo()
            }]
          };
        case 'security://sodium/interop':
          return {
            contents: [{
              uri,
              mimeType: 'text/markdown',
              text: await this.getSodiumInteropInfo()
            }]
          };
        case 'security://patterns/secure-memory':
          return {
            contents: [{
              uri,
              mimeType: 'text/markdown',
              text: await this.getSecureMemoryInfo()
            }]
          };
        default:
          throw new McpError(ErrorCode.InvalidRequest, `Unknown resource: ${uri}`);
      }
    });
  }

  async generateProtocolStep(args) {
    const { name, stepType } = args;
    const className = name.endsWith('Step') ? name : `${name}Step`;
    
    const code = `using Ecliptix.Protocol.System.Core;
using Ecliptix.Utilities;

namespace Ecliptix.Protocol.System.Core;

public class ${className} : EcliptixProtocolChainStep
{
    public override ChainStepType StepType => ChainStepType.${stepType};

    public ${className}()
    {
    }

    public override Result<Unit> Execute()
    {
        try
        {
            // Implement ${stepType.toLowerCase()} logic here
            return Result<Unit>.Success(new Unit());
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(new EcliptixProtocolFailure(
                EcliptixProtocolFailureType.${stepType}Failed,
                $"${stepType} step failed: {ex.Message}"
            ));
        }
    }

    protected override void DisposeInternal()
    {
        // Clean up resources
    }
}`;

    return {
      content: [{
        type: 'text',
        text: code
      }]
    };
  }

  async createEncryptionService(args) {
    const { algorithm, keySize = 256 } = args;
    const serviceName = `${algorithm.replace(/-/g, '')}EncryptionService`;
    
    const code = `using System.Security.Cryptography;
using Ecliptix.Utilities;

namespace Ecliptix.Protocol.System.Core;

public interface I${serviceName}
{
    Result<byte[]> Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce = default);
    Result<byte[]> Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce = default);
}

public class ${serviceName} : I${serviceName}
{
    private const int KeySize = ${keySize} / 8; // ${keySize} bits
    private const int NonceSize = 12; // 96 bits for GCM
    private const int TagSize = 16; // 128 bits

    public Result<byte[]> Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce = default)
    {
        try
        {
            if (key.Length != KeySize)
                return Result<byte[]>.Failure(new CryptoFailure("Invalid key size"));

            // Implementation for ${algorithm} encryption
            // This is a template - implement actual encryption logic
            
            return Result<byte[]>.Success(Array.Empty<byte>());
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(new CryptoFailure($"Encryption failed: {ex.Message}"));
        }
    }

    public Result<byte[]> Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce = default)
    {
        try
        {
            if (key.Length != KeySize)
                return Result<byte[]>.Failure(new CryptoFailure("Invalid key size"));

            // Implementation for ${algorithm} decryption
            // This is a template - implement actual decryption logic
            
            return Result<byte[]>.Success(Array.Empty<byte>());
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(new CryptoFailure($"Decryption failed: {ex.Message}"));
        }
    }
}`;

    return {
      content: [{
        type: 'text',
        text: code
      }]
    };
  }

  async generateGrpcInterceptor(args) {
    const { name, purpose } = args;
    const interceptorName = name.endsWith('Interceptor') ? name : `${name}Interceptor`;
    
    const code = `using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace Ecliptix.Core.Network.Transport.Grpc.Interceptors;

public class ${interceptorName} : Interceptor
{
    private readonly ILogger<${interceptorName}> _logger;

    public ${interceptorName}(ILogger<${interceptorName}> logger)
    {
        _logger = logger;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        // Implement ${purpose.toLowerCase()} logic here
        _logger.LogDebug("Processing {Purpose} for {Method}", "${purpose}", context.Method.Name);
        
        // Modify headers, add metadata, etc.
        Metadata headers = context.Options.Headers ?? new Metadata();
        
        ClientInterceptorContext<TRequest, TResponse> newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(headers)
        );

        return continuation(request, newContext);
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        // Handle client streaming calls
        return base.AsyncClientStreamingCall(context, continuation);
    }
}`;

    return {
      content: [{
        type: 'text',
        text: code
      }]
    };
  }

  async createFailureType(args) {
    const { domain, failures } = args;
    const failureTypeName = `${domain}FailureType`;
    const failureClassName = `${domain}Failure`;
    
    const enumValues = failures.map(f => `    ${f},`).join('\n');
    
    const code = `using Ecliptix.Utilities.Failures;

namespace Ecliptix.Utilities.Failures.${domain};

public enum ${failureTypeName}
{
${enumValues}
}

public class ${failureClassName} : FailureBase
{
    public ${failureTypeName} FailureType { get; }

    public ${failureClassName}(${failureTypeName} failureType, string message) 
        : base(message)
    {
        FailureType = failureType;
    }

    public ${failureClassName}(${failureTypeName} failureType, string message, Exception innerException) 
        : base(message, innerException)
    {
        FailureType = failureType;
    }

    public override string ToString() => $"${domain}Failure: {FailureType} - {Message}";
}`;

    return {
      content: [{
        type: 'text',
        text: code
      }]
    };
  }

  async validateSecurityPattern(args) {
    const { filePath } = args;
    
    try {
      const fullPath = path.join(WORKSPACE_ROOT, filePath);
      const content = await fs.readFile(fullPath, 'utf-8');
      
      const issues = [];
      
      // Check for common security anti-patterns
      if (content.includes('Console.WriteLine') || content.includes('Debug.WriteLine')) {
        issues.push('Potential information disclosure through logging');
      }
      
      if (content.includes('Exception') && !content.includes('catch')) {
        issues.push('Unhandled exceptions may leak sensitive information');
      }
      
      if (content.includes('SecureString') && content.includes('.ToString()')) {
        issues.push('SecureString converted to string defeats its purpose');
      }
      
      if (content.includes('Random') && !content.includes('RNGCryptoServiceProvider')) {
        issues.push('Use cryptographically secure random number generation');
      }
      
      const result = issues.length === 0 
        ? 'No security issues detected'
        : `Security issues found:\n${issues.map(i => `- ${i}`).join('\n')}`;
      
      return {
        content: [{
          type: 'text',
          text: result
        }]
      };
    } catch (error) {
      return {
        content: [{
          type: 'text',
          text: `Validation failed: ${error.message}`
        }]
      };
    }
  }

  async getOpaqueProtocolInfo() {
    return `# OPAQUE Protocol Implementation

## Overview
OPAQUE is a password-authenticated key exchange (PAKE) protocol that provides strong authentication without exposing passwords to the server.

## Key Components
- **OpaqueProtocolService**: Main service for OPAQUE operations
- **OpaqueCryptoUtilities**: Cryptographic utilities
- **OpaqueConstants**: Protocol constants and parameters

## Usage Pattern
\`\`\`csharp
Result<OpaqueResult> result = OpaqueProtocolService.RegisterUser(username, password);
if (result.IsSuccess)
{
    // Handle successful registration
}
\`\`\`

## Security Features
- No password transmission over network
- Server compromise doesn't reveal passwords
- Forward secrecy for session keys`;
  }

  async getAesGcmInfo() {
    return `# AES-GCM Encryption Service

## Overview
AES-GCM provides authenticated encryption with associated data (AEAD).

## Key Features
- 256-bit key size
- 96-bit nonce (IV)
- 128-bit authentication tag
- Additional authenticated data (AAD) support

## Usage Pattern
\`\`\`csharp
Result<byte[]> encrypted = aesGcmService.Encrypt(plaintext, key, nonce, aad);
Result<byte[]> decrypted = aesGcmService.Decrypt(ciphertext, key, nonce, aad);
\`\`\`

## Security Considerations
- Never reuse nonce with same key
- Use cryptographically secure random nonces
- Verify authentication tag before processing plaintext`;
  }

  async getSodiumInteropInfo() {
    return `# Sodium Library Integration

## Overview
Sodium provides high-level cryptographic operations with secure defaults.

## Key Components
- **SodiumInterop**: P/Invoke wrapper for libsodium
- **SodiumSecureMemoryHandle**: Secure memory management
- **ScopedSecureMemory**: RAII pattern for memory cleanup

## Security Features
- Secure memory allocation and deallocation
- Protection against memory dumps
- Automatic zeroing of sensitive data

## Best Practices
- Always use scoped memory for sensitive data
- Verify operations success before proceeding
- Handle disposal properly to prevent memory leaks`;
  }

  async getSecureMemoryInfo() {
    return `# Secure Memory Management

## Patterns
- **ScopedSecureMemory**: RAII pattern for automatic cleanup
- **SecureMemoryPool**: Memory pool for efficient allocation
- **SecureStringHandler**: Safe string operations

## Usage
\`\`\`csharp
using ScopedSecureMemory memory = SecureMemoryPool.Rent(size);
// Use memory.Span for operations
// Automatic cleanup on disposal
\`\`\`

## Security Benefits
- Memory protection from dumps
- Automatic zeroing on disposal
- Prevention of swap file exposure`;
  }

  async run() {
    const transport = new StdioServerTransport();
    await this.server.connect(transport);
    console.error('Security Protocol MCP server running on stdio');
  }
}

const server = new SecurityProtocolServer();
server.run().catch(console.error);