param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
$serviceCs = Join-Path $root "SeriesTagConsistencyService.cs"
$stubsCs = Join-Path $root "tests\SeriesTagConsistencyStubs.cs"
$runnerCs = Join-Path $root "tests\SeriesTagConsistencyRunner.cs"
$outDir = Join-Path $root "tests\bin"
$outExe = Join-Path $outDir "SeriesTagConsistencyRunner.exe"

if (-not (Test-Path -LiteralPath $csc)) { throw "csc.exe not found at $csc" }
if (-not (Test-Path -LiteralPath $serviceCs)) { throw "SeriesTagConsistencyService.cs not found at $serviceCs" }
if (-not (Test-Path -LiteralPath $stubsCs)) { throw "SeriesTagConsistencyStubs.cs not found at $stubsCs" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host "Compiling series tag consistency runner..."
& $csc /nologo /target:exe /platform:anycpu /out:"$outExe" `
    /r:System.dll /r:System.Core.dll `
    "$stubsCs" "$serviceCs" "$runnerCs"
if ($LASTEXITCODE -ne 0) { throw "Test compile failed with exit code $LASTEXITCODE" }

Write-Host "Running series tag consistency tests..."
& $outExe
exit $LASTEXITCODE
