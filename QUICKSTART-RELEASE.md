# Quick Start: Creating Releases

## TL;DR - Three Ways to Release

### 1. 🚀 Fastest Way (Recommended)

```bash
# One command to create and release
./Scripts/create-release.sh 1.0.0 --push
```

That's it! Everything happens automatically.

---

### 2. 📦 Git Tag Method

```bash
git tag v1.0.0
git push origin v1.0.0
```

The automation does the rest.

---

### 3. 🖱️ GitHub UI Method

1. Go to: `https://github.com/YOUR_USERNAME/ecliptix-desktop/releases`
2. Click **"Draft a new release"**
3. Type new tag: `v1.0.0`
4. Click **"Publish release"**

Done!

---

## What Happens Automatically

When you push a tag, GitHub Actions automatically:

1. ✅ Builds for **Windows** (x64 + ARM64)
2. ✅ Builds for **macOS** (Intel + Apple Silicon)
3. ✅ Builds for **Linux** (x64 + ARM64, DEB + RPM)
4. ✅ Creates installers for all platforms
5. ✅ Generates SHA-256 checksums
6. ✅ Creates update manifest
7. ✅ Publishes GitHub release
8. ✅ Deploys update server

**Result:** Users get notified of updates automatically!

---

## First-Time Setup (One-Time)

### Step 1: Enable GitHub Pages

1. Go to **Settings** → **Pages**
2. Source: **Deploy from a branch**
3. Branch: **gh-pages**
4. Click **Save**

### Step 2: Configure Update URL

Edit `Ecliptix.Core.Desktop/appsettings.json`:

```json
{
  "UpdateService": {
    "UpdateServerUrl": "https://YOUR_USERNAME.github.io/ecliptix-desktop/updates",
    "EnableAutoCheck": true,
    "CheckInterval": "06:00:00"
  }
}
```

Replace `YOUR_USERNAME` with your GitHub username.

**That's all you need!**

---

## Optional: Code Signing

To sign installers (removes "Unknown Publisher" warnings):

### Windows
Add these secrets to your GitHub repo:
- `WIN_SIGN_CERT` - Your PFX certificate (base64)
- `WIN_SIGN_PASSWORD` - Certificate password

### macOS
Add these secrets:
- `MACOS_CERT` - Your P12 certificate (base64)
- `MACOS_CERT_PASSWORD` - Certificate password
- `MACOS_SIGN_IDENTITY` - Developer ID
- `MACOS_APPLE_ID` - Your Apple ID
- `MACOS_TEAM_ID` - Team ID
- `MACOS_NOTARIZE_KEY` - App-specific password

### Linux
Add this secret:
- `GPG_PRIVATE_KEY` - Your GPG private key

**Without secrets, builds still work but won't be signed.**

---

## Version Naming

Use semantic versioning:

```
v1.0.0       → First release
v1.0.1       → Bug fix
v1.1.0       → New features
v2.0.0       → Breaking changes
v1.0.0-beta.1 → Pre-release
```

---

## Testing Before Release

### Option 1: Test Locally

```bash
# Test Windows installer creation
.\Scripts\create-windows-installer.ps1 -Runtime win-x64

# Test macOS installer creation
./Scripts/create-macos-installer.sh osx-arm64

# Test Linux installer creation
./Scripts/create-linux-installer.sh linux-x64
```

### Option 2: Test in CI

Push to any branch:
```bash
git checkout -b test-build
git push origin test-build
```

Check **Actions** tab - builds run but don't create releases.

---

## Complete Example Workflow

```bash
# 1. Make your changes
git checkout -b feature/awesome-feature
git commit -am "feat: Add awesome feature"
git push origin feature/awesome-feature

# 2. Create PR, review, merge to main
gh pr create
gh pr merge

# 3. Create release (pick one method)

# Method A: Using helper script
./Scripts/create-release.sh 1.2.0 --push

# Method B: Using git directly
git tag v1.2.0
git push origin v1.2.0

# Method C: Using GitHub UI
# Go to Releases → Draft new release

# 4. Wait ~15-30 minutes

# 5. Users automatically get update notification!
```

---

## Monitoring Releases

After creating a tag:

1. **Actions Tab:** `https://github.com/YOUR_USERNAME/ecliptix-desktop/actions`
   - Watch build progress (15-30 min)

2. **Releases Tab:** `https://github.com/YOUR_USERNAME/ecliptix-desktop/releases`
   - See published release with all installers

3. **Update Server:** `https://YOUR_USERNAME.github.io/ecliptix-desktop/updates/manifest.json`
   - Verify manifest is updated

4. **Test Update:**
   - Open Ecliptix app
   - Should show update notification
   - Click "Update Now"

---

## Troubleshooting

### "Tag already exists"
```bash
# Delete local tag
git tag -d v1.0.0

# Delete remote tag
git push origin --delete v1.0.0

# Create new tag
git tag v1.0.0
git push origin v1.0.0
```

### "Build failed"
1. Check Actions tab for error logs
2. Common issues:
   - Version format wrong (must be `v1.2.3`)
   - Scripts not executable (`chmod +x Scripts/*.sh`)
   - Missing dependencies

### "No update manifest"
1. Check GitHub Pages is enabled
2. Wait 5 minutes for deployment
3. Check `gh-pages` branch exists

### "Update not working in app"
1. Verify `appsettings.json` has correct URL
2. Check manifest URL in browser
3. Verify app version is older than release version

---

## Daily Workflow Cheat Sheet

```bash
# Create new release
./Scripts/create-release.sh 1.0.0 --push

# Create pre-release
./Scripts/create-release.sh 1.0.0-beta.1 --prerelease --push

# Check release status
gh release list

# Download release
gh release download v1.0.0

# Delete release (if needed)
gh release delete v1.0.0
git push origin --delete v1.0.0
```

---

## Getting Help

- **Detailed Automation Docs:** `RELEASE-AUTOMATION.md`
- **Installer Docs:** `INSTALLERS.md`
- **Build Docs:** `BUILD.md`
- **Enhancement Roadmap:** `ROADMAP-INSTALLERS.md`

---

**Ready to release?**

```bash
./Scripts/create-release.sh 1.0.0 --push
```

🎉 That's it! Your users will get automatic updates!

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)
