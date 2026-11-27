# generar-iconos.ps1
# Saca los PNG y el ICO de Arkko de los SVG de assets\arkko\svg (los hace scripts\marca\generar-svg.mjs).
# El icono de la app es siempre la tecla con brazo verde: por encima de 32 px, con su borde y su
# textura; hasta 32, la misma con menos borde y la letra mas grande, que es la que se distingue a
# 16 px. El de 32 tambien va en pequeno porque el Explorador lo reduce para el boton anclado de la barra
# de tareas, y la ventana sin anclar usa el de 24: asi salen iguales. Rasteriza con Edge sin ventana, en
# un canvas, y monta el ICO con los PNG dentro.
#
#   node scripts\marca\generar-svg.mjs assets\arkko\svg
#   .\scripts\marca\generar-iconos.ps1
#   .\scripts\marca\generar-iconos.ps1 -Theme claro
#
# Deja assets\arkko\png\arkko-<tamano>.png, assets\arkko\png\arkko-pequena-48.png (el de 48 que la app
# muestra a unos 18 px) y assets\arkko\arkko.ico. Con -Theme claro, los mismos con -claro en el nombre:
# arkko-claro-<tamano>.png, arkko-claro-pequena-48.png y arkko-claro.ico.

[CmdletBinding()]
param(
    [string]$Theme = 'oscuro'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$svgDir = Join-Path $repo 'assets\arkko\svg'
$outDir = Join-Path $repo 'assets\arkko'
$pngDir = Join-Path $outDir 'png'
$prefix = if ($Theme -eq 'oscuro') { 'arkko' } else { "arkko-$Theme" }
$work = Join-Path $env:TEMP "arkko-iconos-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force $work, $pngDir | Out-Null

$big   = [IO.File]::ReadAllText((Join-Path $svgDir "arkko-brazo-$Theme.svg"))
$small = [IO.File]::ReadAllText((Join-Path $svgDir "arkko-pequena-$Theme.svg"))
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256, 512, 1024

# Cada linea de la salida: "<tamano>:<png en base64>"; la "s48" es la version pequena a 48 px
$page = @"
<!doctype html><html><body><pre id="out"></pre><script>
const big = $(ConvertTo-Json $big), small = $(ConvertTo-Json $small);
const sizes = [$($sizes -join ',')];
const load = svg => new Promise(r => { const i = new Image(); i.onload = () => r(i); i.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg); });
const draw = (img, s) => { const c = document.createElement('canvas'); c.width = c.height = s; c.getContext('2d').drawImage(img, 0, 0, s, s); return c.toDataURL('image/png').split(',')[1]; };
(async () => {
  const b = await load(big), p = await load(small);
  const lines = sizes.map(s => s + ':' + draw(s <= 32 ? p : b, s));
  lines.push('s48:' + draw(p, 48));
  document.getElementById('out').textContent = lines.join('\n');
})();
</script></body></html>
"@
$html = Join-Path $work 'render.html'
[IO.File]::WriteAllText($html, $page)

$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
# Edge escribe avisos sin importancia en stderr; con Stop, PowerShell 5.1 los trataria como error
$ErrorActionPreference = 'Continue'
$dom = & $edge --headless --disable-gpu --virtual-time-budget=5000 --dump-dom "file:///$($html -replace '\\','/')" 2>$null | Out-String
$ErrorActionPreference = 'Stop'
$m = [regex]::Match($dom, '<pre id="out">([\s\S]*?)</pre>')
if (-not $m.Success -or -not $m.Groups[1].Value.Trim()) { throw 'Edge no devolvio las imagenes' }

$pngs = @{}
foreach ($line in $m.Groups[1].Value.Trim() -split "`n") {
    $key, $b64 = $line.Trim() -split ':', 2
    $bytes = [Convert]::FromBase64String($b64)
    if ($key -eq 's48') {
        [IO.File]::WriteAllBytes((Join-Path $pngDir "$prefix-pequena-48.png"), $bytes)
        continue
    }
    $pngs[[int]$key] = $bytes
    [IO.File]::WriteAllBytes((Join-Path $pngDir "$prefix-$key.png"), $bytes)
}

# El ICO: cabecera, una entrada por tamano y los PNG detras
$icoSizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$ms = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$icoSizes.Count)
$offset = 6 + 16 * $icoSizes.Count
foreach ($s in $icoSizes) {
    $d = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$s].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$s].Length
}
foreach ($s in $icoSizes) { $w.Write($pngs[$s]) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $outDir "$prefix.ico"), $ms.ToArray())
Remove-Item -Recurse -Force $work
Write-Output "Hecho: $($sizes.Count) PNG, el de 48 pequeno y $prefix.ico en $outDir"
