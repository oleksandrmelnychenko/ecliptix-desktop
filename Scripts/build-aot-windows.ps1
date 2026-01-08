
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",

    [ValidateSet("major", "minor", "patch")]
    [string]$Increment,

    [switch]$SkipRestore,
    [switch]$SkipTests,
    [switch]$Clean,

    [ValidateSet("size", "speed", "aggressive")]
    [string]$Optimization = "aggressive",

    [switch]$Help
)

$ErrorActionPreference = "Stop"

function Write-Info { param([string]$Message) Write-Host "🔵 [AOT-INFO] $Message" -ForegroundColor Blue }
function Write-Success { param([string]$Message) Write-Host "✅ [AOT-SUCCESS] $Message" -ForegroundColor Green }
function Write-Warning { param([string]$Message) Write-Host "⚠️ [AOT-WARNING] $Message" -ForegroundColor Yellow }
function Write-Error { param([string]$Message) Write-Host "❌ [AOT-ERROR] $Message" -ForegroundColor Red }

if ($Help) {
    Write-Host "AOT Build Script for Ecliptix Desktop Windows" -ForegroundColor Blue
    Write-Host ""
    Write-Host "Usage: .\build-aot-windows.ps1 [OPTIONS]" -ForegroundColor White
    Write-Host ""
    Write-Host "Options:" -ForegroundColor White
    Write-Host "  -Configuration      Build configuration (Debug/Release, default: Release)" -ForegroundColor Gray
    Write-Host "  -Runtime           Runtime identifier (win-x64/win-arm64, default: win-x64)" -ForegroundColor Gray
    Write-Host "  -Increment         Increment version (major/minor/patch)" -ForegroundColor Gray
    Write-Host "  -SkipRestore       Skip package restore" -ForegroundColor Gray
    Write-Host "  -SkipTests         Skip running tests" -ForegroundColor Gray
    Write-Host "  -Clean             Clean build artifacts before building" -ForegroundColor Gray
    Write-Host "  -Optimization      Optimization level (size/speed/aggressive, default: aggressive)" -ForegroundColor Gray
    Write-Host "  -Help              Show this help message" -ForegroundColor Gray
    Write-Host ""
    Write-Host "AOT Features:" -ForegroundColor Yellow
    Write-Host "  • Native code generation for maximum performance" -ForegroundColor Gray
    Write-Host "  • IL trimming to reduce binary size" -ForegroundColor Gray
    Write-Host "  • ReadyToRun image generation" -ForegroundColor Gray
    Write-Host "  • Assembly trimming and dead code elimination" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Examples:" -ForegroundColor White
    Write-Host "  .\build-aot-windows.ps1                           # AOT build for x64" -ForegroundColor Gray
    Write-Host "  .\build-aot-windows.ps1 -Runtime win-arm64 -Clean # Clean AOT build for ARM64" -ForegroundColor Gray
    Write-Host "  .\build-aot-windows.ps1 -Optimization size        # Size-optimized AOT build" -ForegroundColor Gray
    Write-Host "  .\build-aot-windows.ps1 -Increment patch          # Increment version and AOT build" -ForegroundColor Gray
    exit 0
}

Write-Host "🚀 Building Ecliptix Desktop for Windows with AOT compilation..." -ForegroundColor Cyan

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$DesktopProject = Join-Path $ProjectRoot "Ecliptix.Core/Ecliptix.Core.Desktop/Ecliptix.Core.Desktop.csproj"

if ($env:OS -ne "Windows_NT") {
    Write-Error "This script is designed for Windows only"
    exit 1
}

try {
    $dotnetVersion = dotnet --version
    $majorVersion = [int]($dotnetVersion.Split('.')[0])
    if ($majorVersion -lt 8) {
        Write-Error ".NET 8 or higher is required for AOT compilation (found: $dotnetVersion)"
        exit 1
    }
    Write-Info ".NET Version: $dotnetVersion"
} catch {
    Write-Error ".NET SDK is required but not found"
    Write-Error "Please install .NET from: https://dotnet.microsoft.com/download"
    exit 1
}

if (-not (Test-Path $DesktopProject)) {
    Write-Error "Desktop project not found at: $DesktopProject"
    exit 1
}

Write-Info "AOT Build Configuration:"
Write-Info "  • Configuration: $Configuration"
Write-Info "  • Runtime ID: $Runtime"
Write-Info "  • Optimization: $Optimization"

