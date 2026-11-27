# install.ps1
# Compila Arkko e instala sus dos piezas en %LOCALAPPDATA%\ClaudeStats (o CLAUDE_STATS_HOME):
# los hooks y la statusline (ak.exe) en bin, y la ventana (Arkko.exe) en desktop. Registra
# la statusline y los hooks en el settings.json de Claude Code, con copia de seguridad del original.
#
#   .\install.ps1              instalar o actualizar
#   .\install.ps1 -AutoStart   ademas, abrir la ventana al iniciar sesion en Windows
#   .\install.ps1 -Check       diagnostico, no cambia nada
#   .\install.ps1 -Uninstall   retirar hooks, statusline y binarios (los datos se conservan)
#
# Para actualizar una instalacion con las comprobaciones de Smart App Control, mejor
# scripts\deploy-hooks.ps1 y scripts\deploy-desktop.ps1: aqui se compila una sola vez.

[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$Check,
    [switch]$AutoStart
)

$ErrorActionPreference = 'Stop'

$appHome = if ($env:CLAUDE_STATS_HOME) { $env:CLAUDE_STATS_HOME } else { Join-Path $env:LOCALAPPDATA 'ClaudeStats' }
$bin     = Join-Path $appHome 'bin'
$desktop = Join-Path $appHome 'desktop'
$cli     = Join-Path $bin 'ak.exe'
# El de cuando se llamaba ccstats: si es el unico instalado, el diagnostico y la desinstalacion tiran de el
$oldCli  = Join-Path $bin 'ccstats.exe'
$app     = Join-Path $desktop 'Arkko.exe'
$src     = Join-Path $PSScriptRoot 'src'

# Cierra la ventana si esta abierta desde la carpeta de instalacion, con el nombre de ahora o el de cuando
# se llamaba Agent Pulse, para poder sustituir sus binarios.
function Stop-App {
    Get-Process Arkko, AgentPulse -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($desktop, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object {
            $_.CloseMainWindow() | Out-Null
            if (-not $_.WaitForExit(3000)) { $_.Kill() }
        }
}

$installed = if (Test-Path $cli) { $cli } elseif (Test-Path $oldCli) { $oldCli } else { $null }

if ($Check) {
    if (-not $installed) { Write-Output "No instalado: falta $cli"; exit 1 }
    & $installed check
    exit $LASTEXITCODE
}

if ($Uninstall) {
    Stop-App
    if ($installed) { & $installed uninstall }
    foreach ($folder in $bin, $desktop) {
        if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
    }
    Write-Output "Binarios retirados de $bin y $desktop"
    exit 0
}

# Requisito: SDK de .NET 8 o superior para los hooks; la ventana pide el 10 (build-desktop.ps1 lo busca).
$sdks = & dotnet --list-sdks 2>$null
if (-not ($sdks | Where-Object { [int]($_.Split('.')[0]) -ge 8 })) {
    Write-Output 'Falta el SDK de .NET 8 o superior: https://dotnet.microsoft.com/download'
    exit 1
}

Stop-App
New-Item -ItemType Directory -Force -Path $bin | Out-Null

Write-Output 'Compilando los hooks...'
& dotnet publish (Join-Path $src 'Arkko.Hook') -c Release -r win-x64 --self-contained false `
    -p:PublishReadyToRun=true -o $bin -nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Output 'Error al compilar los hooks'; exit 1 }

Write-Output 'Compilando la ventana...'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'scripts\build-desktop.ps1') -Output $desktop
if ($LASTEXITCODE -ne 0) { Write-Output 'Error al compilar la ventana: hace falta el SDK de .NET 10'; exit 1 }
# La ventana de cuando la app se llamaba Agent Pulse y su libreria comun de antes: se quedarian junto a los nuevos.
Get-ChildItem $desktop -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'AgentPulse.*' -or $_.Name -like 'ClaudeStats.Core.*' } | Remove-Item -Force

$installArgs = @('install')
if ($AutoStart) { $installArgs += '--autostart' }
& $cli @installArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Start-Process $app
Write-Output "Listo. Ventana abierta desde $app"
