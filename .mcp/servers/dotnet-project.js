#!/usr/bin/env node

/**
 * .NET Project MCP Server
 * Provides tools for .NET project management and code generation
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
import { exec } from 'child_process';
import { promisify } from 'util';
import fs from 'fs/promises';
import path from 'path';

const execAsync = promisify(exec);
const WORKSPACE_ROOT = process.env.WORKSPACE_ROOT || process.cwd();

class DotNetProjectServer {
  constructor() {
    this.server = new Server(
      {
        name: 'dotnet-project',
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
          name: 'build_solution',
          description: 'Build the entire solution with optional configuration',
          inputSchema: {
            type: 'object',
            properties: {
              configuration: {
                type: 'string',
                description: 'Build configuration (Debug/Release)',
                enum: ['Debug', 'Release'],
                default: 'Debug'
              }
            }
          }
        },
        {
          name: 'run_tests',
          description: 'Run tests with optional filters',
          inputSchema: {
            type: 'object',
            properties: {
              project: {
                type: 'string',
                description: 'Specific test project to run'
              },
              filter: {
                type: 'string',
                description: 'Test filter expression'
              }
            }
          }
        },
        {
          name: 'add_package',
          description: 'Add NuGet package to project',
          inputSchema: {
            type: 'object',
            properties: {
              project: {
                type: 'string',
                description: 'Project file path'
              },
              package: {
                type: 'string',
                description: 'Package name'
              },
              version: {
                type: 'string',
                description: 'Package version (optional)'
              }
            },
            required: ['project', 'package']
          }
        },
        {
          name: 'create_service',
          description: 'Create a new service class following Ecliptix patterns',
          inputSchema: {
            type: 'object',
            properties: {
              name: {
                type: 'string',
                description: 'Service name'
              },
              interface: {
                type: 'boolean',
                description: 'Create interface for service',
                default: true
              },
              project: {
                type: 'string',
                description: 'Target project',
                default: 'Ecliptix.Core'
              }
            },
            required: ['name']
          }
        },
        {
          name: 'analyze_dependencies',
          description: 'Analyze project dependencies and suggest optimizations',
          inputSchema: {
            type: 'object',
            properties: {
              project: {
                type: 'string',
                description: 'Specific project to analyze (optional)'
              }
            }
          }
        }
      ]
    }));

    this.server.setRequestHandler(CallToolRequestSchema, async (request) => {
      switch (request.params.name) {
        case 'build_solution':
          return this.buildSolution(request.params.arguments);
        case 'run_tests':
          return this.runTests(request.params.arguments);
        case 'add_package':
          return this.addPackage(request.params.arguments);
        case 'create_service':
          return this.createService(request.params.arguments);
        case 'analyze_dependencies':
          return this.analyzeDependencies(request.params.arguments);
        default:
          throw new McpError(ErrorCode.MethodNotFound, `Unknown tool: ${request.params.name}`);
      }
    });
  }

  setupResourceHandlers() {
    this.server.setRequestHandler(ListResourcesRequestSchema, async () => ({
      resources: [
        {
          uri: 'dotnet://solution/structure',
          name: 'Solution Structure',
          description: 'Current solution structure and projects'
        },
        {
          uri: 'dotnet://packages/overview',
          name: 'Package Overview',
          description: 'NuGet packages used across projects'
        },
        {
          uri: 'dotnet://build/configuration',
          name: 'Build Configuration',
          description: 'Build settings and configurations'
        }
      ]
    }));

    this.server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
      const uri = request.params.uri;
      
      switch (uri) {
        case 'dotnet://solution/structure':
          return {
            contents: [{
              uri,
              mimeType: 'application/json',
              text: JSON.stringify(await this.getSolutionStructure(), null, 2)
            }]
          };
        case 'dotnet://packages/overview':
          return {
            contents: [{
              uri,
              mimeType: 'application/json',
              text: JSON.stringify(await this.getPackageOverview(), null, 2)
            }]
          };
        case 'dotnet://build/configuration':
          return {
            contents: [{
              uri,
              mimeType: 'text/markdown',
              text: await this.getBuildConfiguration()
            }]
          };
        default:
          throw new McpError(ErrorCode.InvalidRequest, `Unknown resource: ${uri}`);
      }
    });
  }

  async buildSolution(args) {
    const { configuration = 'Debug' } = args || {};
    
    try {
      const { stdout, stderr } = await execAsync(
        `dotnet build -c ${configuration}`,
        { cwd: WORKSPACE_ROOT }
      );
      
      return {
        content: [{
          type: 'text',
          text: `Build successful (${configuration}):\n${stdout}\n${stderr}`
        }]
      };
    } catch (error) {
      return {
        content: [{
          type: 'text',
          text: `Build failed:\n${error.message}`
        }]
      };
    }
  }

  async runTests(args) {
    const { project, filter } = args || {};
    
    let command = 'dotnet test';
    if (project) command += ` ${project}`;
    if (filter) command += ` --filter "${filter}"`;
    
    try {
      const { stdout, stderr } = await execAsync(command, { cwd: WORKSPACE_ROOT });
      
      return {
        content: [{
          type: 'text',
          text: `Tests completed:\n${stdout}\n${stderr}`
        }]
      };
    } catch (error) {
      return {
        content: [{
          type: 'text',
          text: `Tests failed:\n${error.message}`
        }]
      };
    }
  }

  async addPackage(args) {
    const { project, package: packageName, version } = args;
    
    let command = `dotnet add ${project} package ${packageName}`;
    if (version) command += ` --version ${version}`;
    
    try {
      const { stdout, stderr } = await execAsync(command, { cwd: WORKSPACE_ROOT });
      
      return {
        content: [{
          type: 'text',
          text: `Package added successfully:\n${stdout}\n${stderr}`
        }]
      };
    } catch (error) {
      return {
        content: [{
          type: 'text',
          text: `Failed to add package:\n${error.message}`
        }]
      };
    }
  }

  async createService(args) {
    const { name, interface: createInterface = true, project = 'Ecliptix.Core' } = args;
    
    const serviceName = name.endsWith('Service') ? name : `${name}Service`;
    const interfaceName = `I${serviceName}`;
    
    let serviceCode = `using Ecliptix.Utilities;

namespace ${project}.Services;

`;

    if (createInterface) {
      serviceCode += `public interface ${interfaceName}
{
    // Define service contract here
}

`;
    }

    serviceCode += `public class ${serviceName}${createInterface ? ` : ${interfaceName}` : ''}
{
    public ${serviceName}()
    {
    }
}`;

    const filePath = path.join(WORKSPACE_ROOT, project, 'Services', `${serviceName}.cs`);
    
    return {
      content: [{
        type: 'text',
        text: `Service code generated for ${filePath}:\n\n${serviceCode}`
      }]
    };
  }

  async analyzeDependencies(args) {
    const { project } = args || {};
    
    try {
      let command = 'dotnet list package --outdated';
      if (project) command += ` ${project}`;
      
      const { stdout } = await execAsync(command, { cwd: WORKSPACE_ROOT });
      
      return {
        content: [{
          type: 'text',
          text: `Dependency analysis:\n${stdout}`
        }]
      };
    } catch (error) {
      return {
        content: [{
          type: 'text',
          text: `Analysis failed:\n${error.message}`
        }]
      };
    }
  }

  async getSolutionStructure() {
    try {
      const solutionFile = path.join(WORKSPACE_ROOT, 'Ecliptix-Desktop.sln');
      const content = await fs.readFile(solutionFile, 'utf-8');
      
      const projects = [];
      const projectRegex = /Project\(".*?"\) = "(.*?)", "(.*?)", ".*?"/g;
      let match;
      
      while ((match = projectRegex.exec(content)) !== null) {
        projects.push({
          name: match[1],
          path: match[2]
        });
      }
      
      return { projects };
    } catch (error) {
      return { error: error.message };
    }
  }

  async getPackageOverview() {
    try {
      const { stdout } = await execAsync('dotnet list package', { cwd: WORKSPACE_ROOT });
      return { packageList: stdout };
    } catch (error) {
      return { error: error.message };
    }
  }

  async getBuildConfiguration() {
    return `# Build Configuration

## Target Framework
- .NET 9.0

## Key Build Properties
- PublishAot: true
- TrimMode: link
- Nullable: enable
- LangVersion: latest

## Development Commands
\`\`\`bash
# Build
dotnet build

# Run
dotnet run --project Ecliptix.Core/Ecliptix.Core.Desktop/Ecliptix.Core.Desktop.csproj

# Test
dotnet test
\`\`\``;
  }

  async run() {
    const transport = new StdioServerTransport();
    await this.server.connect(transport);
    console.error('.NET Project MCP server running on stdio');
  }
}

const server = new DotNetProjectServer();
server.run().catch(console.error);