switch ($Optimization) {
    "size" {
        $TrimMode = "link"
        $ILLinkMode = "copyused"
        $AOTMode = "partial"
    }
    "speed" {
        $TrimMode = "copyused"
        $ILLinkMode = "copyused"
        $AOTMode = "full"
    }
    "aggressive" {
        $TrimMode = "link"
        $ILLinkMode = "link"
        $AOTMode = "full"
    }
    default {
        Write-Error "Unknown optimization level: $Optimization"
        Write-Error "Supported levels: size, speed, aggressive"
        exit 1
    }
}

Write-Info "AOT Optimization settings:"
Write-Info "  • Trim Mode: $TrimMode"
Write-Info "  • IL Link Mode: $ILLinkMode"
Write-Info "  • AOT Mode: $AOTMode"

if ($Increment) {
    Write-Info "Incrementing $Increment version..."
    $versionScript = Join-Path $ScriptDir "version.sh"
    try {
        & bash $versionScript --action increment --part $Increment
        $newVersionOutput = & bash $versionScript --action current
        $newVersion = ($newVersionOutput | Select-String "Current version: (.+)").Matches[0].Groups[1].Value
        Write-Success "Version incremented to: $newVersion"
    } catch {
        Write-Error "Failed to increment version: $_"
        exit 1
    }
}

Write-Info "Generating build information..."
try {
    $versionScript = Join-Path $ScriptDir "version.sh"
    & bash $versionScript --action build
    Write-Success "Build information generated"
} catch {
    Write-Warning "Could not generate build information (continuing anyway)"
}

Set-Location $ProjectRoot

if ($Clean) {
    Write-Info "Cleaning previous builds..."
    dotnet clean $DesktopProject -c $Configuration --verbosity minimal
    if (Test-Path (Join-Path $ProjectRoot "build")) { Remove-Item (Join-Path $ProjectRoot "build") -Recurse -Force }
    if (Test-Path (Join-Path $ProjectRoot "publish")) { Remove-Item (Join-Path $ProjectRoot "publish") -Recurse -Force }
    Write-Success "Build artifacts cleaned"
}

if (-not $SkipRestore) {
    Write-Info "Restoring NuGet packages for AOT..."
    dotnet restore $DesktopProject --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to restore packages"
        exit 1
    }
    Write-Success "Packages restored successfully"
}

if (-not $SkipTests) {
    Write-Info "Running tests..."
    dotnet test --verbosity minimal --nologo
    if ($LASTEXITCODE -eq 0) {
        Write-Success "All tests passed"
    } else {
        Write-Warning "Some tests failed, but continuing with build..."
    }
}

try {
    $versionScript = Join-Path $ScriptDir "version.sh"
    $currentVersionOutput = & bash $versionScript --action current
    $currentVersion = ($currentVersionOutput | Select-String "Current version: (.+)").Matches[0].Groups[1].Value
    if (-not $currentVersion) { $currentVersion = "1.0.0" }
} catch {
    $currentVersion = "1.0.0"
}

$cleanVersion = $currentVersion -replace '-build.*', '' -replace '^v', ''
if ($cleanVersion -notmatch '^\d+\.\d+\.\d+$') {
    $cleanVersion = "1.0.0"
}
$buildNumber = Get-Date -Format "HHmm"

Write-Info "Building application with AOT compilation..."
Write-Info "This may take several minutes for native code generation..."

$buildOutputDir = Join-Path $ProjectRoot "publish/$Runtime"

$buildArgs = @(
    "publish", $DesktopProject,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "--output", $buildOutputDir,
    "--verbosity", "minimal",
    "-p:PublishAot=true",
    "-p:TrimMode=$TrimMode",
    "-p:PublishTrimmed=true",
    "-p:PublishSingleFile=false",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:StripSymbols=true",
    "-p:OptimizationPreference=Speed",
    "-p:IlcOptimizationPreference=Speed",
    "-p:IlcFoldIdenticalMethodBodies=true"
)

& dotnet @buildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "AOT build failed"
    exit 1
}

Write-Success "AOT compilation completed successfully"
Write-Success "Output directory: $buildOutputDir"

$appName = "Ecliptix"
$executableName = "Ecliptix.exe"
$executablePath = Join-Path $buildOutputDir $executableName

if (-not (Test-Path $executablePath)) {

    $executableName = "Ecliptix"
    $executablePath = Join-Path $buildOutputDir $executableName
    if (-not (Test-Path $executablePath)) {
        Write-Error "AOT executable not found in output directory"
        exit 1
    }
}

