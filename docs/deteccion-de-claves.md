# Cómo funciona la detección de claves

Guía de la detección de secretos (mejora 35): qué mira, cuándo, qué reconoce, qué se le escapa, cómo
se ve en la app y por qué está hecha así. Detecta y avisa; solo podrá impedir el envío de los
prompts que escribe la persona (35.4). Lo pendiente, en [tareas](estado.md).

Código: `Arkko.Core\SecretDetector.cs` (las reglas), `Arkko.Core\SecretScanner.cs` (la
lectura de los transcripts, del historial de prompts y de la configuración, y el almacén) y
`Arkko.Desktop\SecurityMonitor.cs` (cuándo se analiza).

## La regla de oro

El detector mira dos cosas, y nada más: **lo que ha pasado por una conversación con Claude Code** y
**lo escrito en la configuración de Claude Code**. No recorre el disco, ni los repositorios, ni el
gestor de contraseñas.

- **Lo enviado:** un secreto que pasó por una conversación ya ha llegado a la API. Hay que rotarlo.
- **Lo escrito:** una clave escrita tal cual en la configuración de Claude Code aún no ha salido, pero
  Claude la lee cada vez que trabaja con esa configuración. Hay que sacarla a una variable de entorno
  y dejar en el fichero solo su nombre (`${NOMBRE}`). Decidido el 2026-10-09: una clave nunca debe
  estar escrita tal cual en un fichero de configuración.

Una clave en el `.env` de un repositorio que Claude nunca ha leído no se detecta, y es lo correcto:
ni ha salido del equipo ni está en un sitio que Claude lea por su cuenta. Si Claude lo lee, sale como
enviada; y para que no lo lea está «Qué no puede leer Claude», más abajo.

## Dónde busca

### Lo enviado

