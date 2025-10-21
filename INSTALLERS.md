# Ecliptix Desktop - Installers and Auto-Update

This guide covers creating installers for all platforms and setting up the auto-update system.

## Table of Contents

- [Creating Installers](#creating-installers)
  - [Windows Installer](#windows-installer)
  - [macOS Installer](#macos-installer)
  - [Linux Installers](#linux-installers)
- [Auto-Update System](#auto-update-system)
  - [Architecture](#architecture)
  - [Setting Up Update Server](#setting-up-update-server)
  - [Integrating Auto-Updater](#integrating-auto-updater)
- [Distribution Workflow](#distribution-workflow)
- [Troubleshooting](#troubleshooting)

---

## Creating Installers

### Prerequisites

Build the application first for all target platforms:

```bash
# On Linux
./Scripts/build-all.sh --linux

# On macOS
./Scripts/build-all.sh --macos

# On Windows
.\Scripts\build-aot-windows.ps1
```

---

### Windows Installer

**Requirements:**
- [Inno Setup 6.0+](https://jrsoftware.org/isdl.php)

**Create Installer:**

```powershell
# Basic installer
.\Scripts\create-windows-installer.ps1

# With custom build path
.\Scripts\create-windows-installer.ps1 -BuildPath ..\publish\win-x64\Ecliptix

# With custom output directory
.\Scripts\create-windows-installer.ps1 -OutputDir C:\Installers
```

**Output:**
- `installers/Ecliptix-{version}-win-x64-Setup.exe`

**Features:**
- Silent installation support
- Automatic uninstall of previous versions
- Desktop and Start Menu shortcuts
- Proper uninstaller
- User or system-wide installation

**Code Signing (Recommended):**

```powershell
# Sign the installer
signtool sign /f certificate.pfx /p password /t http://timestamp.digicert.com `
  installers\Ecliptix-1.0.0-win-x64-Setup.exe
```

---

### macOS Installer

**Requirements:**
- macOS system
- Xcode Command Line Tools

**Create Installer:**

```bash
# Basic DMG (auto-detects build)
./Scripts/create-macos-installer.sh

# Specify app bundle
./Scripts/create-macos-installer.sh -a publish/universal/Ecliptix.app

# With code signing
./Scripts/create-macos-installer.sh -a publish/universal/Ecliptix.app \
  -s "Developer ID Application: Your Name (TEAMID)"

# With notarization
./Scripts/create-macos-installer.sh -a publish/universal/Ecliptix.app \
  -s "Developer ID Application: Your Name" --notarize
```

**Output:**
- `installers/Ecliptix-{version}-{arch}.dmg`

**Features:**
- Drag-and-drop installation
- Custom background image (optional)
- Applications folder symlink
- Proper volume icon
- Universal binary support

**Code Signing & Notarization:**

```bash
# 1. Sign the app bundle first
codesign --deep --force --options runtime \
  --sign "Developer ID Application: Your Name (TEAMID)" \
  publish/universal/Ecliptix.app

# 2. Create and sign DMG
./Scripts/create-macos-installer.sh -a publish/universal/Ecliptix.app \
  -s "Developer ID Application: Your Name"

# 3. Notarize (requires Apple Developer account)
xcrun notarytool submit installers/Ecliptix-1.0.0-universal.dmg \
  --keychain-profile "notarytool-profile" --wait

# 4. Staple the notarization
xcrun stapler staple installers/Ecliptix-1.0.0-universal.dmg
```

---

### Linux Installers

**Requirements:**
- `dpkg-deb` (pre-installed on most distros)
- `rpmbuild` (for RPM packages)

**Install RPM build tools:**

```bash
# Debian/Ubuntu
sudo apt install rpm

# Fedora/RHEL
sudo dnf install rpm-build
```

**Create Installers:**

```bash
# Create both DEB and RPM
./Scripts/create-linux-installer.sh

# DEB only
./Scripts/create-linux-installer.sh --deb-only

# RPM only
./Scripts/create-linux-installer.sh --rpm-only

# Custom build path
./Scripts/create-linux-installer.sh -b ../publish/linux-x64/Ecliptix
```

**Output:**
- `installers/ecliptix_{version}_amd64.deb`
- `installers/ecliptix-{version}-1.x86_64.rpm`

**Installation:**

```bash
# DEB (Debian/Ubuntu/Mint)
sudo dpkg -i installers/ecliptix_1.0.0_amd64.deb

# RPM (Fedora/RHEL/CentOS)
sudo rpm -i installers/ecliptix-1.0.0-1.x86_64.rpm

# Or use package managers
sudo apt install ./installers/ecliptix_1.0.0_amd64.deb
sudo dnf install ./installers/ecliptix-1.0.0-1.x86_64.rpm
```

**Features:**
- Desktop entry with icon
- System-wide installation to `/usr/share/ecliptix`
- Launcher script in `/usr/bin`
- Post-install hooks for desktop database updates
- Proper uninstallation

---

## Auto-Update System

### Architecture

The auto-update system consists of:

1. **Update Server**: Hosts update manifest and installer files
2. **Update Manifest** (`manifest.json`): Version info and download URLs
3. **Auto-Updater Library** (`Ecliptix.AutoUpdater`): Client-side update logic
4. **Application Integration**: UI for update notifications

### Flow

```
Application Startup
    ↓
Check for Updates (background)
    ↓
If update available → Notify user
    ↓
User accepts → Download installer
    ↓
Verify SHA-256 checksum
    ↓
Launch installer → Exit app
    ↓
Installer runs → Update complete
```

---

### Setting Up Update Server

#### Option 1: Static File Server (Nginx)

**nginx.conf:**

```nginx
server {
    listen 443 ssl http2;
    server_name updates.ecliptix.com;

    ssl_certificate /etc/ssl/certs/ecliptix.crt;
    ssl_certificate_key /etc/ssl/private/ecliptix.key;

    root /var/www/updates;

    # Manifest with shorter cache
    location /manifest.json {
        add_header Access-Control-Allow-Origin *;
        add_header Cache-Control "public, max-age=300";
    }

    # Installers with longer cache
    location /releases/ {
        add_header Access-Control-Allow-Origin *;
        add_header Cache-Control "public, max-age=86400";
    }
}
```

**Directory structure:**

```
/var/www/updates/
├── manifest.json
└── releases/
    └── 1.0.1/
        ├── Ecliptix-1.0.1-win-x64-Setup.exe
        ├── Ecliptix-1.0.1-universal.dmg
        ├── Ecliptix-1.0.1-linux-x64-AOT.AppImage
        └── ecliptix_1.0.1_amd64.deb
```

#### Option 2: AWS S3 + CloudFront

```bash
# Create S3 bucket
aws s3 mb s3://ecliptix-updates

# Upload files
aws s3 cp manifest.json s3://ecliptix-updates/manifest.json
aws s3 cp installers/ s3://ecliptix-updates/releases/1.0.1/ --recursive

# Set CORS policy
aws s3api put-bucket-cors --bucket ecliptix-updates --cors-configuration file://cors.json
```

**cors.json:**

```json
{
  "CORSRules": [{
    "AllowedOrigins": ["*"],
    "AllowedMethods": ["GET", "HEAD"],
    "AllowedHeaders": ["*"],
    "MaxAgeSeconds": 3600
  }]
}
```

#### Option 3: GitHub Releases

Use GitHub Releases as your update server (free option):

1. Create a new release on GitHub
2. Upload all installers as release assets
3. Update `manifest.json` with GitHub download URLs
4. Host `manifest.json` on GitHub Pages or separate server

---

### Creating Update Manifest

**Generate checksums:**

```bash
# Linux/macOS
sha256sum installers/Ecliptix-1.0.1-win-x64-Setup.exe
shasum -a 256 installers/Ecliptix-1.0.1-universal.dmg

# Windows PowerShell
Get-FileHash installers\Ecliptix-1.0.1-win-x64-Setup.exe -Algorithm SHA256
```

**UpdateServer/manifest.json:**

```json
{
  "version": "1.0.1",
  "releaseDate": "2025-01-15T00:00:00Z",
  "releaseNotes": "### What's New\n\n- Bug fixes\n- Performance improvements",
  "isCritical": false,
  "minimumVersion": "1.0.0",
  "platforms": {
    "win-x64": {
      "downloadUrl": "https://updates.ecliptix.com/releases/1.0.1/Ecliptix-1.0.1-win-x64-Setup.exe",
      "fileSize": 52428800,
      "sha256": "abc123...",
      "installerType": "exe"
    },
    "osx-arm64": {
      "downloadUrl": "https://updates.ecliptix.com/releases/1.0.1/Ecliptix-1.0.1-universal.dmg",
      "fileSize": 62914560,
      "sha256": "def456...",
      "installerType": "dmg"
    },
    "linux-x64": {
      "downloadUrl": "https://updates.ecliptix.com/releases/1.0.1/Ecliptix-1.0.1-linux-x64-AOT.AppImage",
      "fileSize": 73400320,
      "sha256": "ghi789...",
      "installerType": "appimage"
    }
  }
}
```

---

### Integrating Auto-Updater

#### 1. Add Project Reference

Edit `Ecliptix.Core/Ecliptix.Core.Desktop/Ecliptix.Core.Desktop.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Ecliptix.AutoUpdater\Ecliptix.AutoUpdater.csproj" />
</ItemGroup>
```

#### 2. Configure Update Service

**appsettings.json:**

```json
{
  "UpdateService": {
    "UpdateServerUrl": "https://updates.ecliptix.com",
    "CheckInterval": "06:00:00",
    "EnableAutoCheck": true,
    "EnableAutoDownload": false
  }
}
```

#### 3. Initialize in Program.cs

```csharp
using Ecliptix.AutoUpdater;

// Get current version
var version = Assembly.GetExecutingAssembly()
    .GetName()
    .Version?
    .ToString() ?? "1.0.0";

// Initialize update service
var updateServerUrl = configuration["UpdateService:UpdateServerUrl"] ?? "";
var updateService = new UpdateService(updateServerUrl, version);

// Register as singleton
services.AddSingleton(updateService);
```

#### 4. Check for Updates in ViewModel

```csharp
public class MainWindowViewModel : ViewModelBase
{
    private readonly UpdateService _updateService;

    public async Task CheckForUpdatesAsync()
    {
        var result = await _updateService.CheckForUpdatesAsync();

        if (result.IsUpdateAvailable)
        {
            // Show update notification UI
            ShowUpdateNotification(result);
        }
    }

    public async Task InstallUpdateAsync()
    {
        var result = await _updateService.CheckForUpdatesAsync();
        if (result.Manifest != null)
        {
            await _updateService.DownloadAndInstallUpdateAsync(result.Manifest);
            // App will exit after installer launches
        }
    }
}
```

See `Ecliptix.AutoUpdater/README.md` for complete integration examples.

---

## Distribution Workflow

### Complete Release Process

```bash
# 1. Build for all platforms
./Scripts/build-all.sh --all --increment patch --clean

# 2. Create installers (on each platform)
# Windows:
.\Scripts\create-windows-installer.ps1

# macOS:
./Scripts/create-macos-installer.sh -a publish/universal/Ecliptix.app

# Linux:
./Scripts/create-linux-installer.sh

# 3. Calculate checksums
find installers/ -type f -exec sha256sum {} \;

# 4. Update manifest.json
# - Update version
# - Update release notes
# - Update download URLs
# - Update checksums

# 5. Deploy to update server
rsync -avz installers/ user@updates.ecliptix.com:/var/www/updates/releases/1.0.1/
rsync -avz UpdateServer/manifest.json user@updates.ecliptix.com:/var/www/updates/

# 6. Create GitHub release (optional)
gh release create v1.0.1 installers/* --notes "Release notes..."

# 7. Test update
# - Run older version
# - Verify update notification appears
# - Test download and installation
```

---

## Troubleshooting

### Windows Installer Issues

**Inno Setup not found:**
```powershell
# Download from https://jrsoftware.org/isdl.php
# Or specify path:
.\Scripts\create-windows-installer.ps1 -InnoSetupPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

**Permission denied:**
```powershell
# Run as administrator or use user-level install
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### macOS Installer Issues

**Code signing fails:**
```bash
# List available identities
security find-identity -v -p codesigning

# Use correct identity
./Scripts/create-macos-installer.sh -s "Developer ID Application: Your Name (TEAMID)"
```

**Notarization fails:**
```bash
# Check notarization status
xcrun notarytool history --keychain-profile "notarytool-profile"

# View submission log
xcrun notarytool log SUBMISSION_ID --keychain-profile "notarytool-profile"
```

### Linux Installer Issues

**dpkg-deb not found:**
```bash
sudo apt install dpkg-dev
```

**rpmbuild not found:**
```bash
# Debian/Ubuntu
sudo apt install rpm

# Fedora/RHEL
sudo dnf install rpm-build
```

### Auto-Update Issues

**Update check fails:**
- Verify update server is accessible: `curl https://updates.ecliptix.com/manifest.json`
- Check HTTPS certificate is valid
- Verify manifest.json is valid JSON

**Download fails:**
- Check network connectivity
- Verify download URLs are accessible
- Ensure sufficient disk space

**Installation fails:**
- Verify SHA-256 checksum matches
- Check installer file permissions
- Ensure sufficient privileges

---

## Security Best Practices

### HTTPS Only
Always use HTTPS for update server to prevent MITM attacks.

### Checksum Verification
The auto-updater verifies SHA-256 checksums before installation.

### Code Signing
Sign all installers:
- **Windows**: Authenticode certificate
- **macOS**: Apple Developer ID + Notarization
- **Linux**: GPG signatures (optional)

### Manifest Integrity
Consider signing the manifest.json file or using a separate signature file.

---

## Additional Resources

- **Build Guide**: [BUILD.md](BUILD.md)
- **Scripts Documentation**: [Scripts/README.md](Scripts/README.md)
- **Auto-Updater API**: [Ecliptix.AutoUpdater/README.md](Ecliptix.AutoUpdater/README.md)
- **Update Server Setup**: [UpdateServer/README.md](UpdateServer/README.md)

---

## Support

For issues:
1. Check troubleshooting section above
2. Review platform-specific documentation
3. Check GitHub Issues for known problems
