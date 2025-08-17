# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

### Build Commands
```bash
# Build the entire solution
dotnet build

# Build in Release mode
dotnet build -c Release

# Build specific project
dotnet build Ecliptix.Core/Ecliptix.Core/Ecliptix.Core.csproj

# Clean build artifacts
dotnet clean
```

### Run Commands
```bash
# Run the desktop application
dotnet run --project Ecliptix.Core/Ecliptix.Core.Desktop/Ecliptix.Core.Desktop.csproj

# Run in Development environment
DOTNET_ENVIRONMENT=Development dotnet run --project Ecliptix.Core/Ecliptix.Core.Desktop/Ecliptix.Core.Desktop.csproj
```

### Test Commands
```bash
# Run all tests
dotnet test

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"
```

### Package Management
```bash
# Restore NuGet packages
dotnet restore

# Add a new package to a project
dotnet add [project] package [package-name]

# Update packages
dotnet list package --outdated
```

## Architecture Overview

### Solution Structure
The Ecliptix Desktop application is a cross-platform Avalonia-based application with a modular architecture:

- **Ecliptix.Core**: Main UI application using Avalonia framework with MVVM pattern
  - Contains views, viewmodels, controls, and localization
  - Uses ReactiveUI for reactive programming
  - Implements membership/authentication flows

- **Ecliptix.Core.Desktop**: Entry point and platform-specific initialization
  - Configures dependency injection with Microsoft.Extensions.DependencyInjection
  - Sets up Serilog logging and gRPC clients
  - Manages application lifecycle and startup

- **Ecliptix.Protocol.System**: Core cryptographic protocol implementation
  - AES-GCM encryption service
  - Protocol chain steps and connection management
  - Sodium library interop for cryptographic operations

- **Ecliptix.Protobufs**: Protocol buffer definitions
  - gRPC service definitions for authentication and membership
  - Message types for client-server communication

- **Ecliptix.Opaque.Protocol**: OPAQUE password-authenticated key exchange implementation
  - Secure authentication without transmitting passwords

- **Ecliptix.Utilities**: Shared utilities and result types
  - Result/Option monads for error handling
  - Failure types for different system components
  - Common constants and helpers

### Key Architectural Patterns

1. **MVVM Pattern**: Views bind to ViewModels using Avalonia's data binding and ReactiveUI
2. **Dependency Injection**: Services registered in Program.cs and resolved via Splat/Microsoft.Extensions.DI
3. **Event Aggregator**: Pub/sub pattern for decoupled component communication (IEventAggregator)
4. **Result Pattern**: Functional error handling using Result<T> and Option<T> types
5. **Interceptor Pattern**: gRPC interceptors for resilience, metadata, and deadlines

### Authentication Flow
The application implements a multi-step authentication process:
1. Splash screen initialization (SplashWindow)
2. Membership verification (MembershipHostWindow)
3. Sign-in/Sign-up flows with mobile verification and OTP
4. Secure key management with OPAQUE protocol
5. Transition to main application (MainHostWindow)

### Network Layer
- gRPC for all client-server communication
- Polly for resilience (retry, circuit breaker)
- Network connectivity monitoring (InternetConnectivityObserver)
- Metadata interceptors for request enrichment

### Security Features
- AES-GCM encryption for data protection
- Secure storage with Microsoft.AspNetCore.DataProtection
- OPAQUE protocol for password-less authentication
- Sodium cryptographic library for key operations

## Technology Stack
- **.NET 9.0** with C# latest
- **Avalonia 11.3.2**: Cross-platform UI framework
- **ReactiveUI**: MVVM and reactive extensions
- **gRPC/Protobuf**: Service communication
- **Serilog**: Structured logging
- **Polly**: Resilience and transient fault handling
- **Sodium.Core**: Cryptographic operations
- **CommunityToolkit.MVVM**: MVVM helpers

## Environment Configuration
The application uses appsettings.json files with environment-specific overrides:
- appsettings.json (base configuration)
- appsettings.Development.json (development overrides)
- Environment variables via DotNetEnv

Key environment variables:
- `DOTNET_ENVIRONMENT`: Sets the runtime environment (Development/Production)

## Platform Support
The application supports Windows, macOS, and Linux with platform-specific handling for:
- File paths and storage locations
- Icon formats (.ico for Windows, .icns for macOS, .png for Linux)
- Unix file permissions on Linux/macOS

## Coding Style Rules

### Type Declarations
- Always use explicit types instead of `var`
- Example: `string text = "Hello"` instead of `var text = "Hello"`
- Example: `List<string> items = new List<string>()` instead of `var items = new List<string>()`

### Code Comments
- Do not add comments within methods or properties
- Keep code self-documenting through clear naming and simple logic

