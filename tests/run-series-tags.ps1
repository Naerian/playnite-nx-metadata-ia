param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
$pluginDll = Join-Path $root "bin\$Configuration\MetaDataIAPlugin.dll"
$sdkDll = "C:\Playnite\Playnite.SDK.dll"
$jsonDll = "C:\Playnite\Newtonsoft.Json.dll"
$runnerCs = Join-Path $root "tests\SeriesTagConsistencyRunner.cs"
$outDir = Join-Path $root "tests\bin"
$outExe = Join-Path $outDir "SeriesTagConsistencyRunner.exe"

if (-not (Test-Path -LiteralPath $csc)) { throw "csc.exe not found at $csc" }
if (-not (Test-Path -LiteralPath $pluginDll)) { throw "Plugin DLL not found at $pluginDll. Build the plugin first." }
if (-not (Test-Path -LiteralPath $sdkDll)) { throw "Playnite.SDK.dll not found at $sdkDll" }
if (-not (Test-Path -LiteralPath $jsonDll)) { throw "Newtonsoft.Json.dll not found at $jsonDll" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host "Compiling series tag consistency runner..."
& $csc /nologo /target:exe /platform:anycpu /out:"$outExe" `
    /r:"$pluginDll" /r:"$sdkDll" /r:"$jsonDll" `
    /r:System.dll /r:System.Core.dll `
    "$runnerCs"
if ($LASTEXITCODE -ne 0) { throw "Test compile failed with exit code $LASTEXITCODE" }

Copy-Item -Force $pluginDll (Join-Path $outDir "MetaDataIAPlugin.dll")
Copy-Item -Force $sdkDll (Join-Path $outDir "Playnite.SDK.dll")
Copy-Item -Force $jsonDll (Join-Path $outDir "Newtonsoft.Json.dll")

Write-Host "Running series tag consistency tests..."
& $outExe
exit $LASTEXITCODE
