# Arquitectura de Arkko

Cómo está hecha la app, de dónde saca los datos, dónde los guarda y cómo se instala. Para qué hace
cada función de cara al usuario, ver el [README](../README.md); para lo pendiente,
[tareas](estado.md).

## En una frase

Claude Code ejecuta un pequeño programa de consola (`ak.exe`) cada vez que cambia algo en una
sesión; ese programa deja el estado en ficheros JSON, y una ventana de escritorio
(`Arkko.exe`) los lee cada segundo y los pinta.

## Tecnología

| Pieza | Tecnología |
|---|---|
| Lenguaje y plataforma | C#: Core, `ak` y tests sobre .NET 8; la ventana, sobre .NET 10 y autocontenida. Hoy se usa en Windows 10/11; macOS y Linux son la mejora 40 |
| Ventana | **Avalonia 12** (XAML), con los temas en diccionarios de recursos (`Themes/`) |
| Icono de la bandeja | `TrayIcon` de Avalonia |
| Notificaciones | En Windows, las modernas (WinRT, SDK 10.0.17763), en `Windows/ModernToasts.cs` |
| Integración con Windows | Llamadas nativas (P/Invoke): procesos, ventanas, contador de la barra de tareas; lo que solo existe en Windows va en `Windows/` y no se compila para otros sistemas |
| Sección Sesiones | Página web propia (HTML, CSS y JavaScript, sin librerías) en el WebView de Avalonia (`Avalonia.Controls.WebView`) |
| Tests | xUnit, sobre la lógica de datos |
| Dependencias | Ninguna fuera de .NET, Avalonia y el SDK de Windows; sin base de datos ni servicios |

### De WPF a Avalonia

Hasta el 2026-10-26 la ventana era de **WPF**, solo para Windows. La mejora 40 construyó al lado una
ventana nueva con Avalonia, que dibuja igual en Windows, macOS y Linux; se usó a diario desde el
2026-10-19 y, en la 40.7, se retiró la de WPF, que no se incluye en esta copia. La ventana se compila con el SDK 10, que no está en el
PATH ni en Visual Studio 17.14: por eso va fuera de `Arkko.sln` y se compila con
`scripts\build-desktop.ps1`, con su `global.json`.

## Componentes

```
src/
├── Arkko.Core            librería común (net8.0)
├── Arkko.Hook            ak.exe, consola (net8.0)
├── Arkko.Desktop         Arkko.exe, la ventana (net10.0, fuera de la solución)
└── Arkko.Tests           tests (xUnit)
```

| Proyecto | Responsabilidad | Ficheros clave |
|---|---|---|
| **Core** | Modelos de datos, almacén JSON, lectura de transcripts, lector de los pasos de una sesión, mezcla de `settings.json`, detección de procesos y de secretos, pintado de la statusline (`TerminalRenderer`, el mismo para ak y para la vista previa del editor), y el motor de los tableros de tareas | `Models.cs`, `TerminalLayout.cs`, `JsonStore.cs`, `SessionStore.cs`, `TranscriptReader.cs`, `SessionTrace.cs`, `SessionCatalog.cs`, `SecretDetector.cs`, `SettingsInstaller.cs`, `ClaudeProcess.cs`, `AppPaths.cs`, `BoardProfile.cs`, `BoardClient.cs`, `BoardHttp.cs`, `BoardStore.cs`, `TaskBoard.cs`, `TaskLinks.cs`, `Worktrees.cs`, `RepoBoards.cs`, `GitReview.cs`, `CodeCatalog.cs` |
| **Hook** | Lo invoca Claude Code como statusline y como hook; también instala, desinstala y diagnostica | `Program.cs`, `Statusline.cs`, `HookHandler.cs`, `Installer.cs` |
| **Desktop** | La ventana: lee el almacén, pinta, avisa; el panel y sus secciones, entre ellas Sesiones y Tareas | `MainWindow.axaml(.cs)`, `PanelWindow.axaml(.cs)`, `DataMonitor.cs`, `ViewModels.cs`, `Notifier.cs`, `Attention.cs`, `SecurityMonitor.cs`, `SessionsView.axaml(.cs)`, `Web/sesiones.*`, `TasksView.axaml(.cs)`, `TaskDirectory.cs`, `BoardsSettingsView.axaml(.cs)`, `Palette.cs`, `Themes/`, `Windows/ModernToasts.cs`, `Windows/TerminalWindow.cs` |

## Flujo de datos

```
Claude Code ──statusline (en cada actualización)──▶ ak.exe statusline ──────┐
            ──hooks (inicio, prompt, pregunta,  ──▶ ak.exe hook ────────────┤
               permiso, plan, fin, cierre)                                  ▼
                                                  %LOCALAPPDATA%\ClaudeStats\data
                                                                            │ lee cada segundo
                                                        Arkko.exe (ventana) ◀┘
                                                  + transcripts, solo para el recap
```

