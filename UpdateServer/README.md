# Ecliptix Desktop - Update Server

This directory contains the update manifest and configuration for the Ecliptix Desktop auto-updater.

## Update Manifest

The `manifest.json` file contains information about the latest available version and download URLs for each platform.

### Manifest Schema

```json
{
  "version": "1.0.1",                    // Latest version number
  "releaseDate": "2025-01-15T00:00:00Z", // ISO 8601 release date
  "releaseNotes": "Markdown text...",    // Release notes (Markdown format)
  "isCritical": false,                   // Is this a critical/mandatory update?
  "minimumVersion": "1.0.0",             // Minimum supported version (optional)
  "platforms": {                         // Platform-specific downloads
    "win-x64": {
      "downloadUrl": "https://...",
      "fileSize": 52428800,
      "sha256": "abc123...",
      "installerType": "exe"
    },
    // ... other platforms
  }
}
```

### Supported Platforms

- `win-x64`: Windows 64-bit (Intel/AMD)
- `win-arm64`: Windows ARM64
- `osx-x64`: macOS Intel
- `osx-arm64`: macOS Apple Silicon
- `linux-x64`: Linux 64-bit (Intel/AMD)
- `linux-arm64`: Linux ARM64

### Installer Types

- `exe`: Windows installer (Inno Setup)
- `dmg`: macOS disk image
- `appimage`: Linux AppImage
- `deb`: Debian package
- `rpm`: Red Hat package

## Deployment

### 1. Build All Platforms

```bash
# Build for each platform on its native OS
./Scripts/build-all.sh --linux          # On Linux
./Scripts/build-all.sh --macos          # On macOS
.\Scripts\build-aot-windows.ps1         # On Windows
```

### 2. Create Installers

```bash
# Linux
./Scripts/create-linux-installer.sh

# macOS
./Scripts/create-macos-installer.sh

# Windows
.\Scripts\create-windows-installer.ps1
```

### 3. Calculate SHA-256 Checksums

```bash
# Linux/macOS
sha256sum installers/Ecliptix-1.0.1-linux-x64-AOT.AppImage
shasum -a 256 installers/Ecliptix-1.0.1-arm64.dmg

# Windows PowerShell
Get-FileHash installers\Ecliptix-1.0.1-win-x64-Setup.exe -Algorithm SHA256
```

### 4. Update Manifest

1. Edit `manifest.json` with new version information
2. Update version number
3. Update release date
4. Add release notes
5. Update download URLs
6. Update file sizes
7. Update SHA-256 checksums

### 5. Deploy to Update Server

Upload the following to your update server:

```
https://updates.ecliptix.com/
├── manifest.json                                    # Update manifest
└── releases/
    └── 1.0.1/
        ├── Ecliptix-1.0.1-win-x64-Setup.exe
        ├── Ecliptix-1.0.1-win-arm64-Setup.exe
        ├── Ecliptix-1.0.1-x64.dmg
        ├── Ecliptix-1.0.1-arm64.dmg
        ├── Ecliptix-1.0.1-linux-x64-AOT.AppImage
        └── Ecliptix-1.0.1-linux-arm64-AOT.AppImage
```

## Update Server Setup

### Static File Server (Simple)

Any static file server will work. Examples:

#### Nginx

```nginx
server {
    listen 443 ssl http2;
    server_name updates.ecliptix.com;

    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    root /var/www/updates;

    location / {
        add_header Access-Control-Allow-Origin *;
        add_header Cache-Control "public, max-age=3600";
    }

    location /manifest.json {
        add_header Access-Control-Allow-Origin *;
        add_header Cache-Control "public, max-age=300";
    }
}
```

#### AWS S3 + CloudFront

1. Create S3 bucket for updates
2. Enable static website hosting
3. Upload manifest.json and installers
4. Configure CloudFront distribution
5. Set CORS policy:

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

### GitHub Releases (Free Option)

You can use GitHub Releases as your update server:

1. Create a new release on GitHub
2. Upload all installer files as release assets
3. Update manifest.json with GitHub download URLs
4. Host manifest.json on GitHub Pages or separate server

Example URLs:
```
https://github.com/user/repo/releases/download/v1.0.1/Ecliptix-1.0.1-win-x64-Setup.exe
```

## Testing Updates

### 1. Test Update Check

```bash
# Check if update service can reach manifest
curl https://updates.ecliptix.com/manifest.json
```

### 2. Test in Application

1. Set update server URL in app configuration
2. Trigger manual update check
3. Verify update notification appears
4. Test download progress
5. Test installer launch

### 3. Version Testing

Test different version scenarios:

- **Current < Latest**: Update should be offered
- **Current = Latest**: No update available
- **Current > Latest**: No update available
- **Current < Minimum**: Critical update (mandatory)

## Security

### HTTPS Required

Always use HTTPS for update server to prevent man-in-the-middle attacks:

- ✅ `https://updates.ecliptix.com`
- ❌ `http://updates.ecliptix.com`

### SHA-256 Verification

The auto-updater verifies SHA-256 checksums before installation:

1. Download completes
2. Calculate SHA-256 of downloaded file
3. Compare with manifest checksum
4. Reject if mismatch

### Code Signing

Sign all installers with valid certificates:

- **Windows**: Authenticode certificate
- **macOS**: Apple Developer ID
- **Linux**: GPG signature (optional)

## Monitoring

Track update metrics:

- Download counts per platform
- Update success/failure rates
- Version distribution
- Average download time

Use CDN/server logs or analytics service.

## Rollback

If a bad update is released:

1. Update manifest.json to previous version
2. Remove bad version from download server
3. Update release notes with rollback information
4. Notify users if critical

## Automation

### CI/CD Pipeline Example

```yaml
# .github/workflows/release.yml
name: Create Release

on:
  push:
    tags:
      - 'v*'

jobs:
  build-and-release:
    runs-on: ${{ matrix.os }}
    strategy:
      matrix:
        os: [ubuntu-latest, macos-latest, windows-latest]

    steps:
      - uses: actions/checkout@v3

      - name: Build Application
        run: |
          # Build commands for each platform

      - name: Create Installer
        run: |
          # Installer creation commands

      - name: Calculate Checksum
        run: |
          # SHA-256 calculation

      - name: Upload to Release
        uses: actions/upload-release-asset@v1
        # Upload installer assets

      - name: Update Manifest
        run: |
          # Update manifest.json with new version
```

## Support

For issues with the update system:

1. Check update server is accessible
2. Verify manifest.json is valid JSON
3. Confirm SHA-256 checksums match
4. Check application logs for errors
5. Test with different network conditions
