<#
.SYNOPSIS
    Ecliptix Application Data Cleanup Tool for Windows

.DESCRIPTION
    Removes all persisted Ecliptix application data including:
    - DataProtection encryption keys
    - Secure storage (settings, membership, master keys)
    - Application logs

    Use this tool to perform a fresh initialization of the application.

.PARAMETER DryRun
    Show what would be deleted without actually deleting

.PARAMETER KeepLogs
    Keep log files (only remove keys and settings)

.PARAMETER Force
    Skip confirmation prompt

.EXAMPLE
    .\cleanup-app-data.ps1
    Interactive cleanup with confirmation

.EXAMPLE
    .\cleanup-app-data.ps1 -DryRun
    Preview what will be deleted

.EXAMPLE
    .\cleanup-app-data.ps1 -Force
    Delete without confirmation

.EXAMPLE
    .\cleanup-app-data.ps1 -KeepLogs
    Delete keys/settings but keep logs
#>

[CmdletBinding()]
param(
    [Parameter(HelpMessage = "Show what would be deleted without actually deleting")]
    [switch]$DryRun,

    [Parameter(HelpMessage = "Keep log files (only remove keys and settings)")]
    [switch]$KeepLogs,

    [Parameter(HelpMessage = "Skip confirmation prompt")]
    [switch]$Force
)

function Write-Banner {
    Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║         Ecliptix Application Data Cleanup Tool            ║" -ForegroundColor Cyan
    Write-Host "║                                                            ║" -ForegroundColor Cyan
    Write-Host "║  Removes all persisted application data for fresh init    ║" -ForegroundColor Cyan
    Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host
}

function Get-AppDataDirectory {
    $appDataPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
    return Join-Path $appDataPath "Ecliptix"
}

function Get-DirectorySize {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return "0 B"
    }

    try {
        $totalBytes = (Get-ChildItem $Path -Recurse -File -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum

        if ($totalBytes -eq 0) { return "0 B" }
        if ($totalBytes -lt 1KB) { return "{0:N2} B" -f $totalBytes }
        if ($totalBytes -lt 1MB) { return "{0:N2} KB" -f ($totalBytes / 1KB) }
        if ($totalBytes -lt 1GB) { return "{0:N2} MB" -f ($totalBytes / 1MB) }
        return "{0:N2} GB" -f ($totalBytes / 1GB)
    }
    catch {
        return "Unknown"
    }
}

function Get-FileCount {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return 0
    }

    try {
        return (Get-ChildItem $Path -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
    }
    catch {
        return 0
    }
}

function Write-DirectoryInfo {
    param(
        [string]$Path,
        [string]$Label
    )

    if (Test-Path $Path) {
        $size = Get-DirectorySize -Path $Path
        $fileCount = Get-FileCount -Path $Path

        Write-Host "  ✓ $Label" -ForegroundColor Green
        Write-Host "    Path: $Path" -ForegroundColor Gray
        Write-Host "    Size: $size ($fileCount files)" -ForegroundColor Gray
    }
    else {
        Write-Host "  ✗ $Label (not found)" -ForegroundColor DarkGray
    }
}

function Test-DataExists {
    param([string]$AppData)

    return (Test-Path "$AppData\Storage\DataProtection-Keys") -or
           (Test-Path "$AppData\Storage\state") -or
           (Test-Path "$AppData\Storage\logs") -or
           (Test-Path "$AppData\.keychain") -or
           (Get-ChildItem "$AppData\master_*.ecliptix" -ErrorAction SilentlyContinue) -or
           (Get-ChildItem "$AppData\*.ecliptix" -ErrorAction SilentlyContinue)
}

function Remove-DirectorySafe {
    param(
        [string]$Path,
        [bool]$IsDryRun
    )

    if (-not (Test-Path $Path)) {
        return $true
    }

    if ($IsDryRun) {
        Write-Host "  [DRY-RUN] Would delete: $Path" -ForegroundColor Yellow
        return $true
    }
    else {
        Write-Host "  Deleting: $Path" -ForegroundColor White
        try {
            Remove-Item -Path $Path -Recurse -Force -ErrorAction Stop
            Write-Host "    ✓ Deleted successfully" -ForegroundColor Green
            return $true
        }
        catch {
            Write-Host "    ✗ Failed to delete: $_" -ForegroundColor Red
            return $false
        }
    }
}

