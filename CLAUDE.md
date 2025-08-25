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

## AOT Build Scripts

The project includes comprehensive Ahead-of-Time (AOT) compilation scripts for all major platforms, providing maximum performance and self-contained deployment.

### Available AOT Build Scripts

#### `./Scripts/build-aot-windows.ps1` - Windows AOT Build
PowerShell script for Windows with full AOT optimization:
```powershell
# Basic AOT build for x64
.\Scripts\build-aot-windows.ps1

# ARM64 build with clean
.\Scripts\build-aot-windows.ps1 -Runtime win-arm64 -Clean

# Size-optimized build with version increment
.\Scripts\build-aot-windows.ps1 -Optimization size -Increment patch
```

**Features:**
- Full native code generation with IL trimming
- Automatic version increment and build info generation
- Creates distributable ZIP archives
- Comprehensive optimization levels (size/speed/aggressive)
- Support for both x64 and ARM64 architectures

#### `./Scripts/build-aot-linux.sh` - Linux AOT Build
Bash script for Linux with AppImage support:
```bash
# Basic AOT build for x64
./Scripts/build-aot-linux.sh

# ARM64 build with AppImage
./Scripts/build-aot-linux.sh --arm64

# Clean build without AppImage
./Scripts/build-aot-linux.sh --clean --no-appimage
```

**Features:**
- Native code compilation with IL trimming
- AppImage portable package creation (if tools available)
- Creates .tar.gz archives for distribution
- Desktop file and icon integration
- Support for x64 and ARM64 architectures

#### `./Scripts/build-aot-macos.sh` - Enhanced macOS AOT Build
Enhanced bash script for macOS with Universal Binary support:
```bash
# Basic AOT build for Apple Silicon
./Scripts/build-aot-macos.sh

# Intel Mac build
./Scripts/build-aot-macos.sh --intel

# Universal Binary (both Intel and Apple Silicon)
./Scripts/build-aot-macos.sh --universal

# Clean aggressive optimization build
./Scripts/build-aot-macos.sh --clean --optimization aggressive
```

**Features:**
- Native code generation with comprehensive optimizations
- **Universal Binary support** using `lipo` for dual architecture
- macOS app bundle creation with proper Info.plist
- DMG creation (if create-dmg is installed)
- Support for Intel (x64), Apple Silicon (ARM64), and Universal binaries

### AOT Build Benefits
- **Faster startup time**: Pre-compiled native code eliminates JIT compilation
- **Reduced memory footprint**: IL trimming removes unused code
- **Self-contained deployment**: No .NET runtime installation required  
- **Better performance**: Native machine code execution
- **Smaller distribution size**: Dead code elimination and compression

### AOT Optimization Levels
All scripts support three optimization levels:
- **size**: Minimize binary size (partial AOT, copyused trimming)
- **speed**: Optimize for execution speed (full AOT, balanced trimming)
- **aggressive**: Maximum optimization (full AOT, link-level trimming)

## Auto-Versioning System

The project includes an automated versioning system that increments versions based on git commit messages, integrated with the application's version display.

### Setup Auto-Versioning
```bash
# Install auto-versioning git hooks
./Scripts/auto-version.sh install

# Check auto-versioning status
./Scripts/auto-version.sh status

# Uninstall auto-versioning
./Scripts/auto-version.sh uninstall
```

### Version Increment Rules
The system automatically increments versions based on commit message patterns:

#### Major Version (x.0.0)
- `[major]` - Explicit major increment
- `BREAKING CHANGE:` - Breaking changes
- `breaking:` - Breaking functionality changes

#### Minor Version (x.y.0)  
- `[minor]` - Explicit minor increment
- `feat:` - New features
- `feature:` - Feature additions

#### Patch Version (x.y.z)
- `[patch]` - Explicit patch increment  
- `fix:` - Bug fixes
- `bugfix:` - Bug corrections
- `hotfix:` - Critical fixes
- **Default**: Any commit without explicit pattern

#### Skip Versioning
- `[skip-version]` or `[no-version]` - No version change
- `docs:` - Documentation only
- `chore:` - Maintenance tasks
- `style:` - Code style changes
- `refactor:` - Code refactoring
- `test:` - Test additions/changes

### Version Integration
The versioning system is fully integrated with:
- **Directory.Build.props**: Centralized MSBuild version properties
- **build-info.json**: Runtime build information with git metadata
- **VersionHelper.cs**: Application version display in UI
- **MembershipHostWindow**: Shows version in authentication flow

### Example Workflow
```bash
# Make changes
git add .

# Commit with version increment
git commit -m "feat: add new user authentication flow"
# → Automatically increments minor version (e.g., 1.2.3 → 1.3.0)

# Commit bug fix
git commit -m "fix: resolve memory leak in protocol handler" 
# → Automatically increments patch version (e.g., 1.3.0 → 1.3.1)

# Commit documentation
git commit -m "docs: update API documentation"
# → No version increment (skipped)
```

### Configuration
Version behavior is configured in `./Scripts/version-config.json`:
- Commit message patterns
- Branch inclusion/exclusion rules
- Build info generation settings
- Hook behavior customization

