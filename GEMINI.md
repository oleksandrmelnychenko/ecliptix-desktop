# GEMINI.md

## Project Overview

Ecliptix Desktop is a cross-platform desktop application for secure communication, built with .NET 9.0 and Avalonia UI. It appears to be a client for a secure messaging or data exchange system, utilizing the OPAQUE protocol for password-authenticated key exchange. The application uses gRPC and Protobufs for communication with a server.

The project is structured using the Model-View-ViewModel (MVVM) pattern and leverages Native AOT (Ahead-Of-Time) compilation for improved performance and smaller application size.

## Building and Running

The project includes comprehensive build scripts for Windows, macOS, and Linux.

### Prerequisites

*   .NET 9.0 SDK
*   Git

### Development

To run the application in a development environment:

```bash
# Restore .NET packages
dotnet restore

# Run the application
dotnet run --project Ecliptix.Core/Ecliptix.Core.Desktop/

# Run tests
dotnet test
```

### Production Builds

For production builds, use the provided scripts in the `Scripts/` directory. These scripts handle Native AOT compilation and packaging for each platform.

**Linux:**

```bash
./Scripts/build-aot-linux.sh
```

**macOS:**

```bash
./Scripts/build-aot-macos.sh --universal
```

**Windows:**

```powershell
.\\Scripts\\build-aot-windows.ps1
```

Refer to the `BUILD.md` file for more detailed build options and instructions.

## Development Conventions

*   **MVVM Pattern:** The project follows the MVVM pattern, with Views, ViewModels, and Models separated into their respective directories.
*   **Avalonia UI:** The user interface is built with Avalonia UI, a cross-platform UI framework for .NET.
*   **ReactiveUI:** The project uses ReactiveUI for implementing the MVVM pattern.
*   **Dependency Injection:** The project uses `Microsoft.Extensions.DependencyInjection` for dependency injection.
*   **Secure Coding:** The use of the OPAQUE protocol and certificate pinning suggests a strong focus on security.
*   **Versioning:** The project uses an automatic versioning system based on git commit messages. See `BUILD.md` for more details.
