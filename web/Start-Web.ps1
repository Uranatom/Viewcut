[CmdletBinding()]
param([int]$Port = 4173)
$ErrorActionPreference = 'Stop'
$env:PORT = "$Port"
$nodeCommand = Get-Command node -ErrorAction SilentlyContinue
$nodePath = if ($nodeCommand) { $nodeCommand.Source } else {
    Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
}
if (!(Test-Path -LiteralPath $nodePath)) { throw 'Install Node.js 22 or newer, then run Start-Web again.' }
& $nodePath (Join-Path $PSScriptRoot 'server.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Viewcut web server failed.' }
