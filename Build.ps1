[CmdletBinding()]
param(
    [string]$Dotnet,
    [string]$Tools = (Join-Path $PSScriptRoot '.tools'),
    [string]$RestoreConfig
)

$ErrorActionPreference = 'Stop'
$Tools = (Resolve-Path -LiteralPath $Tools).Path
if (!$Dotnet) {
    $localSdk = Join-Path $Tools 'dotnet\dotnet.exe'
    $Dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
}

$app = Join-Path $PSScriptRoot 'Viewcut.csproj'
$output = Join-Path $PSScriptRoot 'artifacts\Viewcut'
$dependencies = @('mpv\libmpv-2.dll', 'ffmpeg\ffmpeg.exe', 'ffmpeg\ffprobe.exe')
foreach ($dependency in $dependencies) {
    if (!(Test-Path -LiteralPath (Join-Path $Tools $dependency))) {
        throw "Missing $dependency under $Tools."
    }
}

$restore = @($app, '-r', 'win-x64', '-p:SelfContained=true')
if ($RestoreConfig) { $restore += @('--configfile', $RestoreConfig) }
& $Dotnet restore @restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
& $Dotnet publish $app -c Release -r win-x64 --self-contained true --no-restore -o $output
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

foreach ($dependency in $dependencies) {
    Copy-Item -LiteralPath (Join-Path $Tools $dependency) -Destination $output -Force
}
$ffmpegLicense = Join-Path $Tools 'ffmpeg\LICENSE'
if (Test-Path -LiteralPath $ffmpegLicense) {
    Copy-Item -LiteralPath $ffmpegLicense -Destination (Join-Path $output 'FFmpeg-LICENSE.txt') -Force
}
Write-Host (Join-Path $output 'Viewcut.exe')
