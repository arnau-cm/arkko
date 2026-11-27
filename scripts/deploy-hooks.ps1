# deploy-hooks.ps1
# Compila los hooks y la statusline (ak.exe), los copia a la carpeta de instalacion (la de
# install.ps1: %LOCALAPPDATA%\ClaudeStats\bin, o CLAUDE_STATS_HOME\bin) y comprueba ALLI que Smart App
# Control deja ejecutarlos. Si sale bloqueado, prueba con otra etiqueta; si no entra ninguna, restaura lo
# que habia instalado. La ventana va aparte, con deploy-desktop.ps1.
#
#   .\scripts\deploy-hooks.ps1                    hasta 10 intentos
#   .\scripts\deploy-hooks.ps1 -Tries 30
#   .\scripts\deploy-hooks.ps1 -Version 1.0.1     otra version de los ficheros
#
# -Version es la version de los ficheros; por defecto, la de src/Directory.Build.props. Entre intentos
# solo cambia la etiqueta del exe: si se bloquean todos tras tocar la libreria comun, hay que pasar otra
# -Version para que cambie tambien.
#
# La prueba es en la carpeta de instalacion y no en una temporal: una compilacion que se ejecutaba en
# %TEMP% salio bloqueada en bin, y un ak.exe bloqueado rompe la statusline y los hooks de todas
# las sesiones. Mientras duran los intentos pueden fallar unos segundos; al acabar, o funciona lo nuevo
# o se ha restaurado lo anterior.
#
# Lo instalado se aparta a bin.anterior, junto a bin, y no se copia: Smart App Control guarda en cada
# fichero que ya lo dejo ejecutarse, y una copia es un fichero nuevo que vuelve a evaluar y puede
# rechazar. Restaurar una copia dejaba bloqueado lo que funcionaba. Si al empezar ya hay un
# bin.anterior, un despliegue anterior no termino: no se toca nada hasta revisarlo a mano.
#
# No toca settings.json: los hooks ya apuntan a esta carpeta desde install.ps1 (o desde ak install).
#
# Los ficheros de cuando el ejecutable se llamaba ccstats (ccstats.* y ClaudeStats.Core.*) se devuelven a
# bin despues de instalar, movidos y no copiados: Claude Code lee los hooks al arrancar cada sesion, y las
# abiertas antes de ak install siguen llamando a ccstats.exe hasta que se reinician.

[CmdletBinding()]
param(
    [int]$Tries = 10,
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$src = Join-Path (Split-Path $PSScriptRoot -Parent) 'src'
$appHome = if ($env:CLAUDE_STATS_HOME) { $env:CLAUDE_STATS_HOME } else { Join-Path $env:LOCALAPPDATA 'ClaudeStats' }
$target = Join-Path $appHome 'bin'
$probe = Join-Path $env:TEMP 'cs-build-probe'
$backup = Join-Path $appHome 'bin.anterior'

# Copia una carpeta sobre la de instalacion, reintentando si un fichero esta en uso.
#
# Un hook que se ejecuta justo en ese momento tiene abiertos ak.exe y la libreria comun; dura
# menos de un segundo, asi que basta con esperar y volver a copiarlo todo.
function Copy-Build([string]$from) {
    for ($i = 1; $i -le 8; $i++) {
        try {
            Copy-Item (Join-Path $from '*') $target -Force -Recurse
            return $true
        } catch {
            Start-Sleep -Seconds 1
        }
    }
    return $false
}

# Mueve una carpeta, reintentando si un hook tiene abierto alguno de sus ficheros.
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

# Borra una carpeta, reintentando si un hook tiene abierto alguno de sus ficheros.
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

# Ejecuta la statusline de un ejecutable instalado (ak.exe si no se dice otro) y dice si llega a pintar algo.
#
# Con una carpeta de datos aparte y una configuracion de Claude Code vacia: la prueba no toca los
# datos reales de la app.
function Test-Cli([string]$exe = 'ak.exe') {
    $prevHome = $env:CLAUDE_STATS_HOME
    $prevConfig = $env:CLAUDE_CONFIG_DIR
    $env:CLAUDE_STATS_HOME = $probe
    $env:CLAUDE_CONFIG_DIR = Join-Path $probe 'claude'
    try {
        $out = '{}' | & (Join-Path $target $exe) statusline 2>&1 | Out-String
        return $out.Trim().Length -gt 0 -and $out -notmatch 'FileLoadException'
    } catch {
        return $false
    } finally {
        $env:CLAUDE_STATS_HOME = $prevHome
        $env:CLAUDE_CONFIG_DIR = $prevConfig
    }
}

# Devuelve a su sitio la carpeta apartada y dice como ha quedado.
#
# Si aun no se habia apartado, lo instalado sigue en su sitio y no hay nada que devolver.
function Restore-Backup {
    if (-not (Test-Path $backup)) { return }
    if (-not (Remove-Folder $target) -or -not (Move-Folder $backup $target)) {
        Write-Output "No se pudo devolver lo anterior a $target (ficheros en uso): sigue en $backup, revisar a mano."
        return
    }
    Write-Output "Restaurado lo anterior. Hooks: $(if (Test-Cli) { 'responden' } else { 'NO responden' })."
}

# Devuelve a bin los ficheros del nombre de antes que se apartaron con lo instalado; false si alguno no se ha
# podido mover.
function Restore-Legacy {
    $ok = $true
    foreach ($file in Get-ChildItem $backup -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'ccstats.*' -or $_.Name -like 'ClaudeStats.Core.*' }) {
        if (-not (Move-Folder $file.FullName (Join-Path $target $file.Name))) { $ok = $false }
    }
    return $ok
}

