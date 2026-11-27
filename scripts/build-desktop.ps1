# build-desktop.ps1
# Publica la ventana (src\Arkko.Desktop) con el SDK 10, autocontenida.
#
#   .\scripts\build-desktop.ps1 -Output <carpeta>                      para Windows
#   .\scripts\build-desktop.ps1 -Output <carpeta> -Runtime linux-x64   para Linux
#   .\scripts\build-desktop.ps1 -Output <carpeta> -Runtime osx-arm64   para macOS
#   .\scripts\build-desktop.ps1 -Output <carpeta> -Version 1.0.1       otra version de los ficheros
#   .\scripts\build-desktop.ps1 -Output <carpeta> -NoSymbols           sin simbolos de depuracion
#
# Autocontenida: lleva su propio .NET 10, firmado por Microsoft, porque en el equipo solo estan el 8 y
# el 9 y el ejecutable no arrancaria. El SDK 10 tampoco esta en el PATH: se usa el del perfil
# (%LOCALAPPDATA%\Microsoft\dotnet) si existe. Se ejecuta desde la carpeta del proyecto para que tome
# su global.json, que fija la 10.0.1xx: la 10.0.4xx esconde los bloqueos de Smart App Control tras una
# espera de 5 minutos (MSB4216).
#
# No toca la carpeta de instalacion: publica siempre en -Output.

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Output,
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$Version = '',
    [string]$BuildTag = '',
    [switch]$NoSymbols
)

$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Arkko.Desktop'
$profileDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
$dotnet = if (Test-Path $profileDotnet) { $profileDotnet } else { 'dotnet' }
$Output = [IO.Path]::GetFullPath($Output)

$props = @()
if ($Version) { $props += "-p:Version=$Version" }
if ($BuildTag) { $props += "-p:BuildTag=$BuildTag" }
# Sin simbolos, las DLL no guardan la ruta de compilacion del .pdb: es lo que va en una release publica.
if ($NoSymbols) { $props += '-p:DebugType=None', '-p:DebugSymbols=false' }

Push-Location $project
try {
    & $dotnet publish -c $Configuration -r $Runtime --self-contained -o $Output -nologo -v q @props
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
