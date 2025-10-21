# Automated Release System

This document explains how the automated release pipeline works and how to use it.

## Overview

The automated release system handles:
- ✅ Cross-platform builds (Windows, macOS, Linux)
- ✅ Platform-specific installers (EXE, DMG, DEB, RPM)
- ✅ Code signing (optional, when secrets configured)
- ✅ SHA-256 checksum generation
- ✅ Update manifest generation
- ✅ GitHub Release creation
- ✅ Update server deployment

## How to Create a Release

### Method 1: Git Tag (Recommended)

Simply create and push a version tag:

```bash
# Tag the current commit
git tag v1.0.0

# Push the tag to GitHub
git push origin v1.0.0
```

**That's it!** The automation will:
1. Detect the tag push
2. Build for all platforms
3. Create installers
4. Generate update manifest
5. Create GitHub release with all files
6. Deploy update manifest to GitHub Pages

### Method 2: GitHub UI (Manual Trigger)

1. Go to **Actions** tab in GitHub
2. Select **Automated Release Pipeline**
3. Click **Run workflow**
4. Enter version (e.g., `v1.0.0`)
5. Choose if it's a pre-release
6. Click **Run workflow**

### Method 3: GitHub Release (UI)

1. Go to **Releases** tab in GitHub
2. Click **Draft a new release**
3. Create a new tag (e.g., `v1.0.0`)
4. Fill in release notes
5. Click **Publish release**

The automation triggers and creates all installers automatically.

## What Gets Built

For each release, the system creates:

### Windows
- `Ecliptix-{version}-win-x64.exe` (Intel/AMD 64-bit)
- `Ecliptix-{version}-win-arm64.exe` (ARM64)
- `.sha256` checksums for each

### macOS
- `Ecliptix-{version}-osx-x64.dmg` (Intel)
- `Ecliptix-{version}-osx-arm64.dmg` (Apple Silicon)
- `.sha256` checksums for each

### Linux
- `ecliptix_{version}_amd64.deb` (Debian/Ubuntu)
- `ecliptix-{version}.x86_64.rpm` (Fedora/RHEL)
- `ecliptix_{version}_arm64.deb` (ARM64 Debian)
- `ecliptix-{version}.aarch64.rpm` (ARM64 Fedora)
- `Ecliptix-{version}-linux-*.tar.gz` (Generic)
- `.sha256` checksums for each

### Update System
- `manifest.json` - Auto-generated update manifest
- Deployed to GitHub Pages at `https://{user}.github.io/{repo}/updates/manifest.json`

## Version Numbering

Use semantic versioning: `v{MAJOR}.{MINOR}.{PATCH}`

Examples:
- `v1.0.0` - Initial release
- `v1.0.1` - Bug fix
- `v1.1.0` - New features
- `v2.0.0` - Breaking changes
- `v1.0.0-beta.1` - Pre-release

## Code Signing (Optional)

To enable code signing, add these secrets to your GitHub repository:

### Windows Signing
- `WIN_SIGN_CERT` - Base64-encoded PFX certificate
- `WIN_SIGN_PASSWORD` - Certificate password

```bash
# Generate the secret value
base64 -i certificate.pfx | pbcopy  # macOS
base64 -w 0 certificate.pfx         # Linux
```

### macOS Signing & Notarization
- `MACOS_CERT` - Base64-encoded P12 certificate
- `MACOS_CERT_PASSWORD` - Certificate password
- `MACOS_SIGN_IDENTITY` - Developer ID (e.g., "Developer ID Application: Your Name")
- `MACOS_APPLE_ID` - Your Apple ID email
- `MACOS_TEAM_ID` - Your Apple Team ID
- `MACOS_NOTARIZE_KEY` - App-specific password for notarization

### Linux Signing
- `GPG_PRIVATE_KEY` - GPG private key for package signing

```bash
# Export GPG key
gpg --armor --export-secret-key your@email.com
```

**Without these secrets, builds still work but won't be signed.**

## Update Server Setup

The automation deploys update manifests to GitHub Pages automatically.

### Enable GitHub Pages

1. Go to **Settings** → **Pages**
2. Source: **Deploy from a branch**
3. Branch: **gh-pages** / `/ (root)`
4. Click **Save**

Your update server URL will be:
```
https://{username}.github.io/{repo}/updates/manifest.json
```

