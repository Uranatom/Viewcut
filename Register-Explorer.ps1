[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Executable = (Join-Path $PSScriptRoot 'artifacts\Viewcut\Viewcut.exe'),
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$verbs = @(
    @{ Key = 'HKCU:\Software\Classes\SystemFileAssociations\.mp4\shell\Viewcut.CullFile'; Text = '用 Viewcut 粗筛'; Mode = '--file' },
    @{ Key = 'HKCU:\Software\Classes\SystemFileAssociations\.mov\shell\Viewcut.CullFile'; Text = '用 Viewcut 粗筛'; Mode = '--file' },
    @{ Key = 'HKCU:\Software\Classes\Directory\shell\Viewcut.CullFolder'; Text = '粗筛此文件夹'; Mode = '--folder'; Position = 'Top' }
)

if (!$Remove) {
    $Executable = (Resolve-Path -LiteralPath $Executable).Path
    if (![IO.File]::Exists($Executable) -or [IO.Path]::GetFileName($Executable) -ne 'Viewcut.exe') {
        throw '请选择测试版里的 Viewcut.exe。'
    }
}

$changed = $false
foreach ($verb in $verbs) {
    $commandKey = Join-Path $verb.Key 'command'
    if ($Remove) {
        if ((Test-Path -LiteralPath $verb.Key) -and $PSCmdlet.ShouldProcess($verb.Key, '移除 Viewcut 菜单')) {
            if (Test-Path -LiteralPath $commandKey) { Remove-Item -LiteralPath $commandKey }
            Remove-Item -LiteralPath $verb.Key
            $changed = $true
        }
        continue
    }

    if ($PSCmdlet.ShouldProcess($verb.Key, '注册 Viewcut 菜单')) {
        if (!(Test-Path -LiteralPath $commandKey)) { New-Item -Path $commandKey -Force | Out-Null }
        Set-Item -LiteralPath $verb.Key -Value $verb.Text
        if ($verb.Position) {
            New-ItemProperty -LiteralPath $verb.Key -Name Position -Value $verb.Position -Force | Out-Null
        }
        New-ItemProperty -LiteralPath $verb.Key -Name Icon -Value ('"{0}",0' -f $Executable) -Force | Out-Null
        Set-Item -LiteralPath $commandKey -Value ('"{0}" {1} "%1"' -f $Executable, $verb.Mode)
        $changed = $true
    }
}

if ($changed) {
    if (-not ('Viewcut.ShellChanges' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace Viewcut
{
    public static class ShellChanges
    {
        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        public static void RefreshAssociations()
        {
            const int SHCNE_ASSOCCHANGED = 0x08000000;
            const uint SHCNF_FLUSH = 0x1000;
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
'@
    }
    [Viewcut.ShellChanges]::RefreshAssociations()
}
