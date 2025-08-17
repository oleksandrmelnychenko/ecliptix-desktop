#!/usr/bin/env node

/**
 * Ecliptix Development MCP Server
 * Provides context about the Ecliptix Desktop application architecture
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

class EcliptixDevServer {
  constructor() {
    this.server = new Server(
      {
        name: 'ecliptix-dev',
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
          name: 'get_architecture_info',
          description: 'Get information about Ecliptix application architecture',
          inputSchema: {
            type: 'object',
            properties: {
              component: {
                type: 'string',
                description: 'Specific component to get info about (optional)',
                enum: ['auth', 'crypto', 'network', 'ui', 'storage']
              }
            }
          }
        },
        {
          name: 'generate_view_model',
          description: 'Generate a new ViewModel following Ecliptix patterns',
          inputSchema: {
            type: 'object',
            properties: {
              name: {
                type: 'string',
                description: 'Name of the ViewModel'
              },
              features: {
                type: 'array',
                items: { type: 'string' },
                description: 'Features to include (reactive, validation, navigation)'
              }
            },
            required: ['name']
          }
        },
        {
          name: 'create_avalonia_view',
          description: 'Create an Avalonia view with proper bindings',
          inputSchema: {
            type: 'object',
            properties: {
              name: {
                type: 'string',
                description: 'Name of the view'
              },
              viewModel: {
                type: 'string',
                description: 'Associated ViewModel name'
              }
            },
            required: ['name', 'viewModel']
          }
        }
      ]
    }));

    this.server.setRequestHandler(CallToolRequestSchema, async (request) => {
      switch (request.params.name) {
        case 'get_architecture_info':
          return this.getArchitectureInfo(request.params.arguments?.component);
        case 'generate_view_model':
          return this.generateViewModel(request.params.arguments);
        case 'create_avalonia_view':
          return this.createAvaloniaView(request.params.arguments);
        default:
          throw new McpError(ErrorCode.MethodNotFound, `Unknown tool: ${request.params.name}`);
      }
    });
  }

  setupResourceHandlers() {
    this.server.setRequestHandler(ListResourcesRequestSchema, async () => ({
      resources: [
        {
          uri: 'ecliptix://architecture/overview',
          name: 'Architecture Overview',
          description: 'Complete overview of Ecliptix architecture'
        },
        {
          uri: 'ecliptix://patterns/mvvm',
          name: 'MVVM Patterns',
          description: 'MVVM patterns used in Ecliptix'
        },
        {
          uri: 'ecliptix://security/protocols',
          name: 'Security Protocols',
          description: 'Cryptographic protocols and security patterns'
        }
      ]
    }));

    this.server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
      const uri = request.params.uri;
      
      switch (uri) {
        case 'ecliptix://architecture/overview':
          return {
            contents: [{
              uri,
              mimeType: 'text/markdown',
              text: await this.getArchitectureOverview()
            }]
          };
        case 'ecliptix://patterns/mvvm':
          return {
            contents: [{
              uri,
              mimeType: 'text/markdown',
              text: await this.getMvvmPatterns()
            }]
          };
        case 'ecliptix://security/protocols':
          return {
            contents: [{
              uri,
              mimeType: 'text/markdown',
              text: await this.getSecurityProtocols()
            }]
          };
        default:
          throw new McpError(ErrorCode.InvalidRequest, `Unknown resource: ${uri}`);
      }
    });
  }

  async getArchitectureInfo(component) {
    const architectureMap = {
      auth: {
        description: 'OPAQUE protocol-based authentication with mobile verification',
        key_classes: ['OpaqueAuthenticationService', 'SignInViewModel', 'MobileVerificationViewModel'],
        patterns: ['Result pattern for error handling', 'Event aggregator for state changes']
      },
      crypto: {
        description: 'AES-GCM encryption with Sodium library for secure operations',
        key_classes: ['AesGCMService', 'EcliptixProtocolSystem', 'SodiumInterop'],
        patterns: ['Secure memory management', 'Protocol chain steps']
      },
      network: {
        description: 'gRPC with Polly resilience patterns and connection management',
        key_classes: ['RpcServiceManager', 'SecrecyChannelRpcServices', 'ConnectionStateManager'],
        patterns: ['Retry policies', 'Circuit breaker', 'Metadata interceptors']
      },
      ui: {
        description: 'Avalonia MVVM with ReactiveUI and event aggregation',
        key_classes: ['ViewModelBase', 'MembershipHostWindow', 'BottomSheetControl'],
        patterns: ['Data binding', 'Reactive commands', 'Bottom sheet modals']
      },
      storage: {
        description: 'Secure storage with Microsoft.AspNetCore.DataProtection',
        key_classes: ['ApplicationSecureStorageProvider', 'SecureProtocolStateStorage'],
        patterns: ['Cross-platform security', 'Encrypted persistence']
      }
    };

    if (component && architectureMap[component]) {
      return {
        content: [{
          type: 'text',
          text: JSON.stringify(architectureMap[component], null, 2)
        }]
      };
    }

    return {
      content: [{
        type: 'text',
        text: JSON.stringify(architectureMap, null, 2)
      }]
    };
  }

  async generateViewModel(args) {
    const { name, features = [] } = args;
    const className = name.endsWith('ViewModel') ? name : `${name}ViewModel`;
    
    let code = `using ReactiveUI;
using System.Reactive;
using Ecliptix.Core.ViewModels;

namespace Ecliptix.Core.ViewModels;

public class ${className} : ViewModelBase
{
    public ${className}()
    {
        InitializeCommands();
    }

    private void InitializeCommands()
    {
        // Initialize reactive commands here
    }
`;

    if (features.includes('reactive')) {
      code += `
    // Reactive properties
    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }
`;
    }

    if (features.includes('validation')) {
      code += `
    // Validation logic
    public IObservable<bool> IsValid => this.WhenAnyValue(
        x => x.Title,
        title => !string.IsNullOrWhiteSpace(title)
    );
`;
    }

    if (features.includes('navigation')) {
      code += `
    // Navigation commands
    public ReactiveCommand<Unit, Unit> NavigateBackCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> NavigateNextCommand { get; private set; } = null!;
`;
    }

    code += `}`;

    return {
      content: [{
        type: 'text',
        text: code
      }]
    };
  }

  async createAvaloniaView(args) {
    const { name, viewModel } = args;
    const viewName = name.endsWith('View') ? name : `${name}View`;
    
    const axamlContent = `<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Ecliptix.Core.ViewModels"
             x:Class="Ecliptix.Core.Views.${viewName}"
             x:DataType="vm:${viewModel}">
  
  <Design.DataContext>
    <vm:${viewModel} />
  </Design.DataContext>

  <Grid>
    <!-- Add your UI elements here -->
    <TextBlock Text="{Binding Title}" 
               HorizontalAlignment="Center"
               VerticalAlignment="Center" />
  </Grid>
</UserControl>`;

    const codeBehinds = `using Avalonia.Controls;

namespace Ecliptix.Core.Views;

public partial class ${viewName} : UserControl
{
    public ${viewName}()
    {
        InitializeComponent();
    }
}`;

    return {
      content: [{
        type: 'text',
        text: `${viewName}.axaml:\n${axamlContent}\n\n${viewName}.axaml.cs:\n${codeBehinds}`
      }]
    };
  }

  async getArchitectureOverview() {
    return `# Ecliptix Desktop Architecture

## Project Structure
- **Ecliptix.Core**: Main UI application (Avalonia + MVVM)
- **Ecliptix.Core.Desktop**: Entry point and DI configuration
- **Ecliptix.Protocol.System**: Cryptographic protocols
- **Ecliptix.Protobufs**: gRPC service definitions
- **Ecliptix.Opaque.Protocol**: OPAQUE authentication
- **Ecliptix.Utilities**: Shared utilities and Result types

## Key Patterns
- MVVM with ReactiveUI
- Result<T> for error handling
- Event aggregator for decoupled communication
- Dependency injection with Microsoft.Extensions.DI
- gRPC with Polly resilience patterns

## Coding Style Rules
- Always use explicit types (no var)
- No code comments in methods/properties
- Use expression-bodied members for single-line methods`;
  }

  async getMvvmPatterns() {
    return `# MVVM Patterns in Ecliptix

## Base Classes
- ViewModelBase: Base class for all ViewModels with ReactiveUI
- All ViewModels inherit from ViewModelBase

## Reactive Properties
\`\`\`csharp
private string _title = string.Empty;
public string Title
{
    get => _title;
    set => this.RaiseAndSetIfChanged(ref _title, value);
}
\`\`\`

## Commands
\`\`\`csharp
public ReactiveCommand<Unit, Unit> SaveCommand { get; private set; } = null!;
\`\`\`

## View Binding
- Use x:DataType for compile-time binding
- Prefer compiled bindings for performance`;
  }

  async getSecurityProtocols() {
    return `# Security Protocols

## OPAQUE Authentication
- Password-authenticated key exchange
- No password transmission over network
- Implemented in Ecliptix.Opaque.Protocol

## AES-GCM Encryption
- Authenticated encryption for data protection
- Implemented in AesGCMService

## Sodium Library
- Low-level cryptographic operations
- Secure memory management
- Protocol chain implementation`;
  }

  async run() {
    const transport = new StdioServerTransport();
    await this.server.connect(transport);
    console.error('Ecliptix Development MCP server running on stdio');
  }
}

const server = new EcliptixDevServer();
server.run().catch(console.error);