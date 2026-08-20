param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$ToolboxPath = "C:\Playnite\Toolbox.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$msbuild = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
$solution = Join-Path $root "MetaDataIAPlugin.sln"
$extensionYaml = Join-Path $root "extension.yaml"

if (-not (Test-Path -LiteralPath $msbuild)) {
    throw "MSBuild was not found at $msbuild"
}

if (-not (Test-Path -LiteralPath $ToolboxPath)) {
    throw "Playnite Toolbox was not found at $ToolboxPath"
}

$manifestVersion = (
    Select-String -LiteralPath $extensionYaml -Pattern '^\s*Version:\s*(.+)\s*$' |
        Select-Object -First 1
).Matches[0].Groups[1].Value.Trim()
if ([string]::IsNullOrWhiteSpace($manifestVersion)) {
    throw "Could not read Version from extension.yaml"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $manifestVersion
}
elseif (-not [string]::Equals($Version, $manifestVersion, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Version '$Version' does not match extension.yaml ($manifestVersion). Update extension.yaml first."
}

Write-Host "Building Metadata AI $Version ($Configuration)..."
& $msbuild $solution /p:Configuration=$Configuration /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$build = Join-Path $root "bin\$Configuration"
$required = @(
    (Join-Path $build "MetaDataIAPlugin.dll"),
    (Join-Path $build "extension.yaml"),
    (Join-Path $build "XamlAnimatedGif.dll"),
    (Join-Path $build "media"),
    (Join-Path $build "Localization"),
    (Join-Path $build "Icons")
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing build output: $path"
    }
}

# Pack from a clean TEMP stage. Never leave a stage folder inside dist/.
$stage = Join-Path $env:TEMP "mtda-pext-stage"
$dist = Join-Path $root "dist"
$distVersion = Join-Path $dist $Version
if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage | Out-Null
if (-not (Test-Path -LiteralPath $dist)) {
    New-Item -ItemType Directory -Path $dist | Out-Null
}
# Remove legacy flat packages at dist root; keep other version folders.
Get-ChildItem -LiteralPath $dist -File -Filter '*.pext' -ErrorAction SilentlyContinue |
    Remove-Item -Force
if (Test-Path -LiteralPath $distVersion) {
    Remove-Item -LiteralPath $distVersion -Recurse -Force
}
New-Item -ItemType Directory -Path $distVersion | Out-Null

Copy-Item -LiteralPath (Join-Path $build "MetaDataIAPlugin.dll") -Destination $stage
Copy-Item -LiteralPath (Join-Path $build "extension.yaml") -Destination $stage
Copy-Item -LiteralPath (Join-Path $build "XamlAnimatedGif.dll") -Destination $stage
Copy-Item -LiteralPath (Join-Path $build "media") -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $build "Localization") -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $build "Icons") -Destination $stage -Recurse

& $ToolboxPath pack $stage $distVersion
$packExit = $LASTEXITCODE
Remove-Item -LiteralPath $stage -Recurse -Force
if ($packExit -ne 0) {
    throw "Playnite Toolbox pack failed with exit code $packExit"
}

$package = Get-ChildItem -LiteralPath $distVersion -Filter '*.pext' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $package) {
    throw "Playnite Toolbox did not create a .pext package."
}

Write-Host "Package created: $($package.FullName)"
Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256