1. **Claude Code ejecuta `ak.exe`** en cada evento y le pasa un JSON por la entrada estándar.
2. **`ak.exe` actualiza los ficheros** del almacén y termina (unos 200 ms). A la statusline le
   devuelve además las dos líneas que se ven en la terminal; al hook `Stop`, la línea de resumen del
   prompt.
3. **La ventana relee el almacén cada segundo** y solo repinta si algo ha cambiado: con ventana
   translúcida, cada repintado se recompone entero en CPU.

### Hooks que registra

| Evento de Claude Code | Estado resultante |
|---|---|
| `SessionStart` | Lista; guarda el proceso `claude.exe` de la sesión |
| `UserPromptSubmit` | Trabajando |
| `PreToolUse` (`AskUserQuestion`, `ExitPlanMode`) | Pregunta o Plan |
| `Notification` (permiso, diálogo) | Permiso |
| `PostToolUse` (todas) | Vuelve a Trabajando tras una espera |
| `Stop` | Terminado; calcula los tokens del prompt |
| `SessionEnd` | Retira la sesión |

Al cerrar cada turno, `Stop` mira en el transcript con qué empezó: un prompt, un comando «!» o un aviso de una
tarea en segundo plano. Claude Code guarda los dos últimos como mensajes de la persona con etiquetas propias
(`<bash-input>`, con la salida en `<bash-stdout>`, y `<task-notification>`), y un comando «!» no pasa por
`UserPromptSubmit`: sin mirarlo, el turno se apuntaría con el texto del prompt anterior. El registro guarda el
tipo, y solo los prompts cuentan como prompts; los otros dos suman su tiempo y sus tokens.

La statusline actualiza en cada refresco los límites de uso, el modelo, el contexto, el esfuerzo y
el nombre de la sesión.

## De dónde lee

| Dato | Origen |
|---|---|
| Límites de uso (5 h y semanal) | JSON que Claude Code pasa a la statusline (`rate_limits`) |
| Modelo, contexto, esfuerzo, nombre de la sesión | Ese mismo JSON |
| Estados de la sesión | Eventos de hook |
| Tokens por prompt y por modelo | Transcript de la sesión, `~\.claude\projects\...\<sesión>.jsonl`, y el de sus subagentes |
| Recap | Entrada `away_summary` del transcript, que Claude Code escribe al rato de parar la sesión |
| Pasos de una sesión (Sesiones) | Su transcript y los de sus subagentes, con el `.meta.json` de cada uno, que dice qué llamada lo lanzó |
| Si la sesión sigue viva | Proceso `claude.exe` guardado al empezar (PID y hora de arranque) |
| Terminal que traer al frente | Antecesores del proceso `claude.exe` con ventana visible |

Dos detalles al contar tokens: una misma respuesta de la API aparece en varias líneas del transcript
(se cuenta una sola vez por `message.id`), y los subagentes escriben en ficheros aparte
(`<sesión>/subagents/*.jsonl`), que también se suman.

## Dónde guarda

Todo en `%LOCALAPPDATA%\ClaudeStats\data` (se cambia con la variable `CLAUDE_STATS_HOME`):

