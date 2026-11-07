[CmdletBinding()]
param([string]$Version = '0.6.1')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$release = Join-Path $repo 'artifacts\Release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
$sources = @{
    'Viewcut-Setup-x64.exe' = "artifacts\Setup\Viewcut-$Version-Setup-x64.exe"
    'Viewcut-Web-Windows.exe' = 'artifacts\WebLauncher\win-x64\Viewcut.Web.exe'
    'Viewcut-Web-macOS-AppleSilicon.zip' = "artifacts\WebLauncher\Viewcut-Web-$Version-osx-arm64.zip"
    'Viewcut-Web-macOS-Intel.zip' = "artifacts\WebLauncher\Viewcut-Web-$Version-osx-x64.zip"
}
foreach ($item in $sources.GetEnumerator()) {
    $source = Join-Path $repo $item.Value
    if (!(Test-Path -LiteralPath $source)) { throw "Missing $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $release $item.Key) -Force
}
$static = Get-ChildItem -LiteralPath (Join-Path $repo 'web') -File |
    Where-Object { $_.Extension -in '.html', '.js', '.css', '.svg', '.txt' }
Compress-Archive -LiteralPath $static.FullName -DestinationPath (Join-Path $release 'Viewcut-Web-Static.zip') -Force
Copy-Item -LiteralPath (Join-Path $repo 'launcher\README.md') -Destination (Join-Path $release 'Web-README.txt') -Force
$hashes = Get-ChildItem -LiteralPath $release -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
}
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($false))
Get-ChildItem -LiteralPath $release -File | Select-Object Name, Length