if (-not (Test-Path (Join-Path $target 'ak.exe')) -and -not (Test-Path (Join-Path $target 'ccstats.exe'))) {
    Write-Output "No hay instalacion en $target`: ejecutar antes install.ps1."
    exit 1
}

if (Test-Path $backup) {
    Write-Output "Ya hay una instalacion apartada en $backup`: un despliegue anterior no termino. Revisarla a mano antes de desplegar."
    exit 1
}

for ($n = 1; $n -le $Tries; $n++) {
    # Con los segundos: dos ejecuciones en el mismo minuto no repiten las mismas compilaciones.
    $tag = "$(Get-Date -Format 'MMddHHmmss')$n"
    $out = Join-Path $env:TEMP "cs-build-$tag"
    Write-Output "Intento $n de ${Tries}: compilando con etiqueta $tag..."
    $props = @("-p:BuildTag=$tag")
    if ($Version) { $props += "-p:Version=$Version" }
    & dotnet publish (Join-Path $src 'Arkko.Hook') -c Release -r win-x64 --self-contained false `
        -p:PublishReadyToRun=true -o $out @props -nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Output 'Error al compilar los hooks: se aborta.'
        Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
        Restore-Backup
        exit 1
    }

    # Se aparta justo antes de la primera copia: mientras compila, los hooks siguen con lo instalado.
    if (-not (Test-Path $backup)) {
        if (-not (Move-Folder $target $backup)) {
            Write-Output "No se pudo apartar $target (ficheros en uso): no se ha tocado nada."
            Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
            exit 1
        }
        New-Item -ItemType Directory -Force $target | Out-Null
    }
    $copied = Copy-Build $out
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
    if (-not $copied) {
        Write-Output '  No se pudo copiar a la carpeta de instalacion (ficheros en uso); se prueba otra.'
        continue
    }
    if (-not (Test-Cli)) {
        Write-Output '  ak.exe bloqueado en la carpeta de instalacion; se prueba otra.'
        continue
    }
    if (-not (Restore-Legacy)) {
        Write-Output "Instalada la compilacion $tag de los hooks, pero algun ccstats.* sigue en $backup (en uso): moverlo a $target a mano."
        exit 1
    }
    if ((Test-Path (Join-Path $target 'ccstats.exe')) -and -not (Test-Cli 'ccstats.exe')) {
        Write-Output '  Aviso: el ccstats.exe de antes no responde; las sesiones abiertas antes de ak install se quedan sin hooks hasta reiniciarlas.'
    }
    Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
    Write-Output "Instalada la compilacion $tag de los hooks."
    exit 0
}

Write-Output "Las $Tries compilaciones salieron bloqueadas."
Restore-Backup
exit 1