### Configure Auto-Updater

Update your `appsettings.json`:

```json
{
  "UpdateService": {
    "UpdateServerUrl": "https://{username}.github.io/{repo}/updates",
    "EnableAutoCheck": true,
    "CheckInterval": "06:00:00"
  }
}
```

## Testing Before Release

### Test Locally

Run individual build scripts:

```bash
# Windows
.\Scripts\create-windows-installer.ps1 -Runtime win-x64

# macOS
./Scripts/create-macos-installer.sh osx-arm64

# Linux
./Scripts/create-linux-installer.sh linux-x64
```

### Test in CI Without Release

Push to a feature branch - builds run but don't create releases:

```bash
git checkout -b test-release
git push origin test-release
```

Check the **Actions** tab to see build status.

## Workflow Triggers

The automation runs on:

| Trigger | Action | Creates Release? |
|---------|--------|------------------|
| Tag push (`v*`) | Full build + release | ✅ Yes |
| GitHub Release published | Full build + release | ✅ Yes |
| Manual workflow dispatch | Full build + release | ✅ Yes |
| Push to `main` | Build only | ❌ No |
| Pull request | Build only | ❌ No |

## Troubleshooting

### Build Fails

1. Check **Actions** tab for error logs
2. Common issues:
   - Missing dependencies (install them in workflow)
   - Build script not executable (`chmod +x`)
   - Version format incorrect (must be `v1.2.3`)

### Installer Not Created

1. Check if build script succeeded
2. Verify installer script exists and is executable
3. Check platform-specific requirements:
   - Windows: Inno Setup installed
   - Linux: `dpkg-dev`, `rpm` installed

### Update Manifest Not Generated

1. Verify GitHub Pages is enabled
2. Check `gh-pages` branch exists
3. Verify `generate-manifest.sh` has correct permissions

### Code Signing Fails

1. Verify secrets are correctly set
2. Check certificate hasn't expired
3. For macOS: verify Team ID and Apple ID are correct

## Release Checklist

Before creating a release:

- [ ] All tests pass
- [ ] Version number updated in `Ecliptix.Core.Desktop.csproj`
- [ ] CHANGELOG.md updated
- [ ] Documentation updated
- [ ] Git tag follows `v{MAJOR}.{MINOR}.{PATCH}` format
- [ ] GitHub Pages enabled (for update server)
- [ ] Code signing secrets configured (optional)

## Advanced: Custom Update Channels

To support beta/stable channels:

1. Create separate tags:
   - `v1.0.0` → Stable
   - `v1.0.0-beta.1` → Beta
   - `v1.0.0-canary.5` → Canary

2. Modify manifest generator to include channel info

3. Update `UpdateConfiguration` to specify channel:
```csharp
new UpdateConfiguration
{
    UpdateChannel = "beta",
    UpdateServerUrl = "https://.../updates-beta"
}
```

## Monitoring Releases

After releasing:

1. Check **Releases** tab for all assets
2. Verify SHA256 checksums are present
3. Test installers on each platform
4. Monitor update server: `https://{user}.github.io/{repo}/updates/manifest.json`
5. Check auto-updater in production app

## Example Release Workflow

```bash
# 1. Finish your features on develop branch
git checkout develop
git commit -am "feat: Add awesome new feature"
git push origin develop

# 2. Merge to main
git checkout main
git merge develop
git push origin main

# 3. Create and push version tag
git tag v1.2.0
git push origin v1.2.0

# 4. Watch the automation run (Actions tab)
# GitHub Actions will:
#   - Build all platforms
#   - Create all installers
#   - Sign binaries (if configured)
#   - Generate checksums
#   - Create update manifest
#   - Publish GitHub release
#   - Deploy to update server

# 5. Users automatically get notified of update!
```

## Cost & Limits

### GitHub Actions

- **Free tier:** 2,000 minutes/month for private repos
- **Public repos:** Unlimited minutes
- Each release uses ~30-60 minutes total
- **Artifact storage:** 500MB free, then $0.25/GB/month

### GitHub Pages

- **Free:** 1GB storage, 100GB bandwidth/month
- Update manifests are tiny (~5KB each)

### Recommendations

- Use GitHub Actions for public repos (unlimited)
- Enable artifact retention cleanup (90 days)
- Monitor usage in **Settings → Billing**

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)