Write-Info "Creating Windows distribution package..."

$appDir = Join-Path $buildOutputDir $appName
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

Move-Item $executablePath (Join-Path $appDir $executableName)
Get-ChildItem $buildOutputDir -Filter "*.dll" | Move-Item -Destination $appDir
Get-ChildItem $buildOutputDir -Filter "*.json" | Move-Item -Destination $appDir
Get-ChildItem $buildOutputDir -Filter "*.pdb" -ErrorAction SilentlyContinue | Move-Item -Destination $appDir

$iconSource = Join-Path $ProjectRoot "Ecliptix.Core/Ecliptix.Core/Assets/EcliptixLogo.ico"
if (Test-Path $iconSource) {
    Copy-Item $iconSource (Join-Path $appDir "AppIcon.ico")
    Write-Success "Icon copied to distribution package"
} else {
    Write-Warning "Icon file not found at $iconSource, package will use default icon"
}

$versionInfo = @{
    version = $cleanVersion
    build_number = $buildNumber
    full_version = "$cleanVersion-build.$buildNumber"
    timestamp = (Get-Date).ToString("o")
    runtime = $Runtime
    configuration = $Configuration
    optimization = $Optimization
} | ConvertTo-Json -Depth 2

$versionInfo | Out-File -FilePath (Join-Path $appDir "version.json") -Encoding UTF8

$appDirSize = (Get-ChildItem $appDir -Recurse | Measure-Object -Property Length -Sum).Sum
$executableSize = (Get-Item (Join-Path $appDir $executableName)).Length
$appDirSizeMB = [Math]::Round($appDirSize / 1MB, 2)
$executableSizeMB = [Math]::Round($executableSize / 1MB, 2)

Write-Info "Package size: $appDirSizeMB MB"
Write-Info "Executable size: $executableSizeMB MB"

Write-Info "Creating distributable archive..."
$archiveName = "Ecliptix-$cleanVersion-$Runtime-AOT.zip"
$archivePath = Join-Path $buildOutputDir $archiveName

Compress-Archive -Path $appDir -DestinationPath $archivePath -Force

if (Test-Path $archivePath) {
    $archiveSize = (Get-Item $archivePath).Length
    $archiveSizeMB = [Math]::Round($archiveSize / 1MB, 2)
    Write-Success "Archive created: $archiveName ($archiveSizeMB MB)"
}

Write-Host ""
Write-Success "🎉 Windows AOT Build completed successfully!"
Write-Host ""
Write-Host "📦 AOT Build Summary:" -ForegroundColor Cyan
Write-Host "   Version: $cleanVersion" -ForegroundColor White
Write-Host "   Build Number: $buildNumber" -ForegroundColor White
Write-Host "   Configuration: $Configuration" -ForegroundColor White
Write-Host "   Runtime: $Runtime" -ForegroundColor White
Write-Host "   Optimization: $Optimization" -ForegroundColor White
Write-Host "   Trim Mode: $TrimMode" -ForegroundColor White
Write-Host "   Package Directory: $appDir" -ForegroundColor White
Write-Host "   Package Size: $appDirSizeMB MB" -ForegroundColor White
Write-Host "   Executable Size: $executableSizeMB MB" -ForegroundColor White
Write-Host ""
Write-Host "🚀 AOT Benefits:" -ForegroundColor Green
Write-Host "   ✓ Native machine code compilation" -ForegroundColor Gray
Write-Host "   ✓ Faster startup time" -ForegroundColor Gray
Write-Host "   ✓ Reduced memory footprint" -ForegroundColor Gray
Write-Host "   ✓ Self-contained deployment" -ForegroundColor Gray
Write-Host "   ✓ IL trimming and dead code elimination" -ForegroundColor Gray
Write-Host ""
Write-Host "🧪 To test the AOT application:" -ForegroundColor Yellow
Write-Host "   Start-Process '$appDir\$executableName'" -ForegroundColor Gray
Write-Host ""
Write-Host "📋 Next steps for distribution:" -ForegroundColor Blue
Write-Host "   1. Test thoroughly: Start-Process '$appDir\$executableName'" -ForegroundColor Gray
Write-Host "   2. Sign for distribution: signtool sign /f cert.pfx '$appDir\$executableName'" -ForegroundColor Gray
Write-Host "   3. Create installer: Use Inno Setup, NSIS, or WiX toolset" -ForegroundColor Gray
Write-Host "   4. Distribute: Upload '$archivePath' or installer" -ForegroundColor Gray
Write-Host ""