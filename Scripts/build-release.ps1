# Ecliptix Desktop Release Build Script (PowerShell)
# This script builds release versions for all supported platforms

param(
    [string]$Increment,
    [string]$Version,
    [string]$Platforms = "all",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$DesktopProject = Join-Path $ProjectRoot "Ecliptix.Core/Ecliptix.Core.Desktop/Ecliptix.Core.Desktop.csproj"
$OutputDir = Join-Path $ProjectRoot "builds"

Write-Host "Ecliptix Desktop Release Build" -ForegroundColor Blue
Write-Host "==================================" -ForegroundColor Blue

# Show help
if ($args -contains "--help" -or $args -contains "-h") {
    Write-Host "Usage: .\build-release.ps1 [options]"
    Write-Host ""
    Write-Host "Version Options:"
    Write-Host "  -Increment <part>     Increment version part (major|minor|patch)"
    Write-Host "  -Version <version>    Set specific version (e.g., 1.2.3)"
    Write-Host ""
    Write-Host "Build Options:"
    Write-Host "  -Platforms <list>     Comma-separated platform list (win,mac,linux) or 'all'"
    Write-Host "  -Clean               Clean build artifacts before building"
    Write-Host ""
    Write-Host "Examples:"
    Write-Host "  .\build-release.ps1 -Increment patch -Platforms all"
    Write-Host "  .\build-release.ps1 -Version 1.0.0 -Platforms win,mac -Clean"
    exit 0
}

# Update version if requested
if ($Increment) {
    Write-Host "Updating version..." -ForegroundColor Yellow
    python (Join-Path $ScriptDir "version-helper.py") --action increment --part $Increment
}
elseif ($Version) {
    Write-Host "Setting version to $Version..." -ForegroundColor Yellow
    python (Join-Path $ScriptDir "version-helper.py") --action set --version $Version
}

# Generate build info
Write-Host "Generating build information..." -ForegroundColor Yellow
python (Join-Path $ScriptDir "version-helper.py") --action build

# Get current version
$VersionOutput = python (Join-Path $ScriptDir "version-helper.py") --action current
$CurrentVersion = ($VersionOutput | Select-String "Current version: (.+)").Matches[0].Groups[1].Value
$BuildNumber = Get-Date -Format "yyMMdd.HHmm"
$BuildVersion = "${CurrentVersion}-build.${BuildNumber}"

Write-Host "Building version: $BuildVersion" -ForegroundColor Green

# Clean build artifacts if requested
if ($Clean) {
    Write-Host "Cleaning build artifacts..." -ForegroundColor Yellow
    dotnet clean $ProjectRoot -c Release
    if (Test-Path $OutputDir) {
        Remove-Item $OutputDir -Recurse -Force
    }
}

# Create output directory
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Define platform configurations
$PlatformMap = @{
    "win" = "win-x64"
    "mac-intel" = "osx-x64"
    "mac-arm" = "osx-arm64"
    "linux" = "linux-x64"
}

# Determine which platforms to build
$BuildList = @()
if ($Platforms -eq "all") {
    $BuildList = @("win", "mac-intel", "mac-arm", "linux")
} else {
    $BuildList = $Platforms.Split(',')
    foreach ($platform in $BuildList) {
        if (-not $PlatformMap.ContainsKey($platform.Trim())) {
            Write-Warning "Unknown platform '$platform'. Supported: win, mac-intel, mac-arm, linux"
        }
    }
}

# Build each platform
foreach ($platform in $BuildList) {
    $platform = $platform.Trim()
    if (-not $PlatformMap.ContainsKey($platform)) {
        continue
    }
    
    $runtime = $PlatformMap[$platform]
    $platformOutput = Join-Path $OutputDir "ecliptix-$platform-$CurrentVersion"
    
    Write-Host "Building for $platform ($runtime)..." -ForegroundColor Blue
    
    # Build command
    $buildArgs = @(
        "publish", $DesktopProject,
        "-c", "Release",
        "-r", $runtime,
        "--self-contained", "true",
        "-p:PublishAot=false",
        "-p:TrimMode=link", 
        "-p:PublishTrimmed=false",
        "-p:PublishSingleFile=false",
        "-p:BuildNumber=$BuildNumber",
        "-o", $platformOutput,
        "--verbosity", "minimal"
    )
    
    & dotnet @buildArgs
    
    if ($LASTEXITCODE -eq 0) {
        # Copy build info to output
        $buildInfoSource = Join-Path $ProjectRoot "build-info.json"
        if (Test-Path $buildInfoSource) {
            Copy-Item $buildInfoSource $platformOutput
        }
        
        # Create archive
        Write-Host "Creating archive for $platform..." -ForegroundColor Yellow
        $archiveName = "ecliptix-$platform-$CurrentVersion"
        
        Push-Location $OutputDir
        if ($platform -eq "win") {
            Compress-Archive -Path $archiveName -DestinationPath "$archiveName.zip" -Force
        } else {
            # Use tar for other platforms (requires WSL or tar.exe in PATH)
            & tar -czf "$archiveName.tar.gz" $archiveName
        }
        Pop-Location
        
        Write-Host "✓ $platform build completed" -ForegroundColor Green
    } else {
        Write-Host "✗ $platform build failed" -ForegroundColor Red
    }
}

Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "Build artifacts are available in: $OutputDir" -ForegroundColor Blue

# Show build summary
Write-Host ""
Write-Host "Build Summary:" -ForegroundColor Blue
Write-Host "Version: $BuildVersion"
Write-Host "Platforms built: $($BuildList -join ', ')"
Write-Host "Output directory: $OutputDir"

# List created files
Write-Host ""
Write-Host "Created files:" -ForegroundColor Blue
Get-ChildItem $OutputDir -Filter "*.zip" -ErrorAction SilentlyContinue | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  $($_.Name) ($size MB)"
}
Get-ChildItem $OutputDir -Filter "*.tar.gz" -ErrorAction SilentlyContinue | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  $($_.Name) ($size MB)"
}