- **Los transcripts:** `~\.claude\projects\<proyecto>\<sesión>.jsonl`, y los de subagentes en
  `<sesión>\subagents\`. Son la grabación de todo lo que viajó a la API.
- **Los de Cowork** (desde el 2026-10-15): Claude Desktop lleva un Claude Code propio en cada sesión de
  Cowork, y guarda su transcript con el mismo formato en
  `local-agent-mode-sessions\…\local_<id>\.claude\projects\`, dentro de sus datos: `%APPDATA%\Claude`, o
  `%LOCALAPPDATA%\Packages\Claude_…\LocalCache\Roaming\Claude` si se instaló desde la Microsoft Store.
  Sus rutas pasan de 260 caracteres. Su `audit.jsonl` repite la conversación y no se mira. Los hallazgos
  salen con el proyecto «Cowork».
- **El historial de prompts** (desde el 2026-10-09): `~\.claude\history.jsonl`, con todo lo que se
  escribe en cualquier sesión. Claude Code **no lo borra nunca**, así que recoge lo enviado en sesiones
  cuyo transcript ya no existe: pasado su plazo o eliminada desde Sesiones.
- **El texto pegado** (desde el 2026-10-09): `~\.claude\paste-cache\`, un fichero por cada texto largo
  pegado en un prompt. El historial cita cada uno por su nombre, y de ese prompt sale su sesión. Uno
  que ningún prompt cita no se envió: se espera un día por si se envía y después se deja.

Un mismo secreto en la misma sesión se apunta una vez, venga del transcript o del historial.

De cada entrada se analizan estos orígenes, y el hallazgo guarda cuál fue:

| Origen | Cuándo ocurre |
|---|---|
| **Tu mensaje** | El secreto se escribió o se pegó en un prompt, según el transcript |
| **Historial de prompts** | Lo mismo, según el historial |
| **Texto pegado** | Iba en un texto pegado en un prompt, según el historial o `paste-cache` |
| **Respuesta de Claude** | Claude lo escribió en su respuesta |
| **Entrada de `<herramienta>`** | Claude lanzó un comando o escribió un fichero con el secreto dentro |
| **Salida de `<herramienta>`** | Claude leyó un fichero o lanzó un comando y el secreto salió en el resultado |

El último es el más frecuente y el menos esperado: nadie pegó nada, pero un `cat .env`, un `env` o la
lectura de un `appsettings.json` lo metieron en el contexto. Cuando la herramienta trabaja con un
fichero (`file_path`, `path`, `notebook_path`), el hallazgo guarda también su ruta, y de ahí salen
«Mostrar en la carpeta» y «Copiar ruta».

### Lo escrito en la configuración

Desde el 2026-10-09. Solo los bloques donde se ponen claves, no los ficheros enteros:

| Fichero | Bloque |
|---|---|
| `~\.claude\settings.json` y `settings.local.json` | `env` |
| `<proyecto>\.claude\settings.json` y `settings.local.json` | `env` |
| `<proyecto>\.mcp.json` | Cada servidor de `mcpServers` |
| `~\.claude.json` | Cada servidor de `mcpServers`, el global y el de cada proyecto |
| `data\backups\*.json`, las copias de `settings.json` que guarda la app | `env` |

- **Proyectos:** los que conoce Claude Code, de la lista de `~\.claude.json`, si su carpeta sigue
  existiendo.
- **Origen:** «Variable `<NOMBRE>` en env» o «Servidor MCP «`<nombre>`»», con el fichero.
- **De un servidor MCP se analiza su JSON tal cual,** para que las reglas vean el nombre de cada
  valor: `"API_KEY": "…"` o `"Authorization": "Bearer …"`.
- **Una referencia no cuenta:** `"GITHUB_TOKEN": "${GITHUB_TOKEN}"` es justo lo que hay que hacer.
- **Es una foto, no un registro:** se rehace en cada análisis, en lugar de acumularse. Una clave que se
  quita del fichero deja de salir sola.

## Cuándo se analiza

- **La ventana de Arkko tiene que estar abierta:** el análisis lo lanza la app, no `ak`.
  Cerrada, no se analiza nada.
- **Cada 2 minutos** se miran los transcripts escritos desde el análisis anterior, con 1 minuto de
  margen porque la fecha de modificación de un fichero puede ir por detrás.
- **Tras un aviso en el momento** (desde el 2026-10-06), la app no espera a los 2 minutos: lanza el
  análisis 5 segundos después, lo que tarda Claude Code en escribir la línea en el transcript. Si
  aún no estaba entera, la recoge el siguiente automático.
- **Incremental:** de cada transcript se recuerda hasta qué byte se leyó (`scan-state.json`) y solo
  se leen líneas completas, porque Claude Code puede estar escribiendo la última.
- **La primera vez** solo se mira el último día de transcripts; el historial de prompts, entero.
- **El historial de prompts** se lee por partes como un transcript, y cada texto pegado, una sola vez.
- **La configuración** se mira entera en cada análisis: son pocos ficheros y pequeños.
- **«Analizar todo el historial»** recorre todos los transcripts, también los anteriores a instalar
  la app. Es la única forma de volver a mirar lo ya leído: si cambian las reglas, el barrido normal
  no revisa lo viejo.
- Nunca corren dos análisis a la vez; si se pide el historial completo con uno en marcha, espera.

El análisis es **a posteriori**: los hallazgos llegan a la sección hasta 2 minutos después, o unos
segundos si el hook ya avisó. Lo que pasa en el momento es cosa de los hooks, en la sección siguiente.

## En el momento: aviso y bloqueo

Desde el 2026-10-06 (fases 3 y 4), `ak` mira cada evento de Claude Code con el mismo detector
(`Arkko.Core\LiveSecrets.cs`). No necesita la ventana abierta y no guarda hallazgos: solo avisa.
Los hallazgos los sigue guardando el análisis, que es la fuente de la sección Seguridad.

- **Aviso** (encendido por defecto): en cada `PostToolUse`, la entrada y la salida de la herramienta;
  en cada `UserPromptSubmit`, el prompt. Si hay un secreto, la sesión enseña una línea del hook
  («⚠ Arkko: se ha enviado un secreto a la API: Token de GitHub (salida de Read · .env)»), y la
  app lanza una notificación roja y pone un escudo rojo delante del nombre de la tarjeta, hasta que se
  hace clic en ella. El secreto sigue pendiente en Seguridad.
- **Bloqueo** (apagado por defecto): si un prompt lleva un secreto, Claude Code no lo envía y enseña
  el motivo, con cómo desactivarlo y que, desactivado, lo que se envíe es responsabilidad de quien lo
  desactiva. Es lo único que se puede frenar antes de salir: la salida de una herramienta ya está en
  el contexto cuando el hook la ve.
- **Lo ya resuelto no avisa ni bloquea** (desde el 2026-10-06): el hook calcula la huella de cada
  secreto, la misma que guarda Seguridad, y se salta los marcados como falso positivo o rotados. Las
  huellas de lo que sí avisa se guardan en la sesión, y la tarjeta retira el escudo en cuanto todas
  quedan resueltas, aunque se marquen después del aviso. El almacén solo se abre si hay algún secreto,
  y si no se puede abrir, se avisa igual.
- **Solo lo que se envía** (desde el 2026-10-15): de la respuesta de una herramienta, el hook no mira lo que
  Claude Code se guarda para sí y no manda al modelo: el fichero entero de antes de editarlo
  (`originalFile`) y los diffs (`structuredPatch`, `bashEditDiff`). Lo escrito en la edición sí lo mira,
  porque va en la llamada. En una entrada o una salida enorme, mira 256 KB de cada punta; lo de en medio
  lo encuentra el análisis.
- **Un aviso que el análisis no confirma se retira** (desde el 2026-10-15): si un análisis que empezó
  después del aviso, con su margen, no deja ningún secreto pendiente en esa sesión, el escudo se va. Sin
  hallazgo no había nada que rotar ni descartar, y se quedaba para siempre.
- **Nunca el valor:** ni la línea, ni la notificación, ni el motivo del bloqueo, ni el estado de la
  sesión llevan el secreto; solo su tipo, su origen y su huella, que no permite recuperarlo.
- Los dos se activan en Configuración → Seguridad → «Avisos de secretos», y el hook los lee de
  `app-settings.json` en cada evento.
- **Coste:** con una salida normal, unos 20 ms más por herramienta, dentro del ruido; con una salida
  de 500 KB, unos 150 ms. Las reglas del detector se generan al compilar (`[GeneratedRegex]`) para no
  compilarlas en cada arranque de `ak`.

## Qué reconoce

**Por su forma** (reglas «conocidas»): el prefijo es inconfundible, así que casi no fallan.

| Tipo | Forma |
|---|---|
| Clave privada | `-----BEGIN … PRIVATE KEY-----` … `-----END … PRIVATE KEY-----` |
| Clave de Anthropic | `sk-ant-…` |
| Clave de OpenAI | `sk-…`, `sk-proj-…` |
| Token de GitHub | `ghp_…`, `gho_…`, `ghu_…`, `ghs_…`, `ghr_…`, `github_pat_…` |
| Token de GitLab | `glpat-…` |
| Token de Slack | `xoxb-…`, `xoxa-…`, `xoxp-…`, `xoxr-…`, `xoxs-…` |
| Clave de Google | `AIza…` (35 caracteres más) |
| Clave de AWS | `AKIA…`, `ASIA…` (16 caracteres más) |
| Clave de Stripe | `sk_live_…`, `rk_live_…` |
| Token de npm | `npm_…` (36 caracteres más) |
| Token JWT | `eyJ….….…`: cabecera que empieza por `eyJ`, tres segmentos y firma de 10 o más |

**Por el contexto** (reglas «generales»): aquí el valor podría ser cualquier cosa, así que lo que
delata es lo que lo rodea.

| Tipo | Qué lo delata |
|---|---|
| Token Bearer | `Authorization: Bearer …` |
| Clave de Azure Storage | `AccountKey=…` |
| Firma SAS de Azure | `?sig=…` o `&sig=…` |
| URL con credenciales | `esquema://usuario:clave@host` |
| Contraseña en cadena de conexión | `Server=…;…Password=…` (también `Data Source`, `Host`, `Uid`, `User Id`, `Pwd`) |
| Variable con secreto | Un nombre en mayúsculas que acaba en `API_KEY`, `APIKEY`, `SECRET`, `TOKEN`, `PASSWORD`, `PASSWD`, `PRIVATE_KEY` o `ACCESS_KEY`, con valor de 8 caracteres o más. Vale con `export …`, `set …`, `$env:…` o alineado con espacios, como en la salida de `Get-ChildItem Env:` |
| Secreto en configuración | Una clave `"api_key"`, `"apikey"`, `"secret"`, `"token"` o `"password"` con valor entre comillas |