| Fichero | Contenido | Lo escribe |
|---|---|---|
| `sessions\<id>.json` | Estado actual de cada sesión | Hooks y statusline |
| `prompts\<día>.jsonl` | Un registro por prompt terminado, con tokens por modelo, el texto del prompt (hasta 1000 caracteres), el nombre de la sesión y la ruta del transcript | Hook `Stop` |
| `limits-history\<día>.jsonl` | Evolución de los límites: una foto cuando cambia un punto entero o la renovación, y la primera de cada día | Statusline |
| `states\<día>.jsonl` | Historial de estados: una línea cada vez que una sesión cambia de estado (trabajando, esperando, terminada…) o se cierra. Para el tiempo esperándote del Diario (76) | Hooks y, al cerrar una sesión caída, la ventana |
| `task-history\<día>.jsonl` | Cambios de columna de las tareas: clave, columna de antes y de después, y el estado del proveedor, con la hora de la lectura que lo vio | Ventana, al leer los tableros |
| `task-states.json` | La última columna conocida de cada tarea leída, para ver el siguiente cambio. Solo claves y columnas | Ventana, al leer los tableros |
| `diary\<día>.json` | Resumen del Diario de un día: bloques de trabajo por sesión, con sus esperas, y el parte por tarea (commits, PR y cambios de columna). No caduca; el principio de los prompts que lleva, sí, con el texto de los prompts | Ventana, al arrancar y cada hora (hoy y ayer) y al abrir el Diario |
| `limits.json` | Últimos límites de uso, juntando los de todas las sesiones recientes | Statusline |
| `statusline-last.json` | Último JSON recibido por la statusline, tal cual | Statusline |
| `statusline\<id>.json` | Último JSON de la statusline de cada sesión; `limits.json` junta los límites de las de las últimas 12 h | Statusline |
| `reviewed.json` | Sesiones terminadas marcadas como vistas | Ventana |
| `app-settings.json` | Posición, tamaño, tema, silencio, secciones plegadas | Ventana, al cerrar |
| `terminal-layout.json` | Piezas, orden, separadores y colores de la statusline, y si se muestran la barra y el resumen | Editor de la terminal, al guardar |
| `backups\` | Copias de `settings.json` antes de cada instalación o cambio de reglas. Claude Code no las puede leer: llevan las claves que tuviera el original | Instalador y sección Seguridad |
| `security\` | Hallazgos de secretos, ignorados y rotados, y la foto de las claves escritas en la configuración, cifrados con DPAPI; la clave de las huellas, y hasta dónde se ha analizado cada transcript y el historial de prompts ([seguridad](deteccion-de-claves.md)); las claves de los tableros escritas en la app (`board-secrets.bin`), cifradas; y los tokens de la sección Tokens (`plan.bin`), cifrados | Ventana, en segundo plano, Configuración → Tableros y Tokens |
| `boards.json` | Conexiones con los tableros de tareas (perfil, nombre, color, dirección, de qué variable sale cada clave, la confianza en el certificado y los tableros elegidos) y el patrón de la clave de tarea. Ninguna clave | Configuración → Tableros |
| `board-profiles\<id>.json` | Perfiles importados, tal como se pegaron | Configuración → Tableros |
| `task-plan.json` | Planificación local de Tareas: claves traídas del backlog o devueltas, y hasta cuándo se han limpiado las hechas de cada tablero. Ni tareas ni personas | Sección Tareas |
| `artifacts.json` | Lo publicado en claude.ai desde las sesiones: enlace, clase, título, sesión, carpeta, fechas y versiones, y si se ha quitado de la lista. Se conserva aunque Claude Code borre el transcript | Ventana, en segundo plano |
| `artifacts-gallery.json` | La lista de la galería de claude.ai (enlace, título y última versión), que deja una sesión de Claude Code con la herramienta `Artifact`; la app la junta con su índice. La app no tiene acceso a la cuenta | Una sesión de Claude Code |
| `artifacts-scan.json` | Hasta dónde se ha leído cada transcript para el índice de artifacts, y las llamadas que esperan su respuesta | Ventana, en segundo plano |
| `errors.log` | Fallos; nunca interrumpen Claude Code | Todos |

**Por qué ficheros y no una base de datos:** escriben a la vez muchos procesos cortos (un hook por
evento y sesión). Cada escritura va a un fichero temporal que luego sustituye al original, bajo un
mutex con nombre entre procesos: un lector nunca ve un fichero a medias, y no hace falta servidor ni
dependencias.

La sección Sesiones guarda sus preferencias de vista (anchos, plegados, orden de los grupos, sesiones
vistas) en el almacenamiento local de su página, dentro de `%LOCALAPPDATA%\ClaudeStats\webview-desktop`,
la carpeta de datos de su WebView. Son comodidades de la vista: si se pierden, vuelve a sus valores de serie.

**Fuera de esa carpeta:**

- `~\.claude\settings.json`: las entradas de statusline y hooks y las reglas `deny` de los hallazgos
  y de las copias de `settings.json`, al instalar o desinstalar (y, si falta alguna, al abrir la
  configuración); las reglas `deny` recomendadas, desde la sección Seguridad; y los plazos
  de los transcripts (`cleanupPeriodDays`, `desktopSessionCleanupPeriodDays`), desde Datos y
  retención. Siempre con copia previa en `data\backups`; el resto de la configuración se conserva.
- `HKCU\Software\Classes\AppUserModelId\Arkko`: el registro de remitente de
  notificaciones (nombre e icono), que la ventana crea con la primera notificación; el de cuando la app se
  llamaba Agent Pulse, `AgentPulse`, lo borra entonces, con sus notificaciones. El icono se cambia antes de
  cada notificación según el modo de Windows (la letra clara en modo oscuro y la oscura en modo claro).
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `ClaudeStats`: el arranque con Windows, si
  está activado; al abrirse, la ventana lo cambia a `Arkko.exe` si aún abría el `AgentPulse.exe` de su carpeta.

## Ventana

- **Datos y vista.** `DataMonitor` lee el almacén y detecta cambios de estado; `MainWindow`
  construye las filas (`SessionRow`, `LimitRow`, `ModelRow`) y `MainViewModel` las expone a la vista
  por *data binding*. Una lista solo se sustituye si su contenido cambia.
- **Lo que se repite cada segundo, barato.** El repaso de la ventana pide la configuración de los
  tableros, sus perfiles y el plan de tareas por cada sesión y cada chip. `JsonStore.Read` guarda los
  bytes de cada fichero con su fecha y su tamaño y solo lo vuelve a abrir si cambian (abrir un fichero
  es lo caro: el antivirus revisa cada apertura), y `BoardStore.Profiles` hace lo mismo con la carpeta de
  perfiles. Cada lectura sigue devolviendo un objeto nuevo. Varios avisos de tareas leídas dan un solo
  repintado (`ScheduleRefresh`).
- **Temporizadores.** Ninguno corre sin hacer falta: el de Estadísticas, solo con la sección a la vista;
  Seguridad se salta lo que no ha cambiado; el refresco de tableros, lo leído hace poco. Una animación
  sin fin mantiene la ventana repintando aunque su elemento esté escondido: el aro del cargador de Sesiones
  solo gira a la vista. Los campos estáticos que usa una instancia única van antes que ella, porque se
  inicializan en el orden en que están escritos.
- **Memoria.** El recolector compacta el montón de objetos grandes cuando tiene muchos huecos
  (`System.GC.ConserveMemory` en el proyecto).
- **Temas.** `Themes/Dark.xaml` y `Themes/Light.xaml` son intercambiables en caliente; los colores de
  estado y de la escala de uso se calculan en código (`Brushes_`) según el tema activo.
- **Atención.** Orden de la lista (primero lo que te espera), sesiones sin revisar, contador rojo
  sobre el icono de la barra de tareas y recordatorio a los 5 minutos (`Attention.cs`).
- **Avisos.** `Notifier` pinta el icono de la bandeja y envía las notificaciones: modernas
  (`ModernToasts.cs`) y, si Windows las rechaza, globos de la bandeja.
- **Clic y menú de sesión.** El clic en una tarjeta o fila publica `CardClicked`, que la da por
  revisada. El clic derecho abre `SessionMenu` (en `App.xaml`): «Ver sesión» e «Ir a la terminal».
  Esta última está deshabilitada: su código, que usa `TerminalWindow` para traer al frente la ventana
  de la terminal, se conserva para la terminal integrada.

## Sección Estadísticas

- **Cifras.** `History` (en `Arkko.Core`) agrega los prompts del periodo: `Summarize` da las
  cifras clave; `Series`, las mismas por barra del histórico, para las mini gráficas; y
  `PreviousWindow` y `Between`, el tramo anterior con el que se comparan.
- **Gráficas.** Dibujadas a mano en `Charts.cs`: `StackedBar` (histórico y caché), `StepChart`
  (límites), `Sparkline` (mini gráfica de cada cifra) y `ShareBar` (reparto entre proyectos).
  `ProjectColors` da a cada proyecto un color fijo por una huella de su nombre: `GetHashCode` cambia
  en cada arranque.
- **Cuándo se repinta.** Solo si cambia algo: el periodo, los prompts o los límites, y cada diez
  minutos, porque el tramo anterior crece con la hora. Rehacer las barras cerraría la ficha que se
  esté mirando.

## Sección Sesiones

- **Lector.** `SessionTraceReader` (`SessionTrace.cs`) convierte un transcript y los de sus subagentes en
  pasos (prompt, respuesta, razonamiento, herramienta, agente, skill, aviso), partidos en interacciones
  y con cada secreto ya sustituido por su máscara. `SessionTraceBuilder` es el mismo lector por partes:
  recuerda hasta qué byte leyó cada fichero y solo toma líneas completas, para seguir una sesión en
  directo. Ignora lo que no reconoce: el formato del transcript no es un contrato de Claude Code.
- **Lista.** `SessionCatalog` lista las sesiones del periodo y busca en todos sus prompts, con una caché
  por fichero. Al elegir un día salen las sesiones con actividad ese día (`ActiveDays`, por la hora de
  cada línea del transcript), no solo las que acabaron en él.
- **Página.** La sección es `Web/sesiones.html`, `.css` y `.js`, servida desde la carpeta de la
  instalación con un host propio (`agentpulse.local`). La política de seguridad no deja cargar nada de
  fuera, cualquier navegación a otro sitio se cancela, y un enlace del chat se abre en el navegador solo
  si es `http` o `https`. Un cambio solo de la página se despliega copiándola a `bin\Web`, sin compilar.
- **Norma: igual en el Corcho y en las ventanas sueltas.** Todo lo que funciona en la ventana de Sesiones funciona igual
  en un corcho y en una ventana suelta: es la misma funcionalidad, no un extra. Ahí cada parte (el Flujo, el chat, Cambios,
  la ficha de un fichero, las notas de la revisión) es una página aparte (`sesiones.html?piece=…`, `PiecePane`), así que
  una función se diseña para cada pieza en que tiene sentido, con los mensajes entre piezas que haga falta, y se prueba en
  un corcho antes de darla por hecha. Nada se engancha solo a la ventana principal (`!PIECE`) sin decidir qué hace en las
  piezas.
- **Mensajes.** `SessionsView` y la página hablan por mensajes JSON. La página pide la lista, una
  sesión, el detalle de un paso o una imagen; la app le manda el tema, las sesiones activas, los pasos y
  lo que pide. La entrada y el resultado de cada herramienta no van con los pasos: se piden al
  desplegarla o al pasar por encima, porque una sesión larga daría megas de JSON.
- **La respuesta final, con formato.** En la ficha «Respuesta final» del directo, un botón la pinta con el formato de
  Arkko (`ffShape` y `ffFormatted` en `sesiones.js`): reconoce sobre el texto, sin tocar la página, la entradilla, las
  secciones y su estado por las palabras del título, los puntos numerados, las listas, las tablas, el código, los
  comandos `!` y lo que le toca a la persona, que va al final. Lo que no reconoce va con el Markdown de siempre; sin nada
  que pintar, el botón sale deshabilitado. Se recuerda en los ajustes de la app (`FinalReplyFormat`, por el mensaje
  `finalFormat`).
- **En directo.** Con la sección a la vista, cada 1,5 s se lee lo nuevo de la sesión elegida y de las
  activas, y solo se manda si ha cambiado. Al salir de la sección, se para. La app manda la sesión
  entera, pero el chat no se vuelve a montar: conserva los pasos que no han cambiado y pinta desde el
  primero distinto.
- **Pintar sin el foco.** WebView2 arranca con `--disable-features=CalculateNativeWinOcclusion` y
  `--disable-backgrounding-occluded-windows`. Edge deja de pintar una ventana que cree tapada o en
  segundo plano, y con varias pantallas se equivoca: lo nuevo del chat no se veía hasta hacer clic.
- **Una lectura fallida no cierra la sesión.** Los hooks reescriben el estado de cada sesión a cada
  paso, y leerlo justo entonces falla. `DataMonitor` conserva entonces la última lectura buena mientras
  su fichero siga ahí; si no, la sesión desaparecía un segundo del tablero y Sesiones dejaba de seguirla.
- **Arranque.** WebView2 en frío tarda unos 2 s en tener la página, y mientras enseña casi negro sea cual
  sea su color de fondo. La vista web se queda escondida (`Visibility.Hidden`, que conserva su tamaño)
  hasta que la página manda «ready», y mientras la app enseña su propio cargador. La precarga, al abrir el
  panel en otra sección, suele esconder también esa espera.
- **Volver a la sección.** La página pide otra vez la sesión que tenía; si es la que ya está cargada, se
  lee solo lo nuevo con su `SessionTraceBuilder`. Con la sección escondida no se le manda nada: lo pintaría
  para nada, y al volver le llega al día.
- **En directo, solo lo nuevo.** Cada paso lleva una huella de lo que puede cambiar (su fin, su resultado,
  su agente, la respuesta a su pregunta, sus secretos) y se manda un `tracePatch` desde el primero que ha
  cambiado; la página lo completa con lo que ya tenía y, si no le cuadra, pide la sesión entera.
- **El lector va a trozos.** `ReadNew` lee 1 MB cada vez e interpreta cada línea desde sus bytes, sin
  pasar el fichero entero a texto: abrir una sesión de 72 MB pide unos 220 MB al recolector en lugar de
  700, y no deja la memoria de la app en cientos de MB.
- **Sesiones largas.** Leer cuesta menos de un segundo incluso con decenas de MB: el detector de secretos
  solo mira lo que se enseña de cada texto y un margen (`RedactClip`), no el texto entero, que era lo más
  caro. A la página le llega menos de 1 MB. Lo caro es colocar miles de elementos, y lo evitan cuatro
  reglas de la página:
  - la secuencia solo tiene en la página las filas a la vista y un margen: todas miden lo mismo, así que
    cada una se coloca por su número y se pintan al hacer scroll (`paintSequence`). Con miles de pasos,
    cada novedad en directo pasaba de ~290 a ~70 ms;
  - el diagrama y el chat llevan `contain: strict`. Su tamaño lo da su panel, y sin la contención el
    navegador coloca entero el contenido de los dos para tantearlo: con una sesión de 1.800 pasos en la
    secuencia, de 1 s a más de 7;
  - una fila de la secuencia lleva solo el principio del texto del paso, que es lo que cabe;
  - las respuestas largas se miden todas juntas para decidir si llevan «Ver más», no una a una.
- **Lo que se lee no se mueve.** Abrir o cerrar un paso del chat, o que llegue su detalle, repinta solo
  ese paso. Cuando el chat se repinta entero, se recoloca por el primer elemento que se veía y no por
  los píxeles: recién pintado aún no tiene los «Ver más», y lo de arriba mide distinto hasta que
  aparecen. Por píxeles, la vista saltaba cientos de píxeles en cada repintado.
- **Muchos subagentes.** Un agente se nombra por su encargo (`description` del `.meta.json`) y se
  colorea por su rama: el agente de primer nivel del que cuelga, con su color guardado por agente porque
  se pide en cada trazo. El padre sale de la llamada `Agent` que lo lanzó o, si no la hay (una skill con
  contexto propio), del `parentAgentId` de su `.meta.json`; el modelo, del `.meta.json` o de su primera
  respuesta. La secuencia reparte los carriles como intervalos: cada agente toma el primero libre
  mientras trabaja. El mapa coloca las ramas en fila con sus subagentes apilados debajo y lleva el zoom
  con la propiedad CSS `zoom` sobre el escenario, de modo que la colocación se mide sin zoom y llevar la
  vista a una tarjeta se mide en pantalla.
- **Secretos.** `SecurityMonitor` reparte los hallazgos pendientes por sesión, leyendo el almacén cifrado
  una vez por cambio. Un paso se marca si su texto contiene la máscara de un hallazgo, que es la misma
  con que ya va tapado. El inicio y Sesiones usan la misma regla para el escudo.

## Sección Tareas

- **Perfiles.** La app no sabe nada de ningún proveedor: un perfil (`BoardProfile`), importado pegándolo,
  describe como datos las llamadas de su API, la autenticación, la paginación (cursor, desplazamiento o
  enlace), las llamadas en dos pasos y en qué ruta JSON (`JsonPath`) está cada dato. `BoardClient` lo
  ejecuta y devuelve lo mismo para cualquiera: proyectos, subtableros, tareas y quién es la persona.
  `BoardProfileHelp` lleva la plantilla de la ayuda, que una prueba comprueba que es un perfil válido.
- **Conexiones.** `BoardStore` guarda la configuración sin claves y las claves aparte, cifradas; una
  clave puede leerse de una variable de entorno, y entonces solo se guarda su nombre.
- **Certificados.** `BoardHttp` comprueba siempre el certificado del servidor y no deja desactivarlo. Para
  una CA que no está en el equipo, la conexión puede confiar en ese certificado por su huella SHA-256 o
  en una raíz propia; el nombre del servidor tiene que coincidir siempre.
- **Lectura.** `TaskDirectory` lee los tableros elegidos en segundo plano al arrancar y cada 15 minutos,
  y Tareas lo hace además al entrar y cada 5 minutos a la vista. Todo queda en memoria.
- **Columnas.** `TaskBoard.Arrange` es una función pura: reparte las tareas entre el backlog y las
  columnas de la app con la traducción de estados del perfil, la planificación local (`TaskPlan`) y los
  filtros.
- **Tarea y sesión.** `TaskLinks` saca la clave de tarea de la carpeta de cada sesión (el worktree, que se
  llama como su rama) con el patrón de la configuración, y suma lo que ha costado cada tarea a partir
  del histórico de prompts.
- **PR de la tarea.** La tarjeta y el detalle enseñan la PR del worktree que lleva su clave, de lo que ha
  leído `WorktreeMonitor` (sección Worktrees); si ya sobra, el detalle da la orden que cierra el worktree.

## Sección Worktrees

- **Qué repositorios.** `Worktrees.Read` (Core) parte de las carpetas de todas las sesiones (`SessionCatalog.Folders`,
  que solo lee el principio de cada transcript), encuentra en
  disco la carpeta de git que comparten (`.git` o `.bare`, por el `commondir` de cada worktree) y lista sus
  worktrees con `git worktree list --porcelain`. Un clon con un solo worktree no sale.
- **Cómo está cada uno.** Por worktree, `git status` (sin candado del índice, como Cambios) y los commits
  sin subir a su rama remota. La carpeta borrada que git aún apunta sale como tal.
- **La PR.** Una sola consulta `ls-remote` por repositorio (`refs/pull/*/head` y las ramas), guardada media
  hora. La PR de una rama es la que apunta a su último commit subido; está mergeada si ese commit ya está en
  la rama base (develop, main o master). Si el último commit de la base no está en el equipo, se trae esa
  rama sola (`git fetch` a `origin/<base>`): es lo único que escribe. Una PR cerrada sin mergear sale como
  abierta, y un squash merge no se reconoce.
- **Se puede cerrar** un worktree cuya PR está mergeada, cuya tarea está hecha o descartada, o cuya carpeta ya
  no existe. La app no borra nada: copia la orden que lo cierra (`/close-worktree CLAVE` en un repositorio con
  `.bare`, `git worktree remove` en otro, `git worktree prune` si ya no hay carpeta).
- **Lectura.** `WorktreeMonitor` lee fuera del hilo de la ventana, en dos tiempos: lo local (sin preguntar al
  remoto, con las PR que ya sabía) y, si a algún repositorio le falta una respuesta reciente, las PR, con los
  repositorios a la vez. Lee a los 15 s de arrancar, cada 10 minutos y al entrar en Worktrees o en el Tablero
  si lo leído tiene más de 5; lo comparte con los dos. De paso lee el subtablero de cada repositorio (`RepoBoards`).

## Sección Artifacts

- **La vista.** La entrada es una galería de tarjetas por repositorio (con el orden y los plegados de la persona en
  `data/artifacts-view.json`), con el color de cada clase. Al abrir una, la lista compacta a la izquierda y lo abierto a la derecha,
  en el navegador integrado; la ✕ vuelve a la galería. En un recuadro estrecho, la galería o lo abierto.
- **El navegador integrado**, `BrowserPane`, es un visor compartido para ver webs dentro de la app (de momento, solo en
  Artifacts): carpeta de datos propia, `webview-navegador`, aparte de la de Sesiones, donde se queda la sesión iniciada de las
  webs; ningún puente con la app, porque no se atiende ningún mensaje de la página; solo http y https; y una ventana nueva se
  abre en el mismo visor. La dirección se lee y se copia, no se escribe: es un visor, no un navegador.
- **De dónde sale.** `ArtifactIndex` (Core) lee los transcripts de Claude Code y de Cowork, como la detección de
  secretos y con el mismo lector (`JsonLines`): solo lo nuevo de cada uno y solo las líneas que nombran la herramienta
  `Artifact`, el conector de Claude Docs o un enlace de claude.ai. Cuenta como publicación una llamada a `Artifact` que
  publica con una respuesta que dice que se hizo («Published», «Updated», «Created»), y una del conector de Docs que
  crea o cambia un documento; las acciones que no publican y los enlaces sueltos del texto, no. La clase sale del tipo
  de claude.ai con que se creó (Documento, Presentación, Dashboard, Diseño, Animación), y sin tipo es una página; el
  título, de la etiqueta `title` del HTML de origen si sigue en el equipo. Las líneas `api-request-blob` se saltan: repiten
  llamadas que ya están en su línea normal.
- **Lectura.** `ArtifactMonitor` lo pone al día fuera del hilo de la ventana: al arrancar mira todos los transcripts
  (la primera vez, unos segundos) y después, cada minuto, los escritos desde la vez anterior. Lo comparten la
  sección, la pastilla de la cabecera de Sesiones, el Diario y Worktrees.
- **Lo que no hace.** Lo publicado vive en claude.ai y la app no tiene acceso a la cuenta: no hay vista previa, y
  «Quitar de la lista» solo lo marca en `artifacts.json`, sin tocar claude.ai ni los transcripts. Lo que una sesión
  borró con `Artifact` sale como borrado. Solo se ve lo de este equipo: el chat de claude.ai no deja transcript. Lo demás llega de la galería, si una
  sesión deja su lista en `artifacts-gallery.json` (el menú «Galerías» copia la petición): sale aparte, «De claude.ai»,
  sin repositorio, sesión ni clase, y de lo que conocen los dos se toma el título de la galería, que es el de ahora.

## Revisión de una rama y lo aplicado por `/opsx:review`

- **Dos ficheros, dos dueños.** La revisión que anota la persona vive en `docs/reviews/<worktree>-review.md`
  (`GitReview`), fuera de git; solo la escribe la app. Lo que aplica `/opsx:review` en cada ronda va al lado, en
  `<worktree>-applied.json`; solo lo escribe el agente y la app solo lo lee (`GitReview.WithApplied`). El estado de una
  nota nunca se escribe en el `.md`.
- **Cada entrada, con su nota** por el `id` y el `createdUtc` juntos, comparado al segundo: al borrar la última nota, la
  siguiente reutiliza su `id`, y la de otra fecha es otra nota. Una nota que no está en el `.json` está pendiente.
- **Se lee** cada vez que la ventana manda la revisión a la página: al abrir la sesión, al poner al día los cambios de
  la rama (en directo, tras cada paso) y al anotar, marcar o quitar. Sin fichero, todo pendiente y sin aviso; roto o de
  otra rama, se ignora con un aviso discreto, sin romper la revisión.

## Colores del código

- **El catálogo, en un fichero.** `Web/codigo.json` dice qué lenguajes se colorean (extensiones, motor, comentarios,
  palabras clave y de control) y qué temas hay. La ventana lo lee con `CodeCatalog` y se lo manda entero a la página
  al abrirla; para añadir un lenguaje basta su entrada, y un tema necesita además sus colores en `sesiones.css`.
- **Coloreado propio, sin librerías.** Un pintor por visor lleva el estado de una línea a la siguiente; motores de
  código (C#, JavaScript…), de marcas (HTML, XML, XAML), Razor (marcas con sus `@` y C# dentro), Markdown y texto.
  Lo que solo se sabe compilando (locales, parámetros) no se distingue.
- **El tema, por lenguaje.** Cada visor lleva el de su lenguaje en `data-ct` y el CSS pone sus colores en
  variables, así que cambiarlo es cambiar ese atributo, al momento y sin repintar. Lo elegido se guarda en
  `code_themes` de los ajustes (`CodeThemes`), solo si no es el de serie; se elige en la cabecera del código y en
  Configuración → Código.

## Colores de repositorio y subtablero

- **Uno solo.** `RepoColors` da el color de un repositorio (Sesiones, Diario, Estadísticas, Worktrees) y el de
  un subtablero (Tablero, Configuración → Tableros), y los dos coinciden cuando van juntos.
- **La pareja.** Un repositorio va con el subtablero del `board` de su `.arkko.json` (`RepoBoards`, que lo
  busca en la raíz de cada carpeta de sesión) o, si no declara ninguno, con el que se llama igual.
- **El orden.** El elegido por la persona (`GroupColors` de la configuración, que escriben Sesiones y, para un
  subtablero con repositorio, Configuración → Tableros); el que tenía elegido el subtablero solo; el que da el
  panel; y uno fijo por el nombre del repositorio (`ProjectColors`). Un subtablero sin repositorio sigue con
  su color de Tableros, el del panel o uno por su nombre.
- **Sesiones** recibe los elegidos más el de su subtablero en los repositorios que tienen uno; sin ninguno, el
  grupo sigue neutro, como siempre.
- **Legibles en cualquier tema.** Se pintan con la luminosidad ajustada en OKLCH (mínimo 0,62 en los temas
  oscuros, máximo 0,65 en el claro), con el mismo tono; el color guardado no cambia. En la ventana lo hace
  `HexColor.Readable` desde `RepoColors`; en Sesiones, el CSS a partir de `--g-raw`, con los mismos umbrales.
- **Franjas con texto encima** (cabecera de Worktrees, ficha del Diario, franja ancha y banda del Tablero): el
  texto va siempre en blanco y la franja lleva el color oscurecido hasta 0,6 (`RepoColors.Band`, con
  `HexColor.UnderWhite`).

## Instalación y despliegue

- **Primera instalación:** `install.ps1` compila las dos piezas: los hooks a `%LOCALAPPDATA%\ClaudeStats\bin`
  y la ventana a `%LOCALAPPDATA%\ClaudeStats\desktop`, cada una con su copia de `Arkko.Core.dll`.
  Registra statusline y hooks en `~\.claude\settings.json`, con copia de seguridad.
- **Actualizar los hooks:** `scripts\deploy-hooks.ps1`, con una etiqueta nueva. Aparta lo instalado,
  copia lo nuevo a `bin` y prueba **ahí** que Windows deja ejecutar `ak.exe`; si sale bloqueado,
  prueba otra etiqueta, y si no entra ninguna, devuelve lo apartado. No toca `settings.json`. Un
  `ak.exe` bloqueado rompe la statusline y los hooks de todas las sesiones: hasta el 2026-10-06 la
  prueba era en una carpeta temporal, y una compilación que pasó allí salió bloqueada en la de instalación.
- **Actualizar la ventana:** `scripts\deploy-desktop.ps1`, con el mismo esquema sobre `desktop`. La
  prueba de arranque usa una carpeta de datos vacía, así que no choca con la ventana abierta.
- **Solo la página de Sesiones** (`Web/`): basta copiarla a `desktop\Web` con la ventana cerrada; sin
  binarios nuevos, Smart App Control no interviene.

**Smart App Control.** Los ejecutables no están firmados y Windows decide por reputación, compilación
a compilación, si los deja ejecutar. Por eso nunca se compila directamente sobre las carpetas en uso.
El arreglo definitivo es firmar la app (ver la 104 en [tareas](estado.md)).

## Versiones

| Versión | Dónde | Para qué |
|---|---|---|
| **De producto** (`DisplayVersion`) | `src/Directory.Build.props` | La que se ve en el menú («v. 1.5.0»). Se sube a propósito en cada entrega |
| **De los ficheros** (`Version`) | Se pasa al compilar con `-p:Version=…` | Cambia el contenido de las DLL para pedir otra evaluación a Smart App Control; no tiene significado de producto |
| **Etiqueta de compilación** (`BuildTag`) | Se pasa al compilar con `-p:BuildTag=…` | Varía solo la DLL de la ventana o de `ak`, sin recompilar la librería común |

La de los ficheros y la etiqueta se ven en el tooltip de la versión, en el menú de ajustes.

## Red y seguridad

- **Solo sale a la red para leer los tableros de tareas** que la persona configure (desde la mejora 52):
  por HTTPS, a las direcciones de sus conexiones, solo para leer y con el certificado comprobado. Sin
  ninguna conexión configurada, no sale a la red. La página de Sesiones no carga nada de fuera.
- **git habla con el remoto de los repositorios de las sesiones** para saber sus PR (Diario, modo
  revisión y Worktrees), con las credenciales de git del equipo y sin pedirlas nunca: `ls-remote` y, en
  Worktrees, un `fetch` de la rama base sola.
- **Sin permisos de administrador.** Solo se escribe en el perfil del usuario.
- **Datos:** quedan en el equipo; lo que se guarda son estados, tokens, rutas de proyectos y el recap
  que ya escribe Claude Code.
- **Secretos:** la ventana busca secretos enviados a la API, en los transcripts y en el historial de
  prompts, y claves escritas tal cual en la configuración de Claude Code. Guarda solo su máscara y
  una huella, cifradas. `ak install` añade reglas `deny` para que Claude Code no lea esa carpeta
  ni las copias de `settings.json` ([seguridad](deteccion-de-claves.md)).
