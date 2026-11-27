# release.ps1
# Genera el zip de una version de Arkko para Windows, listo para subir a una Release de GitHub:
# los hooks (bin\ak.exe) y la ventana (desktop\Arkko.exe), los dos autocontenidos, con el instalador
# instalar.ps1 y un LEEME.txt. No instala nada ni toca la instalacion del equipo.
#
#   .\scripts\release.ps1                         deja el zip en .\release
#   .\scripts\release.ps1 -Output C:\tmp\salida
#   .\scripts\release.ps1 -Tag wip                Arkko-<version>-wip-win-x64.zip

[CmdletBinding()]
param(
    [string]$Output = '',
    [string]$Tag = 'wip'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $Output) { $Output = Join-Path $repo 'release' }
$Output = [IO.Path]::GetFullPath($Output)

[xml]$props = Get-Content (Join-Path $repo 'src\Directory.Build.props') -Raw
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.DisplayVersion } | Where-Object { $_ } | Select-Object -First 1)
$name = if ($Tag) { "Arkko-$version-$Tag-win-x64" } else { "Arkko-$version-win-x64" }
$stage = Join-Path $Output $name
$zip = "$stage.zip"

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
if (Test-Path $zip) { Remove-Item -Force $zip }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Write-Output 'Compilando los hooks...'
& dotnet publish (Join-Path $repo 'src\Arkko.Hook') -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $stage 'bin') -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Error al compilar los hooks' }

Write-Output 'Compilando la ventana...'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build-desktop.ps1') -Output (Join-Path $stage 'desktop') -NoSymbols
if ($LASTEXITCODE -ne 0) { throw 'Error al compilar la ventana: hace falta el SDK de .NET 10' }

Get-ChildItem $stage -Recurse -Filter *.pdb | Remove-Item -Force
Copy-Item (Join-Path $PSScriptRoot 'release\instalar.ps1') $stage
Copy-Item (Join-Path $PSScriptRoot 'release\LEEME.txt') $stage
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $stage 'LICENSE.txt')

# Cada entrada a mano, con '/': Compress-Archive y ZipFile de Windows PowerShell 5.1 guardan las rutas con '\',
# que otros descompresores no entienden.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$stream = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
$archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        $entry = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {
    $archive.Dispose()
    $stream.Dispose()
}
Remove-Item -Recurse -Force $stage
Write-Output "Listo: $zip"
