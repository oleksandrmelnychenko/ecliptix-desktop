# Building Ecliptix Desktop

This guide explains how to build Ecliptix Desktop for Windows, macOS, and Linux.

## Prerequisites

### All Platforms
- **.NET 9.0 SDK** or higher - [Download](https://dotnet.microsoft.com/download)
- **Git** - For version control

### Platform-Specific Requirements

#### Windows
- **PowerShell 5.1+** (included with Windows 10/11)
- **Visual Studio 2022** or **Visual Studio Code** (optional, for development)

#### macOS
- **Xcode Command Line Tools**: `xcode-select --install`
- **Bash** (pre-installed on macOS)

#### Linux
- **Bash** (pre-installed on most distributions)
- **AppImage tools** (optional, for creating AppImage packages):
  ```bash
  sudo apt install appimagetool  # Debian/Ubuntu
  ```

## Quick Start

### Build All Platforms

The easiest way to build for multiple platforms is using the unified build script:

```bash
# Build for all platforms (must run on each respective OS)
./Scripts/build-all.sh --all

# Build for Linux only
./Scripts/build-all.sh --linux

# Build with version increment
./Scripts/build-all.sh --all --increment patch

# Build with custom options
./Scripts/build-all.sh --linux --clean --optimization aggressive
```

**Note:** Cross-compilation is not supported. Each platform must be built on its native operating system.

### Build Individual Platforms

#### Linux

```bash
# Standard build
./Scripts/build-aot-linux.sh

# Build with options
./Scripts/build-aot-linux.sh --clean --x64

# Build for ARM64
./Scripts/build-aot-linux.sh --arm64

# Skip AppImage creation
./Scripts/build-aot-linux.sh --no-appimage
```

**Output:** `publish/linux-x64/Ecliptix/`

#### macOS

```bash
# Build Universal Binary (Intel + Apple Silicon)
./Scripts/build-aot-macos.sh --universal

# Build for Apple Silicon only
./Scripts/build-aot-macos.sh --arm

# Build for Intel only
./Scripts/build-aot-macos.sh --intel

# Build and create DMG installer
./Scripts/build-aot-macos.sh --universal --create-dmg
```

**Output:** `publish/universal/Ecliptix.app/` (or `publish/osx-arm64/`, `publish/osx-x64/`)

#### Windows

```powershell
# Standard build
.\Scripts\build-aot-windows.ps1

# Build with options
.\Scripts\build-aot-windows.ps1 -Clean -Optimization aggressive

# Build for ARM64
.\Scripts\build-aot-windows.ps1 -Runtime win-arm64
```

**Output:** `publish/win-x64/Ecliptix/`

## Build Options

All build scripts support the following options:

| Option | Description | Values |
|--------|-------------|--------|
| `--configuration` / `-Configuration` | Build configuration | `Debug`, `Release` (default) |
| `--optimization` / `-Optimization` | Optimization level | `size`, `speed`, `aggressive` (default) |
| `--increment` / `-Increment` | Version increment | `major`, `minor`, `patch` |
| `--clean` / `-Clean` | Clean before building | Flag |
| `--skip-tests` / `-SkipTests` | Skip running tests | Flag |
| `--skip-restore` / `-SkipRestore` | Skip package restore | Flag |

## Build Output

### Linux
- **Directory:** `publish/linux-x64/Ecliptix/`
- **Executable:** `Ecliptix`
- **Archive:** `Ecliptix-{version}-linux-x64-AOT.tar.gz`
- **AppImage:** `Ecliptix-{version}-linux-x64-AOT.AppImage` (if created)

### macOS
- **App Bundle:** `publish/universal/Ecliptix.app/`
- **Archive:** `Ecliptix-{version}-universal-AOT.tar.gz`
- **DMG:** `Ecliptix-{version}.dmg` (if created)

### Windows
- **Directory:** `publish/win-x64/Ecliptix/`
- **Executable:** `Ecliptix.exe`
- **Archive:** `Ecliptix-{version}-win-x64-AOT.zip`

## AOT Compilation

All builds use **Native AOT (Ahead-Of-Time) compilation** for optimal performance:

### Benefits
- ✅ **40% faster startup** - No JIT compilation overhead
- ✅ **25% smaller memory footprint** - IL trimming removes unused code
- ✅ **Self-contained** - No .NET runtime installation required
- ✅ **Native performance** - Fully compiled to native machine code

### Optimization Levels

| Level | Description | Best For |
|-------|-------------|----------|
| `size` | Minimize binary size | Distribution, storage-constrained environments |
| `speed` | Maximize runtime performance | Performance-critical applications |
| `aggressive` | Balance size and speed (default) | General use, recommended for most scenarios |

## Development Builds

For development and debugging, you can use standard .NET commands:

```bash
# Restore packages
dotnet restore

# Build without AOT (faster for development)
dotnet build

# Run the application
dotnet run --project Ecliptix.Core/Ecliptix.Core.Desktop/

# Run tests
dotnet test
```

## Version Management

The project includes automatic versioning based on git commits:

```bash
# Install auto-versioning hooks
./Scripts/auto-version.sh install

# Check current version
./Scripts/version.sh --action current

# Manually increment version
./Scripts/version.sh --action increment --part patch
```

### Version Increment Patterns

Commits automatically increment the version based on their message:

| Pattern | Increment | Example |
|---------|-----------|---------|
| `[major]`, `BREAKING CHANGE:` | Major | 1.2.3 → 2.0.0 |
| `[minor]`, `feat:` | Minor | 1.2.3 → 1.3.0 |
| `[patch]`, `fix:` | Patch | 1.2.3 → 1.2.4 |
| `[skip-version]`, `docs:` | None | 1.2.3 → 1.2.3 |

## Troubleshooting

### Build Fails with "dotnet: command not found"
**Solution:** Install .NET SDK from https://dotnet.microsoft.com/download

### Build Fails with ".NET 8 or higher is required"
**Solution:** Update your .NET SDK to version 9.0 or higher

### Linux: AppImage creation fails
**Solution:** Install AppImage tools: `sudo apt install appimagetool`

### macOS: Icon doesn't appear
**Solution:**
```bash
rm -rf ~/Library/Caches/com.apple.iconservices.store
killall Dock
```

### Windows: Build script won't run
**Solution:** Enable script execution:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### Permission Denied on Scripts
**Solution:** Make scripts executable:
```bash
chmod +x Scripts/*.sh
```

## Continuous Integration

For CI/CD pipelines, use the build scripts with appropriate flags:

```yaml
# Example GitHub Actions
- name: Build for Linux
  run: ./Scripts/build-aot-linux.sh --skip-tests --clean

- name: Build for macOS
  run: ./Scripts/build-aot-macos.sh --universal --skip-tests

- name: Build for Windows
  run: .\Scripts\build-aot-windows.ps1 -SkipTests -Clean
```

## Distribution

After building, you can distribute:

### Linux
- **AppImage** (recommended): Single-file portable application
- **tar.gz archive**: Extract and run
- **DEB/RPM packages**: Create using `fpm`, `checkinstall`, or native packaging

### macOS
- **DMG installer**: For easy installation
- **.app bundle**: Direct distribution
- **Notarization**: For macOS 10.15+ (requires Apple Developer account)

### Windows
- **ZIP archive**: Extract and run
- **Installer**: Create using Inno Setup, NSIS, or WiX Toolset
- **Code signing**: Recommended for distribution (requires code signing certificate)

## Additional Resources

- **Scripts Documentation:** [Scripts/README.md](Scripts/README.md)
- **.NET AOT Documentation:** https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
- **Avalonia Documentation:** https://docs.avaloniaui.net/

## Support

For build issues or questions:
1. Check the [Troubleshooting](#troubleshooting) section above
2. Review the [Scripts/README.md](Scripts/README.md) for detailed script documentation
3. Check GitHub Issues for known problems and solutions