## Cómo se descartan los ejemplos

Sin estos filtros la lista sería inservible: la documentación y las plantillas están llenas de claves
de pega.

- **Trozos que delatan una plantilla**, estén donde estén: `xxx`, `example`, `placeholder`,
  `changeme`, `change_me`, `your_`, `your-`, `dummy`, `sample`, `redacted`, `<`, `>`, `${`, `$(`,
  `{{`, `***`, `...`, `…`, o un valor entre `%` como `%MI_VAR%`.
- **Palabras usadas como valor entero**: `password`, `secret`, `token`, `fake`, `test`, `user`,
  `usuario`, `contraseña`, `clave`, `pass`, `null`, `true`…
- Y solo en las reglas **generales**, además: el valor no puede empezar por `$`, no puede ser igual
  al nombre de su variable, tiene que tener **5 caracteres distintos o más** y una **entropía de
  Shannon de 2,5 o más** por carácter. Eso tira las contraseñas poco aleatorias y los ids repetitivos.

Las reglas conocidas no pasan por el filtro de entropía: un `AKIA…` es un `AKIA…` aunque parezca poco
aleatorio.

## Dos detalles que explican resultados raros

- **Orden de las reglas.** Se aplican en el orden del fichero y las conocidas van primero, así que un
  JWT dentro de una URL se informa como «Token JWT», no como «URL con credenciales».
- **Deduplicado por subcadena.** Dentro de un mismo texto, un valor que contiene a otro ya encontrado
  (o que está contenido en él) no se añade. Evita informar tres veces del mismo secreto porque tres
  reglas lo rodean.

Un mismo secreto se apunta **una vez por sesión**: la lista agrupa por huella y cuenta apariciones.

