# deploy-desktop.ps1
# Compila la ventana (Arkko.exe) y la instala en su propia carpeta, %LOCALAPPDATA%\ClaudeStats\desktop
# (o CLAUDE_STATS_HOME\desktop), junto a bin y sin tocarlo: los hooks y la statusline siguen como estaban
# (se despliegan con deploy-hooks.ps1). Comprueba ALLI que Smart App Control la deja arrancar; si sale
# bloqueada, prueba con otra etiqueta, y si no entra ninguna, deja lo que habia.
#
#   .\scripts\deploy-desktop.ps1                    hasta 10 intentos
#   .\scripts\deploy-desktop.ps1 -Tries 30
#   .\scripts\deploy-desktop.ps1 -Version 1.0.1     otra version de los ficheros
#
# Cierra la ventana instalada para sustituirla y no la vuelve a abrir: al terminar, se abre
# desktop\Arkko.exe. La ventana de antes, AgentPulse.exe, tambien se cierra: se quita al apartar la carpeta.
#
# La prueba de arranque se hace con una carpeta de datos y una configuracion de Claude Code vacias,
# en %TEMP%: asi no choca con la ventana que este abierta ni toca los datos reales.
#
# Lo instalado se aparta a desktop.anterior y no se copia, por lo mismo que en deploy-app.ps1: Smart
# App Control guarda en cada fichero que ya lo dejo ejecutarse, y una copia vuelve a evaluarse. Si al
# empezar ya hay un desktop.anterior, un despliegue anterior no termino: no se toca nada.

[CmdletBinding()]
param(
    [int]$Tries = 10,
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$appHome = if ($env:CLAUDE_STATS_HOME) { $env:CLAUDE_STATS_HOME } else { Join-Path $env:LOCALAPPDATA 'ClaudeStats' }
$target = Join-Path $appHome 'desktop'
$backup = Join-Path $appHome 'desktop.anterior'
$probe = Join-Path $env:TEMP 'cs-desktop-probe'

# Cierra la ventana si esta abierta desde la carpeta de instalacion, con el nombre de ahora o el de cuando
# se llamaba Agent Pulse, y espera a que suelte sus ficheros.
function Stop-Desktop {
    Get-Process Arkko, AgentPulse -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($target, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object {
            $_.CloseMainWindow() | Out-Null
            if (-not $_.WaitForExit(5000)) { $_.Kill() }
        }
}

# Mueve una carpeta, reintentando si algun fichero sigue abierto.
function Move-Folder([string]$from, [string]$to) {
    for ($i = 1; $i -le 20; $i++) {
        try {
            Move-Item $from $to -ErrorAction Stop
            return $true
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }
    return $false
}

# Borra una carpeta, reintentando si algun fichero sigue abierto.
function Remove-Folder([string]$path) {
    for ($i = 1; $i -le 20; $i++) {
        try {
            Remove-Item $path -Recurse -Force -ErrorAction Stop
            return $true
        } catch {
            if (-not (Test-Path $path)) { return $true }
            Start-Sleep -Milliseconds 500
        }
    }
    return $false
}

# Abre la ventana instalada con datos vacios y espera a que se muestre; despues la cierra.
#
# Hasta 20 s: la primera vez que se ejecuta un binario, Smart App Control puede tardar en decidir. Un
# bloqueo de una de sus librerias no impide arrancar el proceso: se ve porque sale sin ventana.
function Test-Launch {
    $prevHome = $env:CLAUDE_STATS_HOME
    $prevConfig = $env:CLAUDE_CONFIG_DIR
    $env:CLAUDE_STATS_HOME = $probe
    $env:CLAUDE_CONFIG_DIR = Join-Path $probe 'claude'
    try {
        $p = Start-Process (Join-Path $target 'Arkko.exe') -PassThru -ErrorAction Stop
    } catch {
        return $false
    } finally {
        $env:CLAUDE_STATS_HOME = $prevHome
        $env:CLAUDE_CONFIG_DIR = $prevConfig
    }
    try {
        for ($i = 0; $i -lt 40; $i++) {
            Start-Sleep -Milliseconds 500
            $p.Refresh()
            if ($p.HasExited) { return $false }
            if ($p.MainWindowHandle -ne 0) { return $true }
        }
        return $false
    } finally {
        if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit(5000) | Out-Null }
        Remove-Item $probe -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Devuelve a su sitio la carpeta apartada; si no habia nada instalado, quita lo que se haya copiado.
function Restore-Backup {
    Stop-Desktop
    if (-not (Test-Path $backup)) {
        Remove-Folder $target | Out-Null
        return
    }
    if (-not (Remove-Folder $target) -or -not (Move-Folder $backup $target)) {
        Write-Output "No se pudo devolver lo anterior a $target (ficheros en uso): sigue en $backup, revisar a mano."
        return
    }
    Write-Output 'Restaurado lo que habia instalado.'
}

if (Test-Path $backup) {
    Write-Output "Ya hay una instalacion apartada en $backup`: un despliegue anterior no termino. Revisarla a mano antes de desplegar."
    exit 1
}

New-Item -ItemType Directory -Force $appHome | Out-Null

for ($n = 1; $n -le $Tries; $n++) {
    # Con los segundos: dos ejecuciones en el mismo minuto no repiten las mismas compilaciones.
    $tag = "$(Get-Date -Format 'MMddHHmmss')$n"
    $out = Join-Path $env:TEMP "cs-desktop-$tag"
    Write-Output "Intento $n de ${Tries}: compilando con etiqueta $tag..."
    $build = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'build-desktop.ps1'), '-Output', $out, '-BuildTag', $tag)
    if ($Version) { $build += @('-Version', $Version) }
    & powershell.exe @build | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Output 'Error al compilar la ventana nueva: se aborta.'
        Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
        Restore-Backup
        exit 1
    }

    Stop-Desktop
    # Lo instalado se aparta la primera vez; en los reintentos, lo copiado en el anterior se borra.
    if ((Test-Path $target) -and -not (Test-Path $backup)) {
        if (-not (Move-Folder $target $backup)) {
            Write-Output "No se pudo apartar $target (ficheros en uso): no se ha tocado nada."
            Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
            exit 1
        }
    } elseif (-not (Remove-Folder $target)) {
        Write-Output "  No se pudo vaciar $target (ficheros en uso); se prueba otra."
        Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
        continue
    }
    $moved = Move-Folder $out $target
    if (-not $moved) {
        Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
        Write-Output '  No se pudo poner en la carpeta de instalacion; se prueba otra.'
        continue
    }
    if (-not (Test-Launch)) {
        Write-Output '  La ventana sale bloqueada en la carpeta de instalacion; se prueba otra.'
        continue
    }
    Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
    Write-Output "Instalada la compilacion $tag en $target."
    Write-Output 'Para usarla, abre Arkko.exe de esa carpeta.'
    exit 0
}

Write-Output "Las $Tries compilaciones salieron bloqueadas."
Restore-Backup
exit 1