# Main script execution
try {
    Write-Banner

    $appData = Get-AppDataDirectory
    Write-Host "Platform: Windows" -ForegroundColor Cyan
    Write-Host "Application data directory: $appData" -ForegroundColor Cyan
    Write-Host

    if (-not (Test-DataExists -AppData $appData)) {
        Write-Host "✓ No application data found. Nothing to clean up." -ForegroundColor Green
        exit 0
    }

    Write-Host "Found application data:" -ForegroundColor Yellow
    Write-Host
    Write-DirectoryInfo -Path "$appData\Storage\DataProtection-Keys" -Label "DataProtection Keys"
    Write-DirectoryInfo -Path "$appData\Storage\state" -Label "Secure Storage (settings, keys, membership)"
    Write-DirectoryInfo -Path "$appData\.keychain" -Label "Keychain (encryption keys, machine key)"

    $masterCount = (Get-ChildItem "$appData\master_*.ecliptix" -ErrorAction SilentlyContinue | Measure-Object).Count
    $ecliptixCount = (Get-ChildItem "$appData\*.ecliptix" -ErrorAction SilentlyContinue | Measure-Object).Count

    if ($masterCount -gt 0) {
        Write-Host "  ✓ Master Key Files" -ForegroundColor Green
        Write-Host "    Path: $appData\master_*.ecliptix" -ForegroundColor Gray
        Write-Host "    Count: $masterCount files" -ForegroundColor Gray
    }
    else {
        Write-Host "  ✗ Master Key Files (not found)" -ForegroundColor DarkGray
    }

    if ($ecliptixCount -gt $masterCount) {
        $otherCount = $ecliptixCount - $masterCount
        Write-Host "  ✓ Other Ecliptix Files" -ForegroundColor Green
        Write-Host "    Path: $appData\*.ecliptix" -ForegroundColor Gray
        Write-Host "    Count: $otherCount files" -ForegroundColor Gray
    }

    Write-DirectoryInfo -Path "$appData\Storage\logs" -Label "Application Logs"
    Write-Host

    if ($DryRun) {
        Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host "DRY-RUN MODE: No files will be deleted" -ForegroundColor Yellow
        Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host
    }

    if (-not $Force -and -not $DryRun) {
        Write-Host "⚠️  WARNING: This will permanently delete all application data!" -ForegroundColor Red
        Write-Host "   This includes:" -ForegroundColor Yellow
        Write-Host "   - Encryption keys" -ForegroundColor Yellow
        Write-Host "   - User settings and preferences" -ForegroundColor Yellow
        Write-Host "   - Membership information" -ForegroundColor Yellow
        Write-Host "   - Device identifiers" -ForegroundColor Yellow
        if (-not $KeepLogs) {
            Write-Host "   - Application logs" -ForegroundColor Yellow
        }
        Write-Host

        $confirmation = Read-Host "Are you sure you want to continue? (yes/no)"
        if ($confirmation -ne "yes") {
            Write-Host "Cleanup cancelled." -ForegroundColor Yellow
            exit 0
        }
    }

    Write-Host "Starting cleanup..." -ForegroundColor Cyan
    Write-Host

    $errors = 0

    if (-not (Remove-DirectorySafe -Path "$appData\Storage\DataProtection-Keys" -IsDryRun $DryRun)) {
        $errors++
    }

    if (-not (Remove-DirectorySafe -Path "$appData\Storage\state" -IsDryRun $DryRun)) {
        $errors++
    }

    if (-not (Remove-DirectorySafe -Path "$appData\.keychain" -IsDryRun $DryRun)) {
        $errors++
    }

    # Remove master key files
    $masterFiles = Get-ChildItem "$appData\master_*.ecliptix" -ErrorAction SilentlyContinue
    if ($masterFiles) {
        if ($DryRun) {
            Write-Host "  [DRY-RUN] Would delete: $appData\master_*.ecliptix" -ForegroundColor Yellow
        }
        else {
            Write-Host "  Deleting: $appData\master_*.ecliptix" -ForegroundColor White
            try {
                Remove-Item "$appData\master_*.ecliptix" -Force -ErrorAction Stop
                Write-Host "    ✓ Deleted successfully" -ForegroundColor Green
            }
            catch {
                Write-Host "    ✗ Failed to delete: $_" -ForegroundColor Red
                $errors++
            }
        }
    }

    # Remove numbered ecliptix files (but not master_*.ecliptix)
    $ecliptixFiles = Get-ChildItem "$appData\[0-9]*.ecliptix" -ErrorAction SilentlyContinue
    if ($ecliptixFiles) {
        if ($DryRun) {
            Write-Host "  [DRY-RUN] Would delete: $appData\*.ecliptix (numbered files)" -ForegroundColor Yellow
        }
        else {
            Write-Host "  Deleting: $appData\*.ecliptix (numbered files)" -ForegroundColor White
            try {
                Remove-Item "$appData\[0-9]*.ecliptix" -Force -ErrorAction Stop
                Write-Host "    ✓ Deleted successfully" -ForegroundColor Green
            }
            catch {
                Write-Host "    ✗ Failed to delete: $_" -ForegroundColor Red
                $errors++
            }
        }
    }

    if (-not $KeepLogs) {
        if (-not (Remove-DirectorySafe -Path "$appData\Storage\logs" -IsDryRun $DryRun)) {
            $errors++
        }
    }
    else {
        Write-Host "  [SKIPPED] Keeping logs: $appData\Storage\logs" -ForegroundColor Yellow
    }

    # Clean up empty Storage directory
    if (Test-Path "$appData\Storage") {
        $remaining = (Get-ChildItem "$appData\Storage" -ErrorAction SilentlyContinue | Measure-Object).Count
        if ($remaining -eq 0 -and -not $DryRun) {
            Write-Host
            Write-Host "  Removing empty Storage directory" -ForegroundColor Gray
            Remove-Item "$appData\Storage" -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "✓ Dry-run completed. No files were deleted." -ForegroundColor Green
    }
    elseif ($errors -eq 0) {
        Write-Host "✓ Cleanup completed successfully!" -ForegroundColor Green
        Write-Host
        Write-Host "The application will initialize with fresh settings on next launch." -ForegroundColor Cyan
    }
    else {
        Write-Host "⚠️  Cleanup completed with $errors error(s)." -ForegroundColor Yellow
        exit 1
    }
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
}
catch {
    Write-Host
    Write-Host "ERROR: An unexpected error occurred during cleanup:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    exit 1
}