## Qué NO detecta

1. **Formatos sin prefijo ni contexto.** Un token de 32 caracteres aleatorios, suelto, sin nombre de
   variable ni `Bearer` delante: invisible. No hay forma honesta de distinguirlo de un hash o un id.
2. **Nombres de variable que no acaban en las palabras conocidas.** `MI_CLAVE=…` o `CREDENCIAL=…` no
   saltan; `MI_API_KEY=…` sí.
3. **Contraseñas cortas o poco aleatorias:** las tira el filtro de entropía.
4. **Secretos partidos en varias líneas o codificados** dentro de un base64 mayor. Los datos en
   base64 de las imágenes y los PDF (el `data` de un bloque de imagen o de documento y el `base64` que
   guarda un Read de una imagen) ni se analizan: son cientos de miles de caracteres al azar, y alguno
   acababa con la forma de una clave («Clave de Google» dentro de una captura). Lo que se ve escrito en
   una captura tampoco se encontraría leyendo su base64.
5. **Lo que ya se borró.** Claude Code borra los transcripts de terminal pasado `cleanupPeriodDays`
   (14 días, elegido en Configuración); lo anterior no se puede analizar porque no existe. Lo que se
   escribió o se pegó en un prompt sí queda, en el historial de prompts. Lo que Claude leyó o
   respondió en esas sesiones, no.
6. **Sitios donde no mira:**
   - **Los chats normales de Claude Desktop:** están en los servidores de Anthropic; en el equipo solo
     queda una caché de la web, incompleta y en un formato interno de Chromium. Code dentro de Desktop
     ejecuta el `claude` de siempre, y sus transcripts ya se analizan; los de Cowork, también.
   - **Las credenciales de otras herramientas:** `.npmrc`, `.git-credentials`, `.aws\credentials`, la
     configuración de Docker. No son de Claude Code; para que Claude no las lea, encaja mejor una
     regla de «Qué no puede leer Claude».
   - **Los ficheros que Claude no ha leído,** como el `.env` de un repositorio (ver la regla de oro).

## Qué se guarda de cada hallazgo

Nunca el secreto. Solo el tipo, la sesión, el proyecto, el origen, el fichero si lo había, si venía de
un subagente, el valor enmascarado, una huella y dónde salió.

- **Dónde salió, sin texto:** la llamada a herramienta en cuya entrada o salida apareció, en qué
  carácter del texto analizado empieza y cuánto medía ese texto. Basta para llevar al paso y para
  saber si la clave cae más allá de los 8.000 primeros caracteres, que son los que guarda un paso de
  Sesiones. Ningún trozo de la conversación se guarda: el texto de alrededor se lee del transcript al
  pedirlo. Los hallazgos de antes de guardarlo se completan una vez desde su transcript.

- **Enmascarado:** los 4 primeros y los 4 últimos caracteres (`AKIA…2DWR`); `••••••` si mide menos de
  12. De una clave privada, solo su primera línea.
- **Huella:** HMAC-SHA256 con una clave aleatoria del equipo protegida con DPAPI. Reconoce el mismo
  secreto entre sesiones sin permitir reconstruirlo, que un hash simple de una contraseña corta sí
  permitiría por fuerza bruta.
