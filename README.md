# Arkko

> **🚧 WIP: trabajo en curso.** Trabajo final de máster de Arnau Calleja. La app se usa a diario, pero
> no está terminada: lo que funciona y lo que falta, en [docs/estado.md](docs/estado.md).

Arkko acompaña el trabajo con agentes de IA en Windows: enseña en tiempo real las sesiones de Claude
Code, su consumo y sus límites, deja repetir paso a paso cómo ha trabajado cada sesión y vigila que no
se filtren secretos.

## Entrega del TFM

| Recurso | Enlace |
|---|---|
| Código fuente | Este repositorio |
| Descarga de la app (despliegue) | [Releases](../../releases): versión de trabajo (WIP) para Windows; cómo instalarla, en [Instalación](#instalación) |
| Presentación (slides) | Pendiente |
| Vídeo de explicación | Pendiente |
| Usuario y contraseña de prueba | No aplica: es una app de escritorio local, sin login |

## Stack tecnológico

| Capa | Tecnología |
|---|---|
| Lenguaje | C# |
| Núcleo y hooks | .NET 8 (`Arkko.Core`, `Arkko.Hook` → `ak.exe`) |
| Ventana | .NET 10 con [Avalonia 12](https://avaloniaui.net/) (XAML, tema Fluent) |
| Vistas web dentro de la app | HTML, CSS y JavaScript sin framework en WebView2; [three.js](https://threejs.org/) r128 para la vista «Obra» en 3D |
| Integración con Claude Code | Hooks y statusline de Claude Code; lectura de los transcripts `.jsonl` |
| Datos | Ficheros JSON y JSON Lines locales, con escritura atómica y mutex con nombre; secretos cifrados con DPAPI |
| Tests | xUnit (516 tests) |
| Scripts | PowerShell (instalación, compilación y despliegue) |

## Estructura del proyecto

```
Arkko/
├── src/
│   ├── Arkko.Core/        modelos, almacén JSON, lectura de transcripts, detección de secretos, tableros
│   ├── Arkko.Hook/        ak.exe: hooks y statusline de Claude Code, instalación y diagnóstico
│   ├── Arkko.Desktop/     la ventana (Avalonia): vistas .axaml y, en Web/, las vistas HTML de Sesiones
│   ├── Arkko.Tests/       tests de xUnit del núcleo y de los hooks
│   └── Arkko.sln          Core, Hook y Tests (la ventana se compila aparte con scripts/build-desktop.ps1)
├── scripts/               compilación y despliegue de la ventana y de los hooks
├── assets/arkko/          icono y marca (PNG, SVG, ICO y bocetos)
├── docs/                  arquitectura, diseño, detección de secretos, estado y dossier de marca
├── install.ps1            instalación completa
└── LICENSE
```

> Nombre de la app; su ventana es `Arkko.exe` y el ejecutable de los hooks, `ak.exe`. El cambio de marca va por
> pasos: mientras no llegue el suyo, la carpeta de datos sigue siendo
> `%LOCALAPPDATA%\ClaudeStats`. El icono, sus originales (PNG y SVG) y el dossier de la marca están en
> `assets/arkko/` y [docs/marca](docs/marca/dossier.html).

Ventana flotante para Windows que muestra, siempre encima del escritorio:

- **Límites de uso** de la cuenta (5 h, semanal y cualquier otro que envíe Claude Code, como uno
  propio de Fable si el plan lo tiene), con el tiempo hasta el reinicio.
- **Consumo de hoy por modelo** (Opus, Fable, Haiku…), en tokens, incluidos los subagentes.
- **Sesiones abiertas** de Claude Code y su estado: lista, trabajando, espera tu respuesta o
  terminada. Cada una muestra su nombre, el modelo, el esfuerzo (cinco barras, de `low` a `max`) y
  un anillo con el uso del contexto, que se vuelve ámbar al 60 % y rojo al 80 %.
- **Avisos** cuando una sesión termina o se queda esperándote (permiso, `AskUserQuestion`, plan por
  aprobar): notificación moderna de Windows con el icono de Arkko, que se queda en el centro de
  notificaciones (una por sesión), y sonido.
- **Contador rojo en el icono de la barra de tareas** con las sesiones que requieren atención: las que
  esperan tu respuesta y las terminadas hace más de 5 minutos sin revisar. Una terminada se marca
  como vista haciendo clic en su tarjeta, y su estado sigue siendo Terminado. El contador funciona
  aunque los avisos estén silenciados.
- **Orden de la lista:** primero lo que requiere atención; después, la actividad más reciente arriba.
- **Minimizado:** la ventana se convierte en una pastilla en la esquina con una gota por sesión, el
  límite de 5 h y el contador; al pasar el ratón muestra los límites y cada sesión.
- **Terminal personalizable:** desde ajustes → «Personalizar terminal» se elige qué sale en la barra de
  estado de Claude Code (una línea), en qué orden, con separadores y colores, y con interruptores si se
  muestran la barra y el resumen de cada prompt. Solo se aplica al guardar.
- **Recap:** cada tarjeta cita el resumen que Claude Code escribe al rato de parar la sesión, si es
  posterior a su estado actual; completo al pasar el ratón.
- **Clic en una tarjeta:** la marca como vista, sin cambiar de ventana. El **clic derecho** abre su
  menú, con **Ver sesión** (la abre en la sección Sesiones del panel) e **Ir a la terminal**,
  deshabilitada hasta la terminal integrada.

Se puede mover, minimizar y cerrar. **No se redimensiona:** su tamaño es fijo y solo se encoge al
plegar secciones. El engranaje de la barra de título abre un menú con el tema (claro u oscuro), el
silencio de avisos y el modo compacto (límites y sesiones, por páginas de 3).
Cada sección se pliega con su flecha, y las sesiones van **por páginas de 3** (`‹ 1 / 2 ›`) en lugar
de con desplazamiento. Recuerda su posición y qué secciones estaban plegadas. Una sesión que lleva más de 5 minutos
esperando tu respuesta se resalta en ámbar, y a partir de 15, en rojo.

## Panel

El botón de maximizar de la barra de título abre el **panel**: una ventana normal de Windows, que se
redimensiona y encaja como cualquier otra y no está siempre encima. Tiene un menú lateral, plegable a
un riel de iconos o del todo. Al abrir la app, arranca siempre en Inicio:

- **Inicio:** los límites de Claude, una ficha por límite, «Hoy por modelo» con el total y el reparto
  entre modelos, y todas las sesiones en rejilla, aprovechando el ancho.
- **Sesiones:** cómo trabaja una sesión, abierta o pasada. A la izquierda, las sesiones activas
  agrupadas por repositorio, con lo que pide cada una; en el centro, la secuencia de pasos o el mapa de
  agentes y herramientas; a la derecha, el chat. Se sigue en directo, se repite paso a paso y marca los
  secretos sin revisar. Clic derecho → «Ver sesión» en una sesión del inicio la abre aquí. La papelera de una sesión cerrada la elimina, tras confirmarlo: su conversación va a la papelera de Windows y la app olvida el texto de sus prompts, pero conserva sus cifras.
- **Tareas:** el tablero de tu panel de tareas (con un perfil importado; la app no trae ninguno), en
  columnas, con su backlog, filtros y el detalle de cada tarea. Cada tarea lleva sus sesiones de Claude
  Code, unidas por la clave de la rama (DEVLAB-99), y lo que han costado. Solo lee: lo que se mueve
  aquí es una planificación local que no cambia nada en el panel.
- **Diario:** lo hecho cada día con los agentes, como una agenda, en vistas de día, semana laboral,
  semana, mes y agenda: un bloque por sesión en su hora, del color de su tarea, con sus esperas, los
  commits y los cambios de columna de las tareas; un clic abre su ficha. «Copiar parte» da en texto lo
  hecho por tarea, para la daily o para imputar horas.
- **Estadísticas:** por periodo (hoy, 7 días, 30 días o todo) — cifras clave con su mini gráfica y el
  cambio frente al periodo anterior, histórico de tokens por modelo, consumo por proyecto o por tarea (cada uno en
  su color), aprovechamiento de caché, los 20 prompts más caros (clic: abre su sesión), evolución de
  los límites y «Exportar CSV».
- **Seguridad:** el informe de la detección de secretos (abajo).
- **Vulnerabilidades:** el informe de seguridad de [arkko-stack](#vulnerabilidades-del-stack) (abajo).
- **Configuración:** general, sesiones, tableros (perfiles, conexiones y el patrón de la clave de tarea),
  avisos, vista flotante, terminal, datos y su retención (con limpieza automática diaria) y seguridad.
- **Terminal:** aparece como «próximamente»; es la [mejora 36](docs/diseno.md#terminal-integrada).

Se vuelve a la flotante con «Vista flotante», y la app arranca en el último modo que se usó.

## Seguridad

Analiza los transcripts de Claude Code en busca de secretos que hayan llegado a la API —tokens,
claves, contraseñas, cadenas de conexión y variables de entorno con valor— y los lista enmascarados,
con el tipo, el proyecto, dónde aparecieron y cuántas veces.

**Nunca guarda el valor:** solo el tipo, una máscara de 4 + 4 caracteres y un HMAC con una clave del
equipo, todo cifrado con DPAPI en `data\security\`, que no sale del equipo. Cada hallazgo se puede
marcar «Ya rotado» o «Falso positivo», y la sección recomienda reglas `deny` para que Claude Code no
lea `.env`, `*.pfx`, `~/.ssh` o `.credentials.json`.

Qué reconoce, qué se le escapa y por qué está hecha así, en
[docs/deteccion-de-claves.md](docs/deteccion-de-claves.md); lo que queda, en [docs/estado.md](docs/estado.md).

### Vulnerabilidades del stack

La sección **Vulnerabilidades** enseña el informe de `check-security.ps1` de arkko-stack
(`%LOCALAPPDATA%\arkko-stack\security.json`, o la carpeta de `ARKKO_STACK_SECURITY_DIR`) en dos
pestañas: **Tu equipo y tus repos**, una tabla de lo instalado con fallos de seguridad conocidos, que
se agrupa por dónde o por tipo y se abre en cada fila con su arreglo y su historial; y **Avisos de
seguridad**, las noticias oficiales (INCIBE-CERT, CISA KEV, Node.js) que tocan al stack, con el
protocolo para revisarlas. Arkko no lleva reglas propias: lee el informe en cuanto cambia, lanza el
script con el botón de cada pestaña y marca los avisos como revisados con su `-Visto`, tras confirmar.
Avisa solo de lo nuevo (algo que actualizar, un aviso, la comprobación vieja o que no termina) y guarda
cuándo empezó a fallar y cuándo se arregló cada cosa. El script solo existe para Windows: en otros
sistemas, Arkko enseña el informe si lo encuentra.

Si el informe trae versiones, la tabla pone la instalada y la que lo arregla (`150.0 → 156.0`), o desde
cuándo no tiene soporte; si trae gravedad, que solo llega en npm y en los paquetes de .NET de cada repo,
la pastilla dice **Crítica** o **Alta** en lugar de «Actualizar», rellena de rojo y con la fila teñida
para que no pese lo mismo, y cada grupo se ordena por ella. Lo que
está al día con gravedad media o baja sigue plegado, con su etiqueta. Los comandos y rutas del arreglo
salen en letra de código, y cada uno se copia con un clic.

Cada pieza lleva sus letras sobre el color de su marca, y solo tres llevan logo, porque su licencia o
su política de marca lo permiten, sin cambiar su forma ni sus colores:

- **Git**: logo de Jason Long, con licencia [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)
  ([git-scm.com/downloads/logos](https://git-scm.com/downloads/logos)).
- **Python**: «Python» y su logo son marcas de la Python Software Foundation
  ([python.org/psf/trademarks](https://www.python.org/psf/trademarks/)). Arkko no tiene relación con ella.
- **Node.js**: «Node.js» y su logo son marcas de la OpenJS Foundation
  ([trademark-policy.openjsf.org](https://trademark-policy.openjsf.org)). Arkko no tiene relación con ella.

## Cómo funciona

```
Claude Code ──statusline──▶ ak.exe      ──▶ data\limits.json, data\sessions\<id>.json
            ──hooks───────▶ ak.exe      ──▶ data\sessions\<id>.json, data\prompts\<día>.jsonl
                                                  │
                                          Arkko.exe (ventana) lee cada segundo
```

| Pieza | Qué hace |
|---|---|
| `ak.exe` | Consola. Claude Code lo llama como statusline y como hook; también instala y diagnostica |
| `Arkko.exe` | La ventana (Avalonia), con icono en la bandeja |
| `Arkko.Core` | Modelos, almacén JSON, lectura del transcript y mezcla de `settings.json` |

Los límites de uso **solo** llegan por la statusline: no hay API pública para consultarlos. Por eso
se actualizan cuando alguna sesión recibe una respuesta, y la ventana indica la antigüedad del dato.

## Datos

Todo queda en `%LOCALAPPDATA%\ClaudeStats` (se cambia con la variable `CLAUDE_STATS_HOME`):

```
ClaudeStats\
├── bin\                       ak.exe: hooks y statusline
├── desktop\                   Arkko.exe: la ventana
└── data\
    ├── sessions\<id>.json     estado actual de cada sesión abierta
    ├── prompts\<día>.jsonl    un registro por prompt terminado, con tokens por modelo
    ├── states\<día>.jsonl     cada cambio de estado de una sesión
    ├── task-history\<día>.jsonl  cada cambio de columna de una tarea
    ├── limits.json            última foto de los límites
    ├── statusline-last.json   último JSON recibido por la statusline, tal cual
    ├── app-settings.json      posición de la ventana y sonido
    ├── backups\               copias de settings.json antes de cada cambio
    ├── security\              secretos encontrados, enmascarados, y claves de los tableros, cifrados
    ├── boards.json            conexiones con los tableros de tareas, sin claves
    ├── board-profiles\        perfiles importados
    ├── task-plan.json         planificación local de Tareas: solo claves y fechas
    ├── vulnerabilities\       estado conocido del informe de arkko-stack e historial por día
    └── errors.log             fallos de los hooks, que nunca interrumpen Claude Code
```

Son ficheros JSON planos en lugar de una base de datos: los escriben varios procesos cortos a la
vez (un hook por evento y sesión), y así cada escritura es atómica —temporal y reemplazo— bajo un
mutex con nombre, sin servidor ni dependencias. Las tareas leídas de los tableros quedan en memoria:
en disco solo se guardan sus claves.

## Instalación

### Desde la release (sin compilar)

Cada release publica `Arkko-<versión>-wip-win-x64.zip`: la app compilada, con su propio .NET, para
probarla sin SDK ni permisos de administrador. Es una **versión de trabajo (WIP)**, del estado actual
del proyecto: lo que funciona y lo que falta, en [docs/estado.md](docs/estado.md).

1. Descarga el zip de [Releases](../../releases) y descomprímelo entero.
2. En esa carpeta: `powershell -ExecutionPolicy Bypass -File .\instalar.ps1` (con `-AutoStart`, se
   abre también al iniciar sesión; con `-Desinstalar`, se retira y los datos se conservan).
3. Reinicia las sesiones de Claude Code abiertas.

Requisitos: Windows 10/11 x64, WebView2 y Claude Code. Los ejecutables **no están firmados**: Windows
(SmartScreen o Smart App Control) puede avisar o bloquearlos; en el aviso de SmartScreen, «Más
información» → «Ejecutar de todas formas».

### Desde el código

Requisitos: Windows 10/11, los SDK de .NET 8 (hooks) y .NET 10 (ventana) para compilar, y el
runtime de .NET 8 para los hooks; la ventana lleva su propio .NET. No hace falta ser administrador.

```powershell
.\install.ps1              # compila, instala, registra en settings.json y abre la ventana
.\install.ps1 -AutoStart   # además, abrirla al iniciar sesión en Windows
.\install.ps1 -Check       # diagnóstico, no cambia nada
.\install.ps1 -Uninstall   # retira hooks, statusline y binarios; los datos se conservan
```

El instalador solo añade o quita **sus** entradas en `~/.claude/settings.json` (o en
`CLAUDE_CONFIG_DIR`); el resto de la configuración y de los hooks se conserva. Si había otra
statusline, avisa de que la sustituye; el original queda en `data\backups`. También añade una regla
`deny` para que Claude Code no lea `data\security\`, donde se guardan los secretos encontrados.

Después, **reinicia las sesiones de Claude Code** abiertas para que carguen los hooks.

## Hooks que registra

| Evento | Estado |
|---|---|
| `SessionStart` | Lista; guarda el proceso de Claude Code para detectar cierres bruscos |
| `UserPromptSubmit` | Trabajando |
| `PreToolUse` (`AskUserQuestion`, `ExitPlanMode`) | Espera tu respuesta |
| `Notification` (permiso, diálogo) | Espera tu respuesta |
| `PostToolUse` (todas) | Vuelve a trabajando tras una espera |
| `Stop` | Terminada; calcula y registra el consumo del prompt |
| `SessionEnd` | Retira la sesión |

Un estado no lo pone ningún hook: **esperando a sus agentes**, en violeta y con el punto en órbita. Si
la sesión acaba su turno con agentes trabajando en segundo plano, la app lo ve en el transcript y no la
da por terminada ni avisa: retomará sola cuando acaben, y el aviso llega entonces. Los comandos que deja
en marcha (un `npm run dev`) no cambian el estado, pero la tarjeta y el aviso lo dicen.

## En un PC corporativo

- Es una app de usuario: no necesita permisos de administrador ni abre puertos. Solo se conecta, por
  HTTPS y para leer, a los tableros de tareas que se configuren; sin ninguno, no usa la red.
- La comprobación de vulnerabilidades la hace `check-security.ps1` de arkko-stack, no Arkko, y sí sale
  a internet: lee por HTTPS fuentes públicas (INCIBE-CERT, CISA, nodejs.org, Microsoft, python.org,
  Git for Windows) y pasa `npm audit` y `dotnet list package` en los repos, que mandan a npm y NuGet la
  lista de paquetes y versiones, sin código ni secretos. Arkko solo la lanza cuando se pulsa
  «Comprobar mi equipo» o «Buscar avisos»; lanzarla sola cuando se queda vieja viene apagado y se activa en
  Configuración → Seguridad → «Comprobar solo, en segundo plano».
- Si la organización restringe ejecutables (AppLocker, WDAC o el antivirus), puede hacer falta
  firmar `ak.exe` y `Arkko.exe` o pedir a IT una excepción para
  `%LOCALAPPDATA%\ClaudeStats\bin` y `%LOCALAPPDATA%\ClaudeStats\desktop`.
- Si las notificaciones de Windows están bloqueadas por directiva, el sonido y el contador de la
  barra de tareas siguen funcionando.

## Limitaciones

- El `PostToolUse` para todas las herramientas añade unos 0,2 s a cada llamada a herramienta: es
  lo que permite ver que una sesión vuelve a trabajar tras concederle un permiso.
- `rate_limits` solo llega con suscripción Claude.ai (Pro/Max/Team/Enterprise) y tras la primera
  respuesta de la sesión. Qué límites concretos trae depende del plan; los que lleguen se muestran.
- El formato del transcript no es un contrato estable de Claude Code. La lectura es defensiva: si
  algo cambia, el hook deja de sumar tokens pero no rompe la sesión.

## Documentación

| Documento | Qué cubre |
|---|---|
| [docs/arquitectura.md](docs/arquitectura.md) | Las piezas, el flujo de datos y las decisiones de fondo |
| [docs/deteccion-de-claves.md](docs/deteccion-de-claves.md) | La detección de secretos: qué mira, cuándo, qué no ve, la sección Seguridad y sus decisiones |
| [docs/diseno.md](docs/diseno.md) | Diseño y decisiones: ventana y modos, aspecto visual y terminal integrada |
| [docs/estado.md](docs/estado.md) | Qué funciona y qué queda por hacer |

## Desarrollo

```powershell
dotnet build src\Arkko.sln          # Core, hooks y tests
dotnet test src\Arkko.sln
.\scripts\build-desktop.ps1 -Output <carpeta>   # la ventana, con el SDK 10
.\scripts\release.ps1                           # el zip de la release, en .\release
```

Para actualizar una instalación hecha con `install.ps1`, con la comprobación de Smart App Control:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy-hooks.ps1     # hooks y statusline
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy-desktop.ps1   # la ventana
```

Cada uno compila su pieza con una etiqueta nueva, la pone en su carpeta de instalación y prueba allí
que Windows la deja ejecutar; si sale bloqueada, prueba con otra, y si no entra ninguna, restaura lo
que había instalado. La versión instalada se ve al pie del menú del engranaje.

La primera versión se escribió solo en PowerShell; después tuvo una ventana de WPF, que se sustituyó
por la de Avalonia actual para poder llevarla a macOS y Linux.

## Licencia

Copyright © 2026 Arnau Calleja. Todos los derechos reservados: ver [LICENSE](LICENSE).
