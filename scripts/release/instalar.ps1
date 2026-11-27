# instalar.ps1
# Instala Arkko desde el zip de una Release: copia los hooks (bin) y la ventana (desktop) a
# %LOCALAPPDATA%\ClaudeStats, registra los hooks y la statusline en el settings.json de Claude Code
# (con copia de seguridad del original) y abre la ventana. No necesita administrador ni el SDK de .NET.
#
#   powershell -ExecutionPolicy Bypass -File .\instalar.ps1              instalar o actualizar
#   powershell -ExecutionPolicy Bypass -File .\instalar.ps1 -AutoStart   ademas, abrirla al iniciar sesion
#   powershell -ExecutionPolicy Bypass -File .\instalar.ps1 -Desinstalar retirar hooks y binarios; los datos se conservan

[CmdletBinding()]
param(
    [switch]$AutoStart,
    [switch]$Desinstalar
)

$ErrorActionPreference = 'Stop'
$appHome = if ($env:CLAUDE_STATS_HOME) { $env:CLAUDE_STATS_HOME } else { Join-Path $env:LOCALAPPDATA 'ClaudeStats' }
$bin = Join-Path $appHome 'bin'
$desktop = Join-Path $appHome 'desktop'

# Cierra la ventana instalada, si esta abierta, para poder sustituir o borrar sus ficheros.
function Stop-Arkko {
    Get-Process Arkko -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($desktop, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object {
            $_.CloseMainWindow() | Out-Null
            if (-not $_.WaitForExit(5000)) { $_.Kill() }
        }
}

if ($Desinstalar) {
    $cli = Join-Path $bin 'ak.exe'
    if (Test-Path $cli) { & $cli uninstall }
    Stop-Arkko
    foreach ($dir in $bin, $desktop) { if (Test-Path $dir) { Remove-Item -Recurse -Force $dir } }
    Write-Output "Arkko desinstalado. Los datos siguen en $appHome\data."
    exit 0
}

Stop-Arkko
foreach ($part in 'bin', 'desktop') {
    $from = Join-Path $PSScriptRoot $part
    $to = Join-Path $appHome $part
    if (-not (Test-Path $from)) { throw "Falta la carpeta $part junto a instalar.ps1: descomprime el zip entero." }
    if (Test-Path $to) { Remove-Item -Recurse -Force $to }
    Copy-Item -Recurse $from $to
}

$installArgs = @('install')
if ($AutoStart) { $installArgs += '--autostart' }
& (Join-Path $bin 'ak.exe') @installArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Start-Process (Join-Path $desktop 'Arkko.exe')
Write-Output 'Listo. Reinicia las sesiones de Claude Code abiertas para que carguen los hooks.'
