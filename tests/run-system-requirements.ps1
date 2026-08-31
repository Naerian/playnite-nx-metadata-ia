$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $root "bin\Release\MetaDataIAPlugin.dll"
if (-not (Test-Path -LiteralPath $dll)) {
    throw "Build the plugin first (bin\Release\MetaDataIAPlugin.dll missing)."
}

$outDir = Join-Path $PSScriptRoot "bin"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
Copy-Item -LiteralPath $dll -Destination $outDir -Force
$locSrc = Join-Path $root "Localization"
$locDst = Join-Path $outDir "Localization"
if (Test-Path -LiteralPath $locSrc) {
    if (Test-Path -LiteralPath $locDst) { Remove-Item -LiteralPath $locDst -Recurse -Force }
    Copy-Item -LiteralPath $locSrc -Destination $locDst -Recurse -Force
}
$playniteSdk = Join-Path $root "libs\Playnite.SDK.dll"
if (-not (Test-Path -LiteralPath $playniteSdk)) {
    $playniteSdk = "C:\Playnite\Playnite.SDK.dll"
}
Copy-Item -LiteralPath $playniteSdk -Destination $outDir -Force -ErrorAction SilentlyContinue
$newtonsoft = Join-Path $root "libs\Newtonsoft.Json.dll"
if (-not (Test-Path -LiteralPath $newtonsoft)) {
    $newtonsoft = Join-Path (Split-Path $dll) "Newtonsoft.Json.dll"
}
if (Test-Path -LiteralPath $newtonsoft) {
    Copy-Item -LiteralPath $newtonsoft -Destination $outDir -Force
}

Write-Host "Compiling system requirements runner..."
$csc = Join-Path ${env:WINDIR} "Microsoft.NET\Framework\v4.0.30319\csc.exe"
$exe = Join-Path $outDir "SystemRequirementsRunner.exe"
$pluginDll = Join-Path $outDir "MetaDataIAPlugin.dll"
$sdkDll = Join-Path $outDir "Playnite.SDK.dll"
$jsonDll = Join-Path $outDir "Newtonsoft.Json.dll"
$src = Join-Path $PSScriptRoot "SystemRequirementsRunner.cs"
& $csc /nologo /target:exe "/out:$exe" "/reference:$pluginDll" "/reference:$sdkDll" "/reference:$jsonDll" $src
if ($LASTEXITCODE -ne 0) { throw "Compile failed" }

Write-Host "Running system requirements tests..."
& $exe
exit $LASTEXITCODE