- **Cifrado:** `findings.bin`, `ignored.bin`, `rotated.bin` y `written.bin` (la foto de las claves
  escritas en la configuración) en `data\security\`, con DPAPI del usuario. El estado del análisis
  va en claro porque no dice nada de los secretos.
- **Fuera del alcance de Claude:** `ak install` añade una regla `deny` sobre esa carpeta. Frena
  las herramientas de ficheros de Claude Code, no un `cat` desde Bash; el cifrado cubre el resto.

## Cómo probarlo sin exponer nada

El detector se prueba con secretos **inventados**, nunca con los reales: los reales los revisa la
persona en la sección Seguridad.

- En `SecretDetectorTests.cs` cada valor de prueba se arma **por trozos** (`"gh" + "p_" + …`). Escrito
  entero, el escáner de secretos de GitHub lo tomaría por real y rechazaría el push. La misma técnica
  sirve para que un fichero de pruebas no deje los valores en el transcript de la sesión que lo crea.
- Para una prueba de punta a punta hace falta un **transcript**, porque es lo único que el analizador
  lee: un `.jsonl` en una carpeta propia de `~\.claude\projects\` que simule un `Read` del fichero de
  pruebas. Los hallazgos salen entonces como «Salida de Read» con su ruta, igual que los de verdad.
- Borrar el transcript y el fichero **no borra los hallazgos**: el almacén guarda huella y máscara.
  Para quitarlos, se marcan como falso positivo y se eliminan desde «Marcados»: los que comparten
  fichero se eliminan juntos, tras el aviso.
- Los hallazgos de la prueba de las 18 reglas del 2026-10-03, hecha desde otro dispositivo, salen bajo
  el proyecto `PRUEBA-secretos-ficticios`: son inventados y su fichero ya no existe.

## La sección Seguridad de la app

En el menú del panel. Desde el 2026-10-09 (S7) se parece a Sesiones: **la página no se desplaza**, solo
la lista y el detalle, cada uno por su cuenta. De arriba abajo:

- **Los colores dicen qué falta, y son los mismos en toda la sección:** rojo, enviada y sin cambiar
  (rotarla); ámbar, escrita tal cual en un fichero (pasarla a una variable de entorno); verde, rotada;
  gris, falso positivo. Solo los llevan lo que dice un estado: la barra del reparto, las etiquetas, el
  punto de cada fila y el recuadro de qué hacer. El título y el icono de cada aparición (que dice de
  dónde salió: un prompt, un texto pegado, la respuesta de Claude, un fichero, un comando o la
  configuración) son neutros, y el candado va en el acento de la app, para que el rojo no lo llene todo.
- **Cabecera:**
  - el candado de la sección y una frase con el estado, como «2 secretos pendientes», con su desglose
    (enviados sin cambiar y escritos en un fichero) y cuándo fue el último análisis;
  - una barra con el reparto de todos los secretos por estado, en sus colores, y su leyenda;
  - los totales (secretos distintos y sesiones) y «Analizar todo el historial».

  Sustituye a la fila de cinco cifras, que en una ventana ancha dejaba casi todo vacío.
- **Lista, a la izquierda:**
  - Tres pestañas: «Pendientes», «Rotados» y «Falsos positivos», cada una con su número.
  - Un buscador por tipo, valor enmascarado, proyecto o fichero.
  - Dos filtros: por tipo y por proyecto, cada valor con cuántos secretos tiene. Sustituyen a los
    antiguos bloques «Por tipo» y «Por proyecto».
  - Con los falsos positivos, además, «Volver a mostrarlos todos».
  - Una fila por secreto: un punto del color de su estado, el tipo, cuándo salió por última vez, el
    valor enmascarado, los proyectos y la etiqueta **«Enviada»**, **«En un fichero»** o las dos.
- **Detalle, a la derecha,** del secreto elegido. Al entrar, el pendiente más reciente:
  - Tipo, valor, etiquetas y estado; cuántas veces salió, en cuántas sesiones y ficheros, y dónde.
  - **Qué hacer con él,** según esté enviado, escrito en un fichero o las dos cosas.
  - Los botones: «Ya rotado» y «Falso positivo» si está pendiente; «Volver a pendiente» y «Eliminar»
    si está marcado. Una clave que sigue escrita en un fichero no tiene «Eliminar»: el siguiente
    análisis la volvería a encontrar, y sale sola al quitarla del fichero.
  - **Todas sus apariciones,** cada una con fecha, proyecto, sesión, origen y la ruta del fichero si
    la había, y sus acciones a la vista:
    - **«Mostrar en la carpeta»** y **«Copiar ruta»**. Si el fichero ya no existe, lo dice y ofrece
      abrir su carpeta. **Nunca abre el fichero**: la ruta sale de un transcript, y abrirlo lanzaría su
      programa asociado, que en un `.ps1`, un `.bat` o un `.exe` es ejecutarlo.
    - **«Ver el prompt»**, para lo que salió del historial de prompts o de un texto pegado: lo
      despliega ahí mismo, con cada secreto tapado por su máscara. La app no guarda el texto: lo lee
      en el momento, buscando el prompt por su sesión y su hora exacta. Si Claude Code ya no lo tiene,
      lo dice.
    - **«Ver dónde»**, para lo que salió en una conversación: en qué parte de la llamada (y, si cae
      más allá de lo que guarda el paso, que no se verá en él), la llamada que lo produjo y el texto de
      alrededor, todo con cada secreto tapado. Se lee del transcript cada vez que se despliega.
    - **«Mantener pulsado para ver»**, de cualquier origen: el valor real, leído en el momento de
      donde salió, solo mientras se mantiene pulsado. Va en un texto que no se puede seleccionar ni
      copiar, no se guarda, y se tapa al soltar, al perder el foco o a los 10 segundos (pulsado con el
      teclado no hay nada que soltar). Por defecto todo sigue tapado: las capturas de la app se pegan
      en sesiones de Claude y volverían a enviar el secreto.
    - **«Ver la sesión»**, si su transcript sigue en el equipo: la abre en Sesiones, en el paso donde
      salió, con su subagente desplegado y su ficha abierta. En Sesiones, ese paso lleva el escudo
      aunque la clave esté más allá de lo que guarda, y su ficha dice dónde está y enseña la llamada y
      el texto de alrededor, tapados.
- La marca va con el valor, así que «Ya rotado» y «Falso positivo» resuelven a la vez lo enviado y lo
  escrito.
- **Aviso fuera de la sección:** mientras haya pendientes, su número sale en rojo junto a
  «Seguridad» en el menú del panel (un punto sobre el icono si el menú está plegado) y en un escudo en
  la barra de la ventana flotante, que abre el panel en Seguridad. No es «nuevos desde la última
  visita»: es siempre el total de pendientes, y desaparece cuando no queda ninguno.

Antes era una sola columna que se desplazaba entera: dos tablas por páginas de 10, una ficha al pasar
el ratón y los ficheros en el clic derecho.

### La sección Tokens

Desde el 2026-10-16 (mejora 58), aparte de Seguridad y con su propia entrada en el menú, justo encima:
Seguridad es siempre la última. Seguridad enseña lo que la app **detecta**; Tokens, lo que la persona
**apunta**: no detecta nada y se llena a mano. Es para tokens y claves de acceso (Git, APIs,
servicios), no para contraseñas.

**La idea: solo se crean tokens, y la app crea sola el recordatorio de cada uno.**

1. **Crear un token:** «Nuevo token» hace cuatro preguntas: cómo se llama, para qué sirve, cuándo se
   creó y cada cuánto cambiarlo (1, 3 o 6 meses, 1 año, «Nunca» u «Otro…» con los días). Al pulsar
   «Crear», el token queda guardado y su recordatorio, «Cambiar NOMBRE», aparece con su fecha. Lo demás
   (dónde se cambia, dónde se guarda, permisos, cuándo cambiarlo sin esperar y los pasos) se añade
   después, en el token.
2. **Hasta esa fecha, nada.** El recordatorio espera en su lista con su cuenta atrás, en verde.
3. **Una semana antes,** pasa a ámbar, en la lista y en el número junto a «Tokens» en el menú. Sus
   pasos ya salen como casillas.
4. **El día que toca,** en rojo. Se abre el recordatorio, se marcan los pasos mientras se hacen y se
   pulsa **«Ya lo he cambiado»**: se apunta hoy como último cambio y el recordatorio pasa solo a la
   siguiente fecha. Si ese día no se puede, **«Recuérdamelo más tarde»** lo deja para mañana, dentro de
   3 días o de una semana.
5. **Mover la fecha de esta vez,** cuando se quiera: si el día cae en vacaciones o sin el equipo
   delante, «Mover la fecha» lo adelanta o lo retrasa (un día o una semana antes; uno, tres o siete
   después), o se elige el día exacto en «Esta vez, recuérdamelo el». Nunca antes de hoy, y se puede
   volver a la fecha normal. Vale solo para esta vez: tras «Ya lo he cambiado», el siguiente se cuenta
   normal.

**Qué se edita y dónde:**

- **En el token,** todo: nombre, para qué sirve, cada cuánto se cambia, fechas, dónde se cambia y se
  guarda, permisos y pasos. Se guarda solo al escribir. **Nunca el valor.**
- **En el recordatorio,** solo lo de esta vez: marcar pasos, «Ya lo he cambiado» y mover su fecha. Lo
  demás viene del token, y «Editar el token» lleva a él.
- **El recordatorio no se guarda aparte:** sale del último cambio del token y de cada cuánto se cambia.
  Con «Nunca» desaparece, y al eliminar el token, también.

**La pantalla:**

- **Cabecera:** cuántos tokens toca cambiar hoy o esta semana, con un enlace a cada uno; lo siguiente
  que toca; cuántos tokens hay y cuántos con recordatorio; y «Nuevo token».
- **Pestaña «Recordatorios»,** la de entrada: uno por token con frecuencia, el que toca antes primero.
- **Pestaña «Tokens»:** los tokens por nombre, para consultarlos o editarlos.
- **Fechas, con calendario,** con el aspecto de la app en tema claro y oscuro; no se escriben.
- **Pasos con formato:** negrita, `código` y enlaces http o https, en Markdown o HTML sencillo, con el mismo
  intérprete que las descripciones de las tareas.
- **Reconocidos en Seguridad** (desde el 2026-10-16): si el token tiene su **variable de entorno**, la app
  lee su valor actual, calcula su huella con la misma clave que Seguridad y lo olvida: el valor nunca se
  guarda ni se enseña, y tras cambiar el token reconoce el nuevo sin hacer nada. Si no vive en una
  variable, se enlaza a mano desde el hallazgo con **«Es uno de mis tokens»**. Cuando un token sale en
  un hallazgo pendiente visto **después de su último cambio**, su recordatorio pasa a **«Toca cambiarlo
  ya»**, tenga o no frecuencia, y no se puede aplazar; el hallazgo dice en Seguridad de qué token es.
  Un hallazgo de antes del último cambio es de un valor que ya no vale y no lo vuelve a poner en rojo.
  Al pulsar «Ya lo he cambiado», la app pregunta si marca también como rotados esos hallazgos.
- **Buscar en este equipo** (desde el 2026-10-16): propone las variables de entorno del usuario y del
  equipo que parecen tokens, por su nombre o porque el detector reconoce su valor, sin contraseñas ni las
  que ya son de un token. El valor sale tapado y solo en la ventana de la app; no se guarda. Las que se
  marcan se crean como tokens con su variable, y así se reconocen solas en Seguridad.
- **Sin notificaciones:** solo el número del menú. Sin correo: la mejora 42, que lo traía, se descartó el 2026-10-29.
- **Se guarda cifrado** con DPAPI en `security\plan.bin`: aunque no lleve el valor, dice qué tokens
  hay y dónde están. Si el fichero no se puede descifrar, se enseña vacío y no se guarda nada encima,
  para no perderlo.

## Qué no puede leer Claude

En **Configuración → Seguridad**, desde el 2026-10-06: una fila por tipo de fichero con su
interruptor. **Cada interruptor se guarda al momento** en el `settings.json` de Claude Code, con copia
previa en `data\backups` (la limpieza automática conserva las 5 más recientes); se probó con Guardar y
Descartar, y la persona prefirió el guardado directo. Apagar un tipo quita sus reglas; las reglas
`deny` que la persona puso a mano no se enseñan ni se tocan. Antes era un
bloque de recomendaciones al final de la sección Seguridad, con todas las reglas y un botón para
añadir las que faltaban; se quitó por ruidoso y poco claro.

| Tipo | Reglas |
|---|---|
| Hallazgos de Arkko | `Read(<datos>/security/**)`: siempre activa y sin interruptor, la añade `ak install` |
| Copias de `settings.json` de Arkko | `Read(<datos>/backups/**)`: igual, desde el 2026-10-09. Llevan las claves que tuviera el original, y restaurar una no necesita leerla, basta con copiarla. Si falta, la app la añade al abrir la configuración, porque actualizar la app no toca `settings.json` |
| Ficheros `.env` | `Read(**/.env)` |
| Variantes de `.env` | `Read(**/.env.*)` |
| Certificados `.pfx` y `.p12` | `Read(**/*.pfx)`, `Read(**/*.p12)`: van juntos, salen encendidos solo si están las dos |
| Claves `.key` | `Read(**/*.key)` |
| Claves SSH | `Read(~/.ssh/**)` |
| Credenciales de Claude Code | `Read(~/.claude/.credentials.json)` |
| Secretos de usuario de .NET | `Read(~/AppData/Roaming/Microsoft/UserSecrets/**)` |

Una regla `Read` de Claude Code frena sus herramientas de ficheros (leer, buscar, editar y escribir),
no un `cat` lanzado desde Bash. En Windows compara rutas en forma POSIX: dentro del perfil se
escriben con `~/` y fuera con `//c/…`.

Al marcar un hallazgo:

- **Falsos positivos:** su huella va a una lista de ignorados; dejan de contar y pasan a «Marcados».
  «Volver a pendiente» quita la marca a uno, y «Volver a mostrarlos todos» vacía la lista.
- **Rotados:** un secreto ya cambiado en su servicio se marca como «Ya rotado»: pasa a «Marcados» y
  deja de contar como pendiente, pero sigue en las cifras de secretos distintos y apariciones.

Eliminar es otra cosa, y solo se ofrece en los marcados: **borra del almacén** el secreto con todas
sus apariciones. Si apareció en algún fichero donde también salieron otros secretos, antes avisa con
la lista de esos otros, con su estado (también los pendientes), y al aceptar se eliminan todos; si no
comparte fichero con ninguno, se elimina sin preguntar. No vuelven al analizar de nuevo, porque lo
ya leído de cada transcript no se relee; si el mismo secreto aparece en algo escrito después, sale
otra vez, porque es una aparición nueva. Las marcas de rotado y falso positivo **se conservan**: son
del valor, no de la aparición, así que un falso positivo eliminado que vuelve a salir sigue oculto.

## Decisiones

| Tema | Decisión |
|---|---|
| **Protección de los hallazgos** | Cifrados en disco con DPAPI (la clave de Windows del usuario), igual que la clave de las huellas: `findings.bin`, `ignored.bin` y `rotated.bin`. La app los abre sin pedir nada; leídos de cualquier otra forma no se entiende nada. Sin contraseña: guardada en la configuración no protegería, y pedirla cada vez es mucha molestia para datos ya enmascarados. El estado del análisis (`scan-state.json`: rutas y bytes leídos) va en claro, porque no dice nada de los secretos |
| **Ficheros en claro de la fase 1** | Al abrir el almacén, `findings.jsonl` e `ignored.json` se cifran y se borran. Si un fichero cifrado no se puede descifrar (otro usuario, dañado), se olvida el estado del análisis para que el siguiente lo recupere de los transcripts |
| **Eliminar hallazgos** | Decidido el 2026-10-05: eliminar es **borrarlos del almacén**, no una tercera marca, y va secreto a secreto, solo desde «Marcados», para que uno real no se borre sin revisarlo. Los secretos que comparten algún fichero con el eliminado cuentan como afectados: se enumeran y se eliminan con él si se acepta. Las marcas de rotado y falso positivo se conservan. Comparte cerrojo con el análisis, para que uno en marcha no reescriba lo borrado. Se descartó olvidar por proyecto, sesión o fichero desde el clic derecho: la persona prefirió un botón por secreto |
| **Análisis en segundo plano** | Nunca dos a la vez: el mutex entre procesos deja de esperar a los 3 s y dos análisis solapados se pisarían los hallazgos. Si se pide el historial completo con uno automático en marcha, se lanza al terminar este |
| **Que Claude Code no los lea** | Regla `deny` sobre `ClaudeStats\data\security\`, que `ak install` añade sola a `~/.claude/settings.json` y `ak uninstall` retira. La bloquea Claude Code, no la voluntad del agente; con el cifrado, ni un comando hecho a propósito vería nada legible |
| **Pruebas durante el desarrollo** | Solo con hallazgos inventados, en una carpeta de datos aparte. El agente no abre ni captura los hallazgos reales: la revisión con datos reales la hace la persona |
| **Más sitios donde buscar** | Hecho el 2026-10-09. Además de los transcripts, `~/.claude/history.jsonl` (los prompts escritos) y `~/.claude/paste-cache/` (lo pegado): un secreto pegado en un prompt queda ahí aunque el transcript ya no exista. Y lo escrito en la configuración de Claude Code |
| **Claves escritas en la configuración** | Decidido el 2026-10-09. Salen en **rojo**, como las enviadas: una clave nunca debe estar escrita tal cual, como mucho su `${NOMBRE}`. Una sola tabla, con la etiqueta «Enviada» o «En un fichero». Tienen «Ya rotado» y «Falso positivo»: marcar el valor viejo como rotado no esconde nada, porque una clave nueva escrita en el mismo sitio tiene otra huella y vuelve a salir. Sin «Eliminar» |
| **Arreglar el fichero desde la app** | Descartado sustituir el valor por un texto fijo al marcar: rompería lo que la usa, podría perder la única copia de una clave viva y la copia previa la conservaría. Queda como mejora un botón «Pasar a variable de entorno», que la crea y deja en el fichero su `${NOMBRE}` (S15). Tampoco se toca nada de las claves enviadas: rotada, ya no sirve |
| **Una sesión eliminada** | Decidido el 2026-10-09: al eliminar una sesión desde Sesiones no se toca el historial de prompts. Es de Claude Code, con formato sin documentar y escrito a la vez por todas las sesiones. Sus secretos siguen en Seguridad, porque se enviaron igual |
| **Transcripts de Claude Code** | Ni se cifran (Claude Code los necesita para retomar sesiones) ni se bloquean al agente (los necesita para depurar el lector de la app) |
| **Plazo de los transcripts** | Se elige en Configuración → Datos y retención, que escribe `cleanupPeriodDays` (terminal) y `desktopSessionCleanupPeriodDays` (Code y Cowork en Claude Desktop) en `settings.json`, con confirmación y copia previa. Sin ajuste, Claude Code borra los de terminal a los 30 días y **nunca** los de Desktop. Valores elegidos: 14 días para la terminal y sin límite para Desktop y Cowork. El borrado lo hace Claude Code en su limpieza automática, no la app. Si la configuración gestionada de la empresa fija un plazo, se muestra sin poder cambiarlo |

## Qué guarda Claude Code en el equipo

Todo en `~/.claude/`. Lo que ya llegó a Anthropic al usarse: los transcripts (`projects/`),
`history.jsonl`, `paste-cache/` y, casi siempre, `file-history/` (copias de ficheros que Claude
editó, y que leyó antes). No se envían en bloque `shell-snapshots/` ni la configuración. Todo está
en texto plano y lo puede leer cualquier programa del usuario. La limpieza de Claude Code borra los
transcripts, `file-history/` y `paste-cache/` pasado `cleanupPeriodDays` (30 días por defecto), y los
de Claude Desktop y Cowork solo si se pone `desktopSessionCleanupPeriodDays`. **`history.jsonl` no
entra en esa limpieza**: los prompts escritos se quedan aunque el transcript ya no exista.

Un secreto enviado no es una filtración a un atacante: Anthropic es el proveedor del servicio, con
las condiciones del contrato de la organización. Pero ha salido del perímetro, y la buena práctica
es tratarlo como expuesto y rotarlo.
