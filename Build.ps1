[CmdletBinding()]
param(
    [string]$Dotnet,
    [string]$Tools = (Join-Path $PSScriptRoot '.tools'),
    [string]$MediaTools,
    [string]$RestoreConfig,
    [string]$Output = (Join-Path $PSScriptRoot 'artifacts\Viewcut')
)

$ErrorActionPreference = 'Stop'
$Tools = (Resolve-Path -LiteralPath $Tools).Path
if (!$Dotnet) {
    $localSdk = Join-Path $Tools 'dotnet\dotnet.exe'
    $Dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
}

$app = Join-Path $PSScriptRoot 'Viewcut.csproj'
$Output = [IO.Path]::GetFullPath($Output)
if (!$MediaTools) { $MediaTools = Join-Path $Tools 'ffmpeg-lite\bin' }
$dependencies = @((Join-Path $Tools 'mpv\libmpv-2.dll'),
    (Join-Path $MediaTools 'ffmpeg.exe'), (Join-Path $MediaTools 'ffprobe.exe'))
foreach ($dependency in $dependencies) {
    if (!(Test-Path -LiteralPath $dependency)) {
        throw "Missing $dependency. Build the compact tools with media/Build-FFmpegLite.sh or specify -MediaTools."
    }
}

$restore = @($app, '-r', 'win-x64', '-p:SelfContained=true')
if ($RestoreConfig) { $restore += @('--configfile', $RestoreConfig) }
& $Dotnet restore @restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
& $Dotnet publish $app -c Release -r win-x64 --self-contained true --no-restore -o $output `
    '-p:SatelliteResourceLanguages=zh-Hans%3Bzh-Hant%3Ben' -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

foreach ($dependency in $dependencies) {
    Copy-Item -LiteralPath $dependency -Destination $output -Force
}
$ffmpegLicense = Join-Path $MediaTools 'FFmpeg-LICENSE.txt'
if (!(Test-Path -LiteralPath $ffmpegLicense)) { $ffmpegLicense = Join-Path $MediaTools 'LICENSE' }
if (Test-Path -LiteralPath $ffmpegLicense) {
    Copy-Item -LiteralPath $ffmpegLicense -Destination (Join-Path $output 'FFmpeg-LICENSE.txt') -Force
}
Write-Host (Join-Path $output 'Viewcut.exe')
