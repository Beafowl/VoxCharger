# Quick build + convert a test chart
param(
    [string]$KSH,
    [string]$GamePath,
    [string]$Mix = "asphyxia_custom",
    [int]$Id = 99999,
    [string]$Code = "test_chart"
)

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$MSBuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
$Exe = Join-Path $Root "Build\Release\VoxCharger.exe"

Write-Host "Building VoxCharger..." -ForegroundColor Yellow
& $MSBuild (Join-Path $Root "VoxCharger.sln") -p:Configuration=Release -verbosity:minimal
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed!" -ForegroundColor Red; exit 1 }
Write-Host "Build OK" -ForegroundColor Green

if ($KSH -and $GamePath) {
    Write-Host "Converting: $KSH" -ForegroundColor Yellow
    & $Exe --full-import $KSH --game-path $GamePath --mix $Mix --music-id $Id --music-code $Code
    if ($LASTEXITCODE -eq 0) { Write-Host "Done!" -ForegroundColor Green }
    else { Write-Host "Conversion failed!" -ForegroundColor Red }
} else {
    Write-Host "Build only (pass -KSH and -GamePath to also convert)" -ForegroundColor Gray
}
