# generar-notificacion.ps1
# Saca los iconos de las notificaciones de los SVG de la letra (assets\arkko\svg): solo la letra AK con el
# brazo verde, sin tecla y recortada, porque Windows la pinta a unos 16 px en la cabecera. Rasteriza con
# Edge sin ventana, como generar-iconos.ps1.
#
#   .\scripts\marca\generar-notificacion.ps1
#
# Deja en assets\arkko\png, a 256 px:
#   arkko-notificacion-modo-oscuro.png  la letra clara, para Windows en modo oscuro
#   arkko-notificacion-modo-claro.png   la letra oscura, para Windows en modo claro
#   arkko-notificacion.png              la letra clara con contorno oscuro, que se lee sobre los dos
#                                       fondos: la de macOS y Linux, donde no se sabe el del aviso

$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$svgDir = Join-Path $repo 'assets\arkko\svg'
$pngDir = Join-Path $repo 'assets\arkko\png'
$work = Join-Path $env:TEMP "arkko-notificacion-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force $work, $pngDir | Out-Null

$light = [IO.File]::ReadAllText((Join-Path $svgDir 'arkko-letra-oscuro.svg'))
$dark = [IO.File]::ReadAllText((Join-Path $svgDir 'arkko-letra-claro.svg'))
$crop = { param($svg, $box) $svg -replace 'viewBox="0 0 128 128"', "viewBox=`"$box`"" }

# El contorno: la misma letra en oscuro, engordada con el filtro "halo" del SVG, debajo de la clara
$paths = ([regex]::Matches($light, '<path [^>]*/>') | ForEach-Object Value) -join ''
$outline = $paths -replace 'fill="#[0-9A-Fa-f]{6}"', 'fill="#1B1B1E"'
$both = ([regex]'<path ').Replace($light, { param($m) "<g filter=`"url(#halo)`">$outline</g><path " }, 1)

$items = [ordered]@{
    'arkko-notificacion-modo-oscuro' = (& $crop $light '22 22 84 84')
    'arkko-notificacion-modo-claro'  = (& $crop $dark '22 22 84 84')
    'arkko-notificacion'             = (& $crop $both '20 20 88 88')
}
$js = ($items.Keys | ForEach-Object { "['$_', $(ConvertTo-Json $items[$_])]" }) -join ','

# Cada linea de la salida: "<nombre>:<png en base64>"
$page = @"
<!doctype html><html><body><pre id="out"></pre><script>
const items = [$js];
const load = svg => new Promise(r => { const i = new Image(); i.onload = () => r(i); i.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg); });
(async () => {
  const lines = [];
  for (const [name, svg] of items) { const img = await load(svg); const c = document.createElement('canvas'); c.width = c.height = 256; c.getContext('2d').drawImage(img, 0, 0, 256, 256); lines.push(name + ':' + c.toDataURL('image/png').split(',')[1]); }
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

foreach ($line in $m.Groups[1].Value.Trim() -split "`n") {
    $name, $b64 = $line.Trim() -split ':', 2
    [IO.File]::WriteAllBytes((Join-Path $pngDir "$name.png"), [Convert]::FromBase64String($b64))
}
Remove-Item -Recurse -Force $work
Write-Output "Hecho: $($items.Count) iconos de notificacion en $pngDir"
