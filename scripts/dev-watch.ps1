# =============================================================================
# VoxCharger Dev Watch
# Watches for C# source changes, auto-rebuilds, and converts a test chart.
# =============================================================================

# --- SETTINGS ----------------------------------------------------------------
$VoxChargerRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$MSBuild        = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
$Solution       = Join-Path $VoxChargerRoot "VoxCharger.sln"
$ExePath        = Join-Path $VoxChargerRoot "Build\Release\VoxCharger.exe"
$Configuration  = "Release"

# Test chart settings — edit these
$TestKSH        = ""   # Path to a .ksh file to auto-convert after build
$GamePath       = ""   # Game root (e.g. D:\Spiele\KFC\contents)
$MixName        = "asphyxia_custom"
$MusicId        = 10999  # Temp ID for test conversions
$MusicCode      = "test_chart"
# -----------------------------------------------------------------------------

if (-not (Test-Path $MSBuild)) {
    Write-Host "MSBuild not found at: $MSBuild" -ForegroundColor Red
    Write-Host "Edit the `$MSBuild path in this script."
    exit 1
}

$WatchPath = Join-Path $VoxChargerRoot "Sources"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  VoxCharger Dev Watch" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Watching:  $WatchPath"
Write-Host "Solution:  $Solution"
if ($TestKSH) { Write-Host "Test KSH:  $TestKSH" }
else { Write-Host "Test KSH:  (none - build only, set TestKSH to enable)" -ForegroundColor Yellow }
Write-Host ""
Write-Host "Waiting for changes... (Ctrl+C to stop)" -ForegroundColor Gray
Write-Host ""

$building = $false
$lastBuild = [DateTime]::MinValue

function Build-And-Convert {
    # Debounce: skip if last build was less than 2 seconds ago
    $now = Get-Date
    if (($now - $script:lastBuild).TotalSeconds -lt 2) { return }
    if ($script:building) { return }
    $script:building = $true
    $script:lastBuild = $now

    Write-Host "[$($now.ToString('HH:mm:ss'))] Change detected, building..." -ForegroundColor Yellow

    # Build
    $buildOutput = & $MSBuild $Solution -p:Configuration=$Configuration -verbosity:minimal 2>&1
    $buildSuccess = $LASTEXITCODE -eq 0

    if (-not $buildSuccess) {
        Write-Host "  BUILD FAILED" -ForegroundColor Red
        $buildOutput | Where-Object { $_ -match "error" } | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        $script:building = $false
        return
    }

    Write-Host "  Build OK" -ForegroundColor Green

    # Convert test chart if configured
    if ($TestKSH -and $GamePath -and (Test-Path $TestKSH)) {
        Write-Host "  Converting test chart..." -NoNewline

        # Remove old test output
        $testDir = Join-Path $GamePath "data_mods\$MixName\music"
        $oldFolders = Get-ChildItem -Path $testDir -Directory -Filter "${MusicId}_*" -ErrorAction SilentlyContinue
        foreach ($f in $oldFolders) { Remove-Item $f.FullName -Recurse -Force }

        $convertOutput = & $ExePath --full-import $TestKSH --game-path $GamePath --mix $MixName --music-id $MusicId --music-code $MusicCode 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host " OK" -ForegroundColor Green
        } else {
            Write-Host " FAILED" -ForegroundColor Red
            $convertOutput | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        }
    }

    Write-Host ""
    $script:building = $false
}

# Initial build
Build-And-Convert

# Set up file watcher
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $WatchPath
$watcher.Filter = "*.cs"
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true
$watcher.NotifyFilter = [System.IO.NotifyFilters]::LastWrite -bor [System.IO.NotifyFilters]::FileName

$action = { Build-And-Convert }
$onChange = Register-ObjectEvent $watcher "Changed" -Action $action
$onCreate = Register-ObjectEvent $watcher "Created" -Action $action
$onRename = Register-ObjectEvent $watcher "Renamed" -Action $action

try {
    while ($true) { Start-Sleep -Seconds 1 }
} finally {
    Unregister-Event -SourceIdentifier $onChange.Name
    Unregister-Event -SourceIdentifier $onCreate.Name
    Unregister-Event -SourceIdentifier $onRename.Name
    $watcher.Dispose()
}