### Single-Line Methods
- Use expression-bodied members for simple one-line methods and properties
- Example: `public string GetName() => _name;`
- Example: `public bool IsValid => _isValid && _isReady;`
- Example: `public void SetValue(string value) => _value = value;`

## MCP (Model Context Protocol) Integration

### Overview
The project includes MCP servers that provide AI-assisted development capabilities specifically tailored for the Ecliptix architecture.

### MCP Configuration
Location: `.mcp/config.json`

Three specialized MCP servers are configured:
1. **ecliptix-dev**: Provides context about Ecliptix application architecture, MVVM patterns, and code generation
2. **dotnet-project**: Handles .NET project management, build operations, and NuGet packages
3. **security-protocol**: Assists with cryptographic operations, security protocols, and secure coding patterns

### Available MCP Tools

#### Ecliptix Development Server
- `get_architecture_info`: Get information about specific components (auth, crypto, network, ui, storage)
- `generate_view_model`: Generate ViewModels following Ecliptix patterns with reactive features
- `create_avalonia_view`: Create Avalonia views with proper MVVM bindings

#### .NET Project Server
- `build_solution`: Build the solution with Debug/Release configurations
- `run_tests`: Execute tests with optional filtering
- `add_package`: Add NuGet packages to projects
- `create_service`: Generate service classes following Ecliptix patterns
- `analyze_dependencies`: Analyze and suggest dependency optimizations

#### Security Protocol Server
- `generate_protocol_step`: Create protocol chain step implementations
- `create_encryption_service`: Generate encryption service classes
- `generate_grpc_interceptor`: Create gRPC interceptors for security operations
- `create_failure_type`: Generate failure types for error handling
- `validate_security_pattern`: Validate code against security best practices

### Usage in Development
When working with Claude Code or other AI tools that support MCP, these servers provide:
- Context-aware code generation following your coding style rules
- Architecture-specific suggestions and patterns
- Automated adherence to security best practices
- Project-specific build and test operations

### Centralized Build Configuration

#### Directory.Build.props
Centralizes common MSBuild properties across all projects:
- .NET 9.0 target framework
- Code analysis and style enforcement
- Security-focused analyzer rules
- Conditional package references based on project type

#### global.json
Locks the .NET SDK version to 9.0.0 for consistent builds across development environments.

#### CodeAnalysis.ruleset
Enforces coding style rules including:
- Explicit type declarations (no var)
- Expression-bodied member suggestions
- Security and cryptography rules
- Performance optimizations

## Git Workflow Rules

### Adding Files to Git
- **ALWAYS** use `git add .` or `git add -A` to add all files to git
- **NEVER** add files selectively unless explicitly requested
- This ensures all related changes (code, config, documentation) are committed together
- Helps maintain project consistency and prevents missing dependencies

### Branching Strategy
The project follows a structured Git flow:

**Main Branches:**
- `main` - Production-ready, stable code
- `develop` - Integration branch for ongoing development

**Feature Branches:**
- `feature/<description>` - New features (e.g., `feature/mobile-auth`)
- `fix/<description>` - Bug fixes (e.g., `fix/memory-leak`)
- `hotfix/<description>` - Emergency production fixes
- `docs/<description>` - Documentation updates

### Commit Message Convention
Use conventional commit format:
```
type: short description

Optional longer explanation

Co-authored-by: Name <email>
```

**Types:**
- `feat` - New features
- `fix` - Bug fixes
- `docs` - Documentation changes
- `refactor` - Code refactoring without functional changes
- `test` - Test additions or modifications
- `chore` - Maintenance tasks, build updates

### Development Scripts
Use the provided scripts for consistent workflow:

**`./scripts/new-branch.sh <type> <description>`**
- Creates properly named feature branches
- Switches from develop branch
- Updates local develop first
- Example: `./scripts/new-branch.sh feature mobile-verification`

**`./scripts/sync-develop.sh`**
- Syncs local develop with origin/develop
- Safely switches branches and updates
- Offers to rebase current branch on updated develop

**`./scripts/pr-checks.sh`**
- Runs comprehensive pre-PR validation
- Checks build, tests, formatting, security
- Validates coding style rules
- Must pass before creating pull requests

### GitHub Actions
Automated workflows handle:
- **CI Pipeline**: Build, test, security scanning on all PRs
- **Release Pipeline**: Automated releases with cross-platform builds
- **Code Quality**: Formatting checks and security validation

### Pull Request Process
1. Create feature branch: `./scripts/new-branch.sh feature your-feature`
2. Make changes following coding style rules
3. Run pre-checks: `./scripts/pr-checks.sh`
4. Add all files: `git add .`
5. Commit: `git commit -m "feat: your change description"`
6. Push: `git push -u origin feature/your-feature`
7. Create PR on GitHub using the provided template

### Branch Protection
- `main` and `develop` branches are protected
- Pull requests required for all changes
- CI checks must pass before merging
- Code review required for all PRs