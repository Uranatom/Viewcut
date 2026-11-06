[CmdletBinding()]
param(
    [string]$Iscc,
    [string]$RestoreConfig
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$tools = Join-Path $repo '.tools'
$output = Join-Path $repo 'artifacts\Setup'
if (!$Iscc) {
    $localCompiler = Join-Path $tools 'inno\ISCC.exe'
    $installedCompiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    if (Test-Path -LiteralPath $localCompiler) { $Iscc = $localCompiler }
    elseif (Test-Path -LiteralPath $installedCompiler) { $Iscc = $installedCompiler }
    else { $Iscc = (Get-Command ISCC.exe -ErrorAction Stop).Source }
}

New-Item -ItemType Directory -Path $tools, $output -Force | Out-Null
$tools = (Resolve-Path -LiteralPath $tools).Path
$stage = Join-Path $tools ('setup-payload-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null

try {
    $build = @{ Output = $stage }
    if ($RestoreConfig) { $build.RestoreConfig = $RestoreConfig }
    & (Join-Path $repo 'Build.ps1') @build
    $version = ((Get-Item -LiteralPath (Join-Path $stage 'Viewcut.exe')).VersionInfo.ProductVersion -split '\+')[0]
    & $Iscc "/DAppVersion=$version" "/DPayload=$stage" "/O$output" (Join-Path $PSScriptRoot 'Viewcut.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed to compile the installer.' }
    Get-Item -LiteralPath (Join-Path $output "Viewcut-$version-Setup-x64.exe")
}
finally {
    $resolved = (Resolve-Path -LiteralPath $stage).Path
    $item = Get-Item -LiteralPath $resolved
    if ([IO.Path]::GetDirectoryName($resolved) -ne $tools -or
        $item.Name -notmatch '^setup-payload-[0-9a-f]{32}$' -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Unexpected staging path: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
