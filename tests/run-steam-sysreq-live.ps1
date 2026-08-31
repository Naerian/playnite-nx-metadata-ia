$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$msbuild = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
$solution = Join-Path $root "MetaDataIAPlugin.sln"
Write-Host "Building plugin..."
& $msbuild $solution /p:Configuration=Release /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$dll = Join-Path $root "bin\Release\MetaDataIAPlugin.dll"
$outDir = Join-Path $PSScriptRoot "bin"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
Copy-Item -LiteralPath $dll -Destination $outDir -Force

$playniteSdk = Join-Path $root "libs\Playnite.SDK.dll"
if (-not (Test-Path -LiteralPath $playniteSdk)) { $playniteSdk = "C:\Playnite\Playnite.SDK.dll" }
Copy-Item -LiteralPath $playniteSdk -Destination $outDir -Force -ErrorAction SilentlyContinue
$newtonsoft = Join-Path $root "libs\Newtonsoft.Json.dll"
if (-not (Test-Path -LiteralPath $newtonsoft)) { $newtonsoft = Join-Path (Split-Path $dll) "Newtonsoft.Json.dll" }
if (Test-Path -LiteralPath $newtonsoft) { Copy-Item -LiteralPath $newtonsoft -Destination $outDir -Force }

$csc = Join-Path ${env:WINDIR} "Microsoft.NET\Framework\v4.0.30319\csc.exe"
$exe = Join-Path $outDir "SteamSysReqLiveRunner.exe"
$pluginDll = Join-Path $outDir "MetaDataIAPlugin.dll"
$sdkDll = Join-Path $outDir "Playnite.SDK.dll"
$jsonDll = Join-Path $outDir "Newtonsoft.Json.dll"
$src = Join-Path $PSScriptRoot "SteamSysReqLiveRunner.cs"
Write-Host "Compiling live runner..."
& $csc /nologo /target:exe "/out:$exe" "/reference:$pluginDll" "/reference:$sdkDll" "/reference:$jsonDll" $src
if ($LASTEXITCODE -ne 0) { throw "Compile failed" }

Write-Host "Running live Steam sys-req tests..."
& $exe
exit $LASTEXITCODE
