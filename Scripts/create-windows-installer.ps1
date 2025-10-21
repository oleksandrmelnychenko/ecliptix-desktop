# Ecliptix Desktop - Windows Installer Builder
# This script creates a Windows installer using Inno Setup

param(
    [string]$BuildPath = "",
    [string]$OutputDir = "",
    [string]$InnoSetupPath = "",
    [switch]$Help
)

$ErrorActionPreference = "Stop"

# Colors for output
function Write-Info { param([string]$Message) Write-Host "🔵 [INSTALLER] $Message" -ForegroundColor Blue }
function Write-Success { param([string]$Message) Write-Host "✅ [INSTALLER] $Message" -ForegroundColor Green }
function Write-Warning { param([string]$Message) Write-Host "⚠️ [INSTALLER] $Message" -ForegroundColor Yellow }
function Write-Error { param([string]$Message) Write-Host "❌ [INSTALLER] $Message" -ForegroundColor Red }

if ($Help) {
    Write-Host "Ecliptix Desktop - Windows Installer Builder" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage: .\create-windows-installer.ps1 [OPTIONS]" -ForegroundColor White
    Write-Host ""
    Write-Host "Options:" -ForegroundColor White
    Write-Host "  -BuildPath PATH      Path to built application (default: auto-detect)" -ForegroundColor Gray
    Write-Host "  -OutputDir PATH      Output directory for installer (default: ..\installers)" -ForegroundColor Gray
    Write-Host "  -InnoSetupPath PATH  Path to Inno Setup compiler (default: auto-detect)" -ForegroundColor Gray
    Write-Host "  -Help                Show this help message" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Examples:" -ForegroundColor White
    Write-Host "  .\create-windows-installer.ps1" -ForegroundColor Gray
    Write-Host "  .\create-windows-installer.ps1 -BuildPath ..\publish\win-x64\Ecliptix" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Requirements:" -ForegroundColor Yellow
    Write-Host "  • Inno Setup 6.0 or higher" -ForegroundColor Gray
    Write-Host "  • Download from: https://jrsoftware.org/isdl.php" -ForegroundColor Gray
    exit 0
}

Write-Info "🚀 Creating Windows Installer..."

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

# Auto-detect build path
if ([string]::IsNullOrEmpty($BuildPath)) {
    $BuildPath = Join-Path $ProjectRoot "publish\win-x64\Ecliptix"
    Write-Info "Auto-detected build path: $BuildPath"
}

# Verify build path exists
if (-not (Test-Path $BuildPath)) {
    Write-Error "Build path not found: $BuildPath"
    Write-Error "Please build the application first: .\Scripts\build-aot-windows.ps1"
    exit 1
}

# Set output directory
if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Join-Path $ProjectRoot "installers"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Write-Info "Output directory: $OutputDir"

# Get version
try {
    $versionScript = Join-Path $ScriptDir "version.sh"
    if (Get-Command bash -ErrorAction SilentlyContinue) {
        $versionOutput = & bash $versionScript --action current 2>&1
        $version = ($versionOutput | Select-String "Current version: (.+)").Matches[0].Groups[1].Value
        if (-not $version) { $version = "1.0.0" }
    } else {
        $version = "1.0.0"
        Write-Warning "Bash not found, using default version: $version"
    }
} catch {
    $version = "1.0.0"
    Write-Warning "Could not determine version, using default: $version"
}

# Clean version
$version = $version -replace '-build.*', '' -replace '^v', ''
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    $version = "1.0.0"
}

Write-Info "Version: $version"

# Find Inno Setup compiler
if ([string]::IsNullOrEmpty($InnoSetupPath)) {
    $possiblePaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 5\ISCC.exe"
    )

    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            $InnoSetupPath = $path
            break
        }
    }
}

if ([string]::IsNullOrEmpty($InnoSetupPath) -or -not (Test-Path $InnoSetupPath)) {
    Write-Error "Inno Setup compiler not found!"
    Write-Error ""
    Write-Error "Please install Inno Setup 6.0 or higher from:"
    Write-Error "  https://jrsoftware.org/isdl.php"
    Write-Error ""
    Write-Error "Or specify the path manually:"
    Write-Error "  .\create-windows-installer.ps1 -InnoSetupPath 'C:\Path\To\ISCC.exe'"
    exit 1
}

Write-Success "Found Inno Setup: $InnoSetupPath"

# Path to ISS script
$issScript = Join-Path $ScriptDir "create-windows-installer.iss"

if (-not (Test-Path $issScript)) {
    Write-Error "Installer script not found: $issScript"
    exit 1
}

# Build installer
Write-Info "Building installer with Inno Setup..."
Write-Info "This may take a few minutes..."

try {
    $buildPathArg = $BuildPath.Replace('\', '\\')
    $outputDirArg = $OutputDir.Replace('\', '\\')

    $arguments = @(
        "/DAppVersion=$version",
        "/DBuildPath=$buildPathArg",
        "/O$outputDirArg",
        "`"$issScript`""
    )

    $process = Start-Process -FilePath $InnoSetupPath -ArgumentList $arguments -Wait -PassThru -NoNewWindow

    if ($process.ExitCode -eq 0) {
        Write-Success "Installer built successfully!"

        $installerName = "Ecliptix-$version-win-x64-Setup.exe"
        $installerPath = Join-Path $OutputDir $installerName

        if (Test-Path $installerPath) {
            $size = (Get-Item $installerPath).Length
            $sizeMB = [Math]::Round($size / 1MB, 2)

            Write-Host ""
            Write-Success "🎉 Windows Installer Created Successfully!"
            Write-Host ""
            Write-Host "📦 Installer Details:" -ForegroundColor Cyan
            Write-Host "   • File: $installerPath" -ForegroundColor White
            Write-Host "   • Size: $sizeMB MB" -ForegroundColor White
            Write-Host "   • Version: $version" -ForegroundColor White
            Write-Host ""
            Write-Host "🧪 To test the installer:" -ForegroundColor Yellow
            Write-Host "   Start-Process '$installerPath'" -ForegroundColor Gray
            Write-Host ""
            Write-Host "📋 Distribution checklist:" -ForegroundColor Yellow
            Write-Host "   1. Test installation on clean Windows system" -ForegroundColor Gray
            Write-Host "   2. Sign the installer with your code signing certificate:" -ForegroundColor Gray
            Write-Host "      signtool sign /f cert.pfx /p password /t http://timestamp.digicert.com '$installerPath'" -ForegroundColor Gray
            Write-Host "   3. Upload to distribution server" -ForegroundColor Gray
            Write-Host "   4. Update download links and release notes" -ForegroundColor Gray
            Write-Host ""
        }
    } else {
        Write-Error "Installer build failed with exit code: $($process.ExitCode)"
        exit 1
    }
} catch {
    Write-Error "Failed to build installer: $_"
    exit 1
}
