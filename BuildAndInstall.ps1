param(
    [string]$Configuration = "Release",
    [string]$PlaynitePath = "C:\Playnite"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $projectRoot "MetaDataIAPlugin.sln"
$buildOutput = Join-Path $projectRoot "bin\$Configuration"
$target = Join-Path $PlaynitePath "Extensions\MetaDataIAPlugin"
$msbuild = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"

if (-not (Test-Path -LiteralPath $msbuild)) {
    throw "MSBuild de .NET Framework no encontrado: $msbuild"
}

& $msbuild $solution /p:Configuration=$Configuration /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "La compilacion fallo."
}

if ($target -notlike (Join-Path $PlaynitePath "Extensions\*")) {
    throw "Ruta de instalacion inesperada: $target"
}

$runningPlaynite = Get-Process Playnite.DesktopApp,Playnite.FullscreenApp -ErrorAction SilentlyContinue
if ($runningPlaynite) {
    throw "Cierra Playnite antes de instalar. La DLL del plugin esta cargada por Playnite y Windows no permite reemplazarla."
}

if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Recurse -Force
}

New-Item -ItemType Directory -Path $target | Out-Null
Copy-Item -LiteralPath (Join-Path $buildOutput "MetaDataIAPlugin.dll") -Destination $target
Copy-Item -LiteralPath (Join-Path $buildOutput "MetaDataIAPlugin.pdb") -Destination $target
Copy-Item -LiteralPath (Join-Path $buildOutput "extension.yaml") -Destination $target
Copy-Item -LiteralPath (Join-Path $buildOutput "media") -Destination $target -Recurse
Copy-Item -LiteralPath (Join-Path $buildOutput "Localization") -Destination $target -Recurse

Write-Host "Metadata AI instalado en $target"
