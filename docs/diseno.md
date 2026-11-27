# Diseño y decisiones

Cómo es Arkko por fuera y por qué: los modos de ventana y el panel, el aspecto, la terminal
integrada que viene y la vista de cómo trabaja una sesión. Cómo está hecha por dentro, en [arquitectura](arquitectura.md); la detección de
secretos, en [deteccion-de-claves](deteccion-de-claves.md); lo pendiente, en [tareas](estado.md).

## Ventana y modos

El maximizado es el modo **estándar** de Arkko (mejora 33): un panel grande para analizar el
uso de Claude Code y, más adelante, con la consola integrada ([mejora 36](#terminal-integrada)). La
ventana flotante sigue para tenerla a la vista.

### Los modos

| Modo | Qué es | Tamaño |
|---|---|---|
| **Maximizado** (estándar) | El panel: análisis y, después, la consola | Libre: ventana normal de Windows que se maximiza, se encaja a media pantalla o se divide. No está siempre encima |
| **Normal** | La ventana flotante de hoy, siempre encima | Fijo, el actual: no se redimensiona. Solo se encoge sola al plegar límites, modelos o sesiones. Las sesiones van **por páginas de 3** (`‹ 1 / 2 ›`), como en el compacto, en lugar de scroll |
| **Compacto** | Como hoy | Fijo |
| **Pastilla** | Como hoy | — |

- **Paso entre modos:** el botón de maximizar de la flotante abre el panel; el panel tiene un botón
  propio «Vista flotante» para volver. Los botones de Windows del panel (minimizar, restaurar,
  maximizar) son los normales, para encajarlo en pantalla.
- **Arranque:** en el último modo usado.
- **Una ventana a la vista:** por dentro son dos ventanas, porque la flotante es transparente (bordes
  redondeados y cristal) y una ventana así ni puede volverse normal mientras está abierta ni alojar
  WebView2, que necesitará la consola. Al pasar de una a otra, la que se deja se oculta: nunca se ven
  las dos a la vez.

### La pastilla y por qué no un widget

**Minimizado como pastilla** (mejora 32): al minimizar, la ventana se convierte en una pastilla en la
esquina, encima de la barra de tareas, con una gota de color por sesión y el contador. Se descartó
minimizar de verdad y mostrar el estado solo en el botón de la barra de tareas (contador, barra de
progreso y miniatura personalizada al pasar el ratón): obliga a pasar el ratón para ver las sesiones.

**Por qué no un widget en la barra de tareas:** Windows 11 eliminó las bandas de escritorio (paneles
dentro de la barra), y el panel de Widgets (Win + W) solo admite apps empaquetadas y firmadas (MSIX).

### El menú del panel

El panel se organiza con un menú lateral. Las estadísticas dejarán de ser lo principal cuando llegue
la terminal integrada ([mejora 36](#terminal-integrada)):

| Sección del menú | Qué tendrá |
|---|---|
| **Terminal** | Las sesiones de Claude Code en pestañas: será la vista principal |
| **Estadísticas** | El panel de hoy (cifras, histórico, proyectos, límites, caché, prompts caros) |
| **Configuración** | Lo que hoy está repartido: el menú del engranaje (tema, avisos, «no molestar», vista), el editor de la terminal y los datos y la retención (14) |
| **Seguridad** | El informe de la detección de secretos ([mejora 35](estado.md)) |

Barra lateral a la izquierda con Inicio, Sesiones, Estadísticas, Seguridad ([seguridad](deteccion-de-claves.md)) y Configuración; Terminal aparece como «próximamente». Se pliega con el
botón ☰ y recuerda si estaba plegado. Al abrir la app, el panel arranca siempre en Inicio. El icono y el nombre de la app están
arriba del menú; la barra de título muestra el nombre de la sección y sus acciones (el periodo y
«Exportar CSV» solo en Estadísticas).

- **Inicio** es la página principal: lo de la ventana flotante aprovechando el ancho (límites y
  «Hoy por modelo» a la izquierda, todas las sesiones en rejilla a la derecha, sin paginar). Usa
  las mismas plantillas que la flotante, y el clic en una sesión hace lo mismo.
- **Configuración** tiene, de momento, los datos y la retención; el resto llegará aquí.
- **Plegado del menú:** una opción en Configuración elige cómo se pliega, a riel de iconos o
  desapareciendo del todo, con solo el botón ☰ para volver a abrirlo.

### Estadísticas: qué responde cada sección

| # | Sección | Qué responde |
|---|---|---|
| — | Cifras clave | Cuánto se ha trabajado en el periodo: tokens nuevos, prompts, sesiones, tiempo de Claude trabajando, % de caché. Cada una con su icono y color, su mini gráfica en el periodo y el cambio frente al tramo anterior |
| 6 | Histórico | Qué días se consume más y qué modelo se lleva el límite |
| 7 | Por proyecto | Qué repositorio gasta más; útil para imputar horas. Barra de reparto arriba y, en cada fila, el color del proyecto de fondo a escala del primero |
| 9 | Caché | Si el aprovechamiento baja, algo rompe la caché y se gasta más |
| 8 | Prompts más caros | Contextos enormes o subagentes de más. Con puesto, proyecto en su color y barra de calor; el clic abre la sesión del prompt |
| — | Evolución de los límites | Cómo suben el de 5 h y el semanal a lo largo del tiempo |
| 10 | Exportar | El periodo a CSV, que Excel abre directamente |
| 14 | Datos y retención | Cuánto ocupa, cuántos días se guardan (90 por defecto) y «limpiar ahora» |

### Decisiones

| Tema | Decisión |
|---|---|
| **Modos** | Maximizado como estándar y redimensionable; la vista normal, de tamaño fijo y con sesiones por páginas de 3 (ver «Los modos») |
| **Cómo se abre** | Con un botón de maximizar en la barra de título de la ventana flotante; se vuelve con «Vista flotante» |
| **Arranque** | En el último modo usado |
| **Siempre encima** | El maximizado no: es una ventana normal. La flotante sí, como hoy |
| **Cifra de las gráficas** | Tokens nuevos (entrada, salida y escritura en caché), como en el resto de la app |
| **Prompts más caros** | Las dos cosas: el inicio del texto del prompt (con el completo en el tooltip) y los metadatos (fecha, proyecto, sesión, tokens, duración). Se revisará al verlo |
| **Evolución de los límites** | Sí: `ak` anota cada cambio de los límites |
| **Comparación de las cifras clave** | Con el tramo anterior de la misma duración, cortado a la misma hora: a las 10:30, «Hoy» se compara con ayer hasta las 10:30, y «7 días» con los 7 anteriores hasta esa hora. Con «Horario laboral», también el tramo anterior. «Todo» no compara |
| **Color del cambio** | Gastar más o menos no es mejor ni peor: la flecha va en el color de la cifra. Solo la caché, donde subir es bueno, sube en verde y baja en ámbar, y se mide en puntos. Por debajo del 3 % (o de 1 punto), «= igual» |
| **Color de cada proyecto** | Fijo por nombre, de los colores de datos del tema, para que un proyecto se reconozca en cualquier periodo; «Otros», en gris |
| **Tiempos en el panel** | Con espacio entre unidades: «2m 05s», «1h 07m». La statusline sigue sin espacios, para ocupar poco |

### Cómo se construye

- **Gráficas dibujadas en la propia ventana**, sin librerías externas: son barras y líneas sencillas, y cada
  fichero nuevo es una evaluación más de Smart App Control.
- **Agregaciones en `Arkko.Core`, con tests**: por día, modelo y proyecto, % de caché,
  prompts más caros. Servirán también para el informe de la [mejora 35](estado.md).
- **CSV con `;` y UTF-8 con BOM**, para que Excel en castellano lo abra bien a la primera.
- **Lo que toca `ak` va primero:** solo acumula desde que se instala.

## Aspecto visual

Lo que hay hoy en el aspecto de la app; lo que falta, en el área Visual de [tareas](estado.md). Las
decisiones visuales estaban solo en comentarios dentro del XAML y del C#; aquí quedan en un sitio
donde consultarlas antes de inventar un color o un tamaño nuevo.

La fuente de la verdad sigue siendo el código de la ventana (`src/Arkko.Desktop`): `App.axaml`
y `Styles/` (estilos y plantillas compartidas), `Themes/` (los colores de cada tema) y el color que se
calcula en `Palette.cs`, `ViewModels.cs` y `Charts.cs`.

Esta parte se escribió para la ventana de WPF y sirvió de especificación a la de Avalonia (mejora 40),
que la sustituyó el 2026-10-26: valen las decisiones; las rutas de fichero y números de línea que cite
pueden ser los de WPF, que no se incluye en esta copia.

### Los temas

Dos temas, **24 claves cada uno y las mismas en los dos**, en `Themes\Dark.xaml` y
`Themes\Light.xaml`. Se consumen siempre con `DynamicResource`, así que el cambio es en caliente.

| Grupo | Claves |
|---|---|
| Texto | `Fg`, `FgMuted`, `FgFaint` |
| Acento | `Accent` |
| Cristal | `GlassBase`, `GlassEdge`, `Sheen`, `LensFill`, `LensEdge` |
| Controles | `ButtonFill`, `ButtonHover`, `TrackFill`, `TrackEdge`, `ThumbFill`, `ThumbHover`, `RingInner`, `RingTrack` |
| Estados y énfasis | `ChipOn`, `ChipOnEdge`, `SwitchOn`, `PrimaryFill`, `PrimaryText` |
| Fichas y tooltips | `TipFill`, `TipEdge` |

- **Oscuro:** cristal grafito, neutro y ligeramente frío. **Claro:** cristal esmerilado gris perla,
  con las tarjetas más claras que la base.
- **El borde especular cambia de criterio entre temas**, a propósito: en oscuro es un reflejo de luz
  que entra por arriba a la izquierda; en claro es un borde gris en todo el contorno, porque un
  reflejo blanco no se distinguiría del fondo.

### El color de los datos

Los colores de modelos y series son la paleta **One Dark**, y hoy viven en cuatro sitios distintos:

| Dónde | Para qué |
|---|---|
| `Charts.cs:88-92` | Colores de modelo: Opus `#F09A76`, Sonnet `#61AFEF`, Haiku `#98C379`, Fable `#C678DD`, otros `#ABB2BF` |
| `PanelWindow.xaml.cs:654-666` | Caché `#56B6C2`, límite de 5 h `#E5C07B`, semanal `#56B6C2`, otros límites `#C678DD` |
| `TerminalLayoutView.xaml.cs:480-481` | Los diez colores elegibles del editor de la terminal |
| `PanelWindow.xaml:371,390,409` | Tres puntos de leyenda, con el hexadecimal escrito a mano |

**Un valor por tema.** El tema claro lleva los mismos tonos con el brillo bajado y algo más de
saturación. Hasta la V2 los dos temas compartían los valores del oscuro, y en claro ninguno llegaba a
3:1 contra el gris perla —lo razonable para un elemento gráfico que hay que distinguir del fondo—:
las barras se lavaban y el ámbar del límite de 5 h, el peor con 1,3:1, era casi invisible.

Contraste contra el tono medio del cristal de cada tema, `#16181F` y `#DCE0E8`:

| Clave | Oscuro | Claro | Antes, en claro |
|---|---|---|---|
| `DataOpus` | `#F09A76` ~7,4:1 | `#C7633A` 3,0:1 | 1,7:1 |
| `DataSonnet` | `#61AFEF` ~8:1 | `#3A84C2` 3,0:1 | 1,8:1 |
| `DataHaiku` | `#98C379` ~8,8:1 | `#608B41` 3,0:1 | 1,5:1 |
| `DataFable`, `DataOtherLimit` | `#C678DD` | `#B15DCA` 3,0:1 | 2,2:1 |
| `DataOther` | `#ABB2BF` | `#7A808B` 3,0:1 | 1,6:1 |
| `DataCache`, `DataSevenDay` | `#56B6C2` ~8:1 | `#348B96` 3,0:1 | 1,8:1 |
| `DataFiveHour` | `#E5C07B` ~9:1 | `#A57723` 3,0:1 | **1,3:1** |

Los tres cálidos llevan más saturación que el resto: un amarillo o un naranja, al oscurecerse, se
convierte en barro, y con la saturación alta el ámbar sigue leyéndose como dorado.

Para situarlos: en el tema claro, `FgFaint` está a 3,6:1 y `Accent` a 2,4:1. Los colores de datos
pesan ahora algo más que el acento de la propia app; si alguna vez molesta, lo que habría que revisar
es el `Accent` claro, no bajarlos otra vez.

### La escala continua de uso

Para anillos de contexto y barras de límites, en `ViewModels.cs:563-589`.

- Recorre el **tono** en HSV entre cuatro paradas —130° a 0 %, 58° a 45 %, 32° a 70 %, −4° a 100 %—
  con saturación 0,82 y brillo fijo. **Mezclar en RGB entre verde y rojo da marrones**, y por eso no
  se hace así.
- Brillo 0,80 en los anillos y 0,70 en las barras: las barras son más largas y pesan más en la vista.
- **Igual en los dos temas, y aquí sí está justificado:** saturados y de brillo medio, se leen sobre
  claro y sobre oscuro.
- Un pincel cacheado por tramo de 5 % y forma. Las filas comparan pinceles por referencia, y crear
  uno nuevo en cada refresco obligaría a repintar la ventana cada segundo.

### Estados de sesión y esfuerzo

En la clase `Palette` de `ViewModels.cs:534`, **con versión clara y oscura**: estados (lista,
trabajando, esperando, terminada, urgente), los seis niveles de esfuerzo, los bordes y los tintes de
tarjeta. Es la parte del color mejor resuelta de la app y el patrón que deberían seguir las demás.

### El rojo de atención

Decidido el 2026-10-05: **un solo rojo para peligro o atención, `Danger`**. `#E03E4C` en oscuro y
`#D2383E` en claro, más oscuro para que se lea como texto sobre el gris perla.

- **Lo usan:** el contador de la barra de tareas, el latido y el chip de las tarjetas sin revisar, la
  espera urgente (más de 15 min), la previsión de un límite que se agota, la papelera y la pastilla
  «Pendiente» de Seguridad, el botón de cerrar al pasar el ratón y el icono de los diálogos de error.
- **En XAML** va con `DynamicResource Danger`; **en código**, con `Theme.Of("Danger", …)`, que también
  da la versión translúcida para un fondo tenue. La espera urgente vive en la paleta de estados de
  `ViewModels.cs` (`High`), que repite el valor de `Danger` en cada tema y debe ir a la par.
- **«Elegido» o «encendido» tiene su propio granate, y se queda:** el chip elegido (`ChipOn`,
  `ChipOnEdge`: `#A3141D` al 40 % con borde `#C2323C` en oscuro, `#86050D` al 15 % con borde `#86050D`
  en claro) y el interruptor encendido (`SwitchOn`: `#B0222C` y `#86050D`). Es el color propio de la
  app, no un aviso, y no se unifica con `Danger`. Se probó pasarlos al `Accent` y la persona lo
  descartó.

Antes había cinco rojos de aviso: `#E03E4C`, `#E0485C` al 80 % en el botón de cerrar, `#E5534B` en
«Pendiente» y `#FF6B6B` en la espera urgente y los errores (`#D2383E` en claro).

### La terminal, siempre oscura

Decidido el 2026-10-05: la vista previa de «Personalizar terminal» es **oscura en los dos temas**, con
sus tres colores propios en `App.xaml`, fuera de los temas: `TerminalBack` `#0D1117`, `TerminalEdge`
`#30363D` y `TerminalText` `#C9D1D9`. No es un descuido del tema claro:

- Enseña la terminal (Warp, Windows Terminal), no la app, y esas terminales casi siempre son oscuras
  sea cual sea el tema de Arkko.
- La paleta de las piezas incluye blanco y tonos claros (`#E5C07B`, `#ABB2BF`), hechos para fondo
  oscuro: sobre uno claro, la vista previa mentiría justo en lo que tiene que enseñar.

Si algún día hace falta una terminal clara, lo correcto no es seguir el tema de la app sino un
selector de fondo en la propia vista previa, que simule la terminal de verdad.

### Tipografía

- **Interfaz:** `Segoe UI Variable Text, Segoe UI` a 12, fijado en la raíz de las cuatro ventanas.
- **Iconos:** `Segoe MDL2 Assets`. **Monoespaciada:** `Cascadia Mono, Consolas`, para rutas y para la
  vista previa de la terminal.
- `TextOptions.TextFormattingMode="Display"` en las cuatro ventanas.
- **Escala tipográfica**, en `App.xaml`: `Metric` 22 seminegrita (la cifra grande de una ficha),
  `Body` 12 (texto corrido, el mismo de la raíz), `Label` 11 (etiqueta o dato secundario) y `Caption`
  10 (nota al pie y cabecera de sección en mayúsculas). Solo fijan el tamaño; el color lo pone cada
  sitio. `SectionTitle`, `MenuLabel` y las cabeceras de columna de tabla heredan de `Caption`.
- **Sin medias de punto** desde el 2026-10-05. Se diluyen con `Display`, que cuantiza las métricas al
  píxel: se pagaba la inconsistencia sin ganar el matiz. Fuera de la escala quedan 8 (el chevron y el
  «%» del anillo), 9 (iconos de la barra), 13 (títulos de ventana y de tarjeta), 14 y 15 (iconos y
  títulos de sección y de diálogo).
- Muchos textos llevan aún el tamaño entero en línea en vez del estilo. Se pasan a la escala cuando
  se toca su fichero.

### Forma y espacio

- **Radios:** cuatro, en `App.xaml`: `RadiusXS` 4 · `RadiusS` 8 · `RadiusM` 12 · `RadiusL` 16. L es la
  carcasa de una ventana; M, una tarjeta, una ficha o un botón; S, una fila, una cabecera o un chip;
  XS, un resalte dentro de una gráfica. Lo que no es un radio sino una forma queda fuera a propósito
  —círculos, pastillas cuyo radio es la mitad de su alto y la geometría de las barras—, y está
  explicado junto a la escala.
- **Espaciado:** escala de 4 —0, 4, 8, 12, 16, 24— en todos los `Margin` y `Padding` desde el
  2026-10-06 (V12). Lo que estaba fuera se llevó al más cercano y, en los empates (6, 10, 14, 20), al
  múltiplo de 8, para que la densidad quede parecida; un margen negativo que compensa a otro se
  redondea igual que su pareja. Quedan fuera, a propósito, los valores de 1 a 3 y los decimales, que
  son ajustes ópticos o geometría (los anillos de foco), y los de más de 24, que no son espacio sino
  posiciones para alinear con otra columna.
- **167 anchos y altos fijos en píxeles**, lo que limita cuánto aguanta el panel al estrecharse.
- **Sombras:** solo cuatro usos (`App.xaml`, `AppDialog.xaml`, `SecurityView.xaml`). La profundidad se
  consigue con el cristal y los bordes, no con sombras.

### Navegación por teclado

**Hoy, sin marca de foco en ningún control** (decidido el 2026-10-22, en la 40.4): la app se usa con el
ratón y los atajos solo se añaden cuando se piden. Lo que sigue es lo que tenía la ventana de WPF.

Desde el 2026-10-05, el foco de teclado se ve: un **anillo de 1,5 px con el `Accent`, a 2 px del
control**, en lugar del rectángulo punteado de WPF, que rompía el acabado de cristal. WPF solo lo
pinta cuando el foco llega con el teclado, pero lo deja puesto mientras el control conserve el foco,
también si después se pulsa en algo que no lo coge, como una tarjeta o el fondo. Por eso `FocusCue`
lo oculta con cualquier clic, en cualquier ventana, y lo vuelve a enseñar con Tab o las flechas. Hay una variante por forma
—`FocusRing` (chips y botones), `FocusRingS` (filas), `FocusRingPill` (interruptor) en `App.xaml`, y
`FocusRingNav` y `FocusRingTab` en el panel, ajustadas al margen interior de sus filas—, porque el
anillo rodea los límites del control y su radio es el del control más 3,5. Las zonas con scroll y las
listas siguen sin foco visible: no se pulsan.

Lo que aún no llega con el teclado: las **tarjetas de sesión**, que no son controles enfocables sino
bordes con clic. Y en el tema claro el `Accent` está a 2,4:1 contra el fondo, por debajo del 3:1 que
se pide a un indicador de foco; si molesta, la salida es la misma que en los colores de datos:
revisar el `Accent` claro.

## Terminal integrada

Propuesta de la [mejora 36](estado.md): una terminal propia dentro de Arkko, para usar las
sesiones de Claude Code como hoy se usan en Warp, pero hecha a medida e integrada con la app.

Estado: **propuesta, bloqueada** por la elección de pila de la mejora 40 (40.1 en [tareas](estado.md)).

### Antes de la fase 1: elegir la pila

Decidido el 2026-10-03 ir a multiplataforma al 100 %. Eso cambia esta propuesta, porque ConPTY y
WebView2 son de Windows: construir la fase 1 sobre ellas obligaría a rehacerla entera.

Conviene aclarar una confusión: **web aquí no es «una página en el navegador»**, es con qué se dibuja
la interfaz de una app de escritorio. Tauri y Electron son apps de escritorio —se instalan, tienen
ventana, icono, notificaciones y acceso a los ficheros, y funcionan sin conexión—; lo único distinto
es que la interfaz se describe en HTML y CSS en lugar de en XAML. Una app web servida desde un
servidor está descartada por diseño: no podría leer `~/.claude`, ni registrarse como hook, ni poner
el contador sobre el icono. Y el plan actual **ya** mete una isla web en la ventana, porque xterm.js
es JavaScript y WebView2 un navegador incrustado.

| Opción | Qué es | La terminal |
|---|---|---|
| **A. Avalonia** | Ventana en C# y XAML en los tres sistemas; se conserva el Core y los conceptos de WPF | **El problema:** Avalonia no tiene un WebView sólido en los tres sistemas, así que la isla de xterm.js se vuelve pantanosa, y una terminal nativa no existe hecha |
| **B. Tauri o Electron** | Interfaz web de arriba abajo, también app de escritorio | Sale casi de regalo: xterm.js en su terreno y los pty de Unix son terreno trillado |

Si se va por la B, **el C# no se tira**: `ak` ya es un ejecutable aparte, así que cruza de
plataforma recompilando. Se queda en C# leyendo transcripts, agregando estadísticas y detectando
secretos —con sus pruebas—, gana un modo que escupa los agregados en JSON, y la ventana se los pide
en vez de releer ficheros. Sin eso habría que reescribir en TypeScript cosas que ya funcionan, y ahí
sí se pierde valor. De paso, una fuente nueva ([mejora 41](estado.md)) se añadiría una sola vez.

**El criterio para decidir:** si la terminal integrada es el futuro de la app, la B. Si lo que importa
es el panel y la terminal es secundaria, la A.

### Objetivo

- Tener la terminal a gusto propio: colores, fuentes, pestañas, atajos y temas a juego con la app.
- Integrarla con las sesiones: la app sabe qué pestaña es cada sesión porque el proceso de Claude
  es hijo suyo.
- Abrir un proyecto y lanzar `claude` desde la propia app.

### Qué resuelve de lo pendiente

| Mejora | Hoy | Con la terminal integrada |
|---|---|---|
| **1. Ir a la sesión con un clic** | Se trae al frente la ventana de Warp, pero no la pestaña: Warp no permite elegirla desde fuera | El clic en una tarjeta selecciona la pestaña exacta |
| **30. Clic en la notificación** (descartado por ahora) | No abre nada | Si se retoma, la notificación podría llevar a la pestaña de su sesión |

### Cómo se haría

| Pieza | Qué es | Notas |
|---|---|---|
| **ConPTY** | La «pseudoconsola» de Windows (`CreatePseudoConsole`): ejecuta PowerShell, `claude` o cualquier programa como si estuviera en una terminal real | API de Windows 10 1809+. Hace falta una envoltura propia en C# (crear la pseudoconsola, las tuberías de entrada y salida, redimensionar) |
| **xterm.js en WebView2** | El motor de terminal de VS Code, dentro del navegador integrado de Windows | xterm.js se incluye con la app, sin descargar nada: la app sigue sin conexiones de red. WebView2 viene con Windows 11 y está firmado por Microsoft |
| **Arkko** | Pestañas, perfiles, integración con las sesiones y estilo | En la ventana, alrededor del WebView |

Alternativas descartadas:

- **Mostrar la salida en un cuadro de texto:** no sirve, porque Claude Code es una interfaz de
  terminal interactiva (colores, menús, cursor).
- **Incrustar la ventana de una consola real:** frágil (foco, redimensionado, DPI) y desaconsejado
  por Microsoft.
- **Control de Windows Terminal para WPF** (el que usa Visual Studio): nativo y rápido, pero su
  paquete no está pensado para uso general y está poco documentado.

### Por dónde empezar

Cuatro fases (36.1 Prototipo, 36.2 Pestañas y perfiles, 36.3 Integración y 36.4 A medida), con su
detalle en [tareas](estado.md).

Recomendación: elegir primero la pila (arriba) y después empezar por la fase 1 como ventana aparte,
para valorar si convence antes de invertir en el resto. Más adelante se puede integrar en el modo maximizado ([mejora 33](estado.md)).

### A tener en cuenta

- **No es Warp.** Sus funciones propias (bloques de comandos, su IA, autocompletado avanzado) no
  vienen; se pueden añadir cosas parecidas poco a poco.
- **Smart App Control.** WebView2 está firmado por Microsoft, pero las DLL propias seguirán sujetas a
  la evaluación de cada compilación. Firmar la app gana todavía más importancia.
- **Esfuerzo.** La fase 1 es asequible; las siguientes crecen en detalle: atajos, copiar y pegar y
  selección de texto suelen dar trabajo.
- **Sesiones.** Las sesiones abiertas en la terminal integrada siguen usando los mismos hooks de
  Claude Code, así que aparecen en la ventana como cualquier otra.

## Ver cómo trabaja una sesión

[Mejora 45](estado.md): una vista para seguir el trabajo de una sesión —qué skills usa, qué agentes
despliega y qué herramientas llama— con un diagrama de flechas, bocadillos tipo cómic cuando habla y,
al lado, un chat con cada paso; y, terminada una interacción, recorrer lo que hizo. Decidida con la
persona el 2026-10-06 y terminada el 2026-10-08 en la rama `feature/vista-sesion`. Cómo está hecha, en
[arquitectura](arquitectura.md#sección-sesiones).

### De dónde salen los datos

Todo del transcript de la sesión, que Claude Code escribe mientras trabaja. Comprobado en
transcripts reales:

| Qué | Cómo se sabe |
|---|---|
| **Herramientas** | Cada llamada (`tool_use`) y su resultado (`tool_result`), enlazados por id y con hora: orden y duración |
| **Agentes** | Cada subagente escribe su transcript en `<sesión>/subagents/agent-*.jsonl`, con un `.meta.json` que da su tipo, su descripción, su profundidad y el id de la llamada `Agent` que lo lanzó |
| **Skills** | La llamada `Skill` dice cuál, y los pasos que provoca llevan `attributionSkill` |
| **Mensajes** | Los textos de la persona y de Claude |
| **Razonamiento** | Solo algunos bloques `thinking` traen texto (uno de cada seis en las sesiones revisadas); el resto viene vacío |
| **Orden y repetición** | Toda entrada lleva hora y el enlace a la anterior (`parentUuid`) |

**El formato del transcript no es un contrato estable de Claude Code**: el lector es defensivo, ignora
lo que no reconoce y tiene tests con la forma real. Una actualización de Claude Code puede obligar a
ajustarlo.

### Decisiones

| Tema | Decisión |
|---|---|
| **Tecnología** | Web (HTML y JavaScript) en un **WebView2** dentro de la app, con las librerías incluidas en ella y sin red. El lector va en C#, en el Core, y entrega los pasos en JSON. Sobrevive si la [mejora 40](estado.md) acaba en la opción B (Tauri o Electron); con la A (Avalonia), la isla web sería el mismo problema que la terminal integrada |
| **Dónde** | Sección nueva del panel, **«Sesiones»**, con el icono de conversación, debajo de Estadísticas. Desde Inicio, clic derecho en una tarjeta o fila de sesión → **«Ver sesión»** |
| **Qué sesiones** | Las abiertas, en directo, y el historial mientras su transcript siga en disco (Claude Code los borra pasado su plazo) |
| **Encontrar una pasada** | Lista flotante que se abre con ☰ (al entrar está recogida y sin foco): las pasadas por día, con proyecto y worktree, título y primer prompt. Buscador por proyecto, título o texto de cualquier prompt, sin mayúsculas ni acentos, y los chips Hoy, 7 días y Todo, más un día concreto en un calendario propio |
| **Unidad** | Al abrir una sesión, su **historial de interacciones** (un prompt de la persona y todo lo que provocó hasta devolverle el control), con la hora, el principio del prompt y su resumen («🤖 2 agentes · 🔧 34 herramientas · 4 min»). Se abre en **«Toda la sesión»**, con el chat en lo último (cambiado en el repaso visual; al principio se abría en la última interacción); una interacción elegida se lee desde su prompt |
| **Diagrama** | Dos pestañas: **Secuencia** (por defecto; se cambia en Configuración → Sesiones → «Vista por defecto»), con una columna por actor y las flechas en el orden en que pasaron, y **Mapa**, con un nodo por actor y flechas de quién llama a quién y cuántas veces |
| **Chat** | Un mensaje por paso, con nombre y emoji: 🧑‍💻 la persona, con el nombre de su perfil («User» si está vacío) · 🧠 Claude, con el modelo · 🤔 razonamiento · 🤖 agente, con su tipo · 🧙 skill · 🔧 herramienta. Herramientas y agentes van plegados y se despliegan |
| **Perfil** | Campo «Tu nombre» en Configuración, para los mensajes de la persona |
| **Contenido** | Completo, con cada secreto que reconoce el detector de Seguridad sustituido por su máscara: la app nunca enseña un valor |
| **Repetición** | Paso a paso, con botones y teclado, y «Reproducir» con velocidad elegible. En cada paso, el diagrama marca dónde va, sale su bocadillo y el chat se coloca en su mensaje |

### A tener en cuenta

- **Smart App Control.** WebView2 está firmado por Microsoft y sus DLL no han dado más problemas que
  los de siempre con los ejecutables propios. Un cambio solo de la página (`Web/sesiones.*`) no necesita
  compilar: basta con copiarla a la instalación.
- **Sesiones largas.** Una sesión de cientos de pasos se lee en menos de un segundo y el diagrama sigue
  fluido, pero abrir una muy larga aún tarda en enseñar algo: es la [mejora 46](estado.md), en su propia
  rama.
- **Relación con la 40.** Se decidió hacer la 45 antes que la elección de pila: es lo que la persona
  quiere atacar ya, y la opción web es la que menos se tira si se cambia de pila.

### El repaso visual

Decidido con la persona el 2026-10-06, a la vista de capturas de la primera versión, que funcionaba
pero se leía mal.

| Tema | Decisión |
|---|---|
| **Dirección** | «Herramienta de trazas»: densa y técnica, al estilo de las de depuración y observabilidad |
| **Vista por defecto** | El mapa |
| **Color por actor** | El mismo en chat, secuencia y mapa: la persona en verde, Claude en naranja, cada agente en el suyo |
| **Chat** | Tipo Telegram: burbujas de la persona a la derecha y de Claude a la izquierda, en sus colores; respuestas con Markdown |
| **Distribución** | Diagrama y chat con un divisor arrastrable; el chat se puede minimizar; barra superior con la repetición y el buscador |
| **Secuencia** | Grafo + texto + cascada: columnas estrechas por actor, el texto del paso a todo el ancho y una barra con su duración. Las herramientas no cruzan la pantalla: son un punto en la columna de quien las llama |
| **Mapa** | Tarjetas por actor con sus herramientas y skills dentro; agentes del mismo tipo agrupados y desplegables |
| **Mapa en directo** | Un agente aparece al lanzarse, late mientras trabaja y, al terminar, se apaga y pasa a «Terminados (N)». La repetición se ve igual |
| **Buscador** | Resalta y salta entre coincidencias, con contador, sin ocultar el contexto |
| **Lista de sesiones** | Sin «30 días»; buscar mira todas las sesiones, sea cual sea el periodo; «Toda la sesión», fija abajo en las interacciones |
| **Chat como conversación** (2026-10-07) | Las herramientas, ocultas por defecto con un botón que las enseña; el razonamiento, como bocadillo de pensamiento; lo que dicen los agentes, directo en el hilo; las preguntas de AskUserQuestion, como tarjeta con la respuesta debajo |
| **Activas a la izquierda** (2026-10-07) | El riel desaparece: en su sitio, las sesiones abiertas agrupadas por repositorio (o por carpeta, si no son de un worktree), con su estado y la que se ve resaltada, como la barra lateral de Warp. Se pliega a una tira de gotas y se recuerda |
| **Barra superior** (2026-10-07) | El nombre de la sesión en lugar de «Secuencia · Toda la sesión», con proyecto, worktree, interacciones y «En directo»; ☰ abre las listas; «Todo» y ◂ ▸ entre interacciones, junto a la repetición |
| **Filtro de la lista** (2026-10-07) | Sin «Activas», que ya están a la izquierda: Hoy, 7 días y Todo, y un día concreto; el selector se apaga en «Hoy» y en «7 días» solo deja elegir entre hoy y seis días atrás |

### Lo decidido al pulirla

Del 2026-10-07 al 2026-10-08, con la persona probando cada cambio en la app.

| Tema | Decisión |
|---|---|
| **Barra superior** | El título es «repo · worktree», con el logo de la herramienta de agente delante (de momento solo Claude Code), el nombre de la sesión debajo y un anillo con el contexto. Lleva la repetición y las pestañas en una sola barra, y dos pastillas cuando tocan: «Cerrada · hace…» y «N secretos sin revisar» |
| **Activas** | Grupos por repositorio con el color que elige la persona (clic derecho) y en el orden que deja arrastrándolos, que se recuerda. Lo que pide cada sesión se ve por urgencia: un aro que gira si trabaja, «Sin revisar» como en el inicio (roja si lleva rato) y la pregunta ámbar, o roja si lleva rato, que brilla. Tooltip propio con estado, modelo, esfuerzo, contexto y coste |
| **Revisar** | Abrir una sesión terminada en Sesiones la da por revisada, igual que pulsar su tarjeta en el inicio; también si termina mientras se mira con la ventana delante |
| **Sesión cerrada** | La última cerrada que se abre queda en un hueco «Viendo» encima de las activas, para volver a ella; su ✕ la quita y vacía la vista si era la que se veía. El historial marca con un ojo y un fondo suave las 10 últimas cerradas vistas. Si la sesión que se mira se cierra en la terminal, la vista se vacía |
| **Secuencia** | Lo que se dice va en una banda del color del actor; las herramientas, en gris y sangradas. El tooltip del paso es una tarjeta que apunta a su icono y enseña el texto o la llamada entera (el comando, la ruta completa), y solo sale sobre la columna del paso. La duración es una barra de lo que duró cada paso, a escala con el más largo |
| **Secretos** | Un secreto sin revisar en Seguridad se ve con un escudo rojo que lleva a Seguridad y solo se quita al rotarlo o descartarlo: en la cabecera del grupo de activas, que late; en la barra; en la fila de la secuencia, que parpadea; en la tarjeta del mapa, y en el chat. El inicio usa el mismo escudo y la misma regla |
| **Chat** | Se queda anclado al final mientras se está abajo, con un botón para volver cuando se sube; los enlaces web se abren en el navegador; las imágenes pegadas, como miniaturas |
| **Tema oscuro** | Superficies en gris azulado algo elevado con el color de cada actor de acento, y grises de texto más claros que en el inicio, porque la vista web pinta el texto más fino |

### Sesiones con muchos subagentes

[Mejora 53](estado.md): la vista se pensó para pocos agentes, y con decenas deja de leerse. Decidido
con la persona el 2026-10-15, a la vista de capturas de DEVLAB-99.

**Cómo son.** Las cinco sesiones con más subagentes tienen la misma forma: la sesión principal lanza
pocas fases (nivel 1: «Propose phase», «Apply phase»…) y cada fase lanza varios ayudantes (nivel 2:
commits, revisiones). La mayor, DEVLAB-99, tiene 47 agentes: 6 de nivel 1 y 41 de nivel 2, 44 de ellos
«general-purpose», y nunca más de 4 trabajando a la vez.

**Qué fallaba.** Cada agente era un actor de primera: su columna en la secuencia, su color, su entrada
en la leyenda y su nombre por el tipo, que con 44 «general-purpose» no distingue nada. La secuencia
gastaba el ancho en 49 columnas casi vacías, el mapa agrupaba por tipo agentes de fases distintas y
abría un abanico de flechas hacia una columna interminable, y la leyenda ocupaba cuatro filas.

| Tema | Decisión |
|---|---|
| **Nombre** | El de un agente es su descripción («Apply phase DEVLAB-99»), en la leyenda, la secuencia, el mapa y el chat. El tipo y el modelo quedan como dato secundario |
| **Color** | Por rama: cada agente de nivel 1 lleva un color de la paleta y sus subagentes lo heredan |
| **Leyenda** | Una línea: la persona, Claude y una entrada por rama, con cuántos subagentes lleva |
| **Secuencia** | Carriles reutilizados: un agente ocupa uno mientras trabaja y lo deja al volver, así que hay tantos como agentes a la vez, no como agentes. Como el carril ya no dice quién es, la fila lleva su nombre cuando cambia el actor. Los subagentes salen desplegados, con un botón que pliega los de nivel 2 de golpe |
| **Mapa** | De arriba abajo: la persona, Claude, las fases en fila por orden de tiempo, y bajo cada una sus subagentes apilados en tarjetas compactas que se abren al pulsarlas. Se agrupa por quién lanzó, no por tipo. Conectores en ángulo |
| **Zoom del mapa** | − / % / + y «Ajustar», Ctrl+rueda y arrastrar el fondo. La repetición centra la tarjeta del paso con el zoom que haya |
| **Modelo a la vista** (al probarlo) | Cada tarjeta de agente del mapa lleva su modelo en una pastilla con un color por familia: Haiku, Sonnet, Opus. La duración baja a la segunda línea para dejar sitio al nombre |
| **Ficha del agente** (al probarlo) | Un mismo tooltip en la leyenda, la cabecera de la secuencia y las tarjetas del mapa: modelo, duración con sus horas, quién lo lanzó, herramientas, skills y sus subagentes, cada uno con su modelo y su duración |

### El Diagrama: la sesión como un plano de metro

[Tarea 130.1](estado.md): la primera vista del refactor visual de Sesiones. Decidido con la persona el
2026-11-09 en cinco rondas de opciones sobre DEVLAB-100, cada una en un artefacto con planos que se
podían reproducir. Va como vista nueva, «Diagrama», y «En directo» se conserva hasta que acabe el
refactor.

| Tema | Decisión |
|---|---|
| **Base** | El lienzo de «En directo»: la misma cámara, «Seguir», el zoom, «Ver todo» y la ficha al pulsar una parada. Se orienta solo: en horizontal si el hueco es tan ancho como alto o más, y si no, en vertical |
| **Filas** | Una por actor: tú, Claude y los agentes, que reutilizan la primera fila libre. La etiqueta de cada eje va en el borde, encima de su línea, y se queda en la letra si va a tocar una parada que pasa |
| **Paradas** | Como estaciones de metro. Las que pesan (tu prompt, una pregunta, lanzar o empezar un agente, lo que se entrega), de 46 px, con aro grueso y su icono; llenos tu prompt, el inicio de un agente y lo que se entrega. Pensar y responder, discretos: del tamaño de una herramienta (36 px) y con un aro fino. La respuesta final, algo mayor (48 px), con el aro de su actor y un tinte suave, sin llenarse ni halo. Las herramientas, un punto lleno de su color con el icono en blanco y, si se repiten seguidas, cuántas veces en una chapita. Revisado el 2026-11-10: los iconos eran pequeños y la respuesta final, toda naranja, pesaba demasiado |
| **Rótulos** | El de un mensaje, con su Markdown en línea, como en el directo. En horizontal, dos rótulos de un mismo lado de un mismo eje se separan lo que miden, para que no se toquen |
| **Sin revisar** | La última respuesta final de una sesión «Sin revisar», en rojo, con un brillo que late despacio y «Sin revisar» bajo su rótulo; abrirla la da por revisada, como en el directo. En la repetición no se marca |
| **Agentes que ya terminaron** | Su eje va fino: la línea delgada y sus paradas en puntos, sin rótulos, y su etiqueta dice que terminó. Pulsarla lo abre a su alto, y volver a pulsarla lo recoge. Así no encogen la línea de Claude, que antes se veía pequeña con muchos agentes cerrados |
| **Color** | La línea, del color de quien actúa. Cada herramienta, por lo que hace: leer azul, buscar turquesa, editar rosa, ejecutar lima y el resto gris pizarra. Piensa, en el amarillo de «Pensando»; Responde, neutro. Así el naranja de Claude queda para su línea y su respuesta final |
| **Herramientas de Claude** | Cuelgan un poco de su línea, unidas con un trazo, para que en la línea se lean los mensajes. Las de los agentes van en su línea |
| **Tiempo** | En una píldora sobre el tramo entre dos paradas: en ámbar desde 30 s, a trazos si es una pausa larga y sin escribir por debajo de 2 s. Si no cabe, no se pone |
| **Lo último de cada fila** | Si una fila se queda sin nada a la vista, su último paso se repite, tenue, en el borde, con su nombre y la hora, hasta que vuelve a verse una parada suya. Su ficha es la de la parada, con el mensaje |
| **Movimiento** | El marcador de «estás aquí» recorre la línea hasta cada parada nueva, la línea se dibuja tras él y la parada aparece al llegar; la cámara lo acompaña. Si llegan más de 12 de golpe aparecen todas juntas, y otro prompt entra fundiéndose. El marcador solo se ve en directo o en la repetición |
| **De cerca y de lejos** | De cerca, una parada por paso; de lejos, las llamadas seguidas de una misma clase se juntan en una fase (entender, cambiar, comprobar), y cada parada viaja a la fase en que se junta |
| **Estados** | Lo de ahora, en rojo con «AHORA»; una herramienta trabajando, con un aro que gira; la pregunta que espera, llena de amarillo con una sola onda lenta; la espera a los agentes, a rayas violetas con su órbita |
| **Barra de abajo** | Una franja a lo ancho: el estado a la izquierda, la regla de pasos en medio (pulsarla o arrastrarla lleva la repetición a ese paso) y los controles en iconos a la derecha. La barra de «En directo» no cambia |
| **Sello** | Arriba a la derecha, el proyecto y el prompt, y la rama como una casilla más: un disco azul con su icono, el nombre y, debajo, lo que pasa en palabras (los commits sin subir en azul, las líneas pendientes en verde y rojo con «pendiente» en amarillo, o «✓ al día»); al pulsarla, su lista cuelga del sello. La escala no va: ya está en la barra de abajo. En vertical o en un hueco estrecho, solo la casilla de la rama, y las etiquetas de los ejes bajan para no quedar debajo |
| **Cambios** | El panel de Cambios sale en todas las vistas, no solo en «En directo». En las vistas sin lienzo, un fichero se abre en su propio panel |
| **Descartado al probar** | Una columna fija con fondo para los ejes (tapaba las fases), las ondas y saltos de la pregunta, el peine de rayas para las repeticiones y la onda detrás de la respuesta final |

### Ahora: la sección de la sesión

[Tarea 130.2](estado.md): el corte de la sesión en el instante que se ve. Decidido con la persona el
2026-11-09 y el 2026-11-10 en varias rondas de opciones, cada una en un artefacto. Va como vista nueva,
«Ahora».

| Tema | Decisión |
|---|---|
| **Acabado** | De plano: el papel con una retícula fina y las fichas como láminas con un borde fino de tinta y marcas de esquina, como las de corte. Sin bordes de un solo lado |
| **Ficha grande** | Arriba, a todo el ancho: el titular en una frase («El agente «X» ejecuta una orden») con el reloj grande a su derecha y desde cuándo; debajo, quién y qué hace; la orden en curso, entera; y lo último que terminó, en un recuadro de rayas con su rótulo sobre el borde (la salida de una orden no se guarda hasta que acaba). Si te pregunta, la lámina entera en amarillo, con la pregunta en grande y sus opciones numeradas; si espera a sus agentes, la espera; terminada, «Terminó:» con la primera frase de la respuesta final, lo que duró el prompt y la respuesta con su formato |
| **Debajo** | El Parte a la izquierda y «Quién trabaja» en una columna fija a la derecha, cada uno con su scroll: la vista ocupa lo que se ve y no se mueve entera. En una ventana estrecha o baja, una columna y con scroll |
| **Quién trabaja** | Una lámina por actor: quién, cuánto lleva, qué hace y desde cuándo; quien tiene agentes suyos trabajando, «Espera a…». Sin nadie, una lámina que lo dice, en el mismo sitio, para que la columna no aparezca de golpe |
| **Parte** | Las últimas 40 fases como un hilo de metro: su chapa (pensar, decir y volver, en un disco suave de su color; las herramientas, llenas), su frase en fuerte con quién la hizo en una pastilla si es un agente, su hora, y debajo su detalle en un recuadro de dos líneas: el mensaje con su formato, o los ficheros y las órdenes en mono. Lo de un agente, sangrado; lo de ahora, recuadrado con «AHORA». Si se dejó al final, sigue al final con lo nuevo |
| **Relojes** | De ancho fijo y con cifras del mismo ancho: al pasar de «59 s» a «1 min 0 s» no se mueve nada |
| **Pulsar** | Va sobre el mismo lienzo que «En directo» y el Diagrama, sin plano, para que la ficha sea la misma con los mismos controles (tamaño, acoplar, el formato de Arkko, cerrar, Esc y pulsar fuera). Se pulsa la ficha grande, una línea del Parte o un actor, con la mano como cursor; lo que tiene la ficha abierta queda marcado. Sin tooltips |
| **Preguntas** | La misma regla que el directo: una pregunta espera mientras su llamada no tiene resultado, o si es el paso de ahora de la repetición; contestada, en pasado con su respuesta, y rechazada, «no la contestaste» |
| **Descartado al probar** | Una ventana propia para la respuesta final (se pidió la ficha de siempre), los acabados «sobrio» y «lámina», y que la página entera hiciera scroll |

### El Parte: el trabajo contado por fases

[Tarea 130.3](estado.md): el trabajo de una sesión contado en castellano, con la Secuencia dentro. Decidido
con la persona el 2026-11-10 en una ronda de opciones sobre DEVLAB-100 y retocado al probarlo.

| Tema | Decisión |
|---|---|
| **Una sola regla** | Las fases son las del Diagrama visto de lejos (Entender, Cambiar, Comprobar y Conversación), de cabecera con lo que duraron; dentro, las rachas de Ahora («Leyó 4 ficheros»), y cada racha se despliega en sus pasos (hora, herramienta, sobre qué y lo que tardó). Lo que va solo (una pregunta, un lanzamiento, la respuesta final) es una línea suelta |
| **Aspecto** | El hilo de metro de Ahora, para que las dos vistas se lean igual: las fases, estaciones con su chapa; las rachas, sus líneas con el detalle en un recuadro |
| **Prompts** | Uno por lámina. El título es lo que se escribió y al lado, más claro, su número (`#8`). Abiertos de serie el último y el del paso de ahora; recogidos, dicen cuántas fases y agentes tuvieron. Abierto, su cabecera se queda arriba mientras se lee, para no perder en qué prompt se está, y al recogerlo la página vuelve a ella |
| **Agentes** | Una lámina de rayas de su color bajo su lanzamiento, con «terminó · 33 fases · cambió 15 ficheros» y lo que duró; abierta si trabaja y recogida si terminó, con sus propios agentes dentro |
| **Resumen o cada paso** | Un conmutador en su propia franja, encima de la página, que no tapa nada; con la ficha abierta, en la esquina de lo libre. «Cada paso» es la Secuencia de siempre |
| **Pulsar** | Como en Ahora: va sobre el lienzo, y cada línea, cada paso y cada fila de «Cada paso» abren la ficha de siempre |
| **Descartado** | El Parte como informe o como tabla, los agentes intercalados en el tiempo o en su columna, y el conmutador flotando sobre el contenido |

### Relojes: todas las sesiones a la vez

[Tarea 130.5](estado.md): un reloj por sesión abierta, todos con la misma hora, para ver de un vistazo qué hizo
cada una en la última hora. Decidido con la persona el 2026-11-10 en tres rondas de opciones, cada una en un
artefacto. Empezó como una vista más de una sesión y se cambió: dentro de una sesión repetía lo que ya cuentan el
Diagrama, Ahora y el Parte, y el reloj luce cuando se comparan varias.

| Tema | Decisión |
|---|---|
| **Dónde** | Un apartado propio, «Relojes», en el menú lateral, justo debajo de Sesiones, y un widget del corcho con la misma página y los mismos datos (130.5b). La página elige su tamaño por el ancho, con los cortes de los widgets: en S, cada sesión es su reloj con la carpeta y el estado debajo; en M, la lámina con un cajetín corto (estado y ahora); en L, la lámina entera |
| **La esfera** | La hora real: cada tramo cae en su minuto y se enseña la última hora. El anillo de dentro es la sesión; los de fuera, sus agentes, que comparten anillo si no coinciden en el tiempo, así que hay tantos como agentes a la vez, cada uno con su letra al empezar |
| **Los tramos** | Cada herramienta, de su llamada a su resultado, con los colores del Diagrama; el hueco corto entre dos pasos es Claude pensando; esperar a un agente, a rayas violetas; cada prompt, una raya verde. La aguja roja es ahora; con la sesión terminada no hay aguja, sino una marca de fin |
| **Orden** | Agrupadas por repositorio: cada repositorio es un plano con una franja de su color (el de Sesiones y el Tablero) y su nombre en blanco. Planos y sesiones, por orden de apertura; sin numerar, que la persona no lo quiso. Lo que pide atención lo dice el borde, sin mover nada |
| **Tamaño** | Columnas y tamaño del reloj se eligen para que quepan todos los planos sin scroll, con el reloj más grande posible; un plano ocupa tantas columnas como sesiones tiene, y los pequeños se ponen uno al lado del otro. Por debajo de un mínimo, aparece el scroll |
| **La ficha** | Una lámina apaisada con marcas de esquina: el reloj a la izquierda y un cajetín de plano a la derecha. Su cabecera, la carpeta y la tarea sobre un tinte del color del repositorio; sus filas, el estado con cuánto lleva (los de Inicio), lo que hace (lo que pregunta, la última herramienta con la letra de quien la usa, o su último resumen) y el prompt en curso con lo que lleva; al pie, el modelo con su esfuerzo, el contexto y los agentes de la hora. Las iniciales del centro, del color del repositorio |
| **Pulsar** | Un reloj abre esa sesión en Sesiones |
| **Por dentro** | La app lee cada sesión con la lectura que comparten las páginas que la enseñan, solo mientras se está en el apartado, y manda a la página los tramos ya calculados (`SessionClock`). Medido con cinco sesiones abiertas: unos 16 MB más |
| **Descartado** | Una vista de una sesión con «bajo la aguja»; Inicio con relojes, que es nativo y obligaría a dibujarlos dos veces; una ventana pequeña siempre encima; un solo reloj con una sesión por anillo; y el reloj en las teclas de la pastilla, que se puede retomar con la 130.7 |

### Corcho en directo: los widgets que siguen todas las sesiones

[Tarea 132](estado.md): widgets del corcho para seguir todas las sesiones abiertas a la vez, con las propuestas del
artefacto https://claude.ai/artifact/LoFR31uArxdostPz3E6oM1. Decidido con la persona el 2026-11-11.

| Tema | Decisión |
|---|---|
| **Una lectura para todos** | `LiveSessions` lee las sesiones abiertas una sola vez para todos los widgets en directo, solo mientras alguno se ve, y reparte la misma foto en JSON: estado, lo que espera cada una y desde cuándo, sus últimas acciones, contexto, modelo y su última hora en 30 trozos. Los transcripts son los mismos `SessionFeed` de Sesiones y Relojes. La lógica va en el Core (`SessionLive`), con sus tests |
| **Una página común** | Todos los widgets en directo son la misma página (`directo.html`, `.js` y `.css`), que pinta el que pide su dirección (`?w=tetoca`) y comparte las piezas: el piloto de estado, el repositorio con su color, las acciones y la ficha de los tooltips. Cada widget es su propio navegador, que se descarga a los dos minutos de dejar de verse |
| **Tamaños** | Por el ancho de su recuadro, con los cortes del artefacto: S por debajo de 400 px, M hasta 720 px y L por encima |
| **Te toca** | Preguntas, permisos, planes y revisiones de todas las sesiones, la que más lleva esperando primero; la espera, en rojo pasados 10 minutos. En S, el número y la que más espera; en M, una ficha por cada una, con la pregunta y sus opciones; en L, además, «Antes de esto» con sus tres acciones anteriores. La cabecera del recuadro dice cuántas te tocan |
| **Pulso** | Una fila de estado por sesión, por orden de apertura y sin numerar. En S, un cuadro por sesión con su piloto y su carpeta; en M, el estado con su color y cuánto lleva; en L, además, su última hora en barras, el contexto (ámbar desde el 70 %, rojo desde el 90 %) y el modelo. Arriba, el resumen: cuántas te tocan, trabajan, esperan a sus agentes y están quietas |
| **Ahora, de todas** | Lo que hace cada sesión en este momento. En S, la carpeta y lo que hace; en M, además, quién hizo lo último y cuándo; en L, un bloque por sesión con sus tres últimas acciones. Si espera algo, lo que hace es lo que pide, igual que en «Te toca» |
| **Avisos** | Los límites de 5 h y de la semana, con su barra, su porcentaje y cuándo se renuevan, y las sesiones que pasan del 70 % de contexto. En S, las dos barras y cuántas sesiones van llenas; en M, además, la frase de la previsión (en ámbar si se llega al tope antes de renovarse) y la lista de contexto alto; en L, también la gráfica del límite de 5 horas desde que empezó su ventana, con lo que vendría al ritmo actual y el tramo sin cupo. La previsión es la misma de Inicio (`LimitForecast`), y los colores de los límites, los de su escala de uso; la gráfica sale del histórico de los límites. La cabecera del recuadro dice el aviso más urgente: un límite que pasa del 80 % (rojo), uno que llegaría al tope o una sesión con el contexto por encima del 90 % (ámbar) |
| **Carril de sesión** | Una sesión entera en una columna: la franja de su repositorio, su reloj (el de Relojes, ahora en `reloj.js` y `reloj.css`, que comparten las dos páginas), su estado y lo que hace, lo que espera en la ficha de «Te toca», sus agentes, sus últimas acciones y, al pie, el contexto y el modelo. Cortes propios, más estrechos: S por debajo de 280 px (sin reloj), M hasta 400 px y L por encima. **Sigue a la sesión de una posición**, por orden de apertura, guardada en el widget y elegida en su menú («Seguir a»): así los carriles de una composición se llenan solos con las sesiones abiertas, sin elegir una a una, y se corren si se cierra una. La lectura común solo manda los relojes mientras algún carril escucha |
| **Plantillas** | Son perfiles que vienen con la app, en código (`CorkTemplates`): salen junto a los perfiles guardados, todas en el mismo desplegable: «Abrir una plantilla» en el menú de la barra del corcho y «Plantillas» en el de clavar de Sesiones y de sus paneles, las guardadas arriba y las de la app debajo. Para la persona, perfiles y plantillas son lo mismo: en los menús se llaman plantillas. Se abren igual que un perfil: en el corcho si está vacío y, si no, en uno nuevo con su nombre. Guardada como perfil con su mismo nombre, la de la persona sustituye a la de la app. Al montar un perfil, una vista de la app abierta en otro corcho no se trae de allí, que lo desmontaría: se abre otra igual; las partes de una sesión sí se traen. Cinco: «Directo · Lo que me toca» (A), «Directo · Una columna por sesión» (B, con 5 carriles, como el artefacto), «Directo · Mapa y detalle» (C: la derecha son huecos del Diagrama, el Parte, el Flujo y los Cambios, que se llenan todos con una sesión desde la tecla de cualquiera; sin el cajetín de sesión del artefacto, que no existe como widget), «Lo que me toca» y «Planificar». Una plantilla tocada se guarda como perfil propio |
| **Historia del fichero** | Una parte de sesión («history») para la revisión fichero a fichero: sigue a la última ficha de fichero abierta de su sesión y enseña cada vez que la sesión la cambió, agrupado por ronda de la revisión (si ya se conocen sus rondas) y por el prompt que lo pidió: quién (Claude o un agente), cómo (edita, escribe el fichero entero o, quizá, un comando), las líneas que añade y quita y la primera línea de lo que dijo Claude después. La calcula la app (`FileHistory`), porque la página solo tiene el nombre del fichero de cada paso. En el perfil «Revisión de código · Fichero a fichero» va arriba a la derecha, en pestañas con las notas de la revisión y el Flujo, y el Chat debajo |
| **Tintes** | En «Te toca», «Pulso» y «Ahora, de todas», lo que espera respuesta va en ámbar y una revisión, en rojo. Cualquier fila, cuadro o bloque abre su sesión |
| **Qué cuenta como pendiente** | Que espera lo dicen los hooks; qué pregunta y con qué opciones, una llamada que pregunta y aún no tiene resultado. Un permiso dice la herramienta que se quedó sin resultado o, si no se ha leído aún, el aviso de Claude Code en castellano. Una revisión es una sesión terminada sin marcar como vista, con su último resumen |
| **Contestar** | No se contesta desde el widget: las opciones se enseñan y la ficha abre la sesión, porque escribir en la terminal solo se puede en las lanzadas desde la de Arkko. Una revisión sí se puede **marcar como vista** desde la ficha, como el botón de la tarjeta de Inicio, sin abrir la sesión |

### La obra: la escena de una sesión

[Tarea 131](estado.md): la sesión contada como una obra, al estilo de un videojuego tranquilo. Decidido con la persona el
2026-11-14 tras varias rondas de artefactos con datos reales de DEVLAB-183: el prototipo
(https://claude.ai/artifact/PGF9CFhfZw86ZxijR64wkB) y los casos con la elección
(https://claude.ai/artifact/HQB4i6NVmEkZy6hPLqCCtL). Antes se descartaron la mesa con robots que se mueven en cada
acción («se vuelve loco»), el plano vivo con recorridos y cotas («bonito, pero no se entiende por dónde vamos») y la
hoja de ruta en 2D (le faltaba ver trabajar a los agentes).

| Tema | Decisión |
|---|---|
| **La idea** | Cada tarea es un solar: se marca con estacas y cinta, se construye con andamio mientras el edificio crece con lo que lleva hecho, y se remata con tejado y bandera verde. Claude es el jefe de obra en la caseta, cada agente es un operario con casco y la persona es la dirección de obra, en su oficina (decidido el 2026-11-15: «promotor» no le gustaba). La grúa apunta al solar donde se trabaja |
| **Ritmo** | Nadie se mueve en cada acción: cada operario hace un trabajo continuo (construye, lee planos, prueba, inspecciona, mide) y solo camina al cambiar de tarea. El letrero sobre él resume lo que más repite en los últimos minutos reales. El parte de obra cuenta solo lo importante: quién entra y sale, qué empieza y qué termina, cargas, preguntas y noche. En directo, a tiempo real; la repetición, acelerada, con las esperas largas acortadas |
| **Edificios por sección** | Cada sección del `tasks.md` construye su tipo, todo con aristas redondeadas: torres, tejados a dos aguas, cúpula, cilindros de cristal, pabellones con tejado amarillo para lo de la persona y casetas con cubierta en arco para el triaje |
| **Servicios** | Caseta de dos contenedores (el de arriba en naranja, el color de Claude), mesa de planos bajo pérgola, almacén de chapa con muelle de carga y rótulo «Registro · commits» (el camión es un commit; con el push sale por la barrera), archivo con muro cortina y la oficina de la dirección de obra, acristalada. Los frentes miran a la carretera de servicio y a la cámara |
| **Al abrirla** | Vista isométrica, acabado en color y robots. Se puede cambiar a las vistas Cine, Planta y «Junto al operario», al acabado Plano (azul con aristas) y a figuras. La maqueta blanca se descartó |
| **Interacción** | Pulsar un solar, un operario o un edificio abre su ficha con los datos de ese instante: la tarea, el encargo y el trabajo del operario, las preguntas de Claude, los commits, los documentos o lo que te toca. Un operario se puede seguir de cerca. Los botones llevan al Parte, a los cambios del explorador (134), al transcript, al `tasks.md` y a los commits |
| **Contestar** | Como en el corcho: la pregunta, el plan o el permiso se enseñan con sus opciones, y no se contestan hasta la terminal integrada (36), solo en las sesiones lanzadas desde ella |
| **Dónde vive** | Una vista de Sesiones con la obra grande, y un widget del corcho con la ciudad de obras: una pequeña por sesión abierta, con su «?» si te toca |
| **Formas de trabajar** | Las seis entran (SDD, modo plan, tarea directa, conversación sin cambios, diseño con artefactos y centro de mando), cada una con su forma en la obra (fases 131.1 a 131.8). Fuera del SDD, los temas salen de un corte automático (encargos, pausas y ficheros tocados) y, si Claude apunta su lista de tareas, de la lista |
| **Situaciones** | Entran todas las vistas en la semana del 7 al 14 de noviembre (131.2 y 131.3). Lo más repetido es volver tras una ausencia (29 de 34 sesiones) y que Claude pregunte (23) |
| **Orden** | Primero el SDD en directo; después el parte del día, las situaciones, la tarea directa, la oficina técnica, el modo plan, la ciudad del corcho y la sala de maquetas |
