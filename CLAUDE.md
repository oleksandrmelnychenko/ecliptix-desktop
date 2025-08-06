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