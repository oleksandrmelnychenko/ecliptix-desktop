# Ecliptix Desktop Scripts

This directory contains all build, versioning, and development workflow scripts for the Ecliptix Desktop project.

## 🚀 AOT Build Scripts

### Unified Multi-Platform Build
- **`build-all.sh`** - Build for multiple platforms from a single command

```bash
# Build for all platforms (runs on appropriate OS for each)
./Scripts/build-all.sh --all

# Build for specific platforms
./Scripts/build-all.sh --linux --macos
./Scripts/build-all.sh --windows

# Build with options
./Scripts/build-all.sh --all --increment patch --clean
./Scripts/build-all.sh --linux --optimization size
```

### Platform-Specific AOT Builds
- **`build-aot-windows.ps1`** - Windows AOT build with full optimization
- **`build-aot-linux.sh`** - Linux AOT build with AppImage support
- **`build-aot-macos.sh`** - Enhanced macOS AOT build with Universal Binary support

All AOT scripts provide:
- Native code compilation for maximum performance
- IL trimming and dead code elimination
- Self-contained deployment packages
- Configurable optimization levels (size/speed/aggressive)
- Automatic version integration
- Architecture-specific builds (x64/ARM64/Universal)

### Quick Start Examples
```bash
# Multi-platform builds
./Scripts/build-all.sh --all                    # Build all platforms
./Scripts/build-all.sh --linux                  # Build Linux only

# Windows (PowerShell)
.\Scripts\build-aot-windows.ps1
.\Scripts\build-aot-windows.ps1 -Runtime win-arm64 -Optimization aggressive

# Linux
./Scripts/build-aot-linux.sh
./Scripts/build-aot-linux.sh --arm64 --clean

# macOS
./Scripts/build-aot-macos.sh
./Scripts/build-aot-macos.sh --universal --increment minor
```

## 🔄 Auto-Versioning System

### Core Versioning Scripts
- **`auto-version.sh`** - Setup and manage automatic versioning git hooks
- **`version.sh`** - Manual version management wrapper
- **`version-helper.py`** - Core version management implementation
- **`version-config.json`** - Versioning behavior configuration

### Auto-Versioning Setup
```bash
# Install automatic versioning
./Scripts/auto-version.sh install

# Check status
./Scripts/auto-version.sh status

# Uninstall
./Scripts/auto-version.sh uninstall
```

### Version Increment Patterns
| Commit Pattern | Version Change | Example |
|---|---|---|
| `[major]`, `BREAKING CHANGE:` | Major | 1.2.3 → 2.0.0 |
| `[minor]`, `feat:` | Minor | 1.2.3 → 1.3.0 |  
| `[patch]`, `fix:` | Patch | 1.2.3 → 1.2.4 |
| `[skip-version]`, `docs:` | None | 1.2.3 → 1.2.3 |

## 🔧 Development Workflow

### Git Workflow Scripts
- **`new-branch.sh`** - Create properly named feature branches
- **`sync-develop.sh`** - Sync local develop branch with remote
- **`pr-checks.sh`** - Pre-PR validation and quality checks

### Workflow Examples
```bash
# Create feature branch
./Scripts/new-branch.sh feature user-authentication

# Sync with develop
./Scripts/sync-develop.sh

# Run pre-PR checks
./Scripts/pr-checks.sh
```

## 📁 File Structure

```
Scripts/
├── README.md                    # This documentation
│
├── 🚀 AOT Build Scripts
├── build-all.sh                # Multi-platform build orchestrator
├── build-aot-windows.ps1        # Windows AOT (PowerShell)
├── build-aot-linux.sh          # Linux AOT with AppImage
├── build-aot-macos.sh          # macOS AOT with Universal Binary
│
├── 🔄 Versioning System
├── auto-version.sh              # Auto-versioning setup
├── version.sh                   # Version management wrapper
├── version-helper.py            # Core version implementation
├── version-config.json          # Configuration file
│
└── 🔧 Development Workflow
    ├── new-branch.sh            # Git branch creation
    ├── sync-develop.sh          # Branch synchronization
    └── pr-checks.sh             # Pre-PR validation
```

## 🎯 Integration Points

### Version Display
The versioning system integrates with:
- **Directory.Build.props** - MSBuild version properties
- **build-info.json** - Runtime build metadata
- **VersionHelper.cs** - Application version display
- **MembershipHostWindow** - UI version display

### Git Hooks
Auto-versioning uses git hooks in `.githooks/`:
- **pre-commit** - Branch validation
- **post-commit** - Version increment and build info generation

### Build Integration
All AOT scripts automatically:
- Generate version-specific build artifacts
- Include git commit metadata in build-info.json
- Create platform-appropriate distribution packages
- Support CI/CD pipeline integration

## 🚀 Performance Benefits

### AOT Compilation Advantages
- **~40% faster startup** - No JIT compilation overhead
- **~25% smaller memory footprint** - IL trimming removes unused code
- **Self-contained** - No .NET runtime installation required
- **Better user experience** - Native performance characteristics

### Universal Binary (macOS)
- **Single package** supports both Intel and Apple Silicon Macs
- **Optimal performance** on both architectures
- **Simplified distribution** - One file for all Mac users

## 📋 Requirements

### AOT Build Requirements
- **.NET 8.0+** - Required for AOT compilation support
- **Platform tools**:
  - Windows: PowerShell 5.1+
  - Linux: bash, AppImage tools (optional)
  - macOS: bash, Xcode Command Line Tools

### Auto-Versioning Requirements
- **Git repository** - Must be in a git-managed project
- **Python 3.6+** - For version-helper.py
- **Bash** - For git hooks and setup scripts

## 🔍 Troubleshooting

### Common Issues

**AOT Build Fails**
- Ensure .NET 8+ is installed: `dotnet --version`
- Check project file paths are correct
- Verify all dependencies are restored: `dotnet restore`

**Auto-Versioning Not Working**
- Check hooks are installed: `./Scripts/auto-version.sh status`
- Verify git repository: `git status`
- Check branch is supported (main/develop/release/hotfix)

**Permission Errors**
- Make scripts executable: `chmod +x Scripts/*.sh`
- Check git hooks permissions: `ls -la .git/hooks/`

### Getting Help
- Check script help: `./Scripts/[script-name].sh --help`
- Review CLAUDE.md for detailed project documentation
- Check GitHub Issues for known problems and solutions