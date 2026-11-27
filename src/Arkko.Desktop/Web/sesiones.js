'use strict';

// Estado de la vista: lo que se ha elegido y lo que ha mandado la app.
const state = {
    period: 'week',
    // Si la respuesta final del directo va con el formato de Arkko; lo guarda la app.
    finalFormat: false,
    query: '',
    sessions: [],
    selectedId: null,
    trace: null,
    interaction: null,
    user: 'User',
    expanded: new Set(),
    // Respuestas largas que se han abierto enteras con «Ver más».
    longOpen: new Set(),
    details: new Map(),
    focus: null,
    // Si el chat marca el paso señalado: solo cuando se ha señalado desde una vista o la repetición, no desde el chat.
    focusMark: false,
    // Diseño de la app que pinta la página: con el de terminal, los agentes llevan los colores de un AS/400.
    design: null,
    listsOpen: false,
    tab: 'map',
    cursor: new Map(),
    // Agentes plegados en la secuencia.
    folded: new Set(),
    // Vista de la sesión: «live» (el Flujo), «diagram», «now» o «parte». La vista con que se abre la dice la app, de su
    // configuración.
    mapLayout: 'live',
    // Lienzo en directo: si enseña el prompt en curso o toda la sesión, lo ya pintado (que se pone al día en lugar de
    // rehacerse) y los agentes que la persona ha recogido. Y si el chat estaba abierto al entrar en él.
    flowScope: remembered('flowScope', 'current'),
    wf: null,
    wfClosed: new Set(),
    wfOpen: new Set(),
    // Paso elegido en el detalle de cada nodo del lienzo, y los Markdown que se ven como código en lugar de escritos.
    wfPick: new Map(),
    wfRaw: new Set(),
    // Los ficheros que dejó una MCP de navegador, por su ruta: los abiertos y lo que mandó la app de cada uno.
    mcpOpen: new Set(),
    mcpFiles: new Map(),
    // Lo cambiado en la carpeta de la sesión, según git: el alcance del panel, lo último que mandó la app de cada uno, el
    // diff y el fichero entero de cada fichero (changeKey), la pestaña de su ficha, las carpetas abiertas y si la lista de
    // lo pendiente está desplegada bajo la rama.
    // En revisión: lo último que mandó la app de ella (con su PR), si solo se ve lo que falta, si se ven las notas, la nota
    // que se está escribiendo y la línea a la que llevar la ficha.
    changes: {
        scope: 'branch', sets: { pending: null, branch: null, since: null, repo: null }, texts: new Map(), textSeq: 0, tab: 'diff', timer: null,
        // La pestaña del panel («changes» o «repo»), por dónde iba cada una, las carpetas abiertas del repositorio y las
        // rutas sin commitear, sacadas de lo pendiente que las trajo.
        view: 'changes', tops: {}, repoDirs: new Set(), wip: null,
        // La ronda de la revisión que se ve aparte en toda la rama (null, toda) y la base que ya se ha pedido para ella.
        round: null, sinceAsked: null,
        openDirs: new Set(), listOpen: false,
        review: null, prAsked: false, onlyUnreviewed: false, showNotes: false, openOutcomes: new Set(), compose: null, focusLine: null,
        // La línea a la que llevar la ficha al abrir un resultado de búsqueda, con lo que hay que buscar en ella.
        goTo: null,
        // Las marcas que aún no ha confirmado la app, las carpetas cerradas a mano en revisión y si se ve la rama sin ella.
        pendingMarks: new Map(), closedDirs: new Set(), reviewHidden: false,
        // El menú flotante de la revisión que esté abierto y los plazos de la nota que sale al pasar el ratón.
        pop: null, hover: {},
        // El fichero de la revisión, como lo mandó la app, y si se ve escrito («view») o tal cual («code»).
        reviewText: null, reviewTab: 'view',
        // Si las notas enseñan las notas o el fichero de la revisión; si las leídas están desplegadas; si están abiertas
        // aparte, en su propio panel; y lo último que se dio por leído al revisar un fichero, para deshacerlo.
        notesView: 'notes', readOpen: false, notesAside: false, readUndo: null, readUndoTimer: null,
    },
    chatBeforeLive: null,
    showDone: false,
    // Dónde quedó cada actor y cada fila en el último dibujo, para colocar los bocadillos.
    geo: null,
    // Secuencia dibujada: sus filas y lo que hace falta para pintar cada una al entrar en la vista.
    seqView: null,
    // Paso que lanzó cada agente, por su id, con los pasos de los que sale (callOf).
    callIndex: null,
    // Sesión pedida a la app que aún no ha llegado: mientras, el diagrama enseña su cargador.
    loadingId: null,
    replay: { index: null, playing: false, speed: 1, timer: null },
    find: { query: '', matches: [], index: -1 },
    // Herramientas en el chat, apagadas por defecto; y si el chat tiene novedades sin pintar por haber texto seleccionado.
    showTools: false,
    chatStale: false,
    // Día concreto de la lista («aaaa-mm-dd»), o vacío; y las sesiones abiertas ahora, para el panel de activas.
    day: '',
    // Primer día del mes que enseña el calendario del selector de día.
    calendarMonth: null,
    // Tooltip de las activas: la clave del elemento que lo tiene, o va a tenerlo, y su espera.
    tip: { key: null, timer: null },
    // Pasos cuya llamada ha pedido el tooltip y no el chat: al llegar no hace falta repintar el chat.
    tipAsks: new Set(),
    // Último paso pintado en el chat, de qué sesión e interacción, y si ha llegado algo nuevo sin verlo.
    chatSeen: { id: null, interaction: null, last: -1 },
    chatNews: false,
    // Si el chat está anclado al final (sigue lo que llega) y si va de camino hacia él tras pulsar el botón.
    chatPinned: true,
    chatGliding: false,
    // Lo pintado en el chat, para poner al día solo lo nuevo; y para qué vista se pintó.
    chatItems: null,
    chatView: null,
    active: [],
    // La última sesión cerrada que se ha abierto, para volver a ella desde el panel de activas; y cuándo se vio cada sesión.
    viewing: remembered('viewing', null),
    // El Parte, en resumen («summary») o paso a paso («steps», la Secuencia).
    parteMode: remembered('parteMode', 'summary'),
    // La sesión que se cerró mientras se miraba y se quitó de la vista.
    dropped: null,
    seen: remembered('seen', {}),
    // Orden de los grupos de activas que dejó la persona arrastrándolos; y el grupo que se arrastra ahora.
    groupOrder: remembered('groupOrder', []),
    dragGroup: null,
    // Color de cada grupo de activas y colores del menú, que manda la app; e imágenes ya recibidas, por «orden:índice».
    groupColors: {},
    palette: [],
    images: new Map(),
};

const $ = id => document.getElementById(id);
const host = window.chrome && window.chrome.webview;
// Si la página es una pieza en su propia ventana (sesiones.html?piece=…), cuál: «file», la ficha de un fichero; «live»,
// «chat» o «changes», solo esa parte de una sesión; «history», la historia del fichero abierto; «step», la ficha de un
// paso; «diagram», «now» o «parte», solo esa vista. null en la sección.
const PIECE = new URLSearchParams(location.search).get('piece');
// Si la pieza enseña el lienzo de la sesión y nada más: el Flujo, una vista o la ficha de un paso sobre el Flujo.
const DIAGRAM_PIECE = ['live', 'step', 'diagram', 'now', 'parte', 'obra'].includes(PIECE);
// En la pieza de un paso, cuál: la clave de su nodo en el directo y el número de su prompt.
const STEP = PIECE === 'step'
    ? { key: new URLSearchParams(location.search).get('key'), interaction: Number(new URLSearchParams(location.search).get('interaction')) }
    : null;
const SVG = 'http://www.w3.org/2000/svg';

// Colores de los agentes, en el orden en que aparecen: los define la hoja de estilos (--agent-0 a --agent-7), que los
// cambia con el diseño de terminal.
const AGENT_COLORS = 8;

/** Manda un mensaje a la app: con invokeCSharpAction en la ventana multiplataforma, que la pone su WebView, o con WebView2. */
function post(message) {
    if (window.invokeCSharpAction) window.invokeCSharpAction(message);
    else if (host) host.postMessage(message);
}

/** Crea un elemento con su clase y su texto; el texto nunca se interpreta como HTML. */
function el(tag, cls, text) {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text !== undefined && text !== null) node.textContent = text;
    return node;
}

/** Crea un elemento SVG con sus atributos. */
function svg(tag, attrs, text) {
    const node = document.createElementNS(SVG, tag);
    for (const [k, v] of Object.entries(attrs || {})) node.setAttribute(k, v);
    if (text !== undefined) node.textContent = text;
    return node;
}

/** Hora local corta: «14:05». */
function hour(iso) {
    return iso ? new Date(iso).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }) : '';
}

/** Fecha corta local: «8 oct». */
function shortDate(iso) {
    return iso ? new Date(iso).toLocaleDateString('es-ES', { day: 'numeric', month: 'short' }).replace('.', '') : '';
}

/** Hora local con segundos: «14:05:32». */
function clock(iso) {
    return iso ? new Date(iso).toLocaleTimeString('es-ES') : '';
}

/** Duración legible: «850 ms», «12 s», «3 min 4 s», «1 h 5 min». */
function duration(ms) {
    if (ms === null || ms === undefined || ms < 0) return '';
    if (ms < 1000) return `${Math.round(ms)} ms`;
    const s = Math.round(ms / 1000);
    if (s < 60) return `${s} s`;
    const m = Math.floor(s / 60);
    if (m < 60) return s % 60 ? `${m} min ${s % 60} s` : `${m} min`;
    return `${Math.floor(m / 60)} h ${m % 60} min`;
}

/** Milisegundos entre dos horas ISO; null si falta alguna. */
function between(start, end) {
    return start && end ? new Date(end) - new Date(start) : null;
}

/** Texto con sus saltos de línea, cortado con «…» a un largo: para lo que se pinta con su formato, que clip aplanaría. */
function cut(text, max) {
    const t = String(text || '');
    return t.length <= max ? t : `${t.slice(0, max).trimEnd()}\n\n…`;
}

/** Texto en una línea, cortado con «…» a un largo. */
function clip(text, max) {
    const line = (text || '').replace(/\s+/g, ' ').trim();
    return line.length <= max ? line : line.slice(0, Math.max(1, max - 1)).trimEnd() + '…';
}

/** Texto para comparar sin mayúsculas ni acentos. */
function norm(text) {
    return (text || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

/** Nombre del día de una sesión pasada: «Hoy», «Ayer» o «lunes, 5 oct». */
function dayName(iso) {
    const d = new Date(iso);
    const today = new Date();
    const start = x => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
    const days = Math.round((start(today) - start(d)) / 86400000);
    if (days === 0) return 'Hoy';
    if (days === 1) return 'Ayer';
    return d.toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric', month: 'short' }).replace('.', '');
}

/** Resumen de una interacción o de la sesión: «🤖 2 · 🧙 1 · 🔧 34 · 4 min». */
function counts(agents, skills, tools, ms) {
    const parts = [];
    if (agents) parts.push(`🤖 ${agents}`);
    if (skills) parts.push(`🧙 ${skills}`);
    if (tools) parts.push(`🔧 ${tools}`);
    const d = duration(ms);
    if (d) parts.push(d);
    return parts.join(' · ');
}

/** Pasos de lo elegido, en orden: los de la interacción, o todos con «Toda la sesión». */
function selectedSteps() {
    const t = state.trace;
    if (!t) return [];
    return state.interaction === 'all' ? t.steps : t.steps.filter(s => s.interaction === state.interaction);
}

/** Agente de la sesión por su id. */
function agentOf(id) {
    return state.trace ? state.trace.agents.find(a => a.id === id) : undefined;
}

/** Paso por su orden. */
function stepAt(order) {
    return state.trace ? state.trace.steps.find(s => s.order === order) : undefined;
}

// ACTORES Y COLORES

/** Quién actúa en un paso: la persona en sus prompts; en el resto, Claude o el agente donde pasa. */
function speaker(s) {
    if (s.kind === 'prompt' || s.kind === 'command') return 'user';
    if (s.kind === 'notification') return 'main';
    return s.agentId || 'main';
}

/** Color con que se pinta un paso: el de quien actúa, salvo un comando de consola de la persona, que va en el suyo.
 *
 * Quien actúa sigue siendo la persona: su carril, su nombre y su lado en el chat no cambian, solo el color. */
function inkOf(s) {
    return s.shell ? 'shell' : speaker(s);
}

/** El comando de consola de un paso, sin la marca «!». */
function shellCommand(s) {
    return (s.text || s.title || '').replace(/^!\s*/, '');
}

/** El paso con que empezó una interacción si fue un comando de consola; null si la empezó un prompt.
 *
 * Se pregunta por cada interacción al pintar las listas: el primer paso de cada una se apunta de una vez y se rehace
 * solo cuando cambian los pasos, que en directo crecen sin cambiar de lista. */
function shellStart(interaction) {
    const steps = state.trace.steps;
    const cache = state.shellCache;
    if (!cache || cache.steps !== steps || cache.count !== steps.length) {
        const firsts = new Map();
        for (const s of steps) if ((s.kind === 'prompt' || s.kind === 'command') && !firsts.has(s.interaction)) firsts.set(s.interaction, s);
        state.shellCache = { steps, count: steps.length, firsts };
    }
    const first = state.shellCache.firsts.get(interaction);
    return first && first.shell ? first : null;
}

/** Color de un actor como valor CSS: verde la persona, naranja Claude y, cada agente, el del papel de su rama.
 *
 * Por papel y no por rama: en una sesión de rondas, todos los reviews salen de un color y todos los commits de otro. */
function actorColor(key) {
    if (key === 'user') return 'var(--c-user)';
    // Los comandos de consola de la persona van en lila, como el modo «!» de Claude Code, para no confundirlos con sus prompts.
    if (key === 'shell') return 'var(--c-shell)';
    if (key === 'main') return 'var(--c-main)';
    // Se pinta en cada trazo de la secuencia: buscar la rama cada vez se notaba con miles de pasos.
    const colors = branchColors();
    if (!colors.has(key)) {
        const root = agentOf(branchOf(key));
        const index = root ? branchRoles().indexOf(roleOf(root).key) : -1;
        colors.set(key, `var(--agent-${(index < 0 ? 0 : index) % AGENT_COLORS})`);
    }
    return colors.get(key);
}

/** Colores ya resueltos de cada agente, que valen mientras no cambien los agentes de la sesión. */
function branchColors() {
    const agents = state.trace ? state.trace.agents : null;
    if (!state.colorCache || state.colorCache.agents !== agents) state.colorCache = { agents, colors: new Map() };
    return state.colorCache.colors;
}

/** Agente de primer nivel del que cuelga un agente: él mismo si lo lanzó la sesión principal.
 *
 * Las sesiones con muchos agentes son fases que lanzan ayudantes: por la rama se sabe de qué fase es
 * cada uno, y con ella se le da color y se agrupa en la leyenda. */
function branchOf(id) {
    let a = agentOf(id);
    for (let guard = 0; a && a.parentAgentId && guard < 50; guard++) {
        const parent = agentOf(a.parentAgentId);
        if (!parent) break;
        a = parent;
    }
    return a ? a.id : id;
}

/** Agentes de primer nivel de la sesión, por orden de lanzamiento; se recalculan solo si cambian los agentes. */
function branches() {
    const t = state.trace;
    if (!t) return [];
    if (state.branchCache && state.branchCache.agents === t.agents) return state.branchCache.ids;
    const ids = t.agents
        .filter(a => !a.parentAgentId || !agentOf(a.parentAgentId))
        .sort((x, y) => (x.startUtc || '').localeCompare(y.startUtc || ''))
        .map(a => a.id);
    state.branchCache = { agents: t.agents, ids };
    return ids;
}

/** Agentes que cuelgan de una rama, a cualquier profundidad, sin contarla a ella. */
function branchAgents(root) {
    return state.trace ? state.trace.agents.filter(a => a.id !== root && branchOf(a.id) === root) : [];
}

/** Tipo, modelo y esfuerzo de un agente, el dato que acompaña a su nombre: «general-purpose · Haiku · medio». */
function agentKind(a) {
    return [a.type, modelLabel(a.model, a.effort)].filter(Boolean).join(' · ');
}

// El esfuerzo de razonamiento, como lo dice Claude Code y en castellano.
const EFFORTS = { low: 'bajo', medium: 'medio', high: 'alto', xhigh: 'muy alto', max: 'máximo' };

/** Un modelo con su esfuerzo: «Opus 5.5 · alto»; solo el modelo si no consta el esfuerzo, y vacío sin modelo. */
function modelLabel(model, effort) {
    if (!model) return '';
    return effort ? `${model} · ${EFFORTS[effort] || effort}` : model;
}

// Nivel de cada esfuerzo, de 1 a 5, para las barras de su ficha.
const EFFORT_LEVELS = { low: 1, medium: 2, high: 3, xhigh: 4, max: 5 };

/** Los modelos y esfuerzos de unos pasos, del más usado al que menos, cada uno con su modelo y su esfuerzo. */
function modelsOf(steps) {
    const count = new Map();
    for (const s of steps) {
        const label = modelLabel(s.model, s.effort);
        if (!label) continue;
        const seen = count.get(label) || { model: s.model, effort: s.effort, n: 0 };
        seen.n++;
        count.set(label, seen);
    }
    return [...count.values()].sort((a, b) => b.n - a.n);
}

/** Ficha de un modelo: su nombre y, si consta, su esfuerzo, con un icono de cinco barras que se llenan hasta su nivel. */
function modelBadge(model, effort) {
    const chip = el('span', 'model-badge');
    chip.append(el('span', 'model-badge-name', model));
    if (effort) {
        const level = EFFORT_LEVELS[effort] || 0;
        const bars = el('span', 'model-badge-bars');
        for (let i = 1; i <= 5; i++) bars.append(el('i', i <= level ? 'on' : null));
        chip.dataset.level = level;
        chip.append(bars, el('span', 'model-badge-effort', EFFORTS[effort] || effort));
    }
    return chip;
}

/** Fichas de unos modelos, las primeras que quepan, y una «+N» con las que no. */
function modelBadges(models, max) {
    const box = document.createDocumentFragment();
    for (const m of models.slice(0, max)) box.append(modelBadge(m.model, m.effort));
    if (models.length > max) box.append(el('span', 'model-badge more', `+${models.length - max}`));
    return box;
}

/** Pinta un elemento con el color de un actor. */
function paint(node, key) {
    node.style.setProperty('--actor', actorColor(key));
    return node;
}

/** Nombre de un actor: el de la persona, «Claude» o el encargo del agente, y su tipo si no lo tiene.
 *
 * El tipo no distingue: en una sesión con decenas de agentes, casi todos son «general-purpose». */
function actorName(key) {
    if (key === 'user') return state.user;
    if (key === 'main') return 'Claude';
    const a = agentOf(key);
    return a ? a.description || a.type : 'agente';
}

/** Emoji de un actor; el de la persona va en color porque la silueta gris casi no se ve en el tema oscuro. */
function actorIcon(key) {
    return key === 'user' ? '🧑‍💻' : key === 'main' ? '🧠' : '🤖';
}

/** Paso que lanzó un agente, por su id; undefined si no hay ninguno (un agente lanzado por una skill).
 *
 * Se busca para cada paso al pintar la secuencia: recorrer todos los pasos cada vez costaba millones de
 * comparaciones con una sesión grande, en cada novedad en directo. El índice se rehace al cambiar los pasos. */
function callOf(id) {
    const t = state.trace;
    if (!t) return undefined;
    if (!state.callIndex || state.callIndex.steps !== t.steps) {
        const map = new Map();
        for (const s of t.steps) if (s.childAgentId) map.set(s.childAgentId, s);
        state.callIndex = { steps: t.steps, map };
    }
    return state.callIndex.map.get(id);
}

/** Llamadas a agente que contienen un paso, de la más externa a la más interna. */
function ancestors(s) {
    const chain = [];
    let id = s.agentId;
    while (id) {
        const call = callOf(id);
        if (!call) break;
        chain.unshift(call);
        id = call.agentId;
    }
    return chain;
}

/** Pinta la leyenda de colores: la persona, Claude y una entrada por papel de las ramas de agentes de lo elegido.
 *
 * Una rama sola lleva su nombre y cuántos más cuelgan de ella; varias del mismo papel, el papel y cuántas son. */
function renderLegend() {
    const box = $('legend');
    box.replaceChildren();
    if (!state.trace) return;
    const seen = new Set();
    for (const s of selectedSteps()) for (const id of [s.childAgentId, s.agentId]) if (id) seen.add(id);
    for (const key of ['user', 'main']) box.append(legendItem(key, `${actorIcon(key)} ${actorName(key)}`, 0));
    const roots = branches().filter(root => seen.has(root) || branchAgents(root).some(a => seen.has(a.id)));
    for (const role of branchRoles()) {
        const same = roots.filter(root => roleOf(agentOf(root)).key === role);
        if (same.length === 0) continue;
        if (same.length === 1) {
            box.append(legendItem(same[0], `${actorIcon(same[0])} ${actorName(same[0])}`, branchAgents(same[0]).filter(a => seen.has(a.id)).length));
            continue;
        }
        const r = roleOf(agentOf(same[0]));
        const item = legendItem(same[0], `${r.skill ? '🧙' : '🤖'} ${r.name}`, 0, `×${same.length}`);
        delete item.dataset.tip;
        item.title = `${same.length} agentes de primer nivel: ${r.name}`;
        box.append(item);
    }
}

/** Entrada de la leyenda: el punto del color del actor, su nombre y, si se da, cuántos más lleva. */
function legendItem(key, name, more, count) {
    const item = paint(el('span'), key);
    item.append(el('i'), el('span', 'legend-name', name));
    if (more) item.append(el('span', 'legend-more', `+${more}`));
    if (count) item.append(el('span', 'legend-more', count));
    item.dataset.tip = `l:${key}`;
    item.dataset.tipActor = key;
    return item;
}

// LISTAS

/** Abre o cierra las listas flotantes. */
function setListsOpen(open) {
    state.listsOpen = open;
    $('lists').classList.toggle('open', open);
    $('toggle-lists').classList.toggle('on', open);
}

/** Pide a la app la lista del periodo, el día y la búsqueda actuales. */
function requestList() {
    post({ type: 'list', period: state.period, query: state.query, day: state.day });
}

/** Fecha local como «aaaa-mm-dd», la forma del selector de día. */
function isoDay(date) {
    const pad = n => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Días que se pueden elegir en el periodo actual: de hoy a seis días atrás en «7 días», hasta hoy en «Todo». */
function dayRange() {
    const today = new Date();
    const week = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 6);
    return { min: state.period === 'week' ? isoDay(week) : '', max: isoDay(today) };
}

/** Fecha «aaaa-mm-dd» como día local, sin el desfase de zona que da leerla como UTC. */
function parseDay(iso) {
    const [y, m, d] = iso.split('-').map(Number);
    return new Date(y, m - 1, d);
}

/** Ajusta el selector de día al periodo: apagado en «Hoy» y sin días fuera del periodo elegido. */
function updateDayPicker() {
    const { min } = dayRange();
    const button = $('day');
    button.disabled = state.period === 'today';
    // Un día que ya no cabe en el periodo elegido se quita.
    if (state.period === 'today' || (min && state.day && state.day < min)) state.day = '';
    if (button.disabled) closeCalendar();
    $('day-label').textContent = state.day
        ? parseDay(state.day).toLocaleDateString('es-ES', { weekday: 'short', day: 'numeric', month: 'short' }).replace(',', '')
        : 'Cualquier día';
    button.classList.toggle('on', !!state.day);
    $('day-clear').hidden = !state.day;
}

/** Abre el calendario en el mes del día elegido, o en el actual; si ya estaba abierto, lo cierra. */
function toggleCalendar() {
    if (!$('calendar').hidden) {
        closeCalendar();
        return;
    }
    const base = state.day ? parseDay(state.day) : new Date();
    state.calendarMonth = new Date(base.getFullYear(), base.getMonth(), 1);
    renderCalendar();
    $('calendar').hidden = false;
}

/** Cierra el calendario, si estaba abierto. */
function closeCalendar() {
    $('calendar').hidden = true;
}

/** Pinta el mes del calendario: cabecera con ‹ ›, semanas de lunes a domingo y el atajo a hoy. */
function renderCalendar() {
    const box = $('calendar');
    const { min, max } = dayRange();
    const month = state.calendarMonth;
    const firstOf = (y, m) => isoDay(new Date(y, m, 1));
    const head = el('div', 'cal-head');
    const prev = el('button', 'pbtn small', '‹');
    const next = el('button', 'pbtn small', '›');
    prev.title = 'Mes anterior';
    next.title = 'Mes siguiente';
    // Sin salir de los meses donde hay días elegibles.
    prev.disabled = !!min && firstOf(month.getFullYear(), month.getMonth()) <= min;
    next.disabled = firstOf(month.getFullYear(), month.getMonth() + 1) > max;
    prev.addEventListener('click', () => moveCalendar(-1));
    next.addEventListener('click', () => moveCalendar(1));
    const title = month.toLocaleDateString('es-ES', { month: 'long', year: 'numeric' }).replace(' de ', ' ');
    head.append(prev, el('span', 'cal-title', title), next);

    const grid = el('div', 'cal-grid');
    for (const name of ['L', 'M', 'X', 'J', 'V', 'S', 'D']) grid.append(el('span', 'cal-dow', name));
    // Huecos hasta el día de la semana del 1, contando desde el lunes.
    for (let i = (month.getDay() + 6) % 7; i > 0; i--) grid.append(el('span'));
    const days = new Date(month.getFullYear(), month.getMonth() + 1, 0).getDate();
    for (let d = 1; d <= days; d++) {
        const iso = isoDay(new Date(month.getFullYear(), month.getMonth(), d));
        const day = el('button', 'cal-day', String(d));
        day.disabled = iso > max || (!!min && iso < min);
        day.classList.toggle('today', iso === max);
        day.classList.toggle('on', iso === state.day);
        day.addEventListener('click', () => pickDay(iso));
        grid.append(day);
    }

    const foot = el('div', 'cal-foot');
    const today = el('button', 'chip', 'Hoy');
    const any = el('button', 'chip', 'Cualquier día');
    today.addEventListener('click', () => pickDay(max));
    any.addEventListener('click', () => pickDay(''));
    foot.append(today, any);
    box.replaceChildren(head, grid, foot);
}

/** Pasa el calendario al mes anterior o al siguiente. */
function moveCalendar(delta) {
    const month = state.calendarMonth;
    state.calendarMonth = new Date(month.getFullYear(), month.getMonth() + delta, 1);
    renderCalendar();
}

/** Filtra la lista por un día, o la deja sin filtro con el día vacío, y cierra el calendario. */
function pickDay(iso) {
    state.day = iso;
    closeCalendar();
    updateDayPicker();
    requestList();
}

/** Pinta la lista agrupada por día; las abiertas llevan su gota de estado. */
function renderSessions() {
    const box = $('sessions');
    box.replaceChildren();
    if (state.sessions.length === 0) {
        box.append(el('div', 'empty', state.query ? 'Ninguna sesión coincide con la búsqueda.'
            : state.day ? 'No hay sesiones ese día.' : 'No hay sesiones en este periodo.'));
        return;
    }

    let day = null;
    for (const s of state.sessions) {
        const name = dayName(s.last);
        if (name !== day) {
            box.append(el('div', 'group', name));
            day = name;
        }
        box.append(sessionRow(s));
    }
}

/** Repinta el historial por sus marcas de vista, sin perder por dónde iba. */
function refreshSessionList() {
    const keep = $('sessions').scrollTop;
    renderSessions();
    $('sessions').scrollTop = keep;
}

/** Fila de una sesión: proyecto y worktree, título y primer prompt; a la derecha, fecha y hora y, si está cerrada, la papelera. */
function sessionRow(s) {
    const row = el('button', 'row session-row' + (s.id === state.selectedId ? ' on' : ''));
    row.dataset.id = s.id;
    const main = el('div', 'row-main');
    const top = el('div', 'row-top');
    if (s.open) {
        const dot = el('span', 'dot ' + (s.statusKey || 'idle'));
        dot.title = s.status || '';
        top.append(dot);
    }
    const name = el('span', 'row-name');
    name.append(el('strong', null, s.project));
    if (s.worktree) name.append(el('span', 'sep', '  ·  '), el('span', 'muted', s.worktree));
    top.append(name);
    const seen = s.open ? null : state.seen[s.id];
    if (seen) {
        const tag = el('span', 'seen-tag' + (state.viewing && state.viewing.id === s.id ? ' current' : ''), '👁');
        tag.title = state.viewing && state.viewing.id === s.id ? 'La que estabas revisando' : `Vista ${agoText(new Date(seen).toISOString())}`;
        top.append(tag);
    }
    if (seen) row.classList.add('seen');
    main.append(top);
    if (s.title) main.append(el('div', 'row-sub', s.title));
    if (s.firstPrompt) main.append(el('div', 'row-prompt', s.firstPrompt));
    const models = (s.models || []).filter(m => m.model);
    if (models.length) {
        const line = el('div', 'row-models');
        line.append(modelBadges(models, 3));
        main.append(line);
    }
    const side = el('div', 'row-side');
    side.append(el('span', 'row-time', s.open ? (s.status || '') : `${shortDate(s.last)} · ${hour(s.last)}`));
    // Una sesión abierta no se puede eliminar: Claude Code sigue escribiendo su conversación.
    if (!s.open) {
        const bin = el('span', 'row-delete');
        bin.title = 'Eliminar sesión';
        bin.append(trash());
        bin.addEventListener('click', e => {
            e.stopPropagation();
            post({ type: 'delete', id: s.id });
        });
        side.append(bin);
    }
    row.append(main, side);
    row.addEventListener('click', () => openSession(s.id));
    return row;
}

/** Papelera, de la fila de una sesión o de una nota: un dibujo pequeño que toma el color del texto. */
function trash(label = 'Eliminar sesión') {
    const icon = svg('svg', { class: 'trash', viewBox: '0 0 16 16', width: 13, height: 13, role: 'img', 'aria-label': label });
    icon.append(svg('path', { d: 'M2.5 4h11M6 4V2.5h4V4M4 4l.7 9.5h6.6L12 4M6.6 6.5v4.5M9.4 6.5v4.5' }));
    return icon;
}

/** Quita de la vista una sesión eliminada: de la lista, de lo que se está viendo y del hueco «Viendo». */
function sessionDeleted(id) {
    state.sessions = state.sessions.filter(s => s.id !== id);
    if (state.viewing && state.viewing.id === id) {
        state.viewing = null;
        remember('viewing', null);
        renderActive();
    }
    if (state.seen[id]) {
        delete state.seen[id];
        remember('seen', state.seen);
    }
    if (state.selectedId === id) clearView();
    refreshSessionList();
}

/** Abre una sesión: la marca en la lista y pide sus pasos. */
function openSession(id, keepInteraction) {
    if (state.selectedId !== id || !keepInteraction) {
        state.interaction = null;
        state.focus = null;
        state.expanded.clear();
        state.longOpen.clear();
        state.details.clear();
        state.images.clear();
        state.folded.clear();
        // Una sesión nueva vuelve a encajar; «entero» y «a lo ancho» se mantienen, que son una forma de mirar.
        state.cursor.clear();
    }
    state.selectedId = id;
    state.dropped = null;
    markSeen(id);
    renderActive();
    refreshSessionList();
    document.querySelectorAll('#active-list .act-card').forEach(c => c.classList.toggle('on', c.dataset.id === id));
    if (!state.trace || state.trace.id !== id) {
        $('session-head').replaceChildren(el('div', 'loading', 'Leyendo la sesión…'));
        $('interactions').replaceChildren();
        $('all-row').replaceChildren();
        $('chat').replaceChildren(chatSkeleton());
        $('diagram').replaceChildren(loader('Leyendo la sesión…'));
        state.loadingId = id;
    }
    post({ type: 'open', id });
}

/** Aro que gira con su texto debajo, para lo que tarda; aparece con un retraso, para no parpadear si llega enseguida. */
function loader(text) {
    const box = el('div', 'loader');
    box.append(el('div', 'loader-ring'), el('div', 'loader-text', text));
    return box;
}

/** Burbujas grises que laten en el chat mientras se lee una sesión, con la forma de una conversación. */
function chatSkeleton() {
    const box = el('div', 'skeleton');
    for (const [side, width] of [['left', 72], ['right', 46], ['left', 84], ['left', 58], ['right', 38]]) {
        const bubble = el('div', `sk-bubble ${side}`);
        bubble.style.width = `${width}%`;
        box.append(bubble);
    }
    return box;
}

/** Hace entrar un panel con un fundido, al pintar una sesión recién abierta. */
function fadeIn(node) {
    node.classList.remove('appear');
    void node.offsetWidth;
    node.classList.add('appear');
}

/** Pinta la cabecera de la sesión, su historial de interacciones y, fija al pie, «Toda la sesión». */
function renderInteractions() {
    const t = state.trace;
    const head = $('session-head');
    head.replaceChildren();
    const title = el('div', 'title', t.title || t.interactions.find(i => i.number > 0)?.prompt || t.id);
    const sub = el('div', 'sub');
    sub.append(el('strong', null, t.project || ''));
    if (t.worktree) sub.append(el('span', 'sep', '  ·  '), el('span', null, t.worktree));
    sub.append(el('span', 'faint', `  ·  ${t.interactions.filter(i => i.number > 0).length} interacciones`));
    head.append(title, sub);

    const box = $('interactions');
    box.replaceChildren();
    for (const i of t.interactions) {
        const shell = i.number > 0 ? shellStart(i.number) : null;
        const row = el('button', 'row' + (state.interaction === i.number ? ' on' : '') + (shell ? ' shell' : ''));
        if (shell) paint(row, 'shell');
        row.dataset.number = i.number;
        const top = el('div', 'row-top');
        top.append(el('span', 'row-name', i.number === 0 ? 'Antes del primer prompt' : shell ? `#${i.number} · ! comando` : `#${i.number}`), el('span', 'row-time', hour(i.startUtc)));
        row.append(top, shell ? el('div', 'row-prompt two shell-command', shellCommand(shell)) : el('div', 'row-prompt two', i.prompt));
        const c = counts(i.agents, i.skills, i.tools, between(i.startUtc, i.endUtc));
        if (c) row.append(el('div', 'counts', c));
        row.addEventListener('click', () => selectInteraction(i.number, true));
        box.append(row);
    }

    const all = el('button', 'row' + (state.interaction === 'all' ? ' on' : ''));
    all.dataset.number = 'all';
    all.append(el('div', 'row-name', 'Toda la sesión'));
    const first = t.interactions[0];
    const last = t.interactions[t.interactions.length - 1];
    all.append(el('div', 'counts', counts(
        t.interactions.reduce((n, i) => n + i.agents, 0),
        t.interactions.reduce((n, i) => n + i.skills, 0),
        t.interactions.reduce((n, i) => n + i.tools, 0),
        first && last ? between(first.startUtc, last.endUtc) : null)));
    all.addEventListener('click', () => selectInteraction('all', true));
    $('all-row').replaceChildren(all);

    const on = box.querySelector('.row.on');
    if (on) on.scrollIntoView({ block: 'nearest' });
}

/** Elige una interacción, o 'all' para toda la sesión, y pinta su diagrama y su chat. */
function selectInteraction(which, closeLists) {
    exitReplay();
    state.interaction = which;
    state.focus = null;
    document.querySelectorAll('#interactions .row, #all-row .row').forEach(r => r.classList.toggle('on', r.dataset.number === String(which)));
    updateNav();
    refreshFind();
    renderLegend();
    renderDiagram(true);
    renderChat(true);
    if (closeLists) setListsOpen(false);
}

/** Interacciones que recorren las flechas de la barra; «Toda la sesión» tiene su propio botón. */
function navStops() {
    return state.trace ? state.trace.interactions.map(i => i.number) : [];
}

/** Actualiza el número, las flechas y el botón «Todo» de la barra según lo elegido. */
function updateNav() {
    const stops = navStops();
    const all = state.interaction === 'all';
    const index = stops.indexOf(state.interaction);
    $('inter-number').textContent = all ? '—' : state.interaction === null ? '' : `#${state.interaction}`;
    $('prev').disabled = stops.length === 0 || (!all && index <= 0);
    $('next').disabled = stops.length === 0 || (!all && index === stops.length - 1);
    $('all-btn').disabled = !state.trace;
    $('all-btn').classList.toggle('on', all);
}

/** Pasa a la interacción anterior o siguiente; desde «Toda la sesión», ▸ va a la primera y ◂ a la última. */
function stepInteraction(delta) {
    const stops = navStops();
    if (stops.length === 0) return;
    if (state.interaction === 'all') {
        selectInteraction(delta > 0 ? stops[0] : stops[stops.length - 1], false);
        return;
    }
    const next = stops.indexOf(state.interaction) + delta;
    if (next >= 0 && next < stops.length) selectInteraction(stops[next], false);
}

// DIAGRAMA

/** El aviso del lienzo de una sesión abierta que aún no ha recibido ningún prompt: dónde está y qué saldrá ahí. */
function wfNotStarted() {
    const t = state.trace;
    const box = el('div', 'not-started');
    box.append(el('div', 'not-started-title', 'Sesión abierta, aún sin prompt'));
    if (placeOf(t)) box.append(el('div', 'not-started-place', placeOf(t)));
    box.append(el('p', null, 'Lo que haga Claude aparecerá aquí en cuanto le escribas. La rama y los cambios de su carpeta ya están en Cambios: pulsa un fichero para verlo.'));
    return box;
}

/** Monta el lienzo de una sesión sin pasos, vacío y con su aviso, o deja el que ya hay: su ficha abre los ficheros de
 * Cambios y se coloca como en cualquier otra sesión. */
function wfEmptyStage(box) {
    state.seqView = null;
    state.geo = null;
    renderTitle();
    updatePlayer([]);
    const key = `${state.trace.id}|empty`;
    let v = state.wf;
    if (!v || v.key !== key || !box.contains(v.root)) {
        box.replaceChildren();
        v = state.wf = wfStage(box, key);
        v.notice = el('div', 'wf-notice');
        v.notice.append(wfNotStarted());
        v.root.append(v.notice);
    }
    // Sin pasos no hay nada que seguir ni encuadrar: solo queda la rama, para llegar a los cambios.
    $('flow-bar').hidden = true;
    $('flow-dock').hidden = true;
    $('flow-git').hidden = !changesOn();
    $('flow-git').classList.toggle('in-stamp', state.mapLayout === 'diagram');
    wfPlaceOverlays(v);
}

/** Pinta el diagrama de lo elegido en la pestaña activa y le aplica la repetición, la búsqueda y lo señalado. */
function renderDiagram(scrollToStart) {
    // En la ventana de solo el chat o solo los cambios, el lienzo no se ve: no se pinta.
    if (PIECE === 'chat' || PIECE === 'changes' || PIECE === 'review') return;
    // Un repintado (al cambiar el tamaño, por ejemplo) mientras se lee una sesión borraría su cargador.
    if (state.loadingId) return;
    const box = $('diagram');
    if (state.tab !== 'map' || state.mapLayout !== 'diagram') dgLeave();
    if (state.tab !== 'map' || state.mapLayout !== 'obra') obLeave();
    ptModeBar();
    // El diagrama va en el mismo lienzo que el directo, y como él, se pone al día sin rehacerse.
    if (state.tab === 'map' && state.mapLayout === 'diagram' && state.trace && selectedSteps().length) {
        state.seqView = null;
        state.geo = null;
        renderTitle();
        showView();
        const steps = selectedSteps();
        updatePlayer(steps);
        dgLive(steps, box);
        decorate();
        refreshTip();
        return;
    }
    // La Obra monta su propia escena en el lienzo y la pone al día sola; el reproductor le dice qué momento enseñar.
    if (state.tab === 'map' && state.mapLayout === 'obra' && state.trace) {
        state.seqView = null;
        state.geo = null;
        $('flow-bar').hidden = true;
        $('flow-dock').hidden = true;
        renderTitle();
        showView();
        const steps = selectedSteps();
        updatePlayer(steps);
        obRender(steps, box);
        return;
    }
    // Ahora va sobre el lienzo, sin plano: la página se rehace entera con cada novedad y la ficha es la de siempre.
    if (state.tab === 'map' && state.mapLayout === 'now' && state.trace) {
        state.seqView = null;
        state.geo = null;
        $('flow-bar').hidden = true;
        $('flow-dock').hidden = true;
        renderTitle();
        showView();
        const steps = selectedSteps();
        updatePlayer(steps);
        ahRender(steps, box);
        decorate();
        refreshTip();
        return;
    }
    // El Parte en resumen, también sobre el lienzo; paso a paso es la Secuencia, más abajo.
    if (state.tab === 'map' && state.mapLayout === 'parte' && state.parteMode !== 'steps' && state.trace) {
        state.seqView = null;
        state.geo = null;
        $('flow-bar').hidden = true;
        $('flow-dock').hidden = true;
        renderTitle();
        showView();
        const steps = selectedSteps();
        updatePlayer(steps);
        ptRender(steps, box);
        decorate();
        refreshTip();
        return;
    }
    // El Parte paso a paso: la Secuencia de siempre, pero sobre el lienzo, para abrir cada paso en su ficha.
    if (state.tab === 'map' && state.mapLayout === 'parte' && state.parteMode === 'steps' && state.trace && selectedSteps().length) {
        state.geo = null;
        $('flow-bar').hidden = true;
        $('flow-dock').hidden = true;
        renderTitle();
        showView();
        const steps = selectedSteps();
        updatePlayer(steps);
        ptStepsRender(steps, box, scrollToStart);
        decorate();
        refreshTip();
        return;
    }
    // El Flujo no se rehace: se pone al día, para que no parpadee con cada novedad.
    if (state.tab === 'map' && state.mapLayout === 'live' && state.trace && selectedSteps().length) {
        state.seqView = null;
        state.geo = null;
        renderTitle();
        const steps = selectedSteps();
        updatePlayer(steps);
        liveCanvas(steps, box);
        decorate();
        refreshTip();
        return;
    }
    // Abierta y aún sin prompt: el lienzo se monta vacío, con su aviso, para que Cambios pueda abrir sus ficheros en la
    // ficha como siempre.
    if (state.tab === 'map' && state.mapLayout === 'live' && state.trace && state.trace.live && !state.trace.steps.length) {
        wfEmptyStage(box);
        return;
    }
    // Sin sesión o sin pasos que dibujar: cada vista con pasos ya se ha pintado arriba.
    state.wf = null;
    $('flow-bar').hidden = true;
    $('flow-dock').hidden = true;
    box.replaceChildren();
    state.seqView = null;
    state.geo = null;
    const t = state.trace;
    renderTitle();
    showView();
    updatePlayer(selectedSteps());
    if (t && t.live && !t.steps.length) box.append(wfNotStarted());
    else box.append(el('div', 'empty', t ? 'Esta interacción no tiene pasos que dibujar.' : 'Elige una sesión para ver cómo ha trabajado.'));
}

// Herramientas de agente que sabe dibujar la página; la app solo lee, de momento, sesiones de Claude Code.
const AGENTS = { claude: { name: 'Claude Code', color: '#d97757' } };

/** Logo de la herramienta de agente de la sesión; sin dato, Claude Code.
 *
 * El de Claude es su destello: doce rayos desde el centro, alternando largo y corto. */
function agentLogo(agent) {
    const a = AGENTS[agent] || AGENTS.claude;
    const logo = svg('svg', { class: 'agent-logo', viewBox: '0 0 24 24', width: 15, height: 15, role: 'img', 'aria-label': a.name });
    for (let k = 0; k < 12; k++) {
        const angle = (k * 30 + 8) * Math.PI / 180;
        const length = k % 2 ? 8 : 10.5;
        logo.append(svg('line', {
            x1: (12 + Math.cos(angle) * 2.2).toFixed(2), y1: (12 + Math.sin(angle) * 2.2).toFixed(2),
            x2: (12 + Math.cos(angle) * length).toFixed(2), y2: (12 + Math.sin(angle) * length).toFixed(2),
            stroke: a.color, 'stroke-width': 2.4, 'stroke-linecap': 'round',
        }));
    }
    return logo;
}

/** Escudo de secreto pendiente en Seguridad: un dibujo pequeño del color de peligro, con su exclamación. */
function shield() {
    const icon = svg('svg', { class: 'shield', viewBox: '0 0 16 16', width: 12, height: 12, role: 'img', 'aria-label': 'Secreto sin revisar' });
    icon.append(svg('path', { d: 'M8 1 2.5 3v4.2c0 3.3 2.3 6.3 5.5 7.8 3.2-1.5 5.5-4.5 5.5-7.8V3L8 1Z' }));
    icon.append(svg('path', { class: 'shield-mark', d: 'M8 4.6v3.8M8 10.7v.2' }));
    return icon;
}

/** Triángulo de hooks que fallan: del color de peligro, con su exclamación. */
function hookMark() {
    const icon = svg('svg', { class: 'hook-mark', viewBox: '0 0 16 16', width: 12, height: 12, role: 'img', 'aria-label': 'Los hooks de Arkko fallan' });
    icon.append(svg('path', { d: 'M8 1.2 15.2 14.4H.8Z' }));
    icon.append(svg('path', { class: 'shield-mark', d: 'M8 5.6v3.8M8 11.8v.2' }));
    return icon;
}

/** Órdenes de los pasos de la sesión en los que falló un hook de Arkko; se calculan una vez por sesión recibida. */
function hookOrders() {
    const t = state.trace;
    if (!t) return new Set();
    if (t.hookOrders === undefined) t.hookOrders = new Set(t.steps.filter(s => s.hookError).map(s => s.order));
    return t.hookOrders;
}

/** Lo que se explica al pasar por un fallo de hook: qué supone para lo que enseña la app. */
const HOOK_HINT = 'Lo que pasó en la sesión mientras fallaba no llegó a la app; los turnos que terminaron se cierran al leerlos en el transcript.';

/** Etiqueta del chat para un paso en el que falló un hook de Arkko: el triángulo y «Hook de Arkko», con el motivo en su ficha. */
function hookTag(s) {
    const tag = el('span', 'hook-tag');
    tag.append(hookMark(), 'Hook de Arkko');
    hint(tag, '⚠', 'Falló un hook de Arkko', s.hookError, HOOK_HINT);
    return tag;
}

/** Línea de la ficha de un paso en el que falló un hook de Arkko; null si no falló ninguno. */
function hookLine(s) {
    if (!s.hookError) return null;
    const line = el('div', 'tip-hook');
    line.append(hookMark(), s.hookError);
    return line;
}

/** Órdenes de los pasos de la sesión que llevan un secreto pendiente; se calculan una vez por sesión recibida. */
function secretOrders() {
    const t = state.trace;
    if (!t) return new Set();
    if (t.secretOrders === undefined) t.secretOrders = new Set(t.steps.filter(s => s.secrets).map(s => s.order));
    return t.secretOrders;
}

/** Lleva a la sección Seguridad, donde un secreto se rota o se descarta. */
function goSecurity(e) {
    if (e) e.stopPropagation();
    post({ type: 'security' });
}

/** Nombre de la sesión que se ve: el que tiene, o su primer prompt, o su id. */
function sessionName(t) {
    return t.title || t.interactions.find(i => i.number > 0)?.prompt || (t.steps.length ? t.id : 'Sin empezar');
}

/** Dónde trabaja la sesión que se ve: «repo · worktree», o solo la carpeta; vacío si no se sabe. */
function placeOf(t) {
    return [t.project, t.worktree].filter(Boolean).join(' · ');
}

/** Pinta las tareas de la sesión: todas llevan al Tablero y se quitan con su cruz, las demás se hacen principal con la estrella, las sugeridas se añaden con un clic y el + busca otra. */
function renderTasks() {
    const t = state.trace;
    const box = $('session-tasks');
    box.replaceChildren();
    const info = t && t.task;
    box.hidden = !info || (info.tasks.length === 0 && info.suggestions.length === 0 && !info.canAdd);
    if (box.hidden) return;
    for (const task of info.tasks) {
        const b = el('button', 'task-pill' + (task.principal ? ' principal' : ''), task.key);
        b.dataset.act = 'open';
        b.dataset.key = task.key;
        b.title = `${task.line}\n${task.principal ? 'Tarea principal: se lleva el coste de la sesión' : 'También trabaja en ella'}. Ver en el Tablero`;
        if (!task.principal) {
            const star = el('span', 'task-star', '\uE734');
            star.dataset.act = 'principal';
            star.dataset.key = task.key;
            star.title = 'Hacerla la principal: se lleva el coste de la sesión';
            b.append(star);
        }
        const x = el('span', 'task-x', '×');
        x.dataset.act = 'remove';
        x.dataset.key = task.key;
        x.title = task.principal ? 'Quitar esta tarea de la sesión: es la principal, así que su coste pasa a otra' : 'Quitar esta tarea de la sesión';
        b.append(x);
        box.append(b);
    }
    for (const s of info.suggestions) {
        const b = el('button', 'task-pill task-suggest', `+ ${s.key}`);
        b.dataset.act = 'link';
        b.dataset.key = s.key;
        b.title = `${s.line}\nSus prompts la mencionan: pulsa para añadirla a la sesión`;
        box.append(b);
    }
    if (info.canAdd) {
        const add = el('button', 'task-add', '+');
        add.dataset.act = 'add';
        add.title = 'Añadir una tarea a esta sesión';
        box.append(add);
    }
}

/** Pinta en la barra dónde trabaja la sesión que se ve y si va en directo, y debajo su nombre y sus interacciones. */
function renderTitle() {
    const t = state.trace;
    const box = $('session-title');
    box.replaceChildren();
    renderContext();
    const closed = $('session-closed');
    closed.hidden = !t || t.live;
    if (t && !t.live) closed.textContent = `Cerrada · ${agoText(lastActivity(t))}`;
    renderTasks();
    const secrets = t ? t.secrets || 0 : 0;
    $('session-secrets').hidden = secrets === 0;
    $('session-secrets').replaceChildren(shield(), `${secrets} secreto${secrets === 1 ? '' : 's'} sin revisar`);
    const arts = t ? t.artifacts || [] : [];
    $('session-artifacts').hidden = arts.length === 0;
    $('session-artifacts').replaceChildren(flowIcon('artifact', 'art-pill-icon'), arts.length === 1 ? '1 artifact' : `${arts.length} artifacts`);
    hint($('session-artifacts'), '◳', 'Publicado en claude.ai', null, 'Lo que esta sesión ha publicado en claude.ai: páginas, documentos, presentaciones… Pulsa para verlo y abrirlo.');
    if (!t) {
        box.append(el('div', 'name faint', 'Elige una sesión'));
        return;
    }
    // Sin carpeta conocida, el nombre de la sesión sube a ocupar su sitio.
    const place = placeOf(t);
    const name = el('div', 'name');
    name.append(agentLogo(t.agent), place || sessionName(t));
    if (t.working) name.append(el('span', 'live', '● EN DIRECTO'));
    const sub = el('div', 'sub');
    if (place) sub.append(sessionName(t), el('span', 'sep', '  ·  '));
    sub.append(el('span', 'faint', `${t.interactions.filter(i => i.number > 0).length} interacciones`));
    box.title = place ? `${place}\n${sessionName(t)}` : sessionName(t);
    box.append(name, sub);
}

/** Contexto de la sesión principal que se ve: en directo el de la statusline; cerrada, el de su última respuesta; null si no hay. */
function contextInfo() {
    const t = state.trace;
    if (!t) return null;
    const live = state.active.find(a => a.id === t.id);
    if (live && live.contextPct !== null && live.contextPct !== undefined && live.contextSize) {
        return { pct: live.contextPct, size: live.contextSize, tokens: Math.round(live.contextPct / 100 * live.contextSize) };
    }
    const c = t.context || {};
    if (!c.tokens) return null;
    return { tokens: c.tokens, size: c.size || null, pct: c.size ? Math.min(100, c.tokens / c.size * 100) : null };
}

/** Tokens abreviados: «950», «246,7k», «1M». */
function tokensText(n) {
    const short = (value, unit) => value.toFixed(1).replace(/\.0$/, '').replace('.', ',') + unit;
    if (n >= 1e6) return short(n / 1e6, 'M');
    if (n >= 1e3) return short(n / 1e3, 'k');
    return String(n);
}

/** Barrita de contexto con su porcentaje, ámbar desde el 60 % y roja desde el 80 %; sin tamaño conocido, solo los tokens. */
function contextBar(info) {
    const box = el('span', 'ctx');
    if (info.pct === null) {
        box.append(el('span', 'ctx-text', `${tokensText(info.tokens)} de contexto`));
        return box;
    }
    const bar = el('span', 'ctx-bar ' + contextLevel(info.pct));
    const fill = el('i');
    fill.style.width = `${Math.max(2, Math.min(100, info.pct))}%`;
    bar.append(fill);
    box.append(bar, el('span', 'ctx-text', `${Math.round(info.pct)} % de ${tokensText(info.size)}`));
    return box;
}

/** Nivel de un porcentaje de contexto para su color: «high» desde el 80 %, «mid» desde el 60 %, vacío por debajo. */
function contextLevel(pct) {
    return pct >= 80 ? 'high' : pct >= 60 ? 'mid' : '';
}

/** Pinta junto al título el anillo de contexto de la sesión principal, o lo esconde si no se sabe; sin tamaño, solo los tokens. */
function renderContext() {
    const box = $('session-context');
    const info = contextInfo();
    box.replaceChildren();
    box.hidden = !info;
    if (!info) return;
    if (info.pct === null) {
        box.append(el('span', 'ring-text', tokensText(info.tokens)));
    } else {
        const ring = el('span', 'ring ' + contextLevel(info.pct));
        ring.style.setProperty('--p', String(Math.max(1, Math.min(100, info.pct))));
        box.append(ring, el('span', 'ring-text', `${Math.round(info.pct)} %`));
    }
    box.title = 'Contexto de la sesión principal: '
        + (info.size ? `${info.tokens.toLocaleString('es-ES')} de ${info.size.toLocaleString('es-ES')} tokens` : `${info.tokens.toLocaleString('es-ES')} tokens`);
}

// ACTIVAS

/** Pinta las sesiones abiertas en un bloque por repositorio (o carpeta), en el color del grupo, con la que se ve resaltada. */
function renderActive() {
    // Repintar a mitad de un arrastre lo cortaría: se pinta al soltar.
    if (state.dragGroup) return;
    const box = $('active-list');
    const keep = box.scrollTop;
    box.replaceChildren();
    $('active-count').textContent = state.active.length ? String(state.active.length) : '';
    const viewing = viewingCard();
    if (viewing) box.append(viewing);
    if (state.active.length === 0) {
        box.append(el('div', 'empty small', 'Ninguna sesión abierta.'));
        updateGroupSteps();
        return;
    }
    for (const group of orderedGroups([...new Set(state.active.map(s => s.group))])) {
        const sessions = state.active.filter(s => s.group === group);
        const block = el('div', 'repo-group');
        block.dataset.group = group;
        const color = state.groupColors[group];
        if (color) block.style.setProperty('--g-raw', color);
        const head = el('div', 'repo');
        head.append(el('span', 'repo-name', group));
        // Un secreto sin revisar en cualquiera de sus sesiones: el grupo entero avisa, también plegado.
        const secrets = sessions.filter(s => s.secret);
        if (secrets.length) {
            block.classList.add('has-secret');
            head.append(groupShield(secrets, 'repo-shield'));
        }
        head.append(el('span', 'repo-count', String(sessions.length)));
        // Plegado, el nombre va de lado, junto a las gotas del grupo.
        const side = el('div', 'repo-side', group);
        head.dataset.tip = side.dataset.tip = `g:${group}`;
        dragGroup(block, group, [head, side]);
        const cards = el('div', 'repo-cards');
        sessions.forEach(s => cards.append(activeCard(s)));
        block.append(head, side, cards);
        // Plegado no hay cabecera: el escudo va en una insignia en la esquina del bloque.
        if (secrets.length) block.append(groupShield(secrets, 'repo-flag'));
        block.addEventListener('contextmenu', e => {
            e.preventDefault();
            openColorMenu(group, e.clientX, e.clientY);
        });
        box.append(block);
    }
    box.scrollTop = keep;
    updateGroupSteps();
    refreshTip();
}

/** Escudo de un grupo de activas con secretos sin revisar: dice en qué sesiones y lleva a Seguridad. */
function groupShield(sessions, cls) {
    const guard = el('span', cls);
    guard.title = `Secreto sin revisar en ${sessions.map(s => `${s.worktree || s.name || 'una sesión'} (${s.secret})`).join(', ')}`;
    guard.append(shield());
    guard.addEventListener('pointerdown', e => e.stopPropagation());
    guard.addEventListener('click', goSecurity);
    return guard;
}

// VIENDO: la última sesión cerrada abierta, para volver a ella; y las sesiones ya vistas, marcadas en el historial

// Cuántas sesiones cerradas vistas se recuerdan; las más antiguas se olvidan.
const SEEN_MAX = 10;

/** Apunta que se ha visto una sesión cerrada ahora, para marcarla en el historial; las abiertas no cuentan. */
function markSeen(id) {
    if (state.active.some(a => a.id === id)) return;
    state.seen[id] = Date.now();
    const ids = Object.keys(state.seen);
    if (ids.length > SEEN_MAX) {
        ids.sort((a, b) => state.seen[a] - state.seen[b]).slice(0, ids.length - SEEN_MAX).forEach(old => delete state.seen[old]);
    }
    remember('seen', state.seen);
}

/** Hora de lo último que pasó en una sesión, en ISO; null si no tiene pasos. */
function lastActivity(t) {
    const times = t.steps.map(s => s.end || s.start).filter(Boolean);
    return times.length ? times.reduce((a, b) => (a > b ? a : b)) : null;
}

/** Cuánto hace de una hora, en corto: «ahora», «hace 5 min», «hace 3 h», «hace 2 días». */
function agoText(iso) {
    if (!iso) return 'sin actividad';
    const min = Math.floor((Date.now() - new Date(iso).getTime()) / 60000);
    if (min < 1) return 'ahora';
    if (min < 60) return `hace ${min} min`;
    const hours = Math.floor(min / 60);
    if (hours < 24) return `hace ${hours} h`;
    const days = Math.floor(hours / 24);
    return `hace ${days} día${days === 1 ? '' : 's'}`;
}

/** Al recibir una sesión: si está cerrada pasa a ser la que se está viendo; si es la del hueco y se ha vuelto a abrir, lo deja libre. */
function noteViewing(t) {
    if (!t.live) {
        state.viewing = { id: t.id, place: placeOf(t), name: sessionName(t), last: lastActivity(t) };
        remember('viewing', state.viewing);
    } else if (state.viewing && state.viewing.id === t.id) {
        state.viewing = null;
        remember('viewing', null);
    }
    renderActive();
    refreshSessionList();
}

/** Deja la vista vacía, como al entrar: sin sesión elegida, sin diagrama ni chat.
 *
 * La sesión que se deja se apunta, para no volver a pintarla si la app aún manda algo suyo. */
function clearView() {
    state.dropped = state.selectedId;
    state.loadingId = null;
    pause();
    state.replay.index = null;
    document.querySelector('.app').classList.remove('replaying');
    state.selectedId = null;
    state.trace = null;
    state.interaction = null;
    state.focus = null;
    hideTip();
    $('session-head').replaceChildren(el('div', 'faint', 'Elige una sesión de la lista.'));
    $('interactions').replaceChildren();
    $('all-row').replaceChildren();
    $('chat-label').replaceChildren(el('span', 'chat-title', 'Chat'), el('span', 'chat-sub', 'Sin sesión'));
    $('chat').replaceChildren(el('div', 'empty', 'Aquí se ve, paso a paso, lo que ha hecho la sesión.'));
    $('to-bottom').hidden = true;
    state.changes.sets = { pending: null, branch: null, since: null, repo: null };
    repoSearchReset();
    showChanges();
    renderLegend();
    renderDiagram(true);
    updateNav();
    refreshSessionList();
}

/** Quita la sesión cerrada del hueco «Viendo»; si es la que se está viendo, deja también la vista vacía. */
function clearViewing(e) {
    e.stopPropagation();
    const id = state.viewing && state.viewing.id;
    state.viewing = null;
    remember('viewing', null);
    if (id && id === state.selectedId) clearView();
    renderActive();
}

/** Tarjeta del hueco «Viendo»: la sesión cerrada que se estaba revisando, en gris, para volver a ella; null si no hay o se ha vuelto a abrir. */
function viewingCard() {
    const v = state.viewing;
    if (!v || state.active.some(a => a.id === v.id)) return null;
    const box = el('div', 'viewing');
    box.append(el('div', 'viewing-title', 'Viendo'));
    const card = el('button', 'act-card closed' + (v.id === state.selectedId ? ' on' : ''));
    card.dataset.id = v.id;
    card.title = `${v.place || v.name}\nSesión cerrada`;
    const text = el('div', 'act-text');
    const title = el('div', 'act-name');
    title.append(el('span', 'act-label', v.place || v.name));
    text.append(title, el('div', 'act-sub', `Cerrada · ${agoText(v.last)}${v.place ? ` · ${v.name}` : ''}`));
    const close = el('span', 'viewing-close', '✕');
    close.title = 'Quitar del panel';
    close.addEventListener('click', clearViewing);
    card.append(el('span', 'dot closed'), text, close);
    card.addEventListener('click', () => {
        setListsOpen(false);
        openSession(v.id, v.id === state.selectedId);
    });
    box.append(card);
    return box;
}

/** Grupos de activas en el orden que dejó la persona; los que aún no ha colocado van detrás, en el de la app. */
function orderedGroups(groups) {
    const saved = state.groupOrder;
    const rank = g => (saved.includes(g) ? saved.indexOf(g) : saved.length);
    return groups.map((g, i) => [g, i]).sort((a, b) => rank(a[0]) - rank(b[0]) || a[1] - b[1]).map(x => x[0]);
}

// Lo que hay que mover el ratón con el botón pulsado para que empiece un arrastre y no sea un clic.
const DRAG_START = 4;

/** Deja arrastrar un grupo por su cabecera (o, plegado, por su nombre de lado). */
function dragGroup(block, group, handles) {
    for (const handle of handles) handle.addEventListener('pointerdown', e => startGroupDrag(e, block, group, handle));
}

/** Arrastra un grupo con el puntero: el bloque entero sigue al ratón y los demás se apartan a su paso; al soltar se guarda el orden.
 *
 * El bloque cambia de sitio en la lista en cuanto su centro pasa el del vecino, y un desplazamiento lo deja bajo
 * el ratón; al vecino se le anima el salto, para que se vea apartarse. Se mueven siempre los vecinos y nunca el
 * bloque: sacar del documento el elemento que tiene el puntero capturado suelta la captura, y el arrastre se
 * quedaría sin su «soltar». */
function startGroupDrag(e, block, group, handle) {
    if (e.button !== 0) return;
    const list = $('active-list');
    const startY = e.clientY;
    const startTop = block.offsetTop;
    let moved = false;
    let done = false;
    if (handle.setPointerCapture) handle.setPointerCapture(e.pointerId);

    const move = ev => {
        // Sin botón pulsado, el «soltar» se perdió fuera de la ventana: se suelta ahora.
        if (ev.buttons === 0) {
            end();
            return;
        }
        const dy = ev.clientY - startY;
        if (!moved) {
            if (Math.abs(dy) < DRAG_START) return;
            moved = true;
            state.dragGroup = group;
            hideTip();
            block.classList.add('dragging');
            list.classList.add('sorting');
        }
        const mid = startTop + dy + block.offsetHeight / 2;
        let prev = block.previousElementSibling;
        while (prev && mid < prev.offsetTop + prev.offsetHeight / 2) {
            const before = prev.offsetTop;
            list.insertBefore(prev, block.nextElementSibling);
            glide(prev, before);
            prev = block.previousElementSibling;
        }
        let next = block.nextElementSibling;
        while (next && mid > next.offsetTop + next.offsetHeight / 2) {
            const before = next.offsetTop;
            list.insertBefore(next, block);
            glide(next, before);
            next = block.nextElementSibling;
        }
        block.style.transform = `translateY(${dy - (block.offsetTop - startTop)}px)`;
    };
    const end = () => {
        if (done) return;
        done = true;
        window.removeEventListener('pointermove', move);
        window.removeEventListener('pointerup', end);
        window.removeEventListener('pointercancel', end);
        window.removeEventListener('blur', end);
        handle.removeEventListener('lostpointercapture', end);
        if (!moved) return;
        block.style.transform = '';
        block.classList.remove('dragging');
        list.classList.remove('sorting');
        saveGroupOrder();
        state.dragGroup = null;
        renderActive();
    };
    // En la ventana y no en la cabecera: así llegan aunque se pierda la captura; y si se pierde, se suelta.
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', end);
    window.addEventListener('pointercancel', end);
    window.addEventListener('blur', end);
    handle.addEventListener('lostpointercapture', end);
}

/** Anima el salto de un grupo que se aparta: parte de donde estaba y llega a su sitio nuevo. */
function glide(node, before) {
    const delta = before - node.offsetTop;
    if (!delta) return;
    node.style.transition = 'none';
    node.style.transform = `translateY(${delta}px)`;
    requestAnimationFrame(() => {
        node.style.transition = '';
        node.style.transform = '';
    });
}

/** Guarda el orden de los grupos tal como están en el panel; los que no están abiertos ahora conservan el suyo detrás. */
function saveGroupOrder() {
    const order = [...$('active-list').querySelectorAll('.repo-group')].map(b => b.dataset.group);
    state.groupOrder = [...order, ...state.groupOrder.filter(g => !order.includes(g))];
    remember('groupOrder', state.groupOrder);
}

// TOOLTIPS: los de las activas y los de la secuencia

// Tarda un poco en salir, para no parpadear al pasar el ratón por encima de la lista.
const TIP_DELAY = 350;

/** Sigue el ratón por un panel: saca el tooltip del elemento que lo lleva y lo quita al salir. */
function trackTip(e) {
    const node = e.target.closest ? e.target.closest('[data-tip]') : null;
    if (!node) {
        hideTip();
        return;
    }
    if (node.dataset.tip === state.tip.key) return;
    hideTip();
    state.tip.key = node.dataset.tip;
    state.tip.node = node;
    // Al cumplirse la espera se busca el elemento de nuevo: en directo, la fila se ha podido repintar entretanto.
    state.tip.timer = setTimeout(() => {
        const current = tipNode(state.tip.key);
        if (current) showTip(current);
    }, TIP_DELAY);
}

/** Elemento que lleva ahora el tooltip de una clave: el que está bajo el ratón si sigue en la página; si no, el primero
 * con esa clave, o null si ya no hay ninguno.
 *
 * Dos elementos pueden llevar la misma ficha (en el Diagrama, la copia del borde y la parada de verdad, fuera de la
 * vista): el tooltip va junto al que se señala. */
function tipNode(key) {
    if (!key) return null;
    const held = state.tip.node;
    if (held && held.isConnected && held.dataset.tip === key) return held;
    return [...document.querySelectorAll('[data-tip]')].find(n => n.dataset.tip === key) || null;
}

/** Enseña el tooltip junto a su elemento, siempre dentro de la ventana.
 *
 * Los de las activas salen a la derecha (o a la izquierda si no caben). Los de la secuencia son una
 * tarjeta que apunta al icono de su paso. */
function showTip(node) {
    const key = node.dataset.tip;
    const content = tipContent(key);
    const tip = $('tip');
    if (!content) {
        hideTip();
        return;
    }
    const look = tipLook(key);
    tip.className = look ? `tip ${look}` : 'tip';
    if (node.dataset.tipActor) paint(tip, node.dataset.tipActor);
    tip.replaceChildren(...content);
    tip.hidden = false;
    const r = node.getBoundingClientRect();
    if (look) {
        placeOnIcon(tip, node, (node.closest('.seq-row') || node).getBoundingClientRect());
        return;
    }
    let left = r.right + 8;
    if (left + tip.offsetWidth > window.innerWidth - 8) left = Math.max(8, r.left - tip.offsetWidth - 8);
    tip.style.left = `${left}px`;
    tip.style.top = `${Math.max(8, Math.min(r.top, window.innerHeight - tip.offsetHeight - 8))}px`;
}

/** Aspecto del tooltip de una clave: tarjeta en la secuencia y el de siempre en las activas. */
function tipLook(key) {
    return /^[qralmh]:/.test(key) ? 'as-card' : '';
}

// Separación del tooltip a su fila, que deja sitio al pico, y dónde cae el pico desde su borde izquierdo.
const TIP_GAP = 9;
const TIP_TAIL = 20;

/** Coloca el tooltip de la secuencia con el pico en el icono del paso: debajo si la fila está en la mitad de arriba del diagrama, encima si está en la de abajo. */
function placeOnIcon(tip, node, r) {
    const icon = (node.querySelector('.seq-icon, .avatar') || node).getBoundingClientRect();
    const x = icon.left + icon.width / 2;
    const box = $('diagram').getBoundingClientRect();
    let below = r.top + r.height / 2 < box.top + box.height / 2;
    if (below && r.bottom + TIP_GAP + tip.offsetHeight > window.innerHeight - 8) below = false;
    if (!below && r.top - TIP_GAP - tip.offsetHeight < 8) below = true;
    tip.classList.toggle('below', below);
    const left = Math.max(8, Math.min(x - TIP_TAIL, window.innerWidth - tip.offsetWidth - 8));
    tip.style.setProperty('--tail', `${x - left}px`);
    tip.style.left = `${left}px`;
    tip.style.top = `${below ? r.bottom + TIP_GAP : r.top - TIP_GAP - tip.offsetHeight}px`;
}

/** Quita el tooltip y olvida el que estaba por salir. */
function hideTip() {
    clearTimeout(state.tip.timer);
    state.tip.key = null;
    $('tip').hidden = true;
}

/** Tras repintar un panel, vuelve a poner el tooltip abierto sobre su elemento nuevo, con los datos al día; si ya no está, lo quita. */
function refreshTip() {
    if ($('tip').hidden || !state.tip.key) return;
    const node = tipNode(state.tip.key);
    if (node) showTip(node);
    else hideTip();
}

/** Rejilla de etiqueta y valor del tooltip, con la función que le añade filas. */
function tipGrid() {
    const grid = el('div', 'tip-grid');
    const row = (label, ...value) => {
        const cell = el('span', 'tip-value');
        cell.append(...value);
        grid.append(el('span', 'tip-label', label), cell);
    };
    return [grid, row];
}

/** Contenido del tooltip de una clave.
 *
 * Activas: «s:<id>» una sesión y «g:<grupo>» la cabecera de un grupo. Secuencia: «q:<orden>» un paso,
 * «r:<agente>» la vuelta de un agente y «a:<actor>» un actor de la cabecera. Leyenda: «l:<actor>». Mapa:
 * «m:<agente>» la tarjeta de un agente. */
function tipContent(key) {
    if (key.startsWith('h:')) return hintTip(JSON.parse(key.slice(2)));
    if (key.startsWith('q:')) return stepTip(stepAt(Number(key.slice(2))));
    if (key.startsWith('r:')) return backTip(key.slice(2));
    if (key.startsWith('a:') || key.startsWith('l:')) return actorTip(key.slice(2));
    if (key.startsWith('m:')) return agentTip(key.slice(2));
    if (key.startsWith('g:')) {
        const group = key.slice(2);
        const count = state.active.filter(s => s.group === group).length;
        return [
            tipHead(group, `${count} ${count === 1 ? 'sesión abierta' : 'sesiones abiertas'}`, group),
        ];
    }
    const s = state.active.find(a => a.id === key.slice(2));
    if (!s) return null;
    const now = new Date().toISOString();
    const short = ms => duration(ms).replace(/ \d+ s$/, '');
    const [grid, row] = tipGrid();

    const since = between(s.since, now);
    row('Estado', el('span', 'dot ' + (s.statusKey || 'idle')), s.status,
        since !== null && since >= 60000 ? el('span', 'tip-faint', ` · hace ${short(since)}`) : '');
    // Lo que espera en segundo plano: cada comando, con lo que lleva y el agente que lo lanzó.
    for (const c of s.background && s.statusKey === 'command' ? (s.background.commands || []).filter(x => x.awaited) : [])
        row('Espera a', el('span', 'tip-cmd', '>_'), c.description, el('span', 'tip-faint', ` · ${short(between(c.start, now))}${c.by ? ` · de «${c.by}»` : ''}`));
    if (s.model) row('Modelo', s.model);
    if (s.effort) {
        const bars = el('span', 'tip-effort');
        for (let i = 1; i <= 5; i++) bars.append(el('i', i <= s.effortLevel ? 'on' : null));
        row('Esfuerzo', bars, s.effort);
    }
    if (s.contextPct !== null && s.contextPct !== undefined && s.contextSize) {
        row('Contexto', contextBar({ pct: s.contextPct, size: s.contextSize, tokens: 0 }));
    }
    if (s.cost !== null && s.cost !== undefined) row('Coste', `$${s.cost.toFixed(2)}`);
    if (s.started) row('Abierta', short(between(s.started, now)), el('span', 'tip-faint', ` · desde las ${hour(s.started)}`));
    if (s.secret) row('Secreto', el('span', 'tip-secret-value', s.secret));
    if (s.hooks) row('Hooks', el('span', 'tip-secret-value', `Falló el último, a las ${hour(s.hooksAt)}: ${s.hooks}`));
    return [tipHead(s.name || s.worktree || `Sesión de las ${hour(s.started)}`, [s.group, s.worktree].filter(Boolean).join(' · '), s.group), grid];
}

/** Cabecera del tooltip: el título y, debajo, su dónde, con una raya del color del grupo o del que se le dé. */
function tipHead(title, sub, group, color) {
    const head = el('div', 'tip-head');
    const stripe = color || state.groupColors[group];
    if (stripe) head.style.setProperty('--g-raw', stripe);
    head.append(el('div', 'tip-title', title), el('div', 'tip-sub', sub));
    return head;
}

// Lo que enseña un tooltip de un texto largo: sus primeras líneas, sin pasar de estos caracteres.
const TIP_LINES = 16;
const TIP_CHARS = 1200;

/** Principio de un texto con su formato de Markdown, para un tooltip. */
function tipMarkdown(text) {
    const box = el('div', 'tip-md');
    box.append(markdown(firstLines(text, TIP_LINES, TIP_CHARS)));
    return box;
}

/** Primeras líneas de un texto, cortado con «…» si sigue. */
function firstLines(text, lines, max) {
    const all = (text || '').replace(/\r/g, '').trim();
    let shown = all.split('\n').slice(0, lines).join('\n');
    if (shown.length > max) shown = shown.slice(0, max - 1).trimEnd();
    return shown + (shown.length < all.length ? '…' : '');
}

/** Entrada de la llamada de un paso, ya llegada de la app; null si aún no está o no es un objeto. */
function callInput(s) {
    const detail = state.details.get(s.order);
    if (!detail || !detail.text) return null;
    try {
        const input = JSON.parse(detail.text);
        return input && typeof input === 'object' ? input : null;
    } catch {
        return null;
    }
}

/** Icono y nombre de un rótulo de la secuencia («🔧 Bash» → «🔧», «Bash»). */
function splitLabel(label) {
    const space = label.indexOf(' ');
    return space > 0 ? [label.slice(0, space), label.slice(space + 1)] : ['', label];
}

/** Cabecera de la tarjeta: el icono y el nombre del paso, su hora y, si falló, la marca de error. */
function cardHead(icon, name, meta, error) {
    const head = el('div', 'tip-card-head');
    head.append(el('span', 'tip-card-icon', icon), el('strong', null, name));
    if (meta) head.append(el('span', 'tip-card-meta', meta));
    if (error) head.append(el('span', 'tip-card-error', 'error'));
    return head;
}

/** Cuerpo de la tarjeta con lo que haya; null si no hay nada que enseñar. */
function cardBody(...parts) {
    const shown = parts.filter(Boolean);
    if (shown.length === 0) return null;
    const body = el('div', 'tip-card-body');
    body.append(...shown);
    return body;
}

/** Etiqueta del chat para un paso con secreto pendiente: escudo y tipo; lleva a Seguridad. */
function secretTag(s) {
    const tag = el('span', 'secret-tag');
    tag.title = `${s.secrets.map(x => `${x.kind} ${x.masked}`).join('\n')}\nSin revisar`;
    tag.append(shield(), s.secrets.map(x => x.kind).join(', '));
    tag.addEventListener('click', goSecurity);
    return tag;
}

/** Línea del tooltip con los secretos pendientes del paso: tipo y máscara, y si está más allá de lo guardado; null si no lleva. */
function secretLine(s) {
    if (!s.secrets) return null;
    const line = el('div', 'tip-secret');
    const hidden = s.secrets.some(x => x.hidden) ? ' · más allá de lo que se guarda del paso' : '';
    line.append(shield(), `${s.secrets.map(x => `${x.kind} ${x.masked}`).join(' · ')} · sin revisar en Seguridad${hidden}`);
    return line;
}

/** Dónde está un secreto en el texto de su paso, en una frase; null si no se sabe. */
function secretWhere(x) {
    if (x.at === null || x.at === undefined || !x.of) return null;
    const at = `en el carácter ${(x.at + 1).toLocaleString('es-ES')} de ${x.of.toLocaleString('es-ES')}`;
    return x.hidden ? `No se ve en el paso: está ${at}, más allá de lo que se guarda de él.` : `Está ${at}.`;
}

/** Lo que la app ha dicho del texto de alrededor de un secreto: undefined mientras se lee (y lo pide la primera vez),
 * null si no se encuentra, o su contexto y su llamada, tapados. Solo vive mientras se ve la sesión. */
function secretSpot(ref) {
    const t = state.trace;
    if (!t) return null;
    // En directo cada novedad trae un objeto de sesión nuevo: lo leído se guarda aparte, y solo de la sesión que se ve.
    if (!state.secretSpots || state.secretSpots.id !== t.id) state.secretSpots = { id: t.id, refs: new Map() };
    const refs = state.secretSpots.refs;
    if (!refs.has(ref)) {
        refs.set(ref, undefined);
        post({ type: 'secretSpot', ref });
    }
    return refs.get(ref);
}

/** Guarda el texto de alrededor de un secreto que manda la app y repinta lo que lo enseña: su paso del chat y la ficha abierta. */
function receiveSecretSpot(m) {
    const t = state.trace;
    if (!t || !state.secretSpots || state.secretSpots.id !== t.id) return;
    state.secretSpots.refs.set(m.ref, m.context ? { context: m.context, call: m.call } : null);
    for (const s of t.steps.filter(x => x.secrets && x.secrets.some(y => y.id === m.ref))) repaintStep(s);
    if (state.wf && state.wf.detail && state.wf.drawer && !state.wf.drawer.hidden) wfDetail(state.wf, state.wf.detail);
}

/** Aviso de la ficha de un paso con secretos pendientes: cuál es, dónde está y el texto de alrededor, siempre tapado. */
function secretBox(steps) {
    const box = el('div', 'wf-secret');
    for (const s of steps) {
        for (const x of s.secrets) {
            const row = el('div', 'wf-secret-row');
            const head = el('div', 'tip-secret');
            head.append(shield(), `${x.kind} ${x.masked} · sin revisar en Seguridad`);
            row.append(head);
            const where = secretWhere(x);
            if (where) row.append(el('div', 'wf-secret-where', where));
            // El texto de alrededor no viaja con la sesión: se pide a la app, que lo lee del transcript ya tapado.
            const spot = secretSpot(x.id);
            if (spot === undefined) row.append(el('div', 'wf-secret-where', 'Leyendo el texto de alrededor…'));
            else if (spot && spot.call) row.append(el('div', 'wf-secret-where', `Llamada: ${spot.call}`));
            if (spot && spot.context) row.append(el('code', 'wf-secret-context', spot.context));
            box.append(row);
        }
    }
    const go = el('button', 'wf-secret-go', 'Ver en Seguridad');
    go.type = 'button';
    go.addEventListener('click', goSecurity);
    box.append(go);
    return box;
}

/** Lleva al paso donde apareció un secreto, pedido desde Seguridad: lo señala, despliega su agente y abre su ficha. */
function focusSecret() {
    const ref = state.focusSecret;
    if (!ref || !state.trace) return;
    const s = state.trace.steps.find(x => x.secrets && x.secrets.some(y => y.id === ref));
    state.focusSecret = null;
    if (!s) return;
    setChatOpen(true, false);
    reveal(s);
    if (!state.expanded.has(s.order)) toggle(s);
    focusStep(s.order, 'diagram');
    const v = state.wf;
    if (!v || !v.nodeData) return;
    // El nodo del paso o, si su agente va plegado en el lienzo, el del agente, cuya ficha lista sus llamadas.
    const nodes = [...v.nodeData.entries()];
    const hit = nodes.find(([, n]) => (n.orders || []).includes(s.order) || (n.step && n.step.order === s.order))
        || nodes.find(([, n]) => n.kind === 'agent' && s.agentId && n.lane === s.agentId);
    if (!hit) return;
    v.detail = hit[0];
    wfDetail(v, hit[0]);
}

/** Pide a la app la llamada de un paso para su tooltip, una sola vez. */
function askCall(s) {
    if (!s.hasDetail || state.details.has(s.order) || state.tipAsks.has(s.order)) return;
    state.tipAsks.add(s.order);
    post({ type: 'detail', order: s.order });
}

/** Tooltip de un paso de la secuencia: lo que se dice o, en una herramienta, su llamada entera, que la fila corta.
 *
 * La llamada no viene con la sesión: se pide a la app al pasar por encima y el tooltip se rehace al llegar. */
function stepTip(s) {
    if (!s) return null;
    const [icon, name] = splitLabel(seqLabel({ step: s })[0]);
    const meta = [hour(s.start), s.agentId ? `en ${actorName(s.agentId)}` : null].filter(Boolean).join(' · ');
    const call = (s.kind === 'tool' || s.kind === 'skill' || s.kind === 'agent') && !s.questions;
    if (!call) {
        const text = s.questions ? s.questions.map(q => q.question).join('\n\n') : plain(s.text || s.title);
        const body = s.shell ? el('div', 'tip-code', shellCommand(s)) : tipMarkdown(text);
        return [cardHead(icon, name, meta), cardBody(secretLine(s), hookLine(s), body)].filter(Boolean);
    }

    askCall(s);
    const input = callInput(s);
    const detail = state.details.get(s.order);
    const note = input && input.description ? el('div', 'tip-note', input.description) : null;
    let main;
    if (s.kind === 'agent') main = input && input.prompt ? tipMarkdown(input.prompt) : null;
    else {
        const what = input ? callMain(input) : seqLabel({ step: s })[1];
        main = what ? el('div', 'tip-code', firstLines(what, 12, 900)) : null;
    }
    const error = s.isError && detail && detail.result ? el('div', 'tip-result', firstLines(detail.result, 3, 300)) : null;
    const loading = !detail && s.hasDetail ? el('div', 'tip-note', 'Cargando la llamada…') : null;
    return [cardHead(icon, name, meta, s.isError), cardBody(secretLine(s), hookLine(s), note, main, error, loading)].filter(Boolean);
}

/** Lo que pide una llamada, entero: el comando, el fichero, el patrón, la dirección…; si no trae nada de eso, sus campos de texto. */
function callMain(input) {
    const main = input.command ?? input.file_path ?? input.notebook_path ?? input.pattern ?? input.url ?? input.query ?? input.skill;
    if (main !== undefined && main !== null) {
        const where = [input.path && `en ${input.path}`, input.glob && `ficheros ${input.glob}`, input.args].filter(Boolean);
        return [String(main), ...where].join('\n');
    }
    return Object.entries(input)
        .filter(([key, value]) => key !== 'description' && (typeof value === 'string' || typeof value === 'number'))
        .map(([key, value]) => `${key}: ${value}`)
        .join('\n');
}

/** Pone a un elemento el tooltip de tarjeta con un icono, un título, un dato al lado y una explicación debajo.
 *
 * Sustituye al «title» del sistema, que sale sin formato y tarda en aparecer. */
function hint(node, icon, title, meta, body) {
    node.dataset.tip = `h:${JSON.stringify({ icon, title, meta, body })}`;
    return node;
}

/** Contenido del tooltip que puso «hint». */
function hintTip(h) {
    return [cardHead(h.icon || '', h.title, h.meta), cardBody(h.body ? el('div', 'tip-note', h.body) : null)].filter(Boolean);
}

/** Tooltip de la vuelta de un agente: su encargo. */
function backTip(id) {
    const a = agentOf(id);
    if (!a) return null;
    const head = cardHead('↩', `${actorName(id)} vuelve`, `a ${actorName(a.parentAgentId || 'main')}`);
    return [head, cardBody(el('div', 'tip-note', agentKind(a)))].filter(Boolean);
}

// Subagentes que lista el tooltip de un agente antes de resumir el resto en «y N más».
const TIP_AGENTS = 10;

/** Tooltip de un actor de la cabecera o de la leyenda: la persona, Claude o la ficha de un agente. */
function actorTip(key) {
    if (agentOf(key)) return agentTip(key);
    const what = key === 'user' ? 'Quien escribe los prompts' : 'La sesión principal';
    return [cardHead(actorIcon(key), actorName(key), null), cardBody(el('div', 'tip-says', what))];
}

/** Ficha de un agente: modelo, duración, quién lo lanzó, herramientas y skills y, si lanzó otros, cada uno con su modelo y su duración. */
function agentTip(id) {
    const a = agentOf(id);
    if (!a) return null;
    const [grid, row] = tipGrid();
    if (a.model) row('Modelo', modelChip(a.model));
    const span = [hour(a.startUtc), hour(a.endUtc)].filter(Boolean).join(' → ');
    row('Duración', [duration(between(a.startUtc, a.endUtc)), span].filter(Boolean).join(' · '));
    row('Lanzado por', actorName(agentOf(a.parentAgentId) ? a.parentAgentId : 'main'));

    const tools = new Map();
    const skills = new Set();
    for (const s of state.trace.steps) {
        if (s.agentId !== id) continue;
        if (s.kind === 'tool') {
            const name = s.title.split(' · ')[0];
            tools.set(name, (tools.get(name) || 0) + 1);
        } else if (s.kind === 'skill') skills.add(s.title);
    }
    const total = [...tools.values()].reduce((sum, n) => sum + n, 0);
    if (total) {
        const top = [...tools].sort((x, y) => y[1] - x[1]).slice(0, 4).map(([name, n]) => `${name} ${n}`).join(', ');
        row('Herramientas', `${total} · ${top}`);
    }
    if (skills.size) row('Skills', [...skills].join(', '));
    if (a.background) row('Modo', 'en segundo plano');

    const below = descendants(id).sort((x, y) => (x.startUtc || '').localeCompare(y.startUtc || ''));
    let list = null;
    if (below.length) {
        list = el('div', 'tip-agents');
        const models = new Map();
        for (const x of below) models.set(x.model || '?', (models.get(x.model || '?') || 0) + 1);
        const summary = [...models].sort((x, y) => y[1] - x[1]).map(([m, n]) => `${n} ${m}`).join(' · ');
        list.append(el('div', 'tip-label', `${below.length} subagente${below.length === 1 ? '' : 's'} · ${summary}`));
        for (const x of below.slice(0, TIP_AGENTS)) {
            const line = paint(el('div', 'tip-agent'), x.id);
            line.append(el('span', 'tip-agent-name', actorName(x.id)), x.model ? modelChip(x.model) : el('span'),
                el('span', 'tip-agent-dur', duration(between(x.startUtc, x.endUtc))));
            list.append(line);
        }
        if (below.length > TIP_AGENTS) list.append(el('div', 'tip-faint', `y ${below.length - TIP_AGENTS} más`));
    }
    return [cardHead(actorIcon(id), actorName(id), a.type), cardBody(grid, list)].filter(Boolean);
}

/** Agentes que cuelgan de uno, a cualquier profundidad. */
function descendants(id) {
    if (!state.trace) return [];
    return state.trace.agents.filter(a => {
        for (let p = agentOf(a.parentAgentId), guard = 0; p && guard < 50; p = agentOf(p.parentAgentId), guard++) if (p.id === id) return true;
        return false;
    });
}

/** Pastilla del modelo de un agente, con un color por familia: Haiku, Sonnet, Opus, Fable. */
function modelChip(model) {
    const family = String(model).split(' ')[0].toLowerCase();
    return el('span', `model-chip ${family}`, model);
}

/** Enseña las flechas de grupo en el panel plegado solo si los grupos no caben, y apaga la que no lleva a ningún sitio. */
function updateGroupSteps() {
    const box = $('active-list');
    const folded = document.querySelector('.app').classList.contains('active-folded');
    const overflow = folded && box.scrollHeight > box.clientHeight + 2;
    $('active-up').hidden = !overflow;
    $('active-down').hidden = !overflow;
    if (!overflow) return;
    $('active-up').disabled = box.scrollTop <= 1;
    $('active-down').disabled = box.scrollTop + box.clientHeight >= box.scrollHeight - 1;
}

/** Desplaza el panel plegado al grupo anterior o siguiente al que está arriba del todo. */
function stepGroup(delta) {
    const box = $('active-list');
    const groups = [...box.querySelectorAll('.repo-group')];
    const top = box.scrollTop + 1;
    const target = delta > 0 ? groups.find(g => g.offsetTop - box.offsetTop > top) : groups.reverse().find(g => g.offsetTop - box.offsetTop < top - 2);
    box.scrollTo({ top: target ? target.offsetTop - box.offsetTop : delta > 0 ? box.scrollHeight : 0, behavior: 'smooth' });
}

/** Abre junto al ratón el menú de colores de un grupo: los de serie, los añadidos en la configuración y «Sin color». */
function openColorMenu(group, x, y) {
    const menu = $('color-menu');
    menu.replaceChildren(el('div', 'color-menu-title', group));
    const swatches = el('div', 'color-swatches');
    for (const color of state.palette) {
        const b = el('button', 'swatch' + (state.groupColors[group] === color ? ' on' : ''));
        b.style.background = color;
        b.title = color;
        b.addEventListener('click', () => setGroupColor(group, color));
        swatches.append(b);
    }
    const none = el('button', 'swatch-none', 'Sin color');
    none.addEventListener('click', () => setGroupColor(group, null));
    menu.append(swatches, none);
    menu.hidden = false;
    // Dentro de la ventana aunque se pulse cerca de un borde.
    menu.style.left = `${Math.min(x, window.innerWidth - menu.offsetWidth - 8)}px`;
    menu.style.top = `${Math.min(y, window.innerHeight - menu.offsetHeight - 8)}px`;
}

/** Cierra el menú de colores, si estaba abierto. */
function closeColorMenu() {
    $('color-menu').hidden = true;
}

/** Pide a la app que guarde el color de un grupo; lo pinta ya, sin esperar a su respuesta. */
function setGroupColor(group, color) {
    closeColorMenu();
    if (color) state.groupColors[group] = color;
    else delete state.groupColors[group];
    renderActive();
    post({ type: 'groupColor', group, color });
}

/** Tarjeta de una sesión abierta: gota de estado, worktree o nombre, y el estado con cuánto lleva en él. */
function activeCard(s) {
    const card = el('button', 'act-card' + (s.id === state.selectedId ? ' on' : ''));
    card.dataset.id = s.id;
    // Sus animaciones, al compás del reloj de la página: al rehacerse la tarjeta siguen por donde iban.
    card.style.setProperty('--phase', `-${Math.round(document.timeline.currentTime || performance.now())}ms`);
    const name = s.worktree || s.name || `Sesión de las ${hour(s.started)}`;
    const since = between(s.since, new Date().toISOString());
    const need = needOf(s);
    const ago = since !== null && since >= 60000 ? duration(since).replace(/ \d+ s$/, '') : null;
    // Sin revisar, como en el inicio: la etiqueta en lugar de «Terminado», roja del todo si lleva rato.
    const sub = el('div', 'act-sub');
    if (need === 'review') sub.append(el('span', 'review-badge', 'Sin revisar'));
    const word = s.statusKey === 'agents' || s.statusKey === 'command' ? `Esperando a ${s.status}` : s.status;
    const rest = [need === 'review' ? null : s.fresh ? 'Sin empezar' : word, ago, s.worktree ? s.name : null].filter(Boolean).join(' · ');
    if (rest) sub.append(need === 'review' ? ` · ${rest}` : rest);
    const text = el('div', 'act-text');
    const title = el('div', 'act-name');
    title.append(el('span', 'act-label', name));
    if (s.hooks) {
        title.append(hookMark());
        card.classList.add('hooks-bad');
    }
    text.append(title, sub);
    card.append(el('span', 'dot ' + (s.statusKey || 'idle')), text);
    if (need) card.classList.add(`need-${need}`);
    if ((need === 'review' && s.review === 2) || (need === 'question' && s.wait === 2)) card.classList.add('overdue');
    card.dataset.tip = `s:${s.id}`;
    card.addEventListener('click', () => {
        setListsOpen(false);
        openSession(s.id, s.id === state.selectedId);
    });
    return card;
}

/** Qué pide una sesión, de menos a más urgente: «working» trabaja, «review» terminó y no se ha revisado, «question» espera respuesta; null si nada.
 *
 * Los niveles los manda la app, los mismos del inicio: «review» y «wait» valen 1 recién y 2 si ya lleva rato. */
function needOf(s) {
    if (s.statusKey === 'waiting') return 'question';
    if (s.statusKey === 'agents') return 'agents';
    if (s.statusKey === 'command') return 'command';
    if (s.statusKey === 'done' && s.review > 0) return 'review';
    return s.statusKey === 'working' ? 'working' : null;
}

/** Lo que espera la sesión que se ve con su turno acabado, en directo: sus agentes («agents»: los que trabajan y todos los
 * de la espera) o un comando en marcha («command»: los comandos que espera), y desde cuándo; null si no espera nada o se
 * está viendo una repetición. */
function agentsWait() {
    const t = state.trace;
    if (!t || state.replay.index !== null) return null;
    const s = state.active.find(a => a.id === t.id);
    if (!s || !s.background) return null;
    const since = s.background.turnEnd || s.since;
    if (s.statusKey === 'command') {
        const commands = (s.background.commands || []).filter(c => c.awaited);
        return commands.length ? { kind: 'command', running: [], agents: [], commands, since } : null;
    }
    if (s.statusKey !== 'agents') return null;
    const running = s.background.agents.filter(a => a.running);
    return running.length ? { kind: 'agents', running, agents: s.background.agents, commands: [], since } : null;
}

/** Cuántos comandos, en dos palabras: «1 comando», «2 comandos». */
function commandsWord(n) {
    return n === 1 ? '1 comando' : `${n} comandos`;
}

/** A qué espera una espera, en dos palabras: «2 agentes» o «1 comando». */
function waitWord(wait) {
    return wait.kind === 'command' ? commandsWord(wait.commands.length) : agentsWord(wait.running.length);
}

/** La marca de una espera: la órbita de los agentes o el recuadro de consola de un comando. */
function waitMark(wait, cls) {
    return el('span', (wait.kind === 'command' ? 'cmd-mark' : 'orbit') + (cls ? ` ${cls}` : ''));
}

/** Lo que se espera, en una frase para una ficha: los agentes que trabajan, o cada comando con el agente que lo lanzó. */
function waitWhat(wait) {
    return wait.kind === 'command' ? wait.commands.map(c => c.by ? `${c.description} (lo lanzó «${c.by}»)` : c.description).join(', ')
        : wait.running.map(a => a.description).join(', ');
}

/** Cuántos agentes, en dos palabras: «1 agente», «3 agentes». */
function agentsWord(n) {
    return n === 1 ? '1 agente' : `${n} agentes`;
}

/** Lo que lleva algo desde una hora, corto: «14 min», «2 h 5 min». */
function agoShort(iso) {
    return duration(Math.max(0, Date.now() - new Date(iso).getTime())).replace(/ \d+ s$/, '');
}

/** La nota del final del chat mientras la sesión espera, violeta a sus agentes y cian a un comando: qué espera, cuánto lleva
 * y que retomará sola. De un comando se dice también el agente que lo lanzó, ya acabado. */
function chatWaitNote(wait) {
    const note = el('div', 'chat-wait' + (wait.kind === 'command' ? ' command' : ''));
    const head = el('div', 'chat-wait-head');
    head.append(waitMark(wait), `Esperando a ${waitWord(wait)} · desde las ${hour(wait.since)}`);
    note.append(head);
    for (const c of wait.commands) {
        const line = el('div', 'chat-wait-agent');
        line.append(el('span', 'chat-wait-cmd', '>_'), el('span', 'chat-wait-name', c.description), el('span', 'chat-wait-time', agoShort(c.start)));
        note.append(line);
        if (!c.by) continue;
        const by = el('div', 'chat-wait-agent gone');
        by.append(el('span', 'chat-wait-dot'), el('span', 'chat-wait-name', c.by), el('span', 'chat-wait-time', 'acabó · lo lanzó'));
        note.append(by);
    }
    for (const a of wait.agents) {
        const line = el('div', 'chat-wait-agent' + (a.running ? '' : ' gone'));
        line.append(el('span', 'chat-wait-dot'), el('span', 'chat-wait-name', a.description), el('span', 'chat-wait-time', a.running ? agoShort(a.start) : 'acabó'));
        note.append(line);
    }
    note.append(el('div', 'chat-wait-foot', `La sesión retomará sola cuando ${wait.kind === 'command' && wait.commands.length === 1 ? 'acabe' : 'acaben'}; no tienes que hacer nada.`));
    return note;
}

/** Pliega el panel de activas a una tira de gotas, o lo despliega, y lo recuerda. */
function setActiveFolded(folded) {
    document.querySelector('.app').classList.toggle('active-folded', folded);
    $('active-fold').textContent = folded ? '⇥' : '⇤';
    $('active-fold').title = folded ? 'Desplegar el panel' : 'Plegar el panel';
    remember('activeFolded', folded);
    updateGroupSteps();
}

/** Lo que va encima del dibujo: lo señalado, las coincidencias, lo que aún no ha pasado y el bocadillo. */
function decorate() {
    const box = $('diagram');
    const r = state.replay;
    const steps = selectedSteps();
    const marks = rowMarks();
    const { now, matches } = marks;
    box.querySelectorAll('.seq-row').forEach(row => markRow(row, marks));
    box.querySelectorAll('.wf-node, .dg-st, .ah-item').forEach(card => {
        const orders = (card.dataset.orders || '').split(',').filter(Boolean).map(Number);
        card.classList.toggle('on', state.focus !== null && orders.includes(state.focus));
        card.classList.toggle('match', orders.some(o => matches.has(o)));
    });
    box.querySelectorAll('.comic, .comic-tail').forEach(n => n.remove());
    if (now !== null) bubble(steps[r.index]);
}

/** Desplaza el diagrama hasta la fila o la tarjeta de un paso.
 *
 * Se mide en pantalla y no con offsetTop porque el lienzo puede llevar zoom. */
function scrollDiagramTo(order) {
    const box = $('diagram');
    const v = state.seqView;
    if (v) {
        const i = v.rows.findIndex(r => r.step && r.step.order === order);
        if (i < 0) return;
        const scroller = v.scroller || box;
        scroller.scrollTop = SEQ_HEAD + i * ROW - scroller.clientHeight / 2 + ROW / 2;
        paintSequence();
        return;
    }
    const target = box.querySelector(`.seq-row[data-step="${order}"]`)
        || [...box.querySelectorAll('.wf-node, .dg-st, .ah-item')].find(c => (c.dataset.orders || '').split(',').includes(String(order)));
    if (!target) return;
    // En Ahora, el Parte tiene su propio scroll: la línea se busca dentro de él.
    if (target.classList.contains('ah-item')) {
        target.scrollIntoView({ block: 'center' });
        return;
    }
    const r = target.getBoundingClientRect();
    const view = box.getBoundingClientRect();
    box.scrollTop += r.top - view.top - box.clientHeight / 2 + r.height / 2;
    if (!target.classList.contains('seq-row')) box.scrollLeft += r.left - view.left - box.clientWidth / 2 + r.width / 2;
}

// VISTAS de una sesión, en un desplegable. Con cuál se abre lo dice la configuración; la que se elige aquí dura hasta
// cerrar la app, que la guarda en memoria

// Vista con la que se abre una sesión si la app no dice otra, o si dice una que ya no existe.
const DEFAULT_VIEW = 'live';

// Vistas en el orden del desplegable, con su nombre, lo que enseñan y su dibujo en una cuadrícula de 16.
const VIEWS = {
    live: { name: 'Flujo', hint: 'El flujo del trabajo, de arriba abajo, con una rama por agente', icon: 'M1 8h3l2-5 3 10 2-5h4' },
    diagram: { name: 'Diagrama', hint: 'La planta de la sesión: un eje por actor, una parada por paso y lo que duró cada uno', icon: 'M2 4h12M2 12h12M5 4v8M11 12V8h3M5 8h6' },
    now: { name: 'Ahora', hint: 'La sección de la sesión: lo que pasa en este instante, quién trabaja y las últimas fases', icon: 'M8 1.5a6.5 6.5 0 1 1 0 13a6.5 6.5 0 1 1 0-13zM8 4.5V8l2.5 1.5' },
    parte: { name: 'Parte', hint: 'El trabajo contado por fases, prompt a prompt; o cada paso en su fila', icon: 'M3 2.5h10v11H3zM5.5 5.5h5M5.5 8h5M5.5 10.5h3' },
    obra: { name: 'Obra', hint: 'La sesión como una obra: cada tarea del tasks.md es un solar y cada agente, un operario', icon: 'M1.5 14.5h13M3 14.5V8.5h4v6M7 14.5V3.5h5.5v11M9 6h1.5M9 8.5h1.5M9 11h1.5M4.5 11h1' },
};

/** Devuelve la vista que se ve. */
function currentView() {
    return state.mapLayout;
}

/** Pasa a una vista y la pinta; una que no existe, como las que se retiraron, abre la de por defecto.
 *
 * Si la elige la persona en el desplegable, se lo dice a la app, que la vuelve a mandar si la página se recarga. */
function setView(view, fromApp) {
    closeViewMenu();
    const next = VIEWS[view] ? view : DEFAULT_VIEW;
    state.mapLayout = next;
    state.tab = 'map';
    showView();
    // El Flujo necesita el ancho: el chat se minimiza al entrar y vuelve como estaba al salir.
    const chatOpen = !$('panes').classList.contains('chat-closed');
    if (next === 'live' && state.chatBeforeLive === null) {
        state.chatBeforeLive = chatOpen;
        $('panes').classList.add('chat-closed');
    } else if (next !== 'live' && state.chatBeforeLive !== null) {
        $('panes').classList.toggle('chat-closed', !state.chatBeforeLive);
        state.chatBeforeLive = null;
    }
    chatReading();
    if (!fromApp) post({ type: 'view', view: next });
    syncChanges();
    renderDiagram(true);
    // El Flujo y el diagrama se colocan solos en lo último; las demás vistas empiezan por la izquierda y van
    // al paso señalado.
    if (next === 'live' || next === 'diagram') return;
    $('diagram').scrollLeft = 0;
    if (state.focus !== null) scrollDiagramTo(state.focus);
}

/** Pone en el botón de la vista el dibujo de la que se ve, y en su tooltip qué enseña. */
function showView() {
    const key = currentView();
    const view = VIEWS[key] || VIEWS[DEFAULT_VIEW];
    $('view-btn').querySelector('.view-icon path').setAttribute('d', view.icon);
    hint($('view-btn'), '👁', 'Vista', view.name,
        `${view.hint}. Pulsa para cambiar de vista; la que sale al abrir la app se elige en Configuración → Sesiones.`);
}

/** Abre bajo el botón de la vista el desplegable con todas, y la que se ve marcada; si ya estaba abierto, lo cierra. */
function toggleViewMenu() {
    const menu = $('view-menu');
    if (!menu.hidden) {
        closeViewMenu();
        return;
    }
    menu.replaceChildren(...Object.entries(VIEWS).map(([key, view]) => {
        const item = el('button', 'map-menu-item' + (key === currentView() ? ' on' : ''));
        const text = el('span', 'map-menu-text');
        text.append(el('span', 'map-menu-name', view.name), el('span', 'map-menu-hint', view.hint));
        item.append(viewIcon(key), text);
        item.addEventListener('click', () => setView(key));
        return item;
    }));
    menu.hidden = false;
    const r = $('view-btn').getBoundingClientRect();
    menu.style.left = `${Math.max(8, Math.min(r.left, window.innerWidth - menu.offsetWidth - 8))}px`;
    menu.style.top = `${r.bottom + 6}px`;
}

/** Dibujo de una vista para el desplegable, del color del texto. */
function viewIcon(key) {
    const icon = svg('svg', { class: 'map-menu-icon', viewBox: '0 0 16 16', 'aria-hidden': 'true' });
    icon.append(svg('path', { d: VIEWS[key].icon }));
    return icon;
}

/** Abre bajo su pastilla la lista de lo que la sesión ha publicado en claude.ai; si ya estaba abierta, la cierra. */
function toggleArtifactMenu() {
    const menu = $('artifact-menu');
    if (!menu.hidden) {
        closeArtifactMenu();
        return;
    }
    const arts = state.trace ? state.trace.artifacts || [] : [];
    menu.replaceChildren(...arts.map(a => {
        const item = el('button', 'map-menu-item');
        const text = el('span', 'map-menu-text');
        const versions = a.versions > 1 ? ` · ${a.versions} versiones` : '';
        text.append(el('span', 'map-menu-name', a.title || 'Sin título'), el('span', 'map-menu-hint', `${a.kind} · ${agoText(a.when)}${versions}`));
        item.append(flowIcon('artifact', 'map-menu-icon'), text);
        item.title = 'Abrir en Artifacts';
        item.addEventListener('click', () => {
            closeArtifactMenu();
            post({ type: 'artifact', url: a.url });
        });
        return item;
    }));
    menu.hidden = false;
    const r = $('session-artifacts').getBoundingClientRect();
    menu.style.left = `${Math.max(8, Math.min(r.left, window.innerWidth - menu.offsetWidth - 8))}px`;
    menu.style.top = `${r.bottom + 6}px`;
}

/** Cierra la lista de lo publicado, si estaba abierta. */
function closeArtifactMenu() {
    $('artifact-menu').hidden = true;
}

/** Cierra el desplegable de las vistas, si estaba abierto. */
function closeViewMenu() {
    $('view-menu').hidden = true;
}

// SECUENCIA: grafo de actores, texto del paso a todo el ancho y cascada de duraciones

const LANE = 22;
const ROW = 26;
const SEQ_HEAD = 34;
const TIME_W = 64;
// Caracteres del paso que caben de sobra en una fila de la secuencia, aun a todo el ancho.
const SEQ_TEXT = 400;

/** Filas de la secuencia: un paso por fila, la vuelta de cada agente tras su último paso, y sin lo de los agentes plegados. */
function sequenceRows(steps) {
    const hidden = s => state.folded.size > 0 && ancestors(s).some(call => state.folded.has(call.childAgentId));
    const last = new Map();
    steps.forEach((s, i) => { if (s.agentId) last.set(s.agentId, i); });
    const rows = [];
    steps.forEach((s, i) => {
        if (!hidden(s)) rows.push({ step: s });
        if (s.agentId && last.get(s.agentId) === i) {
            const call = callOf(s.agentId);
            if (!call || !hidden(call)) rows.push({ back: s.agentId, at: agentOf(s.agentId)?.endUtc || s.end || s.start, order: s.order });
        }
    });
    // Un agente plegado no tiene pasos visibles: su vuelta va justo tras su lanzamiento.
    for (const id of state.folded) {
        if (rows.some(r => r.back === id)) continue;
        const index = rows.findIndex(r => r.step && r.step.childAgentId === id);
        if (index >= 0) rows.splice(index + 1, 0, { back: id, at: agentOf(id)?.endUtc, order: rows[index].step.order });
    }
    // Donde acabó el turno, si la sesión espera a sus agentes: tras el último paso suyo de antes de esa hora.
    const wait = agentsWait();
    if (wait) {
        const end = new Date(wait.since).getTime();
        let at = -1;
        rows.forEach((r, i) => { if (r.step && !r.step.agentId && new Date(r.step.start).getTime() <= end) at = i; });
        if (at >= 0) rows.splice(at + 1, 0, { wait, at: wait.since, order: rows[at].step.order + 0.5 });
    }
    return rows;
}

/** Diagrama de secuencia en filas: hora, carriles de actores, texto entero y barra con lo que duró. */
function sequence(steps) {
    const rows = sequenceRows(steps);

    // Vida de cada agente en filas: de su lanzamiento a su vuelta.
    const life = new Map();
    rows.forEach((r, i) => {
        const id = r.step ? (r.step.childAgentId || r.step.agentId) : r.back;
        if (!id) return;
        const span = life.get(id) || { from: i, to: i };
        span.from = Math.min(span.from, i);
        span.to = Math.max(span.to, i);
        life.set(id, span);
    });

    const lane = agentLanes(life);
    const lanesUsed = Math.max(0, ...lane.values()) + 1;
    // Un actor sin carril (el agente padre de un paso suelto) se pinta en el de Claude.
    const laneX = key => (lane.get(key) ?? 1) * LANE + LANE / 2 + 4;
    const graphW = lanesUsed * LANE + 8;

    // Las barras de duración van a escala con el paso más largo de lo que se ve.
    const longest = Math.max(1, ...rows.map(rowDuration).filter(Boolean));

    const seq = el('div', 'seq stage');
    seq.style.setProperty('--graph-w', `${graphW}px`);
    const head = el('div', 'seq-head');
    const lanes = el('div', 'lanes');
    for (const key of ['user', 'main']) {
        const dot = paint(el('span', 'lane-dot', actorIcon(key)), key);
        dot.style.left = `${laneX(key)}px`;
        dot.dataset.tip = `a:${key}`;
        dot.dataset.tipActor = key;
        lanes.append(dot);
    }
    if (lanesUsed > 2) {
        const label = el('span', 'lane-agents', `🤖 ${life.size}`);
        label.style.left = `${2 * LANE + 4}px`;
        label.title = `${life.size} agente${life.size === 1 ? '' : 's'} en ${lanesUsed - 2} carril${lanesUsed - 2 === 1 ? '' : 'es'}: un agente ocupa uno mientras trabaja y lo deja al volver`;
        lanes.append(label);
    }
    head.append(el('span', null, 'HORA'), lanes, seqStepHead(steps), el('span', null, 'DURACIÓN'));
    seq.append(head);

    const rowY = new Map();
    const named = [];
    let previous = null;
    rows.forEach((r, i) => {
        if (r.step) rowY.set(r.step.order, SEQ_HEAD + i * ROW + ROW / 2);
        const who = r.step ? speaker(r.step) : r.back;
        named.push(who !== previous);
        previous = who;
    });
    seq.style.height = `${SEQ_HEAD + rows.length * ROW}px`;
    const keys = ['user', 'main', ...life.keys()];
    state.seqView = { seq, rows, named, keys, laneX, graphW, life, longest, shown: new Map() };
    state.geo = { kind: 'sequence', x: key => TIME_W + laneX(key), rowY };
    return seq;
}

// Filas de más que se pintan por encima y por debajo de la vista, para que al hacer scroll no se vea el hueco.
const SEQ_OVERSCAN = 30;

/** Pinta las filas de la secuencia que caen en la vista, y su margen, y quita las que han salido.
 *
 * Pintar todas costaba casi un segundo con una sesión de miles de pasos, y otra vez con cada novedad en
 * directo; todas miden lo mismo, así que se sabe cuáles se ven sin pintarlas. */
function paintSequence() {
    const v = state.seqView;
    if (!v) return;
    // En el Parte, la Secuencia va en la página del lienzo, que es la que se desplaza.
    const box = v.scroller || $('diagram');
    const first = Math.max(0, Math.floor((box.scrollTop - SEQ_HEAD) / ROW) - SEQ_OVERSCAN);
    const last = Math.min(v.rows.length - 1, Math.ceil((box.scrollTop + box.clientHeight) / ROW) + SEQ_OVERSCAN);
    for (const [i, row] of v.shown) {
        if (i >= first && i <= last) continue;
        row.remove();
        v.shown.delete(i);
    }
    const marks = rowMarks();
    for (let i = first; i <= last; i++) {
        if (v.shown.has(i)) continue;
        const row = seqRow(v.rows[i], i, v.keys, v.laneX, v.graphW, v.life, v.longest, v.named[i]);
        row.style.top = `${SEQ_HEAD + i * ROW}px`;
        row.classList.toggle('odd', i % 2 === 1);
        // En el Parte, cada fila abre la ficha de su parada, como el resto de vistas sobre el lienzo.
        const nkey = v.nkeys && v.rows[i].step ? v.nkeys.get(v.rows[i].step.order) : null;
        if (nkey) {
            row.dataset.nkey = nkey;
            row.classList.add('ah-pick');
            row.classList.toggle('picked', Boolean(state.wf && !state.wf.drawer.hidden && state.wf.detail === nkey));
        }
        markRow(row, marks);
        v.seq.append(row);
        v.shown.set(i, row);
    }
}

/** Vuelve a pintar la ventana de la secuencia en el siguiente fotograma, una sola vez aunque se pida muchas. */
function schedulePaintSequence() {
    if (!state.seqView || state.seqView.pending) return;
    state.seqView.pending = true;
    requestAnimationFrame(() => {
        if (!state.seqView) return;
        state.seqView.pending = false;
        paintSequence();
        refreshTip();
    });
}

/** Lo que marca las filas: el paso señalado, el de la repetición y las coincidencias de la búsqueda. */
function rowMarks() {
    const r = state.replay;
    const steps = selectedSteps();
    return {
        now: r.index !== null && steps[r.index] ? steps[r.index].order : null,
        matches: new Set(state.find.matches),
    };
}

/** Marca una fila de la secuencia: señalada, aún por pasar en la repetición o coincidencia de la búsqueda. */
function markRow(row, { now, matches }) {
    const order = Number(row.dataset.order);
    row.classList.toggle('on', state.focus !== null && row.dataset.step === String(state.focus));
    row.classList.toggle('future', now !== null && order > now);
    row.classList.toggle('match', row.dataset.step !== undefined && matches.has(Number(row.dataset.step)));
}

/** Carril de cada actor: la persona y Claude fijos, y cada agente el primero libre mientras trabaja.
 *
 * Un carril por agente gastaba el ancho en decenas de columnas casi vacías; reutilizándolos, hay
 * tantos como agentes a la vez. */
function agentLanes(life) {
    const lane = new Map([['user', 0], ['main', 1]]);
    const busyUntil = [];
    const spans = [...life].sort((x, y) => x[1].from - y[1].from || x[1].to - y[1].to);
    for (const [id, span] of spans) {
        let free = busyUntil.findIndex(to => to < span.from);
        if (free < 0) free = busyUntil.push(-1) - 1;
        busyUntil[free] = span.to;
        lane.set(id, free + 2);
    }
    return lane;
}

/** Cabecera de la columna del paso, con «Plegar subagentes» si hay agentes lanzados por otros agentes. */
function seqStepHead(steps) {
    const cell = el('span', 'seq-step-head', 'PASO');
    const nested = [...new Set(steps.map(s => s.childAgentId).filter(Boolean))]
        .filter(id => agentOf(id)?.parentAgentId);
    if (nested.length === 0) return cell;
    const folded = nested.every(id => state.folded.has(id));
    const button = el('button', 'seq-fold-all', folded ? 'Desplegar subagentes' : 'Plegar subagentes');
    button.title = folded ? 'Enseñar el trabajo de los agentes lanzados por otros agentes'
        : 'Recoger el trabajo de los agentes lanzados por otros agentes: queda su lanzamiento y su vuelta';
    button.addEventListener('click', e => {
        e.stopPropagation();
        for (const id of nested) {
            if (folded) state.folded.delete(id);
            else state.folded.add(id);
        }
        renderDiagram(false);
    });
    cell.append(button);
    return cell;
}

/** Lo que duró el paso de una fila (un agente, hasta que vuelve); null si no se sabe o la fila es una vuelta. */
function rowDuration(r) {
    const s = r.step;
    if (!s) return null;
    const child = s.kind === 'agent' ? agentOf(s.childAgentId) : null;
    return between(s.start, child ? child.endUtc : s.end);
}

/** Una fila de la secuencia; con «named», lleva delante el nombre del agente que actúa, porque su carril no lo dice. */
function seqRow(r, i, keys, laneX, graphW, life, longest, named) {
    if (r.wait) return seqWaitRow(r, graphW);
    const s = r.step;
    const row = el('div', 'seq-row');
    row.dataset.order = s ? s.order : r.order;
    if (s) row.dataset.step = s.order;
    const who = s ? speaker(s) : r.back;
    const ink = s ? inkOf(s) : who;
    paint(row, ink);
    if (s && (s.kind === 'prompt' || s.kind === 'command' || s.kind === 'reply')) row.classList.add('say');
    if (s && s.shell) row.classList.add('shell');
    if (s && (s.kind === 'tool' || s.kind === 'skill') && !s.questions) row.classList.add('tool');
    if (s && s.isError) row.classList.add('error');
    if (s && s.secrets) row.classList.add('secret');
    if (s && s.hookError) row.classList.add('hook-bad');
    if (r.back) row.classList.add('back');
    row.append(el('span', 'time', clock(s ? s.start : r.at)));

    // Grafo: líneas de vida de quien sigue activo en esta fila, y el punto o la flecha del paso.
    const g = svg('svg', { width: graphW, height: ROW });
    for (const key of keys) {
        const span2 = life.get(key);
        if (key !== 'user' && key !== 'main' && (!span2 || i < span2.from || i > span2.to)) continue;
        const top = span2 && i === span2.from ? ROW / 2 : 0;
        const bottom = span2 && i === span2.to ? ROW / 2 : ROW;
        g.append(paint(svg('line', { class: 'lifeline', x1: laneX(key), x2: laneX(key), y1: top, y2: bottom }), key));
    }
    const y = ROW / 2;
    if (r.back) {
        const parent = agentOf(r.back)?.parentAgentId || 'main';
        g.append(...arrow(laneX(r.back), laneX(parent), y, r.back, true));
    } else if (s.kind === 'prompt' || s.kind === 'command') {
        g.append(...arrow(laneX('user'), laneX('main'), y, ink, false));
    } else if (s.kind === 'reply' && !s.agentId) {
        g.append(...arrow(laneX('main'), laneX('user'), y, 'main', false));
    } else if (s.kind === 'agent' && s.childAgentId) {
        g.append(...arrow(laneX(who), laneX(s.childAgentId), y, who, false));
    } else {
        const tool = s.kind === 'tool' || s.kind === 'skill';
        g.append(paint(svg('circle', { class: tool ? 'node-tool' : 'node-dot', cx: laneX(who), cy: y, r: tool ? 4 : 4.5 }), who));
    }
    row.append(g);

    // El tooltip sale solo sobre el paso, no sobre el grafo ni la duración.
    const text = el('span', 'seq-text');
    text.dataset.tip = s ? `q:${s.order}` : `r:${r.back}`;
    text.dataset.tipActor = ink;
    if (s && s.kind === 'agent' && s.childAgentId) {
        const fold = el('span', 'fold', state.folded.has(s.childAgentId) ? '▸' : '▾');
        fold.title = 'Plegar o desplegar el trabajo del agente';
        fold.addEventListener('click', e => {
            e.stopPropagation();
            if (!state.folded.delete(s.childAgentId)) state.folded.add(s.childAgentId);
            renderDiagram(false);
        });
        text.append(fold);
    }
    const [label, detail] = seqLabel(r);
    const [icon, name] = splitLabel(label);
    // En la vuelta y en lo que dice el agente, su nombre ya va en la etiqueta.
    const says = s && (s.kind === 'reply' || s.kind === 'thinking' || s.questions);
    if (named && agentOf(who) && !r.back && !says) text.append(paint(el('span', 'seq-who', actorName(who)), who));
    const strong = el('strong');
    if (s && s.secrets) {
        const mark = el('span', 'seq-secret');
        mark.append(shield());
        strong.append(mark);
    }
    if (s && s.hookError) {
        const mark = el('span', 'seq-hook');
        mark.append(hookMark());
        strong.append(mark);
    }
    strong.append(el('span', 'seq-icon', icon), ` ${name}`);
    // La fila es una línea cortada con «…»: el texto entero de una respuesta solo costaría maquetarlo.
    text.append(strong);
    if (detail) {
        const said = s && (s.kind === 'prompt' || s.kind === 'reply' || s.kind === 'thinking' || s.kind === 'notification' || s.questions);
        const line = said ? oneLine(detail) : detail;
        const shown = line.length > SEQ_TEXT ? `${line.slice(0, SEQ_TEXT)}…` : line;
        const body = el('span', said ? 'seq-body' : s && (s.shell || (s.kind === 'tool' && !s.questions)) ? 'seq-cmd' : 'seq-note');
        if (said) body.append(flatInline(shown));
        else body.textContent = shown;
        text.append(' ', body);
    }
    row.append(text);

    // Duración: una barra de lo que duró el paso, a escala raíz con el más largo para que lo breve se vea, y el tiempo al final.
    const dur = el('div', 'dur');
    const ms = rowDuration(r);
    if (ms) {
        const bar = el('span', 'bar' + (s.isError ? ' error' : ''));
        bar.style.setProperty('--w', Math.sqrt(ms / longest).toFixed(3));
        dur.append(bar, el('span', 'bar-label', duration(ms)));
    }
    row.append(dur);

    row.addEventListener('click', () => focusStep(s ? s.order : r.order, 'diagram'));
    return row;
}

/** La banda de la secuencia donde acabó el turno de una sesión que espera a sus agentes o a un comando: a cuántos y cuánto
 * lleva esperando. */
function seqWaitRow(r, graphW) {
    const row = el('div', 'seq-row agents-wait' + (r.wait.kind === 'command' ? ' command' : ''));
    row.dataset.order = r.order;
    row.append(el('span', 'time', clock(r.at)));
    const graph = el('span', 'seq-wait-graph');
    graph.style.width = `${graphW}px`;
    graph.append(waitMark(r.wait));
    row.append(graph);
    const text = el('span', 'seq-text');
    text.append(el('strong', null, `Fin del turno · la sesión espera a ${waitWord(r.wait)}`));
    row.append(text);
    const dur = el('div', 'dur');
    dur.append(el('span', 'bar-label', `${duration(Math.max(0, Date.now() - new Date(r.wait.since).getTime())).replace(/ \d+ s$/, '')}, sigue`));
    row.append(dur);
    hint(row, r.wait.kind === 'command' ? '>_' : '◌', r.wait.kind === 'command' ? 'Esperando a un comando' : 'Esperando a sus agentes', null,
        `${waitWhat(r.wait)}.${r.wait.kind === 'command' ? '' : ' Debajo siguen sus pasos.'}`);
    return row;
}

/** Flecha corta entre dos columnas del grafo, del color de quien la manda. */
function arrow(x1, x2, y, from, dashed) {
    const dir = x2 >= x1 ? 1 : -1;
    const line = paint(svg('line', { class: 'msg-line' + (dashed ? ' dashed' : ''), x1, x2: x2 - dir * 5, y1: y, y2: y }), from);
    const head = paint(svg('polygon', { class: 'msg-head', points: `${x2},${y} ${x2 - dir * 6},${y - 4} ${x2 - dir * 6},${y + 4}` }), from);
    const dot = paint(svg('circle', { class: 'node-dot', cx: x1, cy: y, r: 3.5 }), from);
    return [line, head, dot];
}

/** Nombre y detalle de una fila: «🔧 PowerShell» y «git commit…», «🧑‍💻 ana» y el prompt entero. */
function seqLabel(r) {
    if (r.back) {
        const a = agentOf(r.back);
        return [`↩ ${actorName(r.back)}`, `vuelve${a ? ' · ' + duration(between(a.startUtc, a.endUtc)) : ''}`];
    }
    const s = r.step;
    if (s.questions) return [`❓ ${actorName(speaker(s))} pregunta`, s.questions.map(q => q.question).join(' · ')];
    switch (s.kind) {
        case 'prompt': return [`${actorIcon('user')} ${state.user}`, plain(s.text)];
        case 'command': return s.shell ? ['! Comando de consola', shellCommand(s)] : [`${actorIcon('user')} ${state.user}`, s.text];
        case 'reply': return [`${actorIcon(speaker(s))} ${actorName(speaker(s))}`, s.text];
        case 'thinking': return [`🤔 ${actorName(speaker(s))} piensa`, s.text];
        case 'notification': return ['🔔 Aviso', s.text || s.title];
        case 'skill': return ['🧙 Skill', s.title];
        case 'agent': {
            const a = agentOf(s.childAgentId);
            if (a) return [`🤖 lanza ${actorName(a.id)}`, agentKind(a)];
            return [`🤖 lanza ${s.title.split(' · ')[0]}`, s.title.split(' · ').slice(1).join(' · ')];
        }
        default: return [`🔧 ${s.title.split(' · ')[0]}`, s.title.split(' · ').slice(1).join(' · ')];
    }
}

// AGENTES: el papel y la ronda de cada encargo, que usan el Flujo y su ficha

// Palabras que dicen la ronda, el lote o la fase de un encargo, por serie: «round 18» y «fifth batch» cuentan cosas
// distintas.
const ROUND_SERIES = [
    { tag: 'R', words: 'round|ronda|wave|oleada|iteration|iteraci[oó]n|vuelta|pass' },
    { tag: 'L', words: 'batch|lote|tanda' },
    { tag: 'F', words: 'fase|phase' },
];
const ROUND_WORDS = ROUND_SERIES.map(x => x.words).join('|');
const ROUND_AFTER = new RegExp(`\\b(${ROUND_WORDS})[\\s#:-]*(\\d+(?:\\.\\d+)*|[A-Z](?=[\\s.:)-]|$))`, 'i');
// Los ordinales que cuentan una ronda con letras.
const ORDINALS = ['first|primer[ao]?', 'second|segund[ao]', 'third|tercer[ao]?', 'fourth|cuart[ao]', 'fifth|quint[ao]',
    'sixth|sext[ao]', 'seventh|s[eé]ptim[ao]', 'eighth|octav[ao]', 'ninth|noven[ao]', 'tenth|d[eé]cim[ao]'];
const ROUND_ORDINAL = new RegExp(`\\b(${ORDINALS.join('|')})\\s+(${ROUND_WORDS})\\b`, 'i');
// Lo que precede al papel en un encargo y no lo dice: «Ronda», «Fase» o un código como «S4:».
const ROLE_PREFIX = new RegExp(`^(?:(?:${ROUND_WORDS})\\b[\\s#:-]*(?:\\d+(?:\\.\\d+)*\\b)?\\s*|[A-Z]{1,3}\\d+(?:\\.\\d+)*:\\s*)+`, 'i');

/** Ronda que dice el encargo de un agente, con su serie: «R18» en «Commit round 18 fixes», «L5» en «fifth batch» y,
 * con letra, la letra sola: «F» en «Apply visual round F.1-F.17». Null si no dice ninguna. */
function roundLabel(text) {
    if (!text) return null;
    const seriesOf = word => ROUND_SERIES.find(x => new RegExp(`^(?:${x.words})$`, 'i').test(word)).tag;
    const after = ROUND_AFTER.exec(text);
    if (after) return /^\d/.test(after[2]) ? seriesOf(after[1]) + after[2] : after[2].toUpperCase();
    const ordinal = ROUND_ORDINAL.exec(text);
    if (!ordinal) return null;
    const word = ordinal[1].toLowerCase();
    return seriesOf(ordinal[2]) + (ORDINALS.findIndex(o => new RegExp(`^(?:${o})$`).test(word)) + 1);
}

/** Papel de un agente entre los que lanzó su padre: la skill que ejecuta, o la primera palabra de su encargo («Commit», «Apply») sin la ronda. */
function roleOf(a) {
    const d = (a.description || '').trim();
    const skill = /^\/([\w:.-]+)/.exec(d);
    if (skill) return { key: `skill:${skill[1]}`, name: skill[1], skill: true };
    const word = d.replace(ROLE_PREFIX, '').match(/[\p{L}][\p{L}\p{N}_-]*/u);
    const name = word ? word[0] : a.type || 'agente';
    return { key: `do:${name.toLowerCase()}`, name: name[0].toUpperCase() + name.slice(1), skill: false };
}

/** Papeles de los agentes de primer nivel de la sesión, por orden de aparición; dan el color de cada rama. */
function branchRoles() {
    const t = state.trace;
    if (!t) return [];
    if (state.roleCache && state.roleCache.agents === t.agents) return state.roleCache.keys;
    const keys = [...new Set(branches().map(id => roleOf(agentOf(id)).key))];
    state.roleCache = { agents: t.agents, keys };
    return keys;
}

/** Si un agente sigue trabajando: en la repetición, según el paso actual; en directo, si su llamada aún no ha vuelto o, en
 * segundo plano, según lo que la app sabe de él (su llamada vuelve al momento). */
function running(a, call, now) {
    if (now) return Boolean(a.startUtc) && new Date(a.endUtc || a.startUtc) > new Date(now.start) && (!call || call.order <= now.order);
    if (!state.trace.working) return false;
    if (a.background) return Boolean(a.busy);
    if (call && !call.end) return true;
    return Boolean(a.endUtc) && Date.now() - new Date(a.endUtc).getTime() < 20000;
}

/** Cuenta el uso de una herramienta o una skill en lo que ha hecho un actor; los demás pasos no cuentan. */
function addUse(st, s) {
    if (s.kind !== 'tool' && s.kind !== 'skill') return;
    const name = s.kind === 'tool' ? s.title.split(' · ')[0] : s.title;
    const bag = s.kind === 'tool' ? st.tools : st.skills;
    const b = bag.get(name) || { count: 0, errors: 0, orders: [], last: null };
    b.count++;
    if (s.isError) b.errors++;
    b.orders.push(s.order);
    b.last = s;
    bag.set(name, b);
}

// Nombre de cada serie de rondas, para el título de su etiqueta.
const SERIES_NAME = { R: 'Ronda', L: 'Lote', F: 'Fase' };

/** Crea la etiqueta de la ronda de un encargo («R18», «L5», «F»), con su nombre en el tooltip; null si no tiene. */
function roundTag(round) {
    if (!round || !round.label) return null;
    const tag = el('span', 'round-tag', round.label);
    const m = /^([A-Z])(\d.*)$/.exec(round.label);
    return hint(tag, '🔁', m ? `${SERIES_NAME[m[1]]} ${m[2]}` : `Ronda ${round.label}`, null, null);
}

// EL FLUJO: el trabajo como un workflow sobre un lienzo. Una línea central con el prompt y lo que hace Claude, y una
// rama por agente, por arriba y por abajo, que vuelve a la línea al terminar. Cada nodo abre su detalle

// Prompts que enseña «Toda la sesión» de una vez; los anteriores se recogen en una etiqueta.
const FLOW_SHOWN = 15;

// Dibujo de cada clase de herramienta y de cada caja del flujo, en una cuadrícula de 16.
const FLOW_ICONS = {
    artifact: 'M4 1.5h5.5l3 3v10h-8.5zM9.5 1.5v3h3M6 8h4.5M6 10.5h4.5',
    read: 'M2 3.5c2-1 4-1 6 .5c2-1.5 4-1.5 6-.5v9c-2-1-4-1-6 .5c-2-1.5-4-1.5-6-.5zM8 4v9',
    search: 'M7 2.5a4.5 4.5 0 1 1 0 9a4.5 4.5 0 1 1 0-9zM10.3 10.3l3.5 3.5',
    edit: 'M3 13l1-3.5L11 2.5l2.5 2.5L6.5 12zM9.5 4l2.5 2.5',
    write: 'M4 1.5h5.5l3 3v10h-8.5zM9.5 1.5v3h3M6 8h4.5M6 10.5h4.5',
    terminal: 'M1.5 2.5h13v11h-13zM4 6l2.5 2L4 10M8 10.5h4',
    web: 'M8 1.5a6.5 6.5 0 1 1 0 13a6.5 6.5 0 1 1 0-13zM1.5 8h13M8 1.5c2.2 2 2.2 11 0 13M8 1.5c-2.2 2-2.2 11 0 13',
    plug: 'M5.5 1.5v3M10.5 1.5v3M3.5 4.5h9v3a4.5 4.5 0 0 1-9 0zM8 12v2.5',
    tasks: 'M2 3.5l1 1l2-2M7 3.5h7M2 8l1 1l2-2M7 8h7M2 12.5l1 1l2-2M7 12.5h7',
    question: 'M5.5 6a2.5 2.5 0 1 1 3.5 2.3c-.7.3-1 .8-1 1.5v.7M8 12.5v.5',
    skill: 'M3 13l7-7M9 2v2M12 5h2M11.5 2.5l1-1M6 3l.8 1.6l1.6.8l-1.6.8L6 7.8l-.8-1.6l-1.6-.8l1.6-.8z',
    agent: 'M3.5 5.5h9v7h-9zM8 2.5v3M5.8 8.5h.4M9.8 8.5h.4M6 10.5h4M2 8v2M14 8v2',
    tool: 'M10 2a3.5 3.5 0 0 0-3.3 4.6L2 11.3L4.7 14l4.7-4.7A3.5 3.5 0 0 0 14 6l-2 2l-2-.5l-.5-2l2-2z',
    prompt: 'M8 2a3 3 0 1 1 0 6a3 3 0 1 1 0-6zM2.5 14c.5-3 2.7-4.5 5.5-4.5s5 1.5 5.5 4.5',
    reply: 'M2 3h12v8H7l-3 2.5V11H2z',
    think: 'M8 2a4.5 4.5 0 0 1 2.5 8.2V12h-5v-1.8A4.5 4.5 0 0 1 8 2zM6 14h4',
    done: 'M3 8.5l3 3l7-7',
    image: 'M2 3.5h12v9H2zM2 10.5l3.5-3.5l3 3l2-2l3.5 3.5M10.5 6.2a.8.8 0 1 0 0 .01',
    browser: 'M1.5 2.5h13v11h-13zM1.5 5.5h13M3.5 4h.01M5.5 4h.01M7.5 4h.01M5 10l2-2l2 2l2-2',
    send: 'M14.5 1.5L7 9M14.5 1.5l-5 13l-2.5-5.5l-5.5-2.5z',
    handback: 'M6 3.5L2.5 7L6 10.5M2.5 7h7.5a3.5 3.5 0 0 1 0 7H8',
    stop: 'M8 1.5a6.5 6.5 0 1 1 0 13a6.5 6.5 0 1 1 0-13zM5.5 5.5h5v5h-5z',
    toolbox: 'M1.5 6h13v7.5h-13zM5.5 6V3.5h5V6M1.5 9.5h13M7 8.5v2h2v-2',
    plan: 'M3 1.5h10v13H3zM5.5 5h5M5.5 8h5M5.5 11h3',
    feedback: 'M2 2.5h12v8H8l-3.5 3v-3H2zM8 4.5v2.5M8 9h.01',
    artifact: 'M2 2.5h12v11H2zM2 5.5h12M5 8.5l-1.5 1.5L5 11.5M11 8.5l1.5 1.5L11 11.5',
    graph: 'M4 4a1.5 1.5 0 1 0 0 .01M12 4a1.5 1.5 0 1 0 0 .01M8 12a1.5 1.5 0 1 0 0 .01M5.2 5l2 5.5M10.8 5l-2 5.5M5.5 4h5',
    branch: 'M5 1.5a1.5 1.5 0 1 1 0 3a1.5 1.5 0 1 1 0-3zM5 11.5a1.5 1.5 0 1 1 0 3a1.5 1.5 0 1 1 0-3zM11 3.5a1.5 1.5 0 1 1 0 3a1.5 1.5 0 1 1 0-3zM5 4.5v7M11 6.5c0 3-6 2.5-6 5',
    diff: 'M3 1.5h7l3 3v10H3zM10 1.5v3h3M5.5 6.5h4M7.5 4.5v4M5.5 11.5h4',
    folder: 'M1.5 3.5h4.5l1.5 1.5h7v8h-13z',
    file: 'M4 1.5h5.5l3 3v10h-8.5zM9.5 1.5v3h3',
    review: 'M5.5 1.5h5v2h-5zM5.5 2.5h-2.5v12h10v-12h-2.5M5.5 9l1.8 1.8l3.2-3.6',
};

/** Clase de una herramienta por su nombre, para su dibujo: leer, buscar, editar, escribir, terminal, web, MCP… */
function toolKind(name) {
    if (/^mcp__/.test(name)) return 'plug';
    if (/^(Read|NotebookRead)$/.test(name)) return 'read';
    if (name === 'ToolSearch') return 'toolbox';
    if (/^(Grep|Glob|LS)$/.test(name)) return 'search';
    if (/^(Edit|MultiEdit|NotebookEdit)$/.test(name)) return 'edit';
    if (name === 'Write') return 'write';
    if (/^(Bash|PowerShell|BashOutput|KillShell|Monitor)$/.test(name)) return 'terminal';
    if (/^Web/.test(name)) return 'web';
    if (/^(TodoWrite|Task\w+)$/.test(name)) return 'tasks';
    if (name === 'AskUserQuestion') return 'question';
    if (name === 'Skill') return 'skill';
    if (name === 'Agent' || name === 'Task') return 'agent';
    return 'tool';
}

/** Servidor de una herramienta MCP («playwright» en «mcp__playwright__browser_click»); null si no lo es. */
function mcpServer(name) {
    const m = /^mcp__([^_]+(?:_[^_]+)*?)__(.+)$/.exec(String(name || ''));
    return m ? { server: m[1], tool: m[2] } : null;
}

/** Dibujo de una herramienta: el de su clase y, en una MCP, el de lo que hace su servidor (un navegador, un grafo de código). */
function toolIcon(name) {
    const mcp = mcpServer(name);
    if (!mcp) return toolKind(name);
    if (/playwright|browser|puppeteer|chrome/i.test(mcp.server)) return 'browser';
    if (/graph|code/i.test(mcp.server)) return 'graph';
    return 'plug';
}

/** Dibujo de una clase de herramienta o de caja, del color del texto o del que le dé su hoja de estilos. */
function flowIcon(kind, cls) {
    const icon = svg('svg', { class: cls || 'flow-icon', viewBox: '0 0 16 16', 'aria-hidden': 'true' });
    icon.append(svg('path', { d: FLOW_ICONS[kind] || FLOW_ICONS.tool }));
    return icon;
}

/** Momento que enseña el flujo: el paso de la repetición, o el último; y si la sesión está trabajando ahora. */
function flowMoment(steps) {
    const r = state.replay;
    const replaying = r.index !== null && Boolean(steps[r.index]);
    return { now: replaying ? steps[r.index] : steps[steps.length - 1], replaying, working: !replaying && Boolean(state.trace.working) };
}

/** Pasos que entran en el flujo: los del prompt en curso, o los de los últimos prompts de la sesión, hasta el momento que se ve.
 *
 * El prompt en curso es el elegido en la barra; con «Toda la sesión» en la barra, el del momento de la repetición o el último. */
function flowSteps(steps, moment) {
    const upTo = moment.now ? moment.now.order : -1;
    const seen = steps.filter(s => s.order <= upTo);
    const numbers = [...new Set(seen.map(s => s.interaction))];
    if (STEP || state.flowScope === 'current') {
        const target = STEP ? STEP.interaction : state.interaction !== 'all' ? state.interaction : moment.now ? moment.now.interaction : numbers[numbers.length - 1];
        return { steps: seen.filter(s => s.interaction === target), earlier: 0 };
    }
    const earlier = state.showDone ? 0 : Math.max(0, numbers.length - FLOW_SHOWN);
    const keep = new Set(numbers.slice(earlier));
    return { steps: seen.filter(s => keep.has(s.interaction)), earlier };
}

/** Si un agente ya ha terminado en el momento que se ve: en la repetición, por su hora; en directo, si su llamada ha vuelto. */
function flowDone(a, moment) {
    if (!a.endUtc) return false;
    if (moment.replaying) return new Date(a.endUtc) <= new Date(moment.now.start);
    return !running(a, callOf(a.id), null);
}

/** Nodos de un carril (la sesión principal, o un agente) en el orden en que pasaron.
 *
 * Cada herramienta tiene el suyo, y solo se juntan las llamadas seguidas a la misma («Read ×3»): mezclar un Grep, un
 * Edit y una skill en un nodo escondía lo que se hizo. Cada respuesta o mensaje, cada prompt y cada lanzamiento de
 * agentes, también el suyo. */
function laneNodes(lane, laneSteps, moment) {
    const nodes = [];
    let block = null;
    const push = n => {
        Object.assign(n, { lane, orders: n.orders || [] });
        nodes.push(n);
        return n;
    };
    const openBlock = (s, name) => {
        block = push({ kind: 'tools', key: `t:${s.order}`, order: s.order, tool: name, skill: s.kind === 'skill', tools: new Map(), skills: new Map(), replies: 0, start: s.start, end: s.end || s.start });
        return block;
    };
    if (lane !== 'main') {
        const call = callOf(lane);
        push({ kind: 'agent', key: `a:${lane}`, order: (call ? call.order : laneSteps.length ? laneSteps[0].order : 0) + 0.1, agent: agentOf(lane) });
    }
    for (const s of laneSteps) {
        if (s.kind === 'prompt' || s.kind === 'command') {
            block = null;
            push({ kind: 'prompt', key: `p:${s.order}`, order: s.order, step: s, orders: [s.order] });
        } else if (s.kind === 'thinking') {
            const last = nodes[nodes.length - 1];
            if (last && last.kind === 'think') {
                last.orders.push(s.order);
                if (s.text) last.step = s;
            } else {
                block = null;
                push({ kind: 'think', key: `k:${s.order}`, order: s.order, step: s, orders: [s.order] });
            }
        } else if (s.kind === 'agent') {
            const last = nodes[nodes.length - 1];
            if (last && last.kind === 'fork') {
                if (s.childAgentId) last.children.push(s.childAgentId);
                last.orders.push(s.order);
            } else {
                block = null;
                push({ kind: 'fork', key: `f:${s.order}`, order: s.order, children: s.childAgentId ? [s.childAgentId] : [], orders: [s.order] });
            }
        } else if (s.kind === 'reply') {
            block = null;
            push({ kind: 'reply', key: `r:${s.order}`, order: s.order, step: s, orders: [s.order] });
        } else if (s.kind === 'tool' || s.kind === 'skill') {
            const name = s.kind === 'tool' ? s.title.split(' · ')[0] : s.title;
            const same = block && nodes[nodes.length - 1] === block && block.tool === name && block.skill === (s.kind === 'skill');
            const b = same ? block : openBlock(s, name);
            b.orders.push(s.order);
            b.end = s.end || s.start || b.end;
            b.last = s;
            addUse(b, s);
        }
    }
    if (lane !== 'main') {
        const a = agentOf(lane);
        if (a && flowDone(a, moment)) {
            const last = laneSteps[laneSteps.length - 1];
            const said = [...laneSteps].reverse().find(s => s.kind === 'reply');
            push({ kind: 'end', key: `e:${lane}`, order: (last ? last.order : nodes[0].order) + 0.2, agent: a, step: said });
        }
    }
    return nodes;
}

// Medidas del lienzo: ancho de una columna, lado de un nodo, alto de cada herramienta apilada y de la cabeza de la
// columna a su primera herramienta (por debajo de su etiqueta), y hueco entre dos agentes que van en paralelo.
const WF_COL = 190;
const WF_NODE = 56;
const WF_TOOL = 46;
const WF_TOOL_TOP = 122;
const WF_BAND_GAP = 34;
// Hueco extra antes y después de unos agentes, por el que bajan las flechas que van a ellos y suben las que vuelven.
const WF_GUT = 56;
// Alto de la cabecera del marco de un agente, con su nombre y cómo va en dos líneas, encima de su franja.
const WF_FRAME_HEAD = 68;
// Alto de la línea de modelos bajo un prompt, que baja su pila otro tanto.
const WF_MODELS = 26;
// Herramientas distintas que se apilan en una columna antes de recoger el resto en «+N».
const WF_TOOLS_MAX = 8;
// Separación entre una fila y la siguiente, por debajo de lo que la más baja de sus ramas ocupa.
const WF_ROW_GAP = 165;
// Margen izquierdo del lienzo al seguir: las filas caben en el panel, así que la cámara solo baja.
const WF_LEFT = 90;
// Al seguir con historial encima: hueco libre entre lo último de la fila y el pie del panel (sus botones), y cuántas
// herramientas apiladas se reservan bajo la fila antes de que una más la haga subir.
const WF_FOOT = 30;
const WF_TOOLS_RESERVED = 3;
// Zoom más pequeño al que el lienzo se encuadra solo (seguir, ver todo, el marco de un agente): por debajo, los títulos
// de los nodos ya no se leen. A mano se puede alejar más.
const WF_READABLE = 0.7;
// Lo que tapan la barra de abajo en una sola línea y la rama de arriba, con su margen. Los encuadres ya cuentan con ello;
// lo que tapen de más (la barra partida en dos líneas en un panel estrecho) se descuenta de lo que se ve.
const WF_DOCK_BAND = 52;
const WF_GIT_BAND = 40;
// Lo que se desplaza dentro de la ficha de un nodo: su scroll se conserva al volver a pintarla.
const WF_SCROLLERS = '.wf-say.bare, .wf-drawer-list, .wf-calls-list, .wf-call-body, .wf-term-screen, .wf-filewin-body';
// Cuánto lleva Claude sin escribir nada, trabajando, antes de enseñar que está pensando.
const WF_THINK_AFTER = 2000;
// Más rato que este sin novedades, ya no se enseña: no es que piense.
const WF_THINK_MAX = 10 * 60 * 1000;
// Cuánto sigue desplegado un agente terminado después de que quien lo lanzó dé el paso siguiente; y, si no lo da,
// cuánto como mucho.
const WF_HOLD = 6000;
const WF_HOLD_MAX = 30000;

/** Pinta el lienzo en directo o lo pone al día si ya estaba: los nodos que ya están no se rehacen, solo se mueven o
 * cambian lo suyo, y lo nuevo entra. La cámara sigue al nodo que trabaja mientras la persona no mueva el lienzo.
 *
 * El trabajo va por una línea, de izquierda a derecha: el prompt, las respuestas de Claude y sus herramientas. Un
 * agente desplegado entra en esa línea, entre el paso que lo lanzó y el siguiente, y vuelve a ella al terminar; si son
 * varios a la vez, van en paralelo, uno debajo de otro. Un agente terminado sale recogido en un nodo, y se despliega
 * con su flecha. */
function liveCanvas(steps, box) {
    const key = `${state.trace.id}|${state.flowScope}`;
    let v = state.wf;
    if (!v || v.key !== key || !box.contains(v.root)) {
        box.replaceChildren();
        v = state.wf = wfStage(box, key);
    }
    const moment = flowMoment(steps);
    const { steps: shown, earlier } = flowSteps(steps, moment);
    const prompt = moment.working && moment.now ? shown.find(s => s.interaction === moment.now.interaction) : null;
    const wait = agentsWait();
    wfBar(v, moment, earlier, Boolean(state.trace.live) && shown.some(s => !s.agentId && s.title === 'AskUserQuestion' && !s.end), prompt && prompt.start, wait);

    const byLane = new Map([['main', []]]);
    for (const s of shown) {
        const lane = s.agentId || 'main';
        if (!byLane.has(lane)) byLane.set(lane, []);
        byLane.get(lane).push(s);
        if (s.kind === 'agent' && s.childAgentId && !byLane.has(s.childAgentId)) byLane.set(s.childAgentId, []);
    }
    const lanes = new Map();
    for (const [lane, laneSteps] of byLane) if (lane === 'main' || agentOf(lane)) lanes.set(lane, laneNodes(lane, laneSteps, moment));
    const byPrompt = new Map();
    for (const s of shown) if (s.model) (byPrompt.get(s.interaction) || byPrompt.set(s.interaction, []).get(s.interaction)).push(s);
    for (const n of lanes.get('main') || []) if (n.kind === 'prompt' && !n.step.queued) n.models = modelsOf(byPrompt.get(n.step.interaction) || []);
    // La respuesta final de cada prompt: la última de Claude, sin nada detrás (ni herramientas ni preguntas) hasta el
    // prompt siguiente; en el prompt en curso, solo si la sesión ha terminado de verdad, y no si espera a la persona.
    const mainNodes = lanes.get('main') || [];
    const current = state.active.find(a => a.id === state.trace.id);
    const over = !moment.working && !moment.replaying && !(current && current.statusKey === 'waiting');
    for (let i = 0; i < mainNodes.length; i++) {
        const n = mainNodes[i];
        if (n.kind !== 'reply') continue;
        let closed = false;
        let clean = true;
        for (const m of mainNodes.slice(i + 1)) {
            if (m.kind === 'prompt' && !m.step.queued) {
                closed = true;
                break;
            }
            if (m.kind !== 'think' && m.kind !== 'prompt') {
                clean = false;
                break;
            }
        }
        n.final = clean && (closed || over);
    }
    // La última, si la sesión aún está sin revisar: como su tarjeta, en rojo hasta que se lee.
    const lastFinal = [...mainNodes].reverse().find(n => n.final);
    const session = state.active.find(a => a.id === state.trace.id);
    if (lastFinal && session && needOf(session) === 'review' && !moment.replaying) lastFinal.unread = true;
    const thinking = wfThinking(v, shown, moment);
    if (thinking) lanes.get('main').push(thinking);
    // El turno acabó, pero sus agentes siguen: la línea de la sesión acaba en el nudo de la espera.
    const main = shown.filter(s => !s.agentId);
    if (wait && !thinking && main.length) lanes.get('main').push({ kind: 'agentswait', key: 'agentswait:main', lane: 'main', order: main[main.length - 1].order + 0.6, orders: [], wait });
    const active = flowActive(lanes, moment);
    const placed = wfLayout(lanes, moment, wfFree(v).w, wfHeld(v, byLane, moment));
    v.openFrames = new Set(placed.frames.map(f => f.lane));

    // El paso que está usando una herramienta también se nota: su cuadro lleva un anillo que gira, y el conector que baja
    // hasta la herramienta, corriente.
    const busy = new Set();
    const flowing = new Set(active);
    for (const n of placed.nodes) if (active.has(n) && n.stack) {
        for (const m of placed.nodes) {
            if (m.row !== n.row || m.x !== n.x || m.lane !== n.lane) continue;
            if (!m.stack && m.kind !== 'more') busy.add(m);
            else if (m.ly <= n.ly) flowing.add(m);
        }
    }

    for (const n of placed.nodes) if (wfAsking(n, active.has(n))) flowing.add(n);

    // Lo que llega a la vez entra por turnos; lo primero que llega tras la bombilla ocupa su sitio al momento.
    v.entering = 0;
    v.swap = v.nodes.has('pending:main') && !thinking;
    const keep = new Set();
    for (const n of placed.nodes) {
        wfNode(v, n, active.has(n), moment, busy.has(n));
        keep.add(n.key);
    }
    for (const [k, node] of v.nodes) if (!keep.has(k)) {
        // La bombilla se va de golpe: en su sitio ya está el paso que esperaba.
        if (k === 'pending:main') node.remove();
        else {
            node.classList.add('leave');
            setTimeout(() => node.remove(), 260);
        }
        v.nodes.delete(k);
    }
    wfEdges(v, placed.edges, flowing);
    wfFrames(v, placed.frames, moment);
    v.bounds = placed.bounds;
    v.activeNode = placed.nodes.find(n => active.has(n)) || placed.nodes[placed.nodes.length - 1];
    // Tras plegar o desplegar un agente o cambiar el ancho, la cámara deja el nodo donde estaba en pantalla, para no
    // perderlo; si el lienzo ha encogido, dentro de lo que se ve.
    const anchored = v.anchor && placed.nodes.find(n => n.key === v.anchor.key);
    if (anchored) {
        const room = wfSight(v);
        const x = Math.max(room.x + 40, Math.min(v.anchor.x, Math.max(room.x + WF_LEFT, room.x + room.w - 160)));
        const y = Math.max(room.y + 40, Math.min(v.anchor.y, room.y + room.h - 60));
        v.cam.x = x - anchored.x * v.cam.z;
        v.cam.y = y - anchored.y * v.cam.z;
        wfCamera(v, false);
    }
    v.anchor = null;
    if (v.follow) wfFollow(v, box, v.first);
    else if (v.fit) wfFitAgain(v, placed.frames);
    // Sin seguir, un paso nuevo o lo que trabaja en otro sitio es una novedad; la primera pintura no lo es.
    const activeKey = v.activeNode && v.activeNode.key;
    wfNews(v, !v.first && (placed.nodes.length > v.shownCount || activeKey !== v.activeKey));
    v.shownCount = placed.nodes.length;
    v.activeKey = activeKey;
    v.first = false;
    if (STEP) v.detail = STEP.key;
    if (v.detail) wfDetail(v, v.detail);
}

/** Ancla la cámara al nodo de la ficha, si se da su clave, o al más cercano al centro de lo que se ve, para dejarlo en su
 * sitio al volver a colocar el lienzo.
 *
 * Al cambiar el ancho (abrir o cerrar el chat, los cambios o la ficha) las filas se reparten de otra forma y cada nodo
 * cambia de sitio: con el lienzo colocado a mano, la cámara se quedaba donde estaba y lo que se miraba se iba fuera de
 * la vista. Siguiendo o viéndolo entero no hace falta: el lienzo vuelve a encuadrar solo. */
function wfAnchorView(v, key) {
    if (!v || v.follow || v.fit || v.anchor || !v.nodeData) return;
    const picked = key && v.nodes.has(key) && v.nodeData.get(key);
    if (picked) {
        v.anchor = { key, x: picked.x * v.cam.z + v.cam.x, y: picked.y * v.cam.z + v.cam.y };
        return;
    }
    const free = wfSight(v);
    const cx = free.x + free.w / 2;
    const cy = free.y + free.h / 2;
    let best = null;
    let near = Infinity;
    for (const n of v.nodeData.values()) {
        if (!v.nodes.has(n.key)) continue;
        const x = n.x * v.cam.z + v.cam.x;
        const y = n.y * v.cam.z + v.cam.y;
        const d = (x - cx) ** 2 + (y - cy) ** 2;
        if (d < near) {
            near = d;
            best = { key: n.key, x, y };
        }
    }
    if (best) v.anchor = best;
}

/** Si un nodo es una pregunta a la persona: va en la línea, como un paso más, porque Claude se para a esperarla. */
function wfIsAsk(n) {
    return n.kind === 'tools' && n.tool === 'AskUserQuestion';
}

/** Si un nodo es una pregunta a la persona que sigue esperando su respuesta: la que trabaja en la repetición o, en
 * directo, la que aún no tiene respuesta, aunque la sesión ya no conste como trabajando (espera a la persona). */
function wfAsking(n, isActive) {
    if (!wfIsAsk(n)) return false;
    return Boolean(isActive) || (Boolean(state.trace && state.trace.live) && Boolean(n.last) && !n.last.end);
}

/** El principio de un texto en una línea, con su Markdown en línea: negrita, cursiva y código; los enlaces, por su
 * texto, y sin las marcas de títulos, listas y citas del principio de cada línea. Una tabla, fila a fila con sus celdas
 * separadas por «·» y sin la raya de debajo de la cabecera. Una marca que el corte deja a medias, fuera. */
function previewMd(text, max) {
    const flat = text.split('\n')
        .filter(l => !/^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/.test(l))
        .map(l => (/^\s*\|.*\|\s*$/.test(l) ? l.trim().slice(1, -1).split('|').map(c => c.trim()).filter(Boolean).join(' · ') : l))
        .map(l => l.replace(/^\s*(?:#{1,6}\s+|[-*+]\s+|\d+[.)]\s+|>\s*)/, '').trim()).filter(Boolean).join(' ')
        .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1');
    const src = clip(flat, max);
    const box = document.createDocumentFragment();
    const loose = t => t.replace(/\*\*|__|`/g, '');
    const marks = /(\*\*|__)(.+?)\1|`([^`]+)`|(?<![\w*])\*(?!\s)([^*]+?)\*(?![\w*])/g;
    let last = 0;
    for (const m of src.matchAll(marks)) {
        if (m.index > last) box.append(loose(src.slice(last, m.index)));
        if (m[2]) box.append(el('strong', null, m[2]));
        else if (m[3]) box.append(el('code', null, m[3]));
        else box.append(el('em', null, m[4]));
        last = m.index + m[0].length;
    }
    if (last < src.length) box.append(loose(src.slice(last)));
    return box;
}

/** Lo que un agente devolvió a quien lo lanzó: su entrega (SubagentHandback) o, si lo esperaban, el resultado de su
 * llamada. Lo que aún no ha llegado de la app se pide; null mientras tanto o si no consta. */
function agentReturn(lane) {
    const ask = s => {
        if (s.hasDetail && !state.details.has(s.order)) post({ type: 'detail', order: s.order });
    };
    const hand = [...state.trace.steps].reverse().find(s => s.agentId === lane && s.title === 'SubagentHandback');
    if (hand) {
        ask(hand);
        const input = callInput(hand);
        return input && input.message ? String(input.message) : null;
    }
    const call = callOf(lane);
    if (!call) return null;
    ask(call);
    const detail = state.details.get(call.order);
    // Lanzado en segundo plano, la llamada solo dice que arrancó: lo que devuelve llega después, en su entrega.
    return detail && detail.result && !/^Async agent launched/.test(detail.result) ? detail.result : null;
}

/** Mientras Claude piensa, Claude Code no escribe nada: si la sesión trabaja, su último paso no espera a ninguna
 * herramienta y lleva un rato sin novedades, un nodo provisional con una bombilla ocupa el sitio del paso que vendrá.
 * Si aún no ha pasado ese rato, se vuelve a mirar cuando pase. */
function wfThinking(v, shown, moment) {
    clearTimeout(v.thinkTimer);
    if (!moment.working || moment.replaying) return null;
    const main = shown.filter(s => !s.agentId);
    const last = main[main.length - 1];
    if (!last || main.some(s => (s.kind === 'tool' || s.kind === 'skill' || s.kind === 'agent') && !s.end)) return null;
    // Con un agente trabajando, el silencio puede ser suyo: no se sabe si Claude piensa.
    if (state.trace.agents.some(a => shown.some(s => s.agentId === a.id) && !flowDone(a, moment))) return null;
    const since = Math.max(...[last.start, last.end].filter(Boolean).map(t => new Date(t).getTime()));
    const quiet = Date.now() - since;
    if (!(quiet >= WF_THINK_AFTER)) {
        if (quiet >= 0) v.thinkTimer = setTimeout(() => renderDiagram(false), WF_THINK_AFTER - quiet + 50);
        return null;
    }
    // Tanto rato sin nada ya no es pensar: la sesión se ha quedado marcada como trabajando.
    if (quiet > WF_THINK_MAX) return null;
    v.thinkSince = since;
    return { kind: 'pending', key: 'pending:main', lane: 'main', order: last.order + 0.5, orders: [] };
}

/** Cuánto lleva Claude pensando sin escribir nada, para el nodo de la bombilla. */
function wfThinkingFor(v) {
    return `lleva ${duration(Math.max(0, Date.now() - v.thinkSince))}`;
}

/** Los agentes que acaban de terminar y siguen desplegados un rato, en directo o en la repetición: hasta que quien los
 * lanzó da el paso siguiente, y unos segundos más, para que se vea a dónde vuelven; luego se recogen solos. */
function wfHeld(v, byLane, moment) {
    v.hold = v.hold || new Map();
    const now = Date.now();
    if (moment.working || moment.replaying) for (const lane of v.openFrames || []) {
        const a = agentOf(lane);
        if (a && flowDone(a, moment) && !v.hold.has(lane)) v.hold.set(lane, { since: now, back: null, timer: null });
    }
    const held = new Set();
    for (const [lane, h] of v.hold) {
        const a = agentOf(lane);
        const own = byLane.get(lane) || [];
        if (!a || !own.length) continue;
        const end = own[own.length - 1].order;
        const caller = a.parentAgentId && agentOf(a.parentAgentId) ? a.parentAgentId : 'main';
        if (!h.back && (byLane.get(caller) || []).some(s => s.order > end)) h.back = now;
        const until = h.back ? h.back + WF_HOLD : h.since + WF_HOLD_MAX;
        if (now >= until) continue;
        held.add(lane);
        clearTimeout(h.timer);
        h.timer = setTimeout(() => renderDiagram(false), until - now + 50);
    }
    return held;
}

/** Crea el escenario: el mundo que se mueve con la cámara (líneas y nodos), su barra y el panel de detalle. */
function wfStage(box, key) {
    const root = el('div', 'wf-stage');
    const world = el('div', 'wf-world');
    const lines = svg('svg', { class: 'wf-lines', width: 10, height: 10 });
    const defs = svg('defs', {});
    const arrow = svg('marker', { id: 'wf-arrow', viewBox: '0 0 10 10', refX: '6', refY: '5', markerWidth: '7', markerHeight: '7', orient: 'auto' });
    arrow.append(svg('path', { d: 'M 0 0 L 10 5 L 0 10 z', fill: 'context-stroke' }));
    defs.append(arrow);
    lines.append(defs);
    world.append(lines);
    const drawer = el('aside', 'wf-drawer');
    drawer.hidden = true;
    // Lo que se pulsa en el detalle se queda en él: al desplegar una llamada se rehace, y el lienzo, al no encontrar ya
    // la fila pulsada, creería que se ha pulsado fuera y lo cerraría.
    drawer.addEventListener('click', e => e.stopPropagation());
    // Los controles del lienzo se colocan en lo que deja libre la ficha, para no quedar tapados; con la ficha a todo el
    // lienzo se esconden, porque ya no hay lienzo que seguir, encuadrar ni cambiar.
    new ResizeObserver(() => wfPlaceOverlays(v)).observe(drawer);
    const tip = el('div', 'wf-tip');
    tip.hidden = true;
    const grip = el('div', 'wf-grip');
    root.append(world, drawer, grip, tip);
    box.append(root);
    const ticker = setInterval(() => {
        if (!root.isConnected) return clearInterval(ticker);
        const pending = v.nodes.get('pending:main');
        if (pending) pending.querySelector('.wf-sub').textContent = wfThinkingFor(v);
        if (state.wf === v) wfBarStatus(v);
    }, 1000);
    const v = { key, root, world, lines, drawer, tip, tipTimer: null, nodes: new Map(), edges: new Map(), frames: new Map(), cam: { x: 0, y: 0, z: 1 }, follow: true, fit: null, autoZoom: true, first: true, detail: null };
    wfControls(v, box);
    pinnableDrawer(v);
    wfDockGrip(v, grip);
    wfApplyDock(v);
    return v;
}

/** Pone al día la barra: cómo va, qué parte se ve y, si los hay, los prompts anteriores recogidos; seguir y el zoom. */
function wfBar(v, moment, earlier, asking, since, wait) {
    const bar = $('flow-bar');
    bar.hidden = false;
    $('flow-dock').hidden = false;
    bar.querySelectorAll('[data-scope]').forEach(c => c.classList.toggle('on', c.dataset.scope === state.flowScope));
    v.status = { asking, working: moment.working, replaying: moment.replaying, at: moment.now && moment.now.start, live: Boolean(state.trace.live), since,
        agents: wait && wait.kind === 'agents' ? wait.running.length : 0, command: wait && wait.kind === 'command' ? wait.commands.length : 0 };
    wfBarStatus(v);
    const more = $('flow-earlier');
    more.hidden = !earlier;
    more.textContent = `✓ ${earlier} anterior${earlier === 1 ? '' : 'es'}`;
    hint(more, '✓', `${earlier} prompt${earlier === 1 ? '' : 's'} anterior${earlier === 1 ? '' : 'es'}, recogido${earlier === 1 ? '' : 's'}`, null,
        'Con «Sesión» se ven los últimos prompts. Púlsalo para ver también estos.');
    $('flow-follow').classList.toggle('on', v.follow);
    wfZoomLevel(v);
    $('flow-git').hidden = !changesOn();
    $('flow-git').classList.toggle('in-stamp', state.mapLayout === 'diagram');
}

/** Escribe cómo va la sesión, con su punto de color y, si trabaja, cuánto lleva el prompt; se repite cada segundo. */
function wfBarStatus(v) {
    const s = v.status;
    if (!s) return;
    const kind = s.asking ? 'asking' : s.agents && !s.replaying ? 'agents' : s.command && !s.replaying ? 'command' : s.working ? 'on' : s.replaying ? 'replay' : s.live ? 'idle' : 'closed';
    $('flow-status').className = `flow-status ${kind}`;
    // El anillo de «Seguir» solo gira mientras la sesión o sus agentes trabajan: sin nada que seguir, se queda quieto.
    $('flow-dock').classList.toggle('live', kind === 'on' || kind === 'agents' || kind === 'command');
    const took = s.since ? duration(between(s.since, new Date().toISOString())) : '';
    $('flow-status-text').textContent = {
        asking: 'Esperando tu respuesta',
        agents: `Esperando a ${agentsWord(s.agents)}`,
        command: `Esperando a ${commandsWord(s.command)}`,
        on: took ? `En directo · ${took}` : 'En directo',
        replay: `Repetición · ${clock(s.at)}`,
        idle: 'En espera',
        closed: 'Cerrada',
    }[kind];
    const tips = {
        asking: ['Esperando tu respuesta', 'Claude ha hecho una pregunta y está parado hasta que contestes en su terminal.'],
        agents: ['Esperando a sus agentes', 'El turno de la sesión ha acabado, pero le quedan agentes trabajando en segundo plano. Cuando acaben, la sesión retomará sola: no tienes que hacer nada.'],
        command: ['Esperando a un comando', 'El turno de la sesión ha acabado, pero espera el resultado de un comando que sigue en marcha en segundo plano. Cuando acabe, la sesión retomará sola: no tienes que hacer nada.'],
        on: ['En directo', 'La sesión está trabajando. El tiempo es lo que lleva el prompt en curso.'],
        replay: ['Repetición', 'Se ve la sesión tal como iba en ese momento. ▶ y ⏸ en la barra de abajo de la página.'],
        idle: ['En espera', 'La sesión está abierta, pero no trabaja ahora: en cuanto reciba un prompt, se verá aquí.'],
        closed: ['Cerrada', 'La sesión ya no está abierta. Pulsa ▶ para verla crecer paso a paso.'],
    }[kind];
    hint($('flow-status'), '●', tips[0], null, tips[1]);
}

/** Pone el zoom en su botón: el porcentaje y, si lo elige la app, «auto». */
function wfZoomLevel(v) {
    const level = $('flow-zoom-level');
    level.textContent = `${Math.round(v.cam.z * 100)} %`;
    level.classList.toggle('auto', v.autoZoom);
}

/** Acerca o aleja el lienzo un paso desde los botones, alrededor del centro de lo que se ve; si sigue, sigue con el nuevo zoom. */
function wfZoomBy(v, factor) {
    wfMovedByHand(v);
    const box = $('diagram');
    const z = Math.max(0.2, Math.min(2, v.cam.z * factor));
    const room = wfSight(v);
    const cx = room.x + room.w / 2;
    const cy = room.y + room.h / 2;
    v.cam.x = cx - ((cx - v.cam.x) / v.cam.z) * z;
    v.cam.y = cy - ((cy - v.cam.y) / v.cam.z) * z;
    v.cam.z = z;
    v.autoZoom = false;
    if (v.follow) wfFollow(v, box, false);
    else wfCamera(v, true);
}

/** Avisa, si no se sigue, de que lo que trabaja ha cambiado fuera de la vista, y hacia dónde queda; si se ve, lo quita. */
function wfNews(v, changed) {
    const news = $('flow-news');
    const n = v.activeNode;
    if (v.follow || !n || !v.root.isConnected) {
        v.news = false;
        news.hidden = true;
        return;
    }
    const x = n.x * v.cam.z + v.cam.x;
    const y = n.y * v.cam.z + v.cam.y;
    const room = wfSight(v);
    const inside = x > room.x && x < room.x + room.w && y > room.y + 20 && y < room.y + room.h - 20;
    if (changed && !inside) v.news = true;
    if (inside) v.news = false;
    news.hidden = !v.news;
    if (!v.news) return;
    const arrow = y >= room.y + room.h - 20 ? '↓' : y <= room.y + 20 ? '↑' : x >= room.x + room.w ? '→' : '←';
    news.textContent = `${arrow} Novedades · volver a seguir`;
    hint(news, '◎', 'Hay pasos nuevos fuera de la vista', null, 'La sesión ha seguido trabajando donde no se ve. Púlsalo para que la cámara vuelva a seguirla.');
}

/** Vuelve a seguir lo que trabaja, desde el botón o desde el aviso de novedades. */
function wfFollowAgain() {
    const v = state.wf;
    if (!v) return;
    wfMovedByHand(v);
    v.follow = true;
    $('flow-follow').classList.add('on');
    wfFollow(v, $('diagram'), false);
    wfNews(v, false);
}

// LA RAMA DE GIT

/** Los cambios de rama de la sesión, por el paso en que ya se ve la nueva: la de antes y la de después. */
function branchChanges() {
    const t = state.trace;
    const key = t ? `${t.id}|${t.steps.length}` : '';
    if (state.branchMemo && state.branchMemo.key === key) return state.branchMemo.map;
    const map = new Map();
    let prev = null;
    for (const s of t ? t.steps : []) {
        if (!s.branch) continue;
        if (prev && s.branch !== prev) map.set(s.order, { from: prev, to: s.branch });
        prev = s.branch;
    }
    state.branchMemo = { key, map };
    return map;
}

/** El cambio de rama que se enseña en un nodo del lienzo: en la cabecera de una columna, el de cualquiera de sus pasos;
 * null si no hay ninguno o si el nodo va apilado bajo otro.
 *
 * Un cambio que ve una herramienta de la pila (un git switch) sube a la cabecera: encima de la herramienta quedaría
 * entre el paso y su pila, tapando lo que va debajo del paso. */
function wfBranchTurn(n) {
    if (n.stack) return null;
    const map = branchChanges();
    const order = (n.turnOrders || n.orders).find(o => map.has(o));
    return order === undefined ? null : map.get(order);
}

/** Columnas de un carril: cada una es un paso de quien actúa (el prompt, una respuesta, el agente al empezar o su final)
 * con las herramientas que usó hasta el paso siguiente, apiladas, y los agentes que lanzó desde ahí.
 *
 * Las llamadas a una misma herramienta se acumulan en un nodo aunque no vayan seguidas: todos los Bash del paso juntos,
 * todos los Read juntos. En su detalle va cada llamada con su hora. */
function wfColumns(list) {
    const cols = [];
    let col = null;
    for (const n of list) {
        const head = n.kind === 'prompt' || n.kind === 'reply' || n.kind === 'think' || n.kind === 'agent' || n.kind === 'end' || n.kind === 'pending' || n.kind === 'agentswait' || wfIsAsk(n);
        if (head || !col) {
            col = { head: n.kind === 'fork' ? null : n, tools: [], agents: [], orders: [] };
            cols.push(col);
            if (n.kind === 'fork') col.agents.push(...n.children);
            continue;
        }
        if (n.kind === 'tools') col.tools.push(n);
        else if (n.kind === 'fork') col.agents.push(...n.children);
    }
    // Una columna que empieza por un lanzamiento, sin paso propio, se junta con la anterior.
    const kept = cols.filter((c, i) => {
        if (c.head || i === 0) return true;
        cols[i - 1].tools.push(...c.tools);
        cols[i - 1].agents.push(...c.agents);
        return false;
    });
    for (const c of kept) c.tools = wfMerge(c.tools);
    return kept;
}

/** Junta en un nodo cada herramienta de una columna, en el orden en que salió por primera vez, sumando sus llamadas. */
function wfMerge(tools) {
    const byName = new Map();
    for (const t of tools) {
        const key = `${t.skill ? 'skill' : 'tool'}:${t.tool}`;
        const into = byName.get(key);
        if (!into) {
            byName.set(key, t);
            continue;
        }
        into.orders.push(...t.orders);
        into.end = t.end || into.end;
        into.last = t.last || into.last;
        for (const [bag, from] of [[into.tools, t.tools], [into.skills, t.skills]]) {
            for (const [name, b] of from) {
                const a = bag.get(name);
                if (!a) bag.set(name, b);
                else {
                    a.count += b.count;
                    a.errors += b.errors;
                    a.orders.push(...b.orders);
                    a.last = b.last;
                }
            }
        }
    }
    return [...byName.values()];
}

/** Coloca los nodos por columnas: una por paso de Claude, de izquierda a derecha, con sus herramientas apiladas debajo.
 * Un prompt sigue en la misma fila si cabe entero, y si no, abre otra; uno que no cabe ni en una fila entera salta de
 * línea al llegar al borde.
 *
 * Los agentes desplegados que un paso lanza y espera van en el flujo, justo después de ese paso: el primero en la misma
 * línea y los demás debajo, en paralelo, cada uno en su marco y con la misma forma por dentro. Del paso sale una flecha
 * a cada uno, y de cada uno vuelve otra al paso siguiente. Los recogidos van en la pila de herramientas.
 *
 * La fila avanza un paso por respuesta, no uno por herramienta: así cabe lo que pasó y se lee de un vistazo. El borde
 * es el de lo que deja ver la ficha (`width`): con ella abierta, las filas se parten antes y caben en lo que queda. */
function wfLayout(lanes, moment, width, held) {
    const nodes = [];
    const edges = [];
    const frames = [];
    const limit = Math.max(WF_COL * 3, width - 200);
    // Desplegados, solo los de primer nivel que trabajan en el momento que se ve; los terminados y los que lanza otro
    // agente, recogidos. Con todo el historial abierto, el lienzo no se leía. La persona puede abrir o cerrar cualquiera.
    const nested = lane => {
        const a = agentOf(lane);
        return Boolean(a && a.parentAgentId && lanes.has(a.parentAgentId));
    };
    // En directo, también los que lanza otro agente; y los que acaban de terminar, mientras se ve a dónde vuelven.
    const open = lane => state.wfOpen.has(lane) || (!state.wfClosed.has(lane) && (held.has(lane) || ((!nested(lane) || moment.working) && !flowDone(agentOf(lane), moment))));
    // Separa los agentes de una columna: los desplegados, en su franja; los recogidos, en la pila de herramientas.
    const split = col => {
        col.inline = col.agents.filter(l => lanes.get(l) && lanes.get(l).length && !open(l));
        col.agents = col.agents.filter(l => open(l));
    };
    const rows = [];
    let row = null;
    // Los agentes con los que quien los lanzó siguió trabajando: se colocan al cerrar la fila, debajo de todo.
    let later = [];
    const newRow = () => {
        row = { nodes: [], rects: [] };
        rows.push(row);
    };
    const put = (n, x, y) => {
        Object.assign(n, { x, ly: y, row });
        nodes.push(n);
        row.nodes.push(n);
        return n;
    };

    // Las herramientas de una columna, apiladas bajo su paso; si son muchas, las últimas en «+N». Devuelve la altura de
    // la última, o null si no hay ninguna.
    const stack = (col, x, y) => {
        const shown = col.tools.length > WF_TOOLS_MAX ? col.tools.slice(0, WF_TOOLS_MAX - 1) : col.tools;
        // Bajo un agente no hay etiqueta (su nombre va en la cabecera del marco): sus herramientas, justo debajo.
        let ty = y + (col.head.kind === 'agent' ? WF_NODE / 2 + 46 : WF_TOOL_TOP) + (col.head.models && col.head.models.length ? WF_MODELS : 0);
        let above = col.head;
        for (const t of shown) {
            t.stack = true;
            put(t, x, ty);
            edges.push({ key: `down:${t.key}`, from: above, to: t, color: col.head.lane === 'main' ? 'main' : col.head.lane, shape: 'v' });
            above = t;
            ty += WF_TOOL;
        }
        if (shown.length < col.tools.length) {
            const rest = col.tools.slice(shown.length);
            const more = put({ kind: 'more', key: `+:${col.head.key}`, lane: col.head.lane, orders: rest.flatMap(t => t.orders), count: rest.reduce((s, t) => s + t.orders.length, 0) }, x, ty);
            edges.push({ key: `down:${more.key}`, from: above, to: more, color: col.head.lane === 'main' ? 'main' : col.head.lane, shape: 'v' });
            above = more;
            ty += WF_TOOL;
        }
        // Los agentes recogidos, al pie de la pila, cada uno en un nodo con su flecha para desplegarlo.
        for (const lane of col.inline || []) {
            const head = lanes.get(lane)[0];
            Object.assign(head, { stack: true, collapsed: true });
            put(head, x, ty);
            edges.push({ key: `down:${head.key}`, from: above, to: head, color: lane, shape: 'v' });
            above = head;
            ty += WF_TOOL;
        }
        return above === col.head ? null : above.ly;
    };
    const done = lane => flowDone(agentOf(lane), moment);
    const endOf = lane => {
        const list = lanes.get(lane);
        return list[list.length - 1].order;
    };
    // Dónde va el paso de un agente que se pone debajo de otro que acaba en bottom: tras el hueco y su cabecera.
    const nextLine = bottom => bottom + WF_BAND_GAP + WF_FRAME_HEAD + WF_NODE / 2 + 4;
    // Lo más bajo de la fila que se está montando, contando los marcos de sus agentes.
    const rowBottom = () => Math.max(WF_NODE + 40, ...row.nodes.map(n => n.ly + (n.stack || n.kind === 'more' ? 30 : WF_NODE + 40)), ...row.rects.map(r => r.y2));

    // Un paso con su pila y sus agentes. Recibe las flechas que entran en él y devuelve dónde va el siguiente, las
    // flechas que entrarán en ese siguiente y lo más bajo que ha ocupado.
    const place = (col, x, y, next, into) => {
        split(col);
        put(col.head, x, y);
        col.head.collapsed = false;
        col.head.stack = false;
        col.head.turnOrders = [...col.head.orders, ...col.tools.flatMap(t => t.orders), ...(col.inline || []).flatMap(lane => lanes.get(lane).flatMap(m => m.orders))];
        const own = col.head.lane === 'main' ? (col.head.kind === 'prompt' ? 'user' : 'main') : col.head.lane;
        for (const e of into) edges.push({ key: `to:${col.head.key}<${e.from.key}`, from: e.from, to: col.head, color: e.color || own, shape: e.shape || 'h', gx: e.gx });
        const last = stack(col, x, y);
        const bottom = last === null ? y + 72 : last + 30;
        const b = branch(col, x, y, next);
        return b ? { x: b.x, into: b.outs, bottom: Math.max(bottom, b.bottom) } : { x: x + WF_COL, into: [{ from: col.head }], bottom };
    };
    // Los agentes desplegados de un paso. Si el paso siguiente vino después de que todos terminaran, quien los lanzó los
    // esperó: van en el flujo y el siguiente se aparta. Si no, se dejan para debajo de la fila.
    const branch = (col, x, y, next) => {
        const open = col.agents.filter(l => lanes.get(l) && lanes.get(l).length);
        if (!open.length) return null;
        if (next && !open.every(l => done(l) && endOf(l) < next.head.order)) {
            later.push({ from: col.head, x, lanes: open });
            return null;
        }
        const start = x + WF_COL + WF_GUT;
        let end = start;
        let ly = y;
        let bottom = y;
        const outs = [];
        open.forEach((l, i) => {
            const r = agentLine(l, start, ly, { from: col.head, color: l, shape: i ? 'fork' : 'h', gx: start - 70 });
            end = Math.max(end, r.x);
            bottom = r.bottom;
            if (done(l)) outs.push({ from: r.last, color: l, shape: i ? 'join' : 'h' });
            ly = nextLine(r.bottom);
        });
        // Las vueltas suben por un mismo pasillo, pasado el agente más largo.
        for (const o of outs) o.gx = end + 6;
        return { x: end + WF_GUT, outs, bottom };
    };
    // Un agente desplegado en su línea, desde x, dentro de su marco: sus pasos con sus pilas y los agentes que lanzó.
    const agentLine = (lane, x, y, into) => {
        const cols = wfColumns(lanes.get(lane));
        const x1 = x - 58;
        let bottom = y;
        let last = null;
        let ins = [into];
        const mark = later.length;
        cols.forEach((col, i) => {
            const r = place(col, x, y, cols[i + 1], ins);
            x = r.x;
            ins = r.into;
            bottom = Math.max(bottom, r.bottom);
            last = col.head;
        });
        // Los que lanzó sin esperarlos, dentro de su marco, debajo de su línea; el marco se ensancha si van más lejos.
        let right = x;
        for (const d of later.splice(mark)) {
            const h = hang(d, nextLine(bottom));
            bottom = h.bottom;
            right = Math.max(right, h.end);
        }
        const rect = { x1, x2: right - 12, y1: y - WF_NODE / 2 - 4 - WF_FRAME_HEAD, y2: bottom + 12 };
        row.rects.push(rect);
        frames.push({ key: `frame:${lane}`, lane, row, ...rect });
        return { x, last, bottom: rect.y2 };
    };
    // Los agentes que no se esperaron, uno debajo de otro desde la línea ly; devuelve lo más bajo y lo más a la
    // derecha que ocupan. Su vuelta
    // se busca al final, cuando ya está colocado todo: puede caer en esta fila o en la siguiente.
    const returns = [];
    const hang = (d, ly) => {
        let end = 0;
        let bottom = ly;
        const group = [];
        for (const l of d.lanes) {
            const r = agentLine(l, d.x + WF_COL, ly, { from: d.from, color: l, shape: 'fork', gx: d.x + WF_COL - 70 });
            end = Math.max(end, r.x);
            if (done(l)) group.push({ from: r.last, lane: l, caller: d.from.lane, row });
            bottom = r.bottom;
            ly = nextLine(r.bottom);
        }
        // Las vueltas del grupo suben juntas, pasado el más largo, para no cruzar los marcos de los otros.
        group.forEach((g, i) => returns.push({ ...g, gx: end + 6, offset: i * 6 }));
        return { bottom, end };
    };
    // Al cerrar una fila, los de la línea principal, debajo de todo lo suyo.
    const flush = () => {
        while (later.length) hang(later.shift(), nextLine(rowBottom()));
    };

    // Agentes de primer nivel que lanzó una skill, sin lanzamiento en el flujo: salen de la columna que los precede.
    const main = lanes.get('main') || [];
    const columns = wfColumns(main);
    const launched = new Set(columns.flatMap(c => c.agents));
    for (const [lane, list] of lanes) {
        if (lane === 'main' || launched.has(lane) || !list.length) continue;
        const a = agentOf(lane);
        if (a.parentAgentId && lanes.has(a.parentAgentId)) continue;
        const col = [...columns].reverse().find(c => c.head && c.head.order < list[0].order) || columns[0];
        if (col) col.agents.push(lane);
    }

    newRow();
    let x = 0;
    let into = [];
    // Columnas del prompt que empieza en la columna i, hasta el siguiente.
    const span = i => {
        let j = i + 1;
        while (j < columns.length && !(columns[j].head.kind === 'prompt' && !columns[j].head.step.queued)) j++;
        return j - i;
    };
    columns.forEach((col, i) => {
        const opens = col.head.kind === 'prompt' && !col.head.step.queued;
        const wrap = row.nodes.length && (x > limit || (opens && x + (span(i) - 1) * WF_COL > limit));
        if (wrap) {
            flush();
            newRow();
            x = 0;
            into = into.map(e => ({ ...e, shape: 'wrap' }));
        }
        const r = place(col, x, 0, columns[i + 1], into);
        x = r.x;
        into = r.into;
    });
    flush();
    // La vuelta de cada agente que no se esperó: al primer paso de quien lo lanzó que vino después de que terminara. Si
    // cae más allá de la fila siguiente, la flecha cruzaría demasiado; basta con lo que dice su final.
    for (const r of returns) {
        const end = endOf(r.lane);
        // Si quien lo lanzó aún no ha dado el paso siguiente, la flecha va a donde está ahora: ahí es donde vuelve.
        const heads = nodes.filter(n => !n.stack && n.kind !== 'more' && n.lane === r.caller);
        const to = heads.find(n => n.order > end) || heads[heads.length - 1];
        if (!to || rows.indexOf(to.row) - rows.indexOf(r.row) > 1) continue;
        // Sube por el hueco entre dos columnas de la línea, a la derecha de sus etiquetas, para no pisarlas.
        const gaps = to.row.nodes.filter(n => n.ly === to.ly && !n.stack).map(n => n.x + 166).filter(x => x >= r.gx);
        const gx = (gaps.length ? Math.min(...gaps) : r.gx) + r.offset;
        edges.push({ key: `back:${r.lane}`, from: r.from, to, color: r.lane, shape: 'back', gx });
    }

    // Las filas, una debajo de otra, cada una tan alta como lo que tiene por encima (agentes) y por debajo (herramientas).
    let base = 0;
    rows.forEach((r, i) => {
        r.top = Math.min(-WF_NODE, ...r.rects.map(q => q.y1));
        r.bottom = Math.max(WF_NODE + 40, ...r.nodes.map(n => n.ly + (n.stack || n.kind === 'more' ? 24 : WF_NODE + 40)), ...r.rects.map(q => q.y2));
        if (i) base += rows[i - 1].bottom - r.top + WF_ROW_GAP;
        r.base = base;
        for (const n of r.nodes) n.y = base + n.ly;
    });
    for (const f of frames) {
        f.y1 += f.row.base;
        f.y2 += f.row.base;
    }
    // El salto de línea pasa por debajo de la fila que acaba, por el hueco hasta la siguiente. Baja pasados los marcos
    // de los agentes que cuelgan de la fila, para no atravesarlos.
    for (const e of edges) if (e.shape === 'wrap') {
        e.midY = e.from.row.base + e.from.row.bottom + WF_ROW_GAP / 2;
        const past = Math.max(-Infinity, ...e.from.row.rects.filter(q => q.y2 > e.from.ly + WF_NODE).map(q => q.x2 + 24));
        e.gx = Math.max(e.gx !== undefined ? e.gx : e.from.x + WF_NODE / 2 + 34, past);
    }
    const xs = [...nodes.map(n => n.x), ...frames.map(f => f.x1), ...frames.map(f => f.x2 - 140)];
    const ys = [...nodes.map(n => n.y), ...frames.map(f => f.y1), ...frames.map(f => f.y2)];
    return { nodes, edges, frames, bounds: { minX: Math.min(...xs, 0), maxX: Math.max(...xs, 0) + 140, minY: Math.min(...ys, 0), maxY: Math.max(...ys, 0) } };
}

/** Pone al día los marcos de los agentes: el rectángulo de su franja, de su color, con su nombre, su modelo y cómo va.
 * Al pulsar la cabecera se abre su ficha. */
function wfFrames(v, frames, moment) {
    const keep = new Set();
    for (const f of frames) {
        keep.add(f.key);
        let frame = v.frames.get(f.key);
        if (!frame) {
            frame = el('div', v.first ? 'wf-frame' : 'wf-frame enter');
            setTimeout(() => frame.classList.remove('enter'), 700);
            const head = el('button', 'wf-frame-head');
            head.addEventListener('click', e => {
                e.stopPropagation();
                v.detail = `a:${f.lane}`;
                wfDetail(v, v.detail);
                wfFitFrame(v, frame.rect);
            });
            frame.append(head);
            v.world.insertBefore(frame, v.lines);
            v.frames.set(f.key, frame);
        }
        paint(frame, f.lane);
        frame.rect = f;
        Object.assign(frame.style, { transform: `translate(${f.x1}px, ${f.y1}px)`, width: `${f.x2 - f.x1}px`, height: `${f.y2 - f.y1}px` });
        const a = agentOf(f.lane);
        const done = flowDone(a, moment);
        const label = roundLabel(a.description);
        const head = frame.firstChild;
        const state2 = done ? `✓ ${duration(between(a.startUtc, a.endUtc))}` : '● trabajando';
        const sig = `${actorName(f.lane)}|${a.type}|${modelLabel(a.model, a.effort)}|${state2}`;
        if (head.dataset.sig !== sig) {
            head.dataset.sig = sig;
            const words = el('span', 'wf-frame-words');
            const title = el('span', 'wf-frame-name');
            if (label) title.append(roundTag({ label }), ' ');
            title.append(actorName(f.lane));
            const meta = el('span', 'wf-frame-meta');
            if (a.type) meta.append(el('span', 'wf-frame-type', a.type));
            if (a.model) meta.append(modelBadge(a.model, a.effort));
            meta.append(el('span', 'wf-frame-state', state2));
            words.append(title, meta);
            head.replaceChildren(words);
        }
        frame.classList.toggle('working', !done);
    }
    for (const [key, frame] of v.frames) if (!keep.has(key)) {
        frame.classList.add('leave');
        setTimeout(() => frame.remove(), 320);
        v.frames.delete(key);
    }
}

/** Nodo del lienzo, con la forma de un nodo de workflow: un cuadrado con su dibujo y, debajo, qué es. Se crea una vez;
 * si cambia lo que enseña, se rehace por dentro, sin volver a entrar. Al pulsarlo se abre su detalle. */
function wfNode(v, n, isActive, moment, isBusy) {
    const actor = n.kind === 'prompt' && n.step.shell ? 'shell' : n.lane === 'main' ? (n.kind === 'prompt' ? 'user' : 'main') : n.lane;
    const turn = wfBranchTurn(n);
    const sig = [n.kind, n.orders.length, n.collapsed, n.stack, isActive ? flowDoing(moment) : '', n.kind === 'end' ? n.agent.endUtc : '', (n.models || []).map(m => modelLabel(m.model, m.effort)).join(','), n.final, n.unread, wfAsking(n, isActive), turn ? turn.to : '', n.orders.some(o => hookOrders().has(o)), n.wait ? `${waitWord(n.wait)}@${n.wait.since}` : ''].join('|');
    let node = v.nodes.get(n.key);
    if (!node) {
        node = el('div', v.first ? 'wf-node' : 'wf-node enter');
        if (!v.first) {
            // Cada paso nuevo espera a que llegue su conexión, y uno detrás de otro si llegan varios; sus herramientas,
            // tras él. El que sustituye a la bombilla no espera: su sitio ya estaba.
            const head = !n.stack && n.kind !== 'more';
            let delay;
            if (head && v.swap) {
                delay = 0;
                v.swap = false;
                v.entering = 1;
            } else if (head) delay = 0.42 + 0.35 * v.entering++;
            else delay = 0.32 + 0.35 * Math.max(0, v.entering - 1);
            node.style.setProperty('--enter', `${delay}s`);
        }
        setTimeout(() => node.classList.remove('enter'), 1800);
        v.world.append(node);
        v.nodes.set(n.key, node);
        node.addEventListener('click', e => {
            e.stopPropagation();
            if (v.dragged) return;
            v.detail = node.dataset.nkey;
            state.focus = null;
            decorate();
            wfDetail(v, v.detail);
        });
    }
    node.style.transform = `translate(${n.x}px, ${n.y}px)`;
    node.dataset.nkey = n.key;
    v.nodeData = v.nodeData || new Map();
    v.nodeData.set(n.key, n);
    if (node.dataset.sig === sig) {
        node.classList.toggle('picked', v.detail === n.key);
        node.classList.toggle('busy', Boolean(isBusy));
        return node;
    }
    node.dataset.sig = sig;
    node.dataset.orders = n.orders.join(',');
    node.className = `wf-node ${n.kind === 'tools' ? 'step' : n.kind}` + (n.kind === 'prompt' && n.step.shell ? ' shell' : '') + (n.stack || n.kind === 'more' ? ' stack' : '') + (isActive ? ' working' : '') + (node.classList.contains('enter') ? ' enter' : '') + (v.detail === n.key ? ' picked' : '') + (isBusy ? ' busy' : '') + (n.final ? ' final' : '') + (n.unread ? ' unread' : '') + (wfAsking(n, isActive) ? ' asking' : '');
    paint(node, actor);
    const box = el('div', 'wf-box');
    const label = el('div', 'wf-label');
    const title = el('div', 'wf-title');
    const sub = el('div', 'wf-sub');
    label.append(title, sub);
    node.replaceChildren(box, label);
    const words = (t, chars) => clip(oneLine(plain(t || '')), chars);
    const preview = (t, chars) => previewMd(plain(t || ''), chars);
    delete node.dataset.tip;
    switch (n.kind) {
        case 'prompt':
            if (n.step.shell) {
                box.append(flowIcon('terminal', 'wf-icon'));
                title.textContent = 'Comando de consola';
                sub.replaceChildren(el('span', 'shell-command', clip(oneLine(n.step.text || '').replace(/^!\s*/, ''), 70)));
                break;
            }
            box.append(flowIcon('prompt', 'wf-icon'));
            title.textContent = n.step.queued ? 'Mientras trabaja' : `Prompt ${n.step.interaction}`;
            sub.replaceChildren(preview(n.step.text, 70));
            if (n.models && n.models.length) {
                const line = el('div', 'wf-models');
                line.append(modelBadges(n.models, 1));
                label.append(line);
            }
            if (n.step.images) {
                const pics = el('span', 'wf-pics');
                pics.append(flowIcon('image', 'wf-pics-icon'), String(n.step.images));
                box.append(pics);
            }
            break;
        case 'think':
            box.append(flowIcon('think', 'wf-icon'));
            title.textContent = 'Piensa';
            sub.replaceChildren(n.step.text ? preview(n.step.text, 70) : 'Pensando…');
            break;
        case 'agentswait':
            box.append(waitMark(n.wait, 'wf-orbit'));
            title.textContent = n.wait.kind === 'command' ? 'Esperando a un comando' : 'Esperando a sus agentes';
            sub.textContent = `${waitWord(n.wait)} · el turno acabó a las ${hour(n.wait.since)}`;
            node.classList.toggle('command', n.wait.kind === 'command');
            hint(node, n.wait.kind === 'command' ? '>_' : '◌', title.textContent, null,
                `${waitWhat(n.wait)}. La sesión retomará sola cuando acabe${n.wait.running.length + n.wait.commands.length === 1 ? '' : 'n todos'}.`);
            break;
        case 'pending': {
            const dots = el('span', 'wf-think-dots');
            dots.append(el('i'), el('i'), el('i'));
            box.append(flowIcon('think', 'wf-icon'), dots);
            title.textContent = 'Pensando…';
            sub.textContent = wfThinkingFor(v);
            break;
        }
        case 'reply':
            box.append(flowIcon(n.final ? 'done' : 'reply', 'wf-icon'));
            title.textContent = n.final ? 'Respuesta final' : 'Responde';
            sub.replaceChildren(preview(n.step.text, 70));
            if (n.unread) label.append(el('span', 'review-badge wf-unread', 'Sin revisar'));
            break;
        case 'fork':
            box.append(flowIcon('agent', 'wf-icon'), el('span', 'wf-count', `×${n.children.length}`));
            title.textContent = n.children.length === 1 ? 'Lanza un agente' : `Lanza ${n.children.length} agentes`;
            break;
        case 'agent': {
            const a = n.agent;
            box.append(flowIcon('agent', 'wf-icon'));
            const label2 = roundLabel(a.description);
            title.textContent = (label2 ? `${label2} · ` : '') + actorName(n.lane);
            const done = n.agent.endUtc && flowDone(a, moment);
            const how = done ? `✓ ${duration(between(a.startUtc, a.endUtc))}` : 'trabajando';
            if (n.stack && a.model) {
                sub.classList.add('badged');
                sub.append(modelBadge(a.model, a.effort), el('span', 'wf-sub-text', how));
            } else sub.textContent = [n.stack ? a.type : '', modelLabel(a.model, a.effort), how].filter(Boolean).join(' · ');
            const toggle = el('button', 'wf-toggle', n.collapsed ? '▸' : '▾');
            hint(toggle, '🤖', n.collapsed ? 'Desplegar el agente' : 'Recoger el agente', null, n.collapsed ? 'Enseña sus pasos y los agentes que lanzó.' : 'Lo deja en un solo nodo.');
            toggle.addEventListener('click', e => {
                e.stopPropagation();
                if (n.collapsed) {
                    state.wfOpen.add(n.lane);
                    state.wfClosed.delete(n.lane);
                } else {
                    state.wfClosed.add(n.lane);
                    state.wfOpen.delete(n.lane);
                }
                v.anchor = { key: n.key, x: n.x * v.cam.z + v.cam.x, y: n.y * v.cam.z + v.cam.y };
                wfMovedByHand(v);
                v.follow = false;
                $('flow-follow').classList.remove('on');
                renderDiagram(false);
            });
            box.append(toggle);
            break;
        }
        case 'end':
            box.append(flowIcon('done', 'wf-icon'));
            title.textContent = 'Termina';
            sub.textContent = `${duration(between(n.agent.startUtc, n.agent.endUtc))} · vuelve a ${clip(actorName(wfCaller(n.agent)), 26)}`;
            break;
        case 'more':
            box.append(el('span', 'wf-more', `+${n.count}`));
            title.textContent = `${n.count} llamadas más`;
            sub.textContent = 'Pulsa para verlas';
            break;
        default: {
            const kind = n.skill ? 'skill' : toolKind(n.tool);
            const use = (n.skill ? n.skills : n.tools).get(n.tool) || { count: n.orders.length, errors: 0 };
            box.append(flowIcon(n.skill ? 'skill' : toolIcon(n.tool), 'wf-icon'));
            if (kind === 'plug') box.append(el('span', 'wf-mcp-tag', 'MCP'));
            if (use.count > 1) box.append(el('span', 'wf-count', String(use.count)));
            if (use.errors) node.classList.add('failed');
            node.classList.add(kind === 'skill' ? 'skill' : kind === 'plug' ? 'mcp' : 'tool');
            const mcp = /^mcp__([^_]+)__(.+)$/.exec(n.tool);
            title.textContent = mcp ? mcp[2] : n.tool;
            const what = n.last ? n.last.title.split(' · ').slice(1).join(' · ') : '';
            sub.textContent = mcp ? `MCP · ${mcp[1]}` : n.skill ? 'skill' : what ? clip(what, 60) : duration(between(n.start, n.end)) || '';
            // Una pregunta a la persona dice lo que pregunta, y lo que se respondió cuando llega; no lo que tardó.
            const asked = wfIsAsk(n) && n.last && n.last.questions && n.last.questions[0];
            if (asked) {
                title.textContent = 'Pregunta';
                sub.textContent = asked.answer ? `✓ ${clip(withoutAttached(asked.answer), 60)}` : clip(asked.question, 70);
                if (n.last.images) {
                    const pics = el('span', 'wf-pics');
                    pics.append(flowIcon('image', 'wf-pics-icon'), String(n.last.images));
                    box.append(pics);
                }
            }
            break;
        }
    }
    if ((isActive || wfAsking(n, isActive)) && n.kind !== 'pending' && n.kind !== 'agentswait') {
        const now = el('div', 'wf-now', wfAsking(n, isActive) ? 'Esperando tu respuesta' : n.lane === 'main' ? flowDoing(moment) : 'Trabajando');
        (n.stack ? label : node).append(now);
    }
    if (turn) {
        const chip = el('div', 'wf-branch-turn');
        chip.append(flowIcon('branch', 'flow-icon'), `${turn.from} → ${turn.to}`);
        hint(chip, '⑂', 'Cambia de rama', null, `Desde aquí la sesión está en ${turn.to}; antes, en ${turn.from}.`);
        // Encima del cuadro: debajo van las herramientas del paso, apiladas.
        node.append(chip);
    }
    if (n.orders.some(o => secretOrders().has(o))) node.classList.add('secret');
    const failed = n.orders.filter(o => hookOrders().has(o)).map(o => stepAt(o).hookError);
    if (failed.length) {
        node.classList.add('hook-bad');
        const flag = el('span', 'wf-hook-flag');
        flag.append(hookMark());
        hint(flag, '⚠', 'Falló un hook de Arkko', failed[failed.length - 1], HOOK_HINT);
        box.append(flag);
    }
    return node;
}

/** A quién vuelve un agente al terminar: al agente que lo lanzó o, si no lo lanzó ninguno, a Claude. */
function wfCaller(a) {
    return a.parentAgentId && agentOf(a.parentAgentId) ? a.parentAgentId : 'main';
}

/** Pone al día los conectores: curvas como las de un workflow, del lado derecho de un nodo al izquierdo del siguiente.
 * La que entra en un nodo que trabaja lleva una corriente; las nuevas se dibujan creciendo; las vueltas, discontinuas.
 * Las que van a un agente o vuelven de él llevan punta, para que se vea hacia dónde va el trabajo. */
function wfEdges(v, edges, active) {
    const keep = new Set();
    const half = n => (n.stack ? 18 : n.kind === 'more' ? 18 : n.kind === 'agent' ? 32 : n.kind === 'end' || n.kind === 'fork' ? 22 : WF_NODE / 2);
    for (const e of edges) {
        keep.add(e.key);
        let item = v.edges.get(e.key);
        if (!item) {
            const path = svg('path', { class: v.first ? 'wf-edge' : 'wf-edge grow', pathLength: '1' });
            const flow = svg('path', { class: 'wf-current' });
            v.lines.append(path, flow);
            item = { path, flow };
            v.edges.set(e.key, item);
            setTimeout(() => path.classList.remove('grow'), 700);
        }
        const f = e.from;
        const t = e.to;
        let d;
        if (e.shape === 'v') {
            // Bajo la etiqueta del paso, que va debajo de su cuadrado; entre herramientas apiladas, de una a otra.
            const y1 = f.stack || f.kind === 'more' || f.kind === 'agent' ? f.y + half(f) : f.y + half(f) + 64 + (f.models && f.models.length ? WF_MODELS : 0);
            d = `M ${f.x} ${y1} V ${t.y - half(t)}`;
        } else if (e.shape === 'fork' || e.shape === 'join') {
            // Hacia un agente que va debajo, o de vuelta desde él: por el pasillo entre columnas que deja la colocación.
            d = wfRound([[f.x + half(f), f.y], [e.gx, f.y], [e.gx, t.y], [t.x - half(t) - 4, t.y]], 14);
        } else if (e.shape === 'back') {
            const x1 = f.x + half(f);
            const side = e.gx !== undefined ? e.gx : x1 + 26;
            const above = t.y - half(t) - 34;
            d = wfRound([[x1, f.y], [side, f.y], [side, above], [t.x, above], [t.x, t.y - half(t) - 2]], 14);
        } else {
            const x1 = f.x + half(f);
            const x2 = t.x - half(t) - 4;
            const turn = e.gx !== undefined ? e.gx : x1 + 34;
            d = e.shape === 'wrap' ? wfRound([[x1, f.y], [turn, f.y], [turn, e.midY], [x2 - 34, e.midY], [x2 - 34, t.y], [x2, t.y]], 16)
                : `M ${x1} ${f.y} C ${x1 + Math.max(30, (x2 - x1) / 2)} ${f.y}, ${x2 - Math.max(30, (x2 - x1) / 2)} ${t.y}, ${x2} ${t.y}`;
        }
        item.path.setAttribute('d', d);
        item.path.classList.toggle('back', e.shape === 'back');
        item.path.classList.toggle('arrow', f.lane !== t.lane);
        item.flow.setAttribute('d', d);
        item.flow.classList.toggle('on', active.has(e.to));
        paint(item.path, e.color);
        paint(item.flow, e.color);
    }
    for (const [key, item] of v.edges) if (!keep.has(key)) {
        item.path.classList.add('leave');
        item.flow.remove();
        setTimeout(() => item.path.remove(), 260);
        v.edges.delete(key);
    }
}

/** Trazo que pasa por unos puntos con las esquinas redondeadas, para los conectores que saltan de fila. */
function wfRound(points, radius) {
    let d = `M ${points[0][0]} ${points[0][1]}`;
    for (let i = 1; i < points.length - 1; i++) {
        const [px, py] = points[i - 1];
        const [x, y] = points[i];
        const [nx, ny] = points[i + 1];
        const r = Math.min(radius, Math.hypot(x - px, y - py) / 2, Math.hypot(nx - x, ny - y) / 2);
        const ax = x - Math.sign(x - px) * r;
        const ay = y - Math.sign(y - py) * r;
        const bx = x + Math.sign(nx - x) * r;
        const by = y + Math.sign(ny - y) * r;
        d += ` L ${ax} ${ay} Q ${x} ${y} ${bx} ${by}`;
    }
    const last = points[points.length - 1];
    return `${d} L ${last[0]} ${last[1]}`;
}

/** Mueve la cámara para dejar el nodo que trabaja (o el último) a media altura. Como las filas caben en el panel, solo
 * baja; si la persona ha acercado tanto que no caben, también se desplaza de lado. Al abrir, salta; después, se desliza. */
function wfFollow(v, box, jump) {
    if (v.dg) {
        dgFollow(v, jump);
        return;
    }
    const n = v.activeNode;
    if (!n) return;
    // Lo que deja libre la ficha: con ella abajo, también el hueco de los controles que suben encima.
    const room = wfFree(v);
    const under = !v.drawer.hidden && !v.drawer.classList.contains('tall') && v.root.dataset.dock === 'bottom';
    // Lo que tapen de más la barra de abajo y la rama de arriba, partidas en un panel estrecho, tampoco se ve.
    const sight = wfSight(v);
    const free = room.h - (under ? 70 : 0) - Math.max(0, sight.bottom - WF_DOCK_BAND);
    const head = 70 + Math.max(0, sight.top - WF_GIT_BAND);
    const r = n.row;
    const top = r ? r.base + r.top : n.y - WF_NODE;
    const bottom = r ? r.base + r.bottom : n.y + WF_NODE;
    // Mientras la persona no elija su zoom, se aleja lo justo para que quepa la fila entera, de ancho la línea del flujo y
    // de alto con los agentes que cuelgan de ella, sin bajar de lo que se lee. Así, desplegar o recoger un agente cambia
    // el zoom poco a poco, y la cámara no baja a buscarlo y vuelve a subir.
    if (v.autoZoom && r) {
        const wide = Math.max(...r.nodes.filter(m => m.ly === 0 && !m.stack).map(m => m.x)) + WF_COL;
        v.cam.z = Math.max(WF_READABLE, Math.min(1, (room.w - WF_LEFT - 90) / wide, (free - 110) / (bottom - top)));
    }
    const z = v.cam.z;
    v.cam.x = room.x + WF_LEFT;
    if (WF_LEFT + n.x * z > room.w - 140) v.cam.x = room.x + room.w * 0.6 - n.x * z;
    // Sin nada encima (una sesión que empieza, o el prompt en curso solo), la línea de la fila va a un tercio de la altura
    // y algo más abajo, que es donde va a crecer. Con algo encima, lo más abajo que deje caber la fila entera, con lo que
    // cuelga de ella (herramientas y agentes), sobre el pie del panel: así se ve todo lo anterior que quepa y no queda
    // hueco vacío debajo. La fila reserva sitio para unas cuantas herramientas, para no subir con cada una que llega; un
    // agente u otra que no quepa la suben lo justo. Nunca tanto que lo que lleva encima quede fuera por arriba.
    const base = r ? r.base : n.y;
    const fit = head + (base - top) * z + 30;
    const above = v.bounds ? top - v.bounds.minY : 0;
    const reach = Math.max(bottom - base, WF_TOOL_TOP + WF_TOOLS_RESERVED * WF_TOOL);
    const line = Math.max(fit, above > 1 ? free - WF_FOOT - reach * z : free * 0.3 + 30);
    v.cam.y = room.y + line - base * z;
    // Si aun así lo que trabaja no se vería (una fila más alta que el panel), se baja lo justo.
    const below = n.y * z + v.cam.y - (room.y + free - 90);
    if (below > 0) v.cam.y -= below;
    wfCamera(v, !jump);
}

/** Aplica la cámara al mundo: desplazamiento y zoom, con transición si se pide. */
function wfCamera(v, smooth) {
    v.world.classList.toggle('glide', smooth);
    v.world.style.transform = `translate(${v.cam.x}px, ${v.cam.y}px) scale(${v.cam.z})`;
    if (v.dg) dgRail(v, smooth);
    if (state.wf !== v) return;
    wfZoomLevel(v);
    if (v.news) wfNews(v, false);
}

/** Enseña el flujo entero en lo que se ve y deja de seguir. Se queda así: cuando cambia el hueco o llega un paso nuevo,
 * lo vuelve a encajar, hasta que la persona mueve el lienzo.
 *
 * Nunca más pequeño de lo que se lee: si no cabe, enseña lo último, abajo, con el paso que trabaja a la vista. */
function wfFit(v, smooth = true) {
    const b = v.bounds;
    if (!b) return;
    const w = b.maxX - b.minX + WF_COL * 1.5;
    const h = b.maxY - b.minY + WF_NODE * 4;
    const room = wfSight(v);
    const z = Math.max(WF_READABLE, Math.min(1.2, room.w / w, room.h / h));
    let x = room.x + room.w / 2 - ((b.minX + b.maxX) / 2) * z;
    let y = room.y + room.h / 2 - ((b.minY + b.maxY) / 2) * z;
    if (w * z > room.w) {
        x = room.x + (WF_COL * 0.75 - b.minX) * z;
        const n = v.activeNode;
        if (n && n.x * z + x > room.x + room.w - 140) x = room.x + room.w - 140 - n.x * z;
    }
    if (h * z > room.h) y = room.y + room.h - (b.maxY + WF_NODE * 2) * z;
    v.cam = { z, x, y };
    v.follow = false;
    v.fit = { lane: null };
    v.autoZoom = true;
    $('flow-follow').classList.remove('on');
    wfCamera(v, smooth);
}

/** Encuadra el marco de un agente en lo que se ve, para ver su flujo entero de un vistazo, y deja de seguir. Como «Ver
 * todo», se queda encajado mientras el agente siga desplegado. Si no cabe a un tamaño que se lea, desde su principio. */
function wfFitFrame(v, f, smooth = true) {
    const room = wfSight(v);
    const top = room.y + 20;
    const w = room.w;
    const h = room.h - 20;
    const fw = f.x2 - f.x1;
    const fh = f.y2 - f.y1;
    const z = Math.max(WF_READABLE, Math.min(1, (w - 80) / fw, (h - 30) / fh));
    let x = room.x + w / 2 - ((f.x1 + f.x2) / 2) * z;
    let y = top + h / 2 - ((f.y1 + f.y2) / 2) * z;
    if (fw * z > w - 80) x = room.x + 40 - f.x1 * z;
    if (fh * z > h - 30) y = top + 10 - f.y1 * z;
    v.cam = { z, x, y };
    v.follow = false;
    v.fit = { lane: f.lane };
    v.autoZoom = true;
    $('flow-follow').classList.remove('on');
    wfCamera(v, smooth);
}

/** Vuelve a encajar lo que se pidió ver entero, el flujo o el marco de un agente, en lo que se ve ahora. Si el agente ya
 * no tiene marco, el lienzo se queda donde está, como movido a mano. */
function wfFitAgain(v, frames) {
    if (!v.fit.lane) {
        wfFit(v);
        return;
    }
    const f = frames.find(fr => fr.lane === v.fit.lane);
    if (f) wfFitFrame(v, f);
    else v.fit = null;
}

/** Quita el tooltip del lienzo y olvida el que iba a salir. */
function wfHideTip(v) {
    clearTimeout(v.tipTimer);
    v.tipNode = null;
    v.tip.hidden = true;
}

/** Enseña el tooltip de un paso: una tarjeta con la franja del color de quien actúa, su dibujo, qué es, cuándo y con qué
 * modelo, y lo que dice debajo. Encima del nodo, o debajo si no cabe. */
function wfShowTip(v, node) {
    const n = v.nodeData && v.nodeData.get(node.dataset.nkey);
    if (!n || !node.isConnected) return;
    const actor = n.kind === 'prompt' && n.step && n.step.shell ? 'shell' : n.lane === 'main' ? (n.kind === 'prompt' ? 'user' : 'main') : n.lane;
    const t = v.tip;
    paint(t, actor);
    const head = el('div', 'wf-tip-head');
    const icon = el('span', 'wf-tip-icon');
    const body = el('div', 'wf-tip-body');
    const s = n.step;
    let title = '';
    let meta = s ? [n.kind === 'prompt' ? (n.models || []).map(m => modelLabel(m.model, m.effort)).join(', ') : modelLabel(s.model, s.effort), clock(s.start)].filter(Boolean).join(' · ') : '';
    let iconKind = 'done';
    switch (n.kind) {
        case 'prompt':
            if (s.shell) {
                title = 'Comando de consola';
                iconKind = 'terminal';
                body.append(shellLine(cut(s.text || '', 900)));
                break;
            }
            title = s.queued ? `${state.user}, mientras trabaja` : `Prompt ${s.interaction}`;
            iconKind = 'prompt';
            body.append(wfMd(cut(plain(s.text), 900)));
            break;
        case 'reply':
            title = 'Responde';
            iconKind = 'reply';
            body.append(wfMd(cut(plain(s.text), 900)));
            break;
        case 'think':
            title = 'Piensa';
            iconKind = 'think';
            body.append(wfMd(cut(plain(s.text), 900), 'think'));
            break;
        case 'end':
            title = `Termina · ${duration(between(n.agent.startUtc, n.agent.endUtc))}`;
            iconKind = 'done';
            body.append(el('div', 'wf-tip-line', `↩ Lo que devuelve a ${actorName(wfCaller(n.agent))}`));
            const back = agentReturn(n.lane);
            if (back || s) body.append(wfMd(cut(plain(back || s.text), 700)));
            break;
    }
    icon.append(flowIcon(iconKind, 'flow-icon'));
    head.append(icon, el('span', 'wf-tip-title', title));
    if (meta) head.append(el('span', 'wf-tip-meta', meta));
    t.replaceChildren(head, body);
    t.hidden = false;
    // Encima del cuadrado del nodo; si no cabe por arriba, debajo de su etiqueta. Siempre dentro del lienzo.
    const stage = v.root.getBoundingClientRect();
    const boxR = node.querySelector('.wf-box').getBoundingClientRect();
    const nodeR = node.getBoundingClientRect();
    const w = t.offsetWidth;
    const h = t.offsetHeight;
    const cx = boxR.left + boxR.width / 2 - stage.left;
    let top = boxR.top - stage.top - h - 12;
    const below = top < 50;
    if (below) top = nodeR.bottom - stage.top + 10;
    t.classList.toggle('below', below);
    const left = Math.max(10, Math.min(cx - w / 2, stage.width - w - 10));
    t.style.left = `${left}px`;
    t.style.top = `${Math.max(10, Math.min(top, stage.height - h - 10))}px`;
    t.style.setProperty('--tail', `${cx - left}px`);
}

/** Mover el lienzo arrastrando el fondo o con la rueda, y acercar con Ctrl y la rueda hacia el ratón; cualquiera de
 * las tres deja de seguir al nodo que trabaja. Esc cierra el detalle. */
function wfControls(v, box) {
    const stop = () => {
        wfMovedByHand(v);
        v.follow = false;
        $('flow-follow').classList.remove('on');
    };
    // El tooltip de los pasos (lo que se dice y el final de un agente), no de las herramientas: sale al poco de pararse
    // encima y se va al salir, al pulsar o al mover el lienzo.
    v.world.addEventListener('mouseover', e => {
        const node = e.target.closest('.wf-node');
        if (!node || node === v.tipNode) return;
        wfHideTip(v);
        const n = v.nodeData && v.nodeData.get(node.dataset.nkey);
        if (!n || n.stack || !/^(prompt|reply|think|end)$/.test(n.kind)) return;
        v.tipNode = node;
        v.tipTimer = setTimeout(() => wfShowTip(v, node), 280);
    });
    v.world.addEventListener('mouseout', e => {
        const node = e.target.closest('.wf-node');
        if (node && !node.contains(e.relatedTarget)) wfHideTip(v);
    });
    v.root.addEventListener('mousedown', () => wfHideTip(v), true);
    v.root.addEventListener('wheel', () => wfHideTip(v), { capture: true, passive: true });
    // Se arrastra desde cualquier sitio del lienzo, también desde un nodo; si el ratón se ha movido, no es un clic.
    v.root.addEventListener('mousedown', e => {
        if (e.button !== 0 || v.ahora || e.target.closest('.wf-drawer, button')) return;
        e.preventDefault();
        const from = { x: e.clientX, y: e.clientY, cx: v.cam.x, cy: v.cam.y };
        v.dragged = false;
        const move = m => {
            if (!v.dragged && Math.hypot(m.clientX - from.x, m.clientY - from.y) < 4) return;
            if (!v.dragged) {
                v.dragged = true;
                v.root.classList.add('panning');
                stop();
            }
            v.cam.x = from.cx + m.clientX - from.x;
            v.cam.y = from.cy + m.clientY - from.y;
            wfCamera(v, false);
        };
        const end = () => {
            v.root.classList.remove('panning');
            document.removeEventListener('mousemove', move);
            document.removeEventListener('mouseup', end);
        };
        document.addEventListener('mousemove', move);
        document.addEventListener('mouseup', end);
    });
    v.root.addEventListener('wheel', e => {
        if (v.ahora || e.target.closest('.wf-drawer')) return;
        e.preventDefault();
        stop();
        if (e.ctrlKey) {
            v.autoZoom = false;
            const r = box.getBoundingClientRect();
            const px = e.clientX - r.left;
            const py = e.clientY - r.top;
            const z = Math.max(0.2, Math.min(2, v.cam.z * Math.exp(-e.deltaY / 300)));
            v.cam.x = px - ((px - v.cam.x) / v.cam.z) * z;
            v.cam.y = py - ((py - v.cam.y) / v.cam.z) * z;
            v.cam.z = z;
        } else {
            v.cam.x -= e.shiftKey ? e.deltaY : e.deltaX;
            v.cam.y -= e.shiftKey ? 0 : e.deltaY;
        }
        wfCamera(v, false);
    }, { passive: false });
    v.root.addEventListener('click', e => {
        if (!v.dragged && !e.target.closest('.wf-node, .wf-drawer') && !reviewWriting()) wfCloseDetail(v);
    });
}

/** Abre el detalle de un nodo en el panel de la derecha: lo que hizo, con su formato; cada paso lleva a su sitio en el chat. */
function wfDetail(v, key) {
    const opening = v.drawer.hidden;
    if (opening) v.drawer.style.animation = '';
    // Abierta, el lienzo se recoloca en lo que deja ver, también con la ficha de un fichero; y al pasar de una de texto a
    // una de fichero, que tiene su propio ancho.
    const wasFile = v.root.classList.contains('detail-file');
    const isFile = Boolean(key) && key.startsWith('file:') && !v.piece && !STEP;
    v.root.classList.toggle('detail-file', isFile);
    if (opening || wasFile !== isFile) requestAnimationFrame(() => wfSpaceChanged(v));
    // Al abrirse, se guarda cómo se miraba el lienzo, para dejarlo igual al cerrar.
    if (v.drawer.hidden && !v.beforeDetail) {
        v.beforeDetail = { cam: { ...v.cam }, follow: v.follow, fit: v.fit, autoZoom: v.autoZoom };
        v.movedInDetail = false;
    }
    // El directo vuelve a pintar la ficha con cada novedad: si es la misma, se deja cada scroll donde se estaba leyendo.
    const same = !v.drawer.hidden && v.drawer.dataset.key === key;
    v.drawer.dataset.key = key;
    if (state.changes.pop && key !== `file:branch:${state.changes.pop.path}`) reviewPopClose();
    if (!key || !key.startsWith('file:')) delete v.drawer.dataset.look;
    if (key && key.startsWith('file:')) {
        wfFileDetail(v, key.slice(5));
        return;
    }
    delete v.drawer.dataset.sig;
    const n = v.nodeData && v.nodeData.get(key);
    if (n && (n.kind === 'pending' || n.kind === 'agentswait')) return;
    // Abrir la respuesta final que faltaba por leer la da por revisada, como abrir el chat.
    if (n && n.unread) {
        n.unread = false;
        post({ type: 'reviewed' });
    }
    if (!n) {
        wfCloseDetail(v);
        return;
    }
    v.nodes.forEach((node, k) => node.classList.toggle('picked', k === key));
    const d = v.drawer;
    d.hidden = false;
    const actor = n.kind === 'prompt' && n.step && n.step.shell ? 'shell' : n.lane === 'main' ? (n.kind === 'prompt' ? 'user' : 'main') : n.lane;
    paint(d, actor);
    const head = el('div', 'wf-drawer-head');
    const close = el('button', 'pbtn small wf-drawer-close', '✕');
    close.addEventListener('click', () => wfCloseDetail(v));
    const shell = n.kind === 'prompt' && n.step && n.step.shell;
    const titleText = { prompt: shell ? 'Comando de consola' : n.step && n.step.queued ? 'Mensaje mientras trabaja' : `Prompt ${n.step ? n.step.interaction : ''}`, reply: n.final ? 'Respuesta final de Claude' : 'Respuesta de Claude', think: 'Lo que pensó Claude', fork: 'Lanzamiento de agentes', agent: actorName(n.lane), end: n.agent ? `${actorName(n.lane)} · lo que devuelve a ${actorName(wfCaller(n.agent))}` : '', more: 'Más llamadas' }[n.kind] || (n.tool || 'Herramientas');
    const badge = el('span', 'flow-badge');
    badge.append(flowIcon(shell ? 'terminal' : n.kind === 'tools' ? (n.skill ? 'skill' : toolIcon(n.tool)) : n.kind === 'end' ? 'done' : n.kind === 'fork' ? 'agent' : n.kind, 'flow-icon'));
    // Una MCP, por lo que hace y su servidor; no por su nombre interno.
    const mcp = n.kind === 'tools' && mcpServer(n.tool);
    // Abierta desde la fila de un lanzamiento: se vuelve a él.
    if (n.kind === 'agent' && v.backFor === key && v.nodeData.get(v.backTo)) {
        const back = el('button', 'pbtn small wf-drawer-back', '‹ Lanzamiento');
        back.addEventListener('click', () => {
            v.detail = v.backTo;
            wfDetail(v, v.detail);
        });
        head.append(back);
    }
    head.append(badge, el('strong', 'wf-drawer-title', mcp ? `${mcp.tool.replace(/_/g, ' ')} · ${mcp.server}` : wfIsAsk(n) ? 'Pregunta a la persona' : titleText));
    if (n.kind === 'agent' && agentOf(n.lane)) {
        const a = agentOf(n.lane);
        const when = el('span', 'wf-drawer-when');
        when.append(wfAgentMeta(a, Boolean(a.endUtc) && flowDone(a, flowMoment(selectedSteps()))));
        head.append(when);
    }
    if (n.kind === 'fork') {
        const kids = n.children.map(agentOf).filter(Boolean);
        const moment = flowMoment(selectedSteps());
        const done = kids.filter(a => a.endUtc && flowDone(a, moment)).length;
        const parts = [`${kids.length} agente${kids.length === 1 ? '' : 's'}`, done ? `${done} terminado${done === 1 ? '' : 's'}` : '', kids.length - done ? `${kids.length - done} trabajando` : ''];
        head.append(el('span', 'wf-drawer-when wf-drawer-count', parts.filter(Boolean).join(' · ')));
    }
    if (n.step && /^(prompt|reply|think|end)$/.test(n.kind)) {
        const when = el('span', 'wf-drawer-when');
        if (n.kind === 'prompt') when.append(modelBadges(n.models || [], 3));
        else if (n.step.model) when.append(modelBadge(n.step.model, n.step.effort));
        when.append(el('span', 'wf-drawer-clock', clock(n.step.start)));
        head.append(when);
    }
    const size = wfSizeButton(d);
    // La respuesta final lleva además el botón del formato de Arkko.
    if (n.kind === 'reply' && n.final && n.step) head.append(ffButton(v, n.step));
    if (!v.piece && !STEP) head.append(wfDockButton(v));
    head.append(size, close);
    const body = el('div', 'wf-drawer-body');
    const md = text => {
        const box = el('div', 'wf-md');
        box.append(markdown(plain(text || '')));
        return box;
    };
    switch (n.kind) {
        case 'prompt':
            body.append(shell
                ? wfTerminal('Consola', { command: shellCommand(n.step), description: 'Comando «!» de la persona' }, n.step.output, n.step.isError)
                : wfSay(n.step, n.lane, true));
            break;
        case 'reply':
            body.append(n.final ? ffSay(n.step, n.lane) : wfSay(n.step, n.lane, true));
            break;
        case 'think':
            body.append(wfSay(n.step, n.lane, true));
            break;
        case 'fork': {
            const list = el('div', 'wf-drawer-list wf-launch');
            body.append(list);
            const moment = flowMoment(selectedSteps());
            for (const id of n.children) {
                const a = agentOf(id);
                if (!a) continue;
                list.append(wfLaunchRow(v, key, a, moment));
            }
            break;
        }
        case 'agent': {
            const own = selectedSteps().filter(s => s.agentId === n.lane);
            body.append(wfCalls(v, key, own.slice(-160), `Lo que hizo · ${own.length} paso${own.length === 1 ? '' : 's'}`, true, n.lane));
            break;
        }
        case 'end': {
            const back = agentReturn(n.lane);
            const said = back ? { kind: 'reply', text: back, order: -1 } : n.step;
            if (said) body.append(wfSay(said, n.lane, true));
            else body.append(el('div', 'wf-drawer-meta', 'Sin mensaje final.'));
            break;
        }
        default: {
            const all = selectedSteps().filter(s => n.orders.includes(s.order));
            body.append(wfCalls(v, key, all, `${all.length} llamada${all.length === 1 ? '' : 's'}`));
        }
    }
    const marked = (n.kind === 'agent' ? selectedSteps().filter(s => s.agentId === n.lane)
        : n.step ? [n.step] : selectedSteps().filter(s => (n.orders || []).includes(s.order))).filter(s => s.secrets);
    if (marked.length) body.prepend(secretBox(marked));
    const kept = same ? [...d.querySelectorAll(WF_SCROLLERS)].map(box => box.scrollTop) : [];
    d.replaceChildren(head, body);
    d.querySelectorAll(WF_SCROLLERS).forEach((box, i) => {
        if (kept[i]) box.scrollTop = kept[i];
    });
    // Para clavarla con el clic derecho en su cabecera: cómo se llama el paso y de qué prompt es.
    d.dataset.title = titleText;
    const own = n.step || selectedSteps().find(s => (n.orders || []).includes(s.order));
    d.dataset.interaction = own ? own.interaction : '';
}

// LA FICHA COLOCADA COMO F12: a la derecha, a la izquierda o abajo, con su tamaño, que se recuerda para todas las
// sesiones y todos los lienzos; o flotante, fuera del lienzo, en su propia ventana, que se puede clavar en un corcho

const WF_DOCKS = [
    { id: 'left', name: 'Izquierda' },
    { id: 'bottom', name: 'Abajo' },
    { id: 'right', name: 'Derecha' },
];

/** Cómo está colocada la ficha: dónde, su ancho, el de la ficha de un fichero y su alto en porcentaje; a la derecha de
 * partida. El código necesita más ancho que el texto: la ficha de un fichero recuerda el suyo. */
function wfDockPrefs() {
    const kept = remembered('drawerDock', null) || {};
    const num = (value, fallback) => (typeof value === 'number' && isFinite(value) ? value : fallback);
    return {
        side: WF_DOCKS.some(d => d.id === kept.side) ? kept.side : 'right',
        w: num(kept.w, 640),
        fw: num(kept.fw, 900),
        h: num(kept.h, 55),
    };
}

/** Guarda un cambio de la colocación y la devuelve entera. */
function wfSaveDock(change) {
    const prefs = { ...wfDockPrefs(), ...change };
    remember('drawerDock', prefs);
    return prefs;
}

/** Pone en el lienzo la colocación de la ficha y su tamaño. Una pieza de un paso no tiene lienzo: su ficha lo ocupa todo. */
function wfApplyDock(v, prefs = wfDockPrefs()) {
    if (!v || v.piece || STEP) return;
    const r = v.root;
    r.dataset.dock = prefs.side;
    r.style.setProperty('--dock-w', `${prefs.w}px`);
    r.style.setProperty('--dock-fw', `${prefs.fw}px`);
    r.style.setProperty('--dock-h', `${prefs.h}%`);
    const size = v.drawer.querySelector('.wf-size-btn');
    if (size && size.refresh) size.refresh();
}

/** Lo que deja libre la ficha en el lienzo, en sus coordenadas: al lado o encima de ella. Sin ficha, o con ella a todo
 * el lienzo, el lienzo entero. */
function wfFree(v) {
    const W = v.root.clientWidth;
    const H = v.root.clientHeight;
    const d = v.drawer;
    const all = { x: 0, y: 0, w: W, h: H };
    if (!d || d.hidden || v.piece || STEP || d.classList.contains('tall')) return all;
    const side = v.root.dataset.dock;
    if (side === 'right') return { x: 0, y: 0, w: Math.max(80, W - d.offsetWidth), h: H };
    if (side === 'left') return { x: d.offsetWidth, y: 0, w: Math.max(80, W - d.offsetWidth), h: H };
    if (side === 'bottom') return { x: 0, y: 0, w: W, h: Math.max(80, H - d.offsetHeight) };
    return all;
}

/** Lo que se ve del lienzo: lo que deja libre la ficha, sin lo que tapan por arriba la rama y el aviso, y por abajo la
 * barra de controles, medidos en pantalla. Devuelve también cuánto tapa cada franja (`top` y `bottom`). */
function wfSight(v) {
    const free = wfFree(v);
    const r = v.root.getBoundingClientRect();
    let top = 0;
    let bottom = 0;
    const band = (node, edge) => {
        if (!node || !node.offsetParent || getComputedStyle(node).visibility === 'hidden') return;
        const b = node.getBoundingClientRect();
        if (!b.height) return;
        if (edge === 'top') top = Math.max(top, b.bottom - r.top - free.y);
        else bottom = Math.max(bottom, free.y + free.h - (b.top - r.top));
    };
    if (state.wf === v && !v.piece && !STEP) {
        band($('flow-git-head'), 'top');
        // En el Diagrama, la rama va en el sello; en vertical, el sello queda arriba y tapa como lo hacía ella.
        if (v.stamp && v.stamp.classList.contains('compact') && !v.stamp.hidden) band(v.stamp, 'top');
        band($('flow-dock'), 'bottom');
    }
    if (v.notice && !v.notice.hidden) band(v.notice.firstElementChild, 'top');
    top = Math.max(0, Math.min(top, free.h / 3));
    bottom = Math.max(0, Math.min(bottom, free.h / 3));
    return { x: free.x, y: free.y + top, w: free.w, h: Math.max(80, free.h - top - bottom), top, bottom };
}

/** Vuelve a colocar el lienzo cuando cambia lo que se ve de él (la ficha se abre, cambia de tamaño o de sitio): las
 * filas se reparten en el ancho nuevo y la cámara vuelve a encuadrar según cómo se mira. */
function wfSpaceChanged(v) {
    if (!v || v.piece || STEP || state.wf !== v || !v.root.isConnected) return;
    wfPlaceOverlays(v);
    wfAnchorView(v, v.drawer.hidden ? null : v.drawer.dataset.key);
    renderDiagram(false);
    wfKeepPicked(v);
}

/** Coloca los controles del lienzo (la barra de abajo y la rama de arriba) en la esquina de lo libre. */
function wfPlaceOverlays(v) {
    if (!v || v.piece || STEP || state.wf !== v) return;
    const d = v.drawer;
    const open = !d.hidden;
    const tall = open && d.classList.contains('tall');
    v.root.classList.toggle('detail-open', open);
    v.root.classList.toggle('detail-tall', tall);
    const W = v.root.clientWidth;
    const H = v.root.clientHeight;
    const free = wfFree(v);
    const moved = open && !tall;
    const right = moved ? `${Math.max(14, Math.round(W - (free.x + free.w) + 14))}px` : '';
    $('flow-dock').style.right = right;
    $('flow-dock').style.bottom = moved ? `${Math.max(12, Math.round(H - (free.y + free.h) + 12))}px` : '';
    // En lo libre y no debajo de la ficha: si no cabe en una línea, se parte.
    $('flow-dock').style.maxWidth = moved ? `${Math.max(120, Math.round(free.w - 28))}px` : '';
    $('flow-git').style.right = right;
    $('flow-git').style.maxWidth = moved ? `min(420px, ${Math.max(120, Math.round(free.w - 28))}px)` : '';
    $('flow-git').style.top = moved && free.y > 0 ? `${Math.round(free.y + 10)}px` : '';
    $('parte-mode').style.right = right;
    $('parte-mode').style.top = moved && free.y > 0 ? `${Math.round(free.y + PT_BAND_TOP)}px` : '';
    for (const id of ['flow-dock', 'flow-bar', 'flow-git', 'parte-mode']) $(id).style.visibility = tall ? 'hidden' : '';
    // En el Diagrama, la lista de la rama cuelga del sello, que es donde está la rama.
    if (state.mapLayout === 'diagram' && v.stamp) dgGitPlace(v);
    // La página de Ahora, en lo que deja libre la ficha, como el plano del Diagrama.
    // En el Parte, la página empieza bajo la franja del conmutador, que así no tapa nada de lo que se lee.
    const band = v.page && !$('parte-mode').hidden ? PT_BAND : 0;
    if (v.page) Object.assign(v.page.style, { left: `${Math.round(free.x)}px`, top: `${Math.round(free.y + band)}px`, width: `${Math.round(free.w)}px`, height: `${Math.round(free.h - band)}px` });
    if (v.notice) {
        v.notice.hidden = tall;
        v.notice.style.left = `${Math.round(free.x)}px`;
        v.notice.style.width = `${Math.round(free.w)}px`;
    }
}

/** Con la ficha abierta y el lienzo colocado a mano, que no tape lo que se mira: si el nodo de la ficha queda fuera de lo
 * que se ve, se lleva a su centro. Siguiendo o viéndolo entero, ya lo encuadra el lienzo al pintarse. */
function wfKeepPicked(v) {
    if (!v || v.piece || STEP || v.drawer.hidden || v.drawer.classList.contains('tall') || v.follow || v.fit) return;
    const n = v.nodeData && v.nodeData.get(v.drawer.dataset.key);
    if (!n) return;
    const free = wfSight(v);
    const x = n.x * v.cam.z + v.cam.x;
    const y = n.y * v.cam.z + v.cam.y;
    if (x > free.x + 60 && x < free.x + free.w - 60 && y > free.y + 60 && y < free.y + free.h - 60) return;
    v.cam.x = Math.round(free.x + free.w / 2 - n.x * v.cam.z);
    v.cam.y = Math.round(free.y + free.h / 2 - n.y * v.cam.z);
    wfCamera(v, true);
}

/** El borde de la ficha para cambiar su tamaño; se guarda al soltar. */
function wfDockGrip(v, grip) {
    if (v.piece || STEP) return;
    // Lo que se pulsa aquí no es un clic en el lienzo: no lo mueve ni cierra la ficha.
    for (const type of ['mousedown', 'click']) grip.addEventListener(type, e => e.stopPropagation());
    grip.addEventListener('pointerdown', e => {
        e.preventDefault();
        grip.setPointerCapture(e.pointerId);
        grip.classList.add('drag');
        const side = v.root.dataset.dock;
        let size = null;
        const move = ev => {
            const r = v.root.getBoundingClientRect();
            // Lo mismo que deja la hoja de estilos: al lienzo, siempre casi la mitad, para que se siga viendo.
            if (side === 'bottom') {
                const most = 100 - Math.max(40, 14000 / r.height);
                size = { h: Math.round(Math.min(most, Math.max(22, (r.bottom - ev.clientY) / r.height * 100))) };
                v.root.style.setProperty('--dock-h', `${size.h}%`);
            } else {
                const w = side === 'left' ? ev.clientX - r.left : r.right - ev.clientX;
                const px = Math.round(Math.min(r.width - Math.max(160, r.width * 0.45), Math.max(340, w)));
                const file = v.root.classList.contains('detail-file');
                size = file ? { fw: px } : { w: px };
                v.root.style.setProperty(file ? '--dock-fw' : '--dock-w', `${px}px`);
            }
            wfPlaceOverlays(v);
            if (v.follow) wfFollow(v, $('diagram'), true);
        };
        const up = ev => {
            grip.releasePointerCapture(ev.pointerId);
            grip.classList.remove('drag');
            grip.removeEventListener('pointermove', move);
            grip.removeEventListener('pointerup', up);
            if (size) wfSaveDock(size);
            wfSpaceChanged(v);
        };
        grip.addEventListener('pointermove', move);
        grip.addEventListener('pointerup', up);
    });
}

/** El botón ⋮ de la cabecera de la ficha: abre el menú para colocarla. */
function wfDockButton(v) {
    const button = el('button', 'pbtn small wf-drawer-btn wf-dock-btn');
    const icon = svg('svg', { class: 'fit-icon', viewBox: '0 0 16 16', 'aria-hidden': 'true' });
    icon.append(svg('path', { d: 'M8 3.5h.01M8 8h.01M8 12.5h.01' }));
    button.append(icon);
    hint(button, '⋮', 'Colocar la ficha', null,
        'A la derecha, a la izquierda o abajo, como las herramientas de desarrollo del navegador, y se recuerda para todas las sesiones. O flotante, en su propia ventana, que se puede clavar en un corcho.');
    button.addEventListener('click', e => {
        e.stopPropagation();
        wfDockMenu(v, button);
    });
    return button;
}

/** El menú de colocar la ficha: una miniatura de cada colocación, con la de ahora marcada. Va en el lienzo y no en la
 * ficha, para que no se cierre cuando el directo la vuelve a pintar. */
function wfDockMenu(v, button) {
    const open = v.root.querySelector('.wf-dock-menu');
    if (open) {
        open.remove();
        return;
    }
    const now = wfDockPrefs().side;
    const menu = el('div', 'wf-dock-menu');
    for (const type of ['mousedown', 'click', 'wheel']) menu.addEventListener(type, e => e.stopPropagation());
    menu.append(el('span', 'wf-dock-title', 'Colocar la ficha'));
    const row = el('div', 'wf-dock-row');
    for (const d of WF_DOCKS) {
        const opt = el('button', 'wf-dock-opt');
        opt.setAttribute('aria-pressed', String(d.id === now));
        const mini = el('span', `wf-dock-mini ${d.id}`);
        mini.append(el('i'));
        opt.append(mini, el('span', null, d.name));
        opt.addEventListener('click', () => {
            menu.remove();
            wfApplyDock(v, wfSaveDock({ side: d.id }));
            requestAnimationFrame(() => wfSpaceChanged(v));
        });
        row.append(opt);
    }
    // Flotante: fuera del lienzo, en su propia ventana, como «Flotante» de su clic derecho; solo en la app, que es quien
    // la abre. No se recuerda como colocación: el lienzo sigue con la de antes.
    if (window.invokeCSharpAction && state.trace && !v.piece && !STEP) {
        const opt = el('button', 'wf-dock-opt');
        opt.setAttribute('aria-pressed', 'false');
        const mini = el('span', 'wf-dock-mini float');
        mini.append(el('i'));
        opt.append(mini, el('span', null, 'Flotante'));
        opt.addEventListener('click', () => {
            menu.remove();
            wfDetach(v);
        });
        row.append(opt);
    }
    menu.append(row, el('p', 'wf-dock-note', 'Acoplada, su borde cambia el tamaño. Flotante, sale del lienzo a su propia ventana, que se puede clavar en un corcho.'));
    const r = v.root.getBoundingClientRect();
    const b = button.getBoundingClientRect();
    menu.style.right = `${Math.max(8, Math.round(r.right - b.right))}px`;
    menu.style.top = `${Math.round(b.bottom - r.top + 6)}px`;
    v.root.append(menu);
}

/** Saca la ficha flotante a su propia ventana, como «Flotante» de su clic derecho, y la cierra en el lienzo. */
function wfDetach(v) {
    const key = v.drawer.dataset.key || '';
    if (!key) return;
    if (key.startsWith('file:')) {
        const [, scope, ...rest] = key.split(':');
        const file = rest.join(':');
        post({ type: 'pieceFile', scope, path: file, tab: state.changes.tab, title: `${file.split('/').pop()} · ${sessionName(state.trace)}`, where: 'window' });
    } else {
        post({ type: 'pieceOpen', piece: 'step', key, interaction: v.drawer.dataset.interaction, title: `${v.drawer.dataset.title || 'Paso'} · ${sessionName(state.trace)}`, where: 'window' });
    }
    wfCloseDetail(v);
}

// Un clic fuera del menú de colocar lo cierra.
document.addEventListener('pointerdown', e => {
    if (!e.target.closest('.wf-dock-menu, .wf-dock-btn')) document.querySelectorAll('.wf-dock-menu').forEach(m => m.remove());
});

/** Botón que despliega la ficha a todo el lienzo o la devuelve a su sitio; vale para cualquier nodo y el tamaño se
 * mantiene al pasar de un nodo a otro, hasta cerrar la ficha. */
function wfSizeButton(d) {
    const button = el('button', 'pbtn small wf-drawer-btn wf-size-btn');
    const icon = svg('svg', { class: 'fit-icon', viewBox: '0 0 16 16', 'aria-hidden': 'true' });
    const path = svg('path', {});
    icon.append(path);
    button.append(icon);
    // La flecha apunta hacia donde crece la ficha, con una raya en el borde del que sale; entera, hacia donde vuelve.
    const arrows = {
        right: ['M2 3v10M14 8H5M8.5 4.5 5 8l3.5 3.5', 'M2 3v10M5 8h9M10.5 4.5 14 8l-3.5 3.5'],
        left: ['M14 3v10M2 8h9M7.5 4.5 11 8l-3.5 3.5', 'M14 3v10M11 8H2M5.5 4.5 2 8l3.5 3.5'],
        bottom: ['M3 2h10M8 14V5M4.5 8.5 8 5l3.5 3.5', 'M3 2h10M8 5v9M4.5 10.5 8 14l3.5-3.5'],
    };
    const show = () => {
        const tall = d.classList.contains('tall');
        const side = d.parentElement && d.parentElement.dataset.dock;
        path.setAttribute('d', (arrows[side] || arrows.bottom)[tall ? 1 : 0]);
        if (side === 'right' || side === 'left') {
            hint(button, '↔', tall ? 'Volver a su sitio' : 'Ver en todo el lienzo', null,
                tall ? 'Deja otra vez el lienzo a la vista al lado de la ficha.' : 'La ficha ocupa todo el lienzo, para ver más de lo que enseña.');
        } else {
            hint(button, '↕', tall ? 'Volver a media altura' : 'Ver a toda la altura', null,
                tall ? 'Deja otra vez el lienzo a la vista encima de la ficha.' : 'La ficha ocupa todo el lienzo, para ver más de lo que enseña.');
        }
    };
    button.refresh = show;
    button.addEventListener('click', () => {
        const from = wfDrawerBox(d);
        d.classList.toggle('tall');
        show();
        wfGrowDrawer(d, from);
    });
    show();
    return button;
}

/** Dónde está la ficha dentro de su lienzo, en píxeles. */
function wfDrawerBox(d) {
    return { left: d.offsetLeft, top: d.offsetTop, width: d.offsetWidth, height: d.offsetHeight };
}

/** Lleva la ficha colocada de su sitio de antes al de ahora, creciendo o encogiendo desde su lado: la de la derecha se
 * abre hacia la izquierda, la de la izquierda hacia la derecha y la de abajo hacia arriba. Al volver a su sitio, el
 * lienzo reencuadra el nodo en lo libre.
 *
 * La entrada desde su lado es una animación de CSS que se repetiría al cambiar de regla: se apaga hasta que la ficha se
 * vuelve a abrir. */
function wfGrowDrawer(d, from) {
    const v = state.wf;
    if (!v || v.drawer !== d || v.piece || STEP || !v.root.dataset.dock) return;
    d.style.animation = 'none';
    const settle = () => {
        if (!d.classList.contains('tall')) wfSpaceChanged(v);
    };
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) {
        settle();
        return;
    }
    const px = box => ({ left: `${box.left}px`, top: `${box.top}px`, width: `${box.width}px`, height: `${box.height}px`, right: 'auto', bottom: 'auto' });
    d.animate([px(from), px(wfDrawerBox(d))], { duration: 280, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)' }).finished.then(settle, () => {});
}

/** Tarjeta de una herramienta con su dibujo, su título y una etiqueta; devuelve la tarjeta y su cuerpo, para llenarlo. */
function wfToolCard(icon, title, tag, cls) {
    const card = el('div', `wf-tool ${cls || ''}`);
    const head = el('div', 'wf-tool-head');
    const mark = el('span', 'wf-tool-icon');
    mark.append(flowIcon(icon, 'flow-icon'));
    head.append(mark, el('span', 'wf-tool-title', title));
    if (tag) head.append(el('span', 'wf-tool-tag', tag));
    const body = el('div', 'wf-tool-body');
    card.append(head, body);
    return { card, body };
}

/** Un texto con su formato de Markdown, en un bloque de lectura. */
function wfMd(text, cls) {
    const box = el('div', `wf-md ${cls || ''}`);
    box.append(markdown(cut(plain(String(text || '')), 8000)));
    return box;
}

/** Fila de etiquetas «clave valor» con lo que no esté vacío. */
function wfChips(pairs) {
    const row = el('div', 'wf-query-where');
    for (const [k, v] of pairs) {
        if (v === undefined || v === null || v === '') continue;
        const c = el('span', 'wf-query-chip');
        c.append(el('b', null, k), String(v));
        row.append(c);
    }
    return row;
}

/** Lo devuelto, como objeto si es JSON; null si no. */
function wfJson(text) {
    try {
        const t = String(text || '').trim();
        return /^[[{]/.test(t) ? JSON.parse(t) : null;
    } catch {
        return null;
    }
}

/** Una nota de cómo acabó: en verde si fue bien, en rojo si no. */
function wfStatus(text, ok) {
    return el('div', `wf-status ${ok ? 'ok' : 'ko'}`, `${ok ? '✓' : '✕'} ${text}`);
}

/** Las herramientas que no son de ficheros ni de terminal, cada una con su aspecto: preguntar a la persona, mandar un
 * mensaje a un agente, entregar el trabajo, parar una tarea, cargar herramientas, vigilar una salida, proponer un plan,
 * apuntar tareas… Devuelve null para las que se pintan de otra forma. */
function wfSpecial(name, input, result, s) {
    const json = wfJson(result);
    switch (name) {
        case 'AskUserQuestion': {
            if (!s.questions) return null;
            const box = el('div', 'wf-ask');
            box.append(ask(s));
            return box;
        }
        case 'SendMessage': {
            const id = input.to || input.recipient;
            const target = id && (agentOf(`agent-${id}`) ? actorName(`agent-${id}`) : id);
            const { card, body } = wfToolCard('send', `Mensaje a ${target || 'un agente'}`, 'SendMessage', 'send');
            if (input.summary) body.append(el('div', 'wf-tool-lead', input.summary));
            body.append(wfMd(input.message || input.content, 'wf-bubble'));
            if (json) body.append(wfStatus(json.success ? 'Le llega y sigue trabajando' : (json.message || 'No se entregó'), json.success));
            return card;
        }
        case 'SubagentHandback': {
            const { card, body } = wfToolCard('handback', 'Entrega el trabajo a quien lo lanzó', 'SubagentHandback', 'send');
            body.append(wfMd(input.message, 'wf-bubble'));
            if (json) body.append(wfStatus(json.success ? 'Entregado' : (json.message || 'No se entregó'), json.success));
            return card;
        }
        case 'TaskStop': {
            const { card, body } = wfToolCard('stop', 'Para una tarea en segundo plano', 'TaskStop', 'stop');
            body.append(wfChips([['Tarea', input.task_id || input.shell_id]]));
            const m = json && /stopped task: \S+ \(([\s\S]*)\)$/.exec(json.message || '');
            if (m) body.append(wfTerminal('Bash', { command: m[1] }, '', false));
            if (json) body.append(wfStatus(json.message ? 'Parada' : 'No se pudo parar', Boolean(json.message && /Success/i.test(json.message))));
            return card;
        }
        case 'ToolSearch': {
            const { card, body } = wfToolCard('toolbox', 'Carga herramientas para usarlas', 'ToolSearch', 'tools');
            const q = String(input.query || '');
            const chips = el('div', 'wf-tool-list');
            if (q.startsWith('select:')) for (const t of q.slice(7).split(',')) chips.append(el('span', 'wf-tool-pill', t.trim().replace(/^mcp__([^_]+)__/, '$1 · ')));
            else chips.append(el('span', 'wf-tool-pill', `busca «${q}»`));
            body.append(chips);
            const loaded = (String(result).match(/<tool_reference>|"name"/g) || []).length;
            if (result) body.append(wfStatus(loaded ? `${loaded} herramienta${loaded === 1 ? '' : 's'} lista${loaded === 1 ? '' : 's'} para usar` : 'Cargadas', true));
            return card;
        }
        case 'Monitor': {
            const { card, body } = wfToolCard('terminal', input.description || 'Vigila una salida', 'Monitor', 'term');
            body.append(wfTerminal('Bash', { command: input.command }, '', false));
            body.append(wfChips([['Hasta', input.timeout_ms ? duration(input.timeout_ms) : '']]));
            if (result) body.append(wfMd(result));
            return card;
        }
        case 'ExitPlanMode': {
            const { card, body } = wfToolCard('plan', 'Propone un plan', 'Plan', 'plan');
            body.append(wfMd(input.plan, 'wf-doc'));
            const said = /the user said:\s*([\s\S]*)$/i.exec(result);
            if (/doesn't want to proceed|rejected/i.test(result)) {
                body.append(wfStatus('No aprobado', false));
                if (said) body.append(wfMd(said[1], 'wf-bubble user'));
            } else if (result) body.append(wfStatus('Aprobado', true));
            return card;
        }
        case 'SendFeedback': {
            const { card, body } = wfToolCard('feedback', input.title || 'Comentario sobre Claude Code', input.type || 'feedback', 'plan');
            body.append(wfMd(input.details, 'wf-doc'));
            return card;
        }
        case 'Artifact': {
            const { card, body } = wfToolCard('artifact', `Artifact · ${input.action || 'publish'}`, 'Artifact', 'plan');
            body.append(wfChips(Object.entries(input).filter(([k, v]) => k !== 'action' && typeof v !== 'object').slice(0, 8).map(([k, v]) => [k, clip(String(v), 80)])));
            if (result) body.append(wfMd(result));
            return card;
        }
        case 'TodoWrite': {
            const { card, body } = wfToolCard('tasks', 'Apunta las tareas', 'TodoWrite', 'tools');
            const list = el('div', 'wf-todos');
            for (const t of input.todos || []) {
                const row = el('div', `wf-todo ${t.status || ''}`);
                row.append(el('span', 'wf-todo-mark', t.status === 'completed' ? '✓' : t.status === 'in_progress' ? '●' : '○'), el('span', null, t.content || t.activeForm || ''));
                list.append(row);
            }
            body.append(list);
            return card;
        }
        case 'WebFetch':
        case 'WebSearch': {
            const { card, body } = wfToolCard('web', name === 'WebFetch' ? 'Lee una página' : 'Busca en internet', name, 'web');
            if (input.url) body.append(wfBrowserBar(input.url, ''));
            if (input.query) body.append(wfChips([['Busca', input.query]]));
            if (input.prompt) body.append(wfChips([['Para saber', clip(input.prompt, 200)]]));
            if (result) body.append(wfMd(result, 'wf-doc'));
            return card;
        }
        default:
            return null;
    }
}

/** La barra de un navegador: su dirección y el título de la página. */
function wfBrowserBar(url, title) {
    const bar = el('div', 'wf-browser');
    const dots = el('span', 'wf-browser-dots');
    dots.append(el('i'), el('i'), el('i'));
    const address = el('span', 'wf-browser-url');
    address.append(flowIcon('web', 'wf-browser-icon'), url);
    bar.append(dots, address);
    if (title) bar.append(el('span', 'wf-browser-title', title));
    return bar;
}

// Un fichero que deja la MCP de navegador en su carpeta (el árbol de la página, los mensajes de la consola) y, si las
// dice, las líneas que interesan: «console-….log#L3-L4».
const WF_MCP_FILE = /\.playwright-mcp[\\/][^\s)\]#]+(?:#L(\d+)(?:-L(\d+))?)?/;

/** Las pestañas abiertas del navegador de la MCP, como las de un navegador, con la actual marcada. Las de una web se
 * abren en el navegador al pulsarlas; las internas («edge://…»), no. */
function wfTabs(text) {
    const list = el('div', 'wf-tabs');
    for (const line of text.split('\n')) {
        const m = /^\s*-\s*\d+:\s*(\(current\)\s*)?\[([^\]]*)\]\(([^)]*)\)/.exec(line);
        if (!m) continue;
        const [, current, title, url] = m;
        const web = /^https?:\/\//i.test(url);
        const tab = el(web ? 'button' : 'div', 'wf-tab' + (current ? ' current' : '') + (web ? '' : ' off'));
        tab.append(flowIcon('web', 'wf-tab-icon'), el('span', 'wf-tab-title', title || url), el('span', 'wf-tab-url', url));
        hint(tab, '🌐', title || url, current ? 'la actual' : null, web ? 'Pulsa para abrirla en tu navegador.' : 'Es una página interna del navegador: no se abre desde aquí.');
        if (web) tab.addEventListener('click', e => {
            e.stopPropagation();
            post({ type: 'link', url });
        });
        list.append(tab);
    }
    return list.childElementCount ? list : null;
}

/** Las líneas de una parte que nombra ficheros de la MCP: cada fichero, en una fila que lo abre; lo demás, como texto. */
function wfMcpLines(text) {
    const names = { Snapshot: 'Árbol de la página', 'New console entries': 'Mensajes nuevos de la consola' };
    return text.split('\n').filter(l => l.trim()).map(line => {
        const file = WF_MCP_FILE.exec(line);
        if (!file) return wfMd(line);
        const label = line.replace(/^\s*-\s*/, '').replace(/\[([^\]]*)\]\([^)]*\)/, '$1').replace(file[0], '').replace(/[\s:]+$/, '').trim();
        return wfMcpFile(names[label] || label || 'Fichero', file[0]);
    });
}

/** Un fichero que dejó la MCP: al pulsarlo se pide a la app y se abre debajo, como la ventana de un editor; al volver a
 * pulsarlo, se cierra. Si la referencia dice unas líneas («#L3-L4»), se enseñan solo esas, con su número. */
function wfMcpFile(label, ref) {
    const m = WF_MCP_FILE.exec(ref);
    const path = ref.replace(/#.*$/, '');
    const from = m && m[1] ? Number(m[1]) : null;
    const to = m && m[2] ? Number(m[2]) : from;
    const box = el('div', 'wf-mcpfile');
    box.dataset.path = path;
    box.dataset.ref = ref;
    box.dataset.label = label;
    const open = state.mcpOpen.has(ref);
    const row = el('button', 'wf-mcpfile-row' + (open ? ' on' : ''));
    row.append(flowIcon('write', 'wf-mcpfile-icon'), el('span', 'wf-mcpfile-label', label), el('span', 'wf-mcpfile-name', path.replace(/\\/g, '/').split('/').pop()), el('span', 'wf-mcpfile-chev', open ? '▾' : '▸'));
    row.addEventListener('click', e => {
        e.stopPropagation();
        if (open) state.mcpOpen.delete(ref);
        else {
            state.mcpOpen.add(ref);
            if (!state.mcpFiles.has(path)) {
                state.mcpFiles.set(path, null);
                post({ type: 'mcpFile', path });
            }
        }
        box.replaceWith(wfMcpFile(label, ref));
    });
    box.append(row);
    if (!open) return box;
    const f = state.mcpFiles.get(path);
    if (!f) box.append(el('div', 'wf-drawer-meta', 'Cargando…'));
    else if (f.error) box.append(wfError(f.error));
    else {
        const lang = wfLang(path);
        const all = String(f.text || '').replace(/\n$/, '').split('\n').map((t, i) => ({ n: String(i + 1), text: t }));
        const lines = from ? all.slice(from - 1, to) : all;
        const note = from ? `líneas ${from}${to !== from ? `–${to}` : ''} de ${all.length}` : f.truncated ? 'cortado: es muy largo' : `${all.length} líneas`;
        box.append(wfFileWin(wfFile(path, lang, note), [lines.length ? wfCode(lines, lang) : el('div', 'wf-drawer-meta', 'Esas líneas ya no están en el fichero.')]));
    }
    return box;
}

/** Pone el fichero de la MCP que acaba de llegar en las filas que lo esperan, sin repintar lo demás. */
function receiveMcpFile(m) {
    state.mcpFiles.set(m.path, m);
    for (const box of document.querySelectorAll('.wf-mcpfile')) if (box.dataset.path === m.path) box.replaceWith(wfMcpFile(box.dataset.label, box.dataset.ref));
}

/** Lo que devuelve una MCP de navegador por partes («### Page», «### Ran Playwright code», «### Result», «### Error»…):
 * la página como una barra de navegador, el código que corrió con sus colores, el resultado y el error, cada uno con
 * su aspecto; lo demás, con su formato. */
function wfMcpSections(result) {
    const box = el('div', 'wf-mcp-sections');
    const parts = String(result).split(/^### /m).filter(p => p.trim());
    if (parts.length < 2 && !/^### /m.test(result)) return null;
    for (const p of parts) {
        const nl = p.indexOf('\n');
        const title = (nl < 0 ? p : p.slice(0, nl)).trim();
        const text = nl < 0 ? '' : p.slice(nl + 1).trim();
        if (!text) continue;
        if (/^Page$/i.test(title)) {
            const url = (/Page URL:\s*(\S+)/.exec(text) || [])[1];
            const name = (/Page Title:\s*(.+)/.exec(text) || [])[1];
            if (url) {
                box.append(wfBrowserBar(url, name || ''));
                continue;
            }
        }
        const fence = /```(\w*)\n([\s\S]*?)```/.exec(text);
        const section = el('div', 'wf-mcp-section');
        section.append(el('div', 'wf-agent-label', { 'Ran Playwright code': 'Código que corrió', Result: 'Resultado', Error: 'Error', Snapshot: 'Lo que hay en la página', Events: 'Avisos', 'Console messages': 'Consola', 'Open tabs': 'Pestañas abiertas' }[title] || title));
        const tabs = /^Open tabs$/i.test(title) && wfTabs(text);
        if (tabs) section.append(tabs);
        else if (WF_MCP_FILE.test(text)) section.append(...wfMcpLines(text));
        else if (/^Error$/i.test(title)) section.append(wfError(text));
        else if (fence) {
            const lang = { js: WF_LANGS.js, javascript: WF_LANGS.js, yaml: WF_LANGS.yaml, json: WF_LANGS.json }[fence[1]] || WF_LANGS.txt;
            const lines = fence[2].replace(/\n$/, '').split('\n');
            section.append(wfCode(lines.slice(0, 120).map(t => ({ text: t })), lang));
        } else if (wfJson(text)) section.append(wfCode(JSON.stringify(wfJson(text), null, 2).split('\n').map(t => ({ text: t })), WF_LANGS.json));
        else section.append(wfMd(text));
        box.append(section);
    }
    return box;
}

/** Lo que hizo una llamada, con el aspecto de lo que es: una terminal para una orden, el código de su lenguaje para una
 * lectura, un diff como el de un editor para un cambio, la imagen si se leyó una. Mientras llega de la app, «Cargando».
 *
 * Sirve al detalle del directo y al chat. */
function wfCall(s) {
    const box = el('div', 'wf-call-body');
    const detail = state.details.get(s.order);
    if (s.hasDetail && !detail) {
        box.append(el('div', 'faint', 'Cargando…'));
        return box;
    }
    const input = callInput(s) || {};
    const result = detail && detail.result ? String(detail.result) : '';
    const name = s.kind === 'skill' ? 'Skill' : s.title.split(' · ')[0];
    const special = wfSpecial(name, input, result, s);
    if (special) {
        box.append(special);
        return box;
    }
    switch (toolKind(name)) {
        case 'terminal':
            box.append(wfTerminal(name, input, result, s.isError));
            break;
        case 'read': {
            const lang = wfLang(input.file_path);
            if (s.images) box.append(wfFileWin(wfFile(input.file_path, null, null), [wfPictures(s)]));
            else box.append(wfFileWin(wfFile(input.file_path, lang, input.offset ? `desde la línea ${input.offset}` : null), [wfSource(s, wfNumbered(result), lang)]));
            break;
        }
        case 'edit': {
            const lang = wfLang(input.file_path);
            const parts = (input.edits || [input]).map(e => wfDiff(e.old_string || '', e.new_string || '', lang));
            if (s.isError && result) parts.push(wfError(result));
            box.append(wfFileWin(wfFile(input.file_path, lang, parts.length > 1 ? `${parts.length} cambios` : null), parts));
            break;
        }
        case 'write': {
            const lang = wfLang(input.file_path);
            box.append(wfFileWin(wfFile(input.file_path, lang, 'fichero entero'), [wfDiff('', input.content || '', lang)]));
            break;
        }
        case 'search':
            box.append(wfQuery(name, input), wfMatches(result, input));
            break;
        case 'plug':
            box.append(wfMcp(name, input, result, s));
            break;
        case 'skill': {
            const card = el('div', 'wf-skill');
            const head = el('div', 'wf-skill-head');
            const icon = el('span', 'wf-skill-icon');
            icon.append(flowIcon('skill', 'flow-icon'));
            head.append(icon, el('span', 'wf-skill-name', input.skill || s.title), el('span', 'wf-skill-kind', 'skill'));
            card.append(head);
            if (input.args) card.append(el('div', 'wf-skill-args', input.args));
            if (result) {
                const out = el('div', 'wf-skill-out');
                out.append(markdown(cut(result, 6000)));
                card.append(out);
            }
            box.append(card);
            break;
        }
        default: {
            const entries = Object.entries(input);
            for (const [k, value] of entries.slice(0, 8)) {
                const row = el('div', 'wf-field');
                const text = typeof value === 'object' ? JSON.stringify(value, null, 2) : String(value);
                row.append(el('span', 'wf-field-key', k), el(text.length > 80 ? 'pre' : 'span', 'wf-field-val', text));
                box.append(row);
            }
            if (!entries.length && detail && detail.text) box.append(wfCode(String(detail.text).split('\n').map(t => ({ text: t })), WF_LANGS.json));
            if (result) box.append(s.isError ? wfError(result) : wfCode(result.split('\n').map(t => ({ text: t })), wfGuess(result)));
            if (s.images) box.append(wfPictures(s));
        }
    }
    return box;
}

// Lo que se enseña como mucho de un código o una salida: más largo, entero, está en el chat.
const WF_LINES = 400;

/** Cabecera de un fichero: su nombre destacado, su carpeta, el lenguaje en que está y una nota a la derecha. */
function wfFile(path, lang, note) {
    const head = el('div', 'wf-file');
    const p = String(path || '').replace(/\\/g, '/');
    const cut = p.lastIndexOf('/');
    head.append(flowIcon('write', 'wf-file-icon'), el('span', 'wf-file-name', cut >= 0 ? p.slice(cut + 1) : p), el('span', 'wf-file-dir', cut >= 0 ? p.slice(0, cut) : ''));
    if (note) head.append(el('span', 'wf-file-note', note));
    if (lang && lang.name) {
        const badge = el('button', `wf-lang lang-${lang.id}`);
        badge.dataset.lang = lang.id;
        badge.dataset.name = lang.name;
        badge.append(lang.name, el('span', 'wf-lang-caret', '▾'));
        hint(badge, '◐', `Colores de ${lang.name}: ${codeThemeOf(lang).name}`, null, 'Púlsalo para cambiar el tema de este lenguaje. Todos los lenguajes, en Configuración → Código.');
        badge.addEventListener('click', e => {
            e.stopPropagation();
            codeThemeMenu(badge, lang);
        });
        head.append(badge);
    }
    return head;
}

/** Ventana de un fichero, como la de un editor: su cabecera arriba, que no se mueve, y lo de dentro debajo. */
function wfFileWin(head, parts) {
    const win = el('div', 'wf-filewin');
    // El marco toma el tema del código que lleva dentro.
    const code = parts.find(p => p.dataset && p.dataset.ct) || parts.map(p => p.querySelector && p.querySelector('[data-ct]')).find(Boolean);
    if (code) {
        win.dataset.ct = code.dataset.ct;
        if (code.dataset.lang) win.dataset.lang = code.dataset.lang;
    }
    const body = el('div', 'wf-filewin-body');
    body.append(...parts);
    win.append(head, body);
    return win;
}

/** Lo leído de un fichero: un Markdown, como se ve una vez escrito, con un botón para ver su código; lo demás, su código. */
function wfSource(s, lines, lang) {
    if (lang.id !== 'md') return wfCode(lines, lang);
    const raw = state.wfRaw.has(s.order);
    const box = el('div', 'wf-source');
    const switcher = el('div', 'segmented wf-switch');
    for (const [key, label] of [['view', 'Vista'], ['code', 'Código']]) {
        const chip = el('button', 'chip' + ((key === 'code') === raw ? ' on' : ''), label);
        chip.addEventListener('click', e => {
            e.stopPropagation();
            if (key === 'code') state.wfRaw.add(s.order);
            else state.wfRaw.delete(s.order);
            const fresh = wfSource(s, lines, lang);
            box.replaceWith(fresh);
        });
        switcher.append(chip);
    }
    box.append(switcher);
    if (raw) box.append(wfCode(lines, lang));
    else {
        const md = el('div', 'wf-md wf-preview');
        md.append(markdown(lines.map(l => l.text).join('\n')));
        box.append(md);
    }
    return box;
}

/** Una terminal: su ventana con el nombre de la consola, la orden coloreada tras su indicador y lo que contestó, en rojo si falló. */
function wfTerminal(name, input, result, failed) {
    const ps = name === 'PowerShell';
    const term = el('div', 'wf-term');
    const bar = el('div', 'wf-term-bar');
    bar.append(el('i'), el('i'), el('i'), el('span', 'wf-term-title', ps ? 'Windows PowerShell' : name === 'Consola' ? 'consola' : 'bash'));
    if (input.description) bar.append(el('span', 'wf-term-what', input.description));
    const screen = el('pre', 'wf-term-screen');
    const lang = ps ? WF_LANGS.ps : WF_LANGS.sh;
    String(input.command || '').split('\n').forEach((text, i) => {
        const line = el('span', 'wf-term-line');
        line.append(el('span', 'wf-term-prompt', i === 0 ? (ps ? 'PS> ' : '$ ') : '>> '), wfHighlight(text, lang));
        screen.append(line, '\n');
    });
    if (result) {
        const lines = result.split('\n');
        screen.append(el('span', failed ? 'wf-term-err' : 'wf-term-out', lines.slice(0, WF_LINES).join('\n') + (lines.length > WF_LINES ? `\n… ${lines.length - WF_LINES} líneas más` : '')));
    }
    term.append(bar, screen);
    return term;
}

/** Líneas de lo que devuelve una lectura, con su número: Claude Code las numera al leerlas («  12→código»). */
function wfNumbered(result) {
    return String(result || '').split('\n').map(t => {
        const m = /^\s*(\d+)[\t→](.*)$/.exec(t);
        return m ? { n: m[1], text: m[2] } : { text: t };
    });
}

/** Código con sus números de línea y los colores de su lenguaje, desplazable de lado. */
function wfCode(lines, lang) {
    const view = wfCodeView('wf-code', lang);
    const paint = wfPainter(lang);
    for (const l of lines.slice(0, WF_LINES)) {
        const row = el('div', 'wf-code-line');
        row.append(el('span', 'wf-code-num', l.n || ''), paint(l.text));
        view.append(row);
    }
    if (lines.length > WF_LINES) view.append(el('div', 'wf-code-more', `… ${lines.length - WF_LINES} líneas más, enteras en el chat`));
    return view;
}

/** Lo que cambia de un texto a otro, línea a línea, como el diff de un editor: lo que se quita en rojo y lo que entra en verde. */
function wfDiff(before, after, lang) {
    const a = before.split('\n');
    const b = after ? after.split('\n') : [];
    const view = wfCodeView('wf-code wf-diff', lang);
    const paint = wfPainter(lang);
    let shown = 0;
    for (const d of before ? wfPairs(wfLineDiff(a, b)) : b.map(text => ({ op: '+', text }))) {
        if (shown++ >= WF_LINES) {
            view.append(el('div', 'wf-code-more', 'Sigue en el chat'));
            break;
        }
        const row = el('div', `wf-code-line ${d.op === '+' ? 'add' : d.op === '-' ? 'del' : ''}`);
        row.append(el('span', 'wf-code-num', d.op === ' ' ? '' : d.op), d.mark ? wfMarked(d.text, d.mark, d.op) : paint(d.text));
        view.append(row);
    }
    return view;
}

/** Una línea cambiada con su trozo distinto resaltado, como hacen los editores en una línea que se ha tocado. */
function wfMarked(text, [from, to], op) {
    const line = wfIndented(text);
    line.append(text.slice(0, from), el('span', op === '+' ? 'chg-add' : 'chg-del', text.slice(from, to)), text.slice(to));
    return line;
}

/** Crea el elemento de una línea de código con su sangría anotada, en caracteres.
 *
 * Con el código partido en una ficha estrecha, lo que no cabe sigue debajo, a la altura de esa sangría y un poco más
 * adentro: sin ello, la continuación empezaba en la columna cero y no se veía dónde empezaba cada línea. */
function wfIndented(text) {
    const line = el('span', 'wf-code-text');
    const lead = /^[ \t]*/.exec(text)[0].replace(/\t/g, '    ').length;
    if (lead) line.style.setProperty('--ind', `${lead}ch`);
    return line;
}

/** Marca en cada línea quitada y la añadida que la sustituye el trozo que cambia: lo que no comparten por delante ni por detrás. */
function wfPairs(diff) {
    for (let i = 0; i < diff.length; i++) {
        if (diff[i].op !== '-') continue;
        let j = i;
        while (j < diff.length && diff[j].op === '-') j++;
        let k = j;
        while (k < diff.length && diff[k].op === '+') k++;
        for (let p = 0; p < Math.min(j - i, k - j); p++) {
            const a = diff[i + p].text;
            const b = diff[j + p].text;
            let s = 0;
            while (s < a.length && s < b.length && a[s] === b[s]) s++;
            let e = 0;
            while (e < a.length - s && e < b.length - s && a[a.length - 1 - e] === b[b.length - 1 - e]) e++;
            // Solo si se parecen: si cambia casi todo, resaltar el trozo no ayuda.
            if (s + e < Math.min(a.length, b.length) * 0.3) continue;
            diff[i + p].mark = [s, a.length - e];
            diff[j + p].mark = [s, b.length - e];
        }
        i = k - 1;
    }
    return diff;
}

/** Diff de líneas por la subsecuencia común más larga: las que siguen, iguales; las demás, quitadas o añadidas. */
function wfLineDiff(a, b) {
    const n = a.length;
    const m = b.length;
    // Demasiado grande para compararlo entero: todo lo viejo fuera y todo lo nuevo dentro.
    if (n * m > 400000) return [...a.map(text => ({ op: '-', text })), ...b.map(text => ({ op: '+', text }))];
    const len = Array.from({ length: n + 1 }, () => new Uint16Array(m + 1));
    for (let i = n - 1; i >= 0; i--) for (let j = m - 1; j >= 0; j--) len[i][j] = a[i] === b[j] ? len[i + 1][j + 1] + 1 : Math.max(len[i + 1][j], len[i][j + 1]);
    const out = [];
    let i = 0;
    let j = 0;
    while (i < n && j < m) {
        if (a[i] === b[j]) {
            out.push({ op: ' ', text: a[i++] });
            j++;
        } else if (len[i + 1][j] >= len[i][j + 1]) out.push({ op: '-', text: a[i++] });
        else out.push({ op: '+', text: b[j++] });
    }
    while (i < n) out.push({ op: '-', text: a[i++] });
    while (j < m) out.push({ op: '+', text: b[j++] });
    return out;
}

/** Una llamada MCP: su servidor y su herramienta en la cabecera, lo que se le pasó como una tabla y lo que devolvió
 * con su formato: JSON con colores, Markdown escrito o texto, y las capturas si las hay. */
function wfMcp(name, input, result, s) {
    const mcp = mcpServer(name) || { server: '', tool: name };
    const card = el('div', 'wf-mcp');
    const head = el('div', 'wf-mcp-head');
    const icon = el('span', 'wf-mcp-icon');
    icon.append(flowIcon(toolIcon(name), 'flow-icon'));
    head.append(icon, el('span', 'wf-mcp-tool', mcp.tool.replace(/_/g, ' ')), el('span', 'wf-mcp-server', `MCP · ${mcp.server}`));
    card.append(head);
    const code = input.function || input.code;
    if (code) {
        const part2 = el('div', 'wf-mcp-out');
        part2.append(el('div', 'wf-agent-label', 'Lo que corre en la página'), wfCode(String(code).split('\n').map(text => ({ text })), WF_LANGS.js));
        card.append(part2);
    }
    if (input.url && mcp.tool === 'browser_navigate' && !/^### Page/m.test(result || '')) {
        const part2 = el('div', 'wf-mcp-out');
        part2.append(wfBrowserBar(input.url, ''));
        card.append(part2);
    }
    const entries = Object.entries(input).filter(([k]) => k !== 'function' && k !== 'code' && !(k === 'url' && mcp.tool === 'browser_navigate'));
    if (entries.length) {
        const table = el('div', 'wf-mcp-args');
        for (const [k, value] of entries.slice(0, 12)) {
            const text = typeof value === 'object' ? JSON.stringify(value, null, 2) : String(value);
            table.append(el('span', 'wf-mcp-key', k), el(text.length > 60 || text.includes('\n') ? 'pre' : 'span', 'wf-mcp-val', text));
        }
        card.append(table);
    }
    if (result) {
        const out = el('div', 'wf-mcp-out');
        let json = null;
        try {
            json = /^[[{]/.test(result.trim()) ? JSON.parse(result) : null;
        } catch {
            json = null;
        }
        const sections = !s.isError && wfMcpSections(result);
        if (sections) out.append(sections);
        else if (s.isError) out.append(wfError(result));
        else if (json) out.append(wfCode(JSON.stringify(json, null, 2).split('\n').map(text => ({ text })), WF_LANGS.json));
        else {
            const md = el('div', 'wf-md');
            md.append(markdown(cut(result, 8000)));
            out.append(md);
        }
        card.append(out);
    }
    if (s.images) card.append(wfPictures(s));
    return card;
}

/** Lo que pide una búsqueda, en una tarjeta: el patrón como en un buscador, dónde busca, en qué ficheros y cómo. */
function wfQuery(name, input) {
    const card = el('div', 'wf-query');
    const bar = el('div', 'wf-query-bar');
    bar.append(el('span', 'wf-query-label', name === 'Glob' ? 'Ficheros' : 'Busca'), flowIcon('search', 'wf-query-icon'), el('span', 'wf-query-pattern', input.pattern || input.query || ''));
    if (input['-i']) bar.append(el('span', 'wf-query-flag', 'Aa'));
    if (name === 'Grep') bar.append(el('span', 'wf-query-flag', 'regex'));
    card.append(bar);
    const where = el('div', 'wf-query-where');
    const chip = (label, value) => {
        if (!value) return;
        const c = el('span', 'wf-query-chip');
        c.append(el('b', null, label), String(value));
        where.append(c);
    };
    chip('Dónde', input.path ? String(input.path).replace(/\\/g, '/').replace(/^.*\/(source\/repos|src)\//i, '…/') : '');
    chip('Solo', input.glob || input.type);
    chip('Devuelve', { files_with_matches: 'los ficheros', content: 'las líneas', count: 'cuántas hay' }[input.output_mode] || (name === 'Grep' ? 'los ficheros' : ''));
    chip('Contexto', input['-C'] || input['-A'] || input['-B'] ? `${input['-C'] || input['-A'] || input['-B']} líneas alrededor` : '');
    if (where.childElementCount) card.append(where);
    return card;
}

/** Lee lo que devuelve una búsqueda, en cualquiera de sus formas: una lista de ficheros, cuántas veces sale en cada uno,
 * o sus líneas («fichero:12:texto» y, si se pidió contexto, «fichero-13-texto» alrededor, con «--» entre trozos); y, si se
 * buscó en un solo fichero, las líneas sin su nombre («12:texto»). */
function wfParseHits(lines, input) {
    const files = new Map();
    const only = input && input.path && /\.[\w]+$/.test(input.path) ? input.path : null;
    const file = path => {
        if (!files.has(path)) files.set(path, { path, lines: [], count: null });
        return files.get(path);
    };
    let last = null;
    for (const t of lines) {
        if (t === '--') {
            if (last) last.lines.push({ gap: true });
            continue;
        }
        let m = /^(.+?\.[\w]+)([:-])(\d+)\2(.*)$/.exec(t);
        if (m) {
            last = file(m[1]);
            last.lines.push({ n: m[3], text: m[4], hit: m[2] === ':' });
            continue;
        }
        m = only && /^(\d+)([:-])(.*)$/.exec(t);
        if (m) {
            last = file(only);
            last.lines.push({ n: m[1], text: m[3], hit: m[2] === ':' });
            continue;
        }
        m = /^(.+?):(\d+)$/.exec(t);
        if (m && input && input.output_mode === 'count') {
            file(m[1]).count = Number(m[2]);
            continue;
        }
        last = file(t.trim());
    }
    return [...files.values()];
}

/** Cómo se resalta lo buscado: como la expresión que se buscó; si no es válida en la página, o no encuentra nada en una
 * línea que sí coincidió, por los trozos de texto de la expresión, sin distinguir mayúsculas. */
function wfHitRules(input) {
    const pattern = input && (input.pattern || input.query);
    if (!pattern) return [];
    const rules = [];
    try {
        rules.push(new RegExp(pattern, input['-i'] ? 'gi' : 'g'));
    } catch {
        // Lo siguiente lo cubre.
    }
    const words = pattern.split('|')
        .map(p => p.replace(/\\(.)/g, '$1').replace(/\[[^\]]*\]|[(){}*+?^$]|\.\*/g, ' ').trim())
        .flatMap(p => p.split(/\s{2,}/)).filter(w => w.length >= 2);
    if (words.length) rules.push(new RegExp(words.map(w => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|'), 'gi'));
    return rules;
}

/** Una línea de un resultado con lo buscado resaltado, probando cada forma de buscarlo hasta que una lo encuentra. */
function wfHitText(text, rules) {
    const out = el('span', 'wf-hit-text');
    for (const rule of rules) {
        rule.lastIndex = 0;
        const found = [...text.matchAll(rule)].filter(m => m[0]);
        if (!found.length) continue;
        let last = 0;
        for (const m of found) {
            if (m.index > last) out.append(text.slice(last, m.index));
            out.append(el('mark', 'wf-hit-mark', m[0]));
            last = m.index + m[0].length;
        }
        out.append(text.slice(last));
        return out;
    }
    out.append(text);
    return out;
}

/** Resultados de una búsqueda: cuánto se encontró y, por fichero, su nombre, su carpeta y sus líneas, con las que
 * coinciden marcadas y lo buscado resaltado, y las de contexto más apagadas. Sin desplazamiento de lado. */
function wfMatches(result, input) {
    const list = el('div', 'wf-hits');
    const lines = String(result || '').split('\n').filter(t => t.trim() && !/^Found \d+ /.test(t) && !/^No (files|matches) found/i.test(t));
    if (!lines.length) {
        list.append(el('div', 'wf-hits-none', 'No se encontró nada'));
        return list;
    }
    const files = wfParseHits(lines.slice(0, WF_LINES), input);
    const rules = wfHitRules(input);
    const hits = files.reduce((sum, f) => sum + (f.count ?? f.lines.filter(l => l.hit).length), 0);
    const summary = el('div', 'wf-hits-sum');
    summary.append(el('b', null, hits ? `${hits} coincidencia${hits === 1 ? '' : 's'}` : `${files.length} fichero${files.length === 1 ? '' : 's'}`),
        hits ? ` en ${files.length} fichero${files.length === 1 ? '' : 's'}` : ' con coincidencias');
    list.append(summary);
    for (const f of files) {
        const box = el('div', 'wf-hit-file' + (f.lines.length ? '' : ' bare'));
        const p = f.path.replace(/\\/g, '/');
        const cut = p.lastIndexOf('/');
        const head = el('div', 'wf-hit-head');
        head.append(flowIcon('write', 'wf-file-icon'), el('span', 'wf-file-name', cut >= 0 ? p.slice(cut + 1) : p), el('span', 'wf-file-dir', cut >= 0 ? p.slice(0, cut) : ''));
        const n = f.count ?? f.lines.filter(l => l.hit).length;
        if (n) head.append(el('span', 'wf-hit-count', String(n)));
        box.append(head);
        for (const l of f.lines) {
            if (l.gap) {
                box.append(el('div', 'wf-hit-gap', '⋯'));
                continue;
            }
            const row = el('div', 'wf-hit-line' + (l.hit ? ' hit' : ' ctx'));
            row.append(el('span', 'wf-code-num', l.n), l.hit ? wfHitText(l.text, rules) : el('span', 'wf-hit-text', l.text));
            box.append(row);
        }
        list.append(box);
    }
    if (lines.length > WF_LINES) list.append(el('div', 'wf-hits-none', `… ${lines.length - WF_LINES} líneas más`));
    return list;
}

/** El error de una llamada, en rojo. */
function wfError(text) {
    return el('pre', 'wf-error', clip(String(text), 3000));
}

/** Las imágenes de una llamada, grandes; al pulsarlas, a pantalla completa. */
function wfPictures(s) {
    const g = gallery(s);
    g.classList.add('wf-pictures');
    return g;
}

// LOS COLORES DEL CÓDIGO

// Los lenguajes, por su id, y sus extensiones, del catálogo que manda la app (Web/codigo.json); los temas, con el elegido
// para cada lenguaje. Hasta que llega el catálogo, el código se ve sin colores.
const WF_LANGS = {};
const WF_EXT = {};
const CODE = { themes: [], chosen: {} };
// Texto sin colores: lo que se pinta si aún no ha llegado el catálogo.
const WF_PLAIN = { id: 'txt', name: '', engine: 'plain', keys: new Set(), control: new Set() };

// Delante de un tipo: lo que sigue a estas palabras lo es, aunque lleve paréntesis («new Foo()» no llama a un método).
const WF_TYPE_AFTER = new Set(['new', 'class', 'struct', 'record', 'interface', 'enum', 'is', 'as', 'typeof', 'extends', 'implements']);
// En Razor, lo que tras «@» abre un bloque de C# o es una directiva de la página.
const RAZOR_BLOCKS = new Set(['if', 'foreach', 'for', 'while', 'do', 'switch', 'using', 'try', 'lock']);
const RAZOR_KEYWORDS = new Set(['code', 'functions', 'page', 'model', 'inject', 'inherits', 'layout', 'namespace', 'attribute', 'implements',
    'addTagHelper', 'removeTagHelper', 'tagHelperPrefix', 'section', 'typeparam', 'rendermode', 'preservewhitespace']);

/** Palabras de un lenguaje, en un conjunto; en los que no distinguen mayúsculas, en minúsculas. */
function wfWords(list, nocase) {
    return new Set(String(list || '').split(/\s+/).filter(Boolean).map(w => (nocase ? w.toLowerCase() : w)));
}

/** Recibe los lenguajes y los temas del código y lo elegido para cada lenguaje, y vuelve a pintar el código a la vista. */
function receiveCode(m) {
    const catalog = m.catalog || {};
    for (const key of Object.keys(WF_LANGS)) delete WF_LANGS[key];
    for (const key of Object.keys(WF_EXT)) delete WF_EXT[key];
    for (const l of catalog.languages || []) {
        WF_LANGS[l.id] = { ...l, keys: wfWords(l.keywords, l.nocase), control: wfWords(l.control, l.nocase) };
        for (const ext of l.extensions || []) WF_EXT[String(ext).toLowerCase()] = l.id;
    }
    const first = !CODE.themes.length;
    CODE.themes = catalog.themes || [];
    CODE.chosen = m.chosen || {};
    // La primera vez se pinta lo que hubiera sin colores; después, un cambio de tema solo cambia el de lo que se ve.
    if (first) wfRepaintCode();
    else wfRetheme();
}

/** Pone a todo el código a la vista el tema de su lenguaje, sin volver a pintarlo: los colores van en variables del tema,
 * así que el cambio es al momento, también en el fichero que se está leyendo. */
function wfRetheme() {
    for (const node of document.querySelectorAll('[data-lang]')) {
        const lang = WF_LANGS[node.dataset.lang];
        if (lang) node.dataset.ct = codeThemeOf(lang).id;
    }
    for (const badge of document.querySelectorAll('.wf-lang[data-lang]')) {
        const lang = WF_LANGS[badge.dataset.lang];
        if (lang) hint(badge, '◐', `Colores de ${badge.dataset.name || lang.name}: ${codeThemeOf(lang).name}`, null, 'Púlsalo para cambiar el tema de este lenguaje. Todos los lenguajes, en Configuración → Código.');
    }
}

/** Vuelve a pintar el código que se ve: el detalle abierto del directo. */
function wfRepaintCode() {
    const v = state.wf;
    if (v && v.detail && !v.drawer.hidden) wfDetail(v, v.detail);
}

/** Lenguaje de un fichero por su extensión; uno que no se conoce, con los colores generales y su extensión por nombre. */
function wfLang(path) {
    const ext = ((/\.([a-z0-9]+)$/i.exec(String(path || '')) || [])[1] || '').toLowerCase();
    if (WF_EXT[ext] && WF_LANGS[WF_EXT[ext]]) return WF_LANGS[WF_EXT[ext]];
    if (ext && WF_LANGS.other) return { ...WF_LANGS.other, name: ext.toUpperCase() };
    return WF_LANGS.txt || WF_PLAIN;
}

/** Lenguaje de una salida sin fichero, por lo que parece: JSON si lo es; si no, texto. */
function wfGuess(text) {
    const t = String(text).trim();
    return (/^[[{]/.test(t) && /[\]}]$/.test(t) ? WF_LANGS.json : WF_LANGS.txt) || WF_PLAIN;
}

/** El tema del código de un lenguaje: el elegido, el de serie del lenguaje o el de la app. */
function codeThemeOf(lang) {
    const id = lang && (CODE.chosen[lang.id] || lang.defaultTheme);
    return CODE.themes.find(t => t.id === id) || CODE.themes.find(t => t.id === 'app') || { id: 'app', name: 'Como la app' };
}

/** Un visor de código vacío, con el tema de su lenguaje, que es lo que le da los colores. */
function wfCodeView(cls, lang) {
    const view = el('div', cls);
    view.dataset.ct = codeThemeOf(lang).id;
    if (lang) view.dataset.lang = lang.id;
    return view;
}

/** Cambia el tema de un lenguaje desde su cabecera: lo guarda la app y el código a la vista se vuelve a pintar. */
function setCodeTheme(lang, theme) {
    if (theme === (lang.defaultTheme || 'app')) delete CODE.chosen[lang.id];
    else CODE.chosen[lang.id] = theme;
    post({ type: 'codeTheme', lang: lang.id, theme });
    wfRetheme();
}

/** El menú de temas de un lenguaje, bajo su nombre: cada tema con una muestra de sus colores y el actual marcado. */
function codeThemeMenu(anchor, lang) {
    closeCodeThemeMenu();
    const menu = el('div', 'code-menu');
    menu.append(el('div', 'code-menu-title', `Colores de ${lang.id === 'other' ? 'los demás lenguajes' : lang.name}`));
    const current = codeThemeOf(lang).id;
    for (const t of CODE.themes) {
        const item = el('button', 'code-menu-item' + (t.id === current ? ' on' : ''));
        const sample = el('span', 'code-sample');
        sample.dataset.ct = t.id;
        sample.append(el('i', 'tok-key'), el('i', 'tok-fn'), el('i', 'tok-type'), el('i', 'tok-str'), el('i', 'tok-com'));
        item.append(sample, el('span', 'code-menu-name', t.name));
        item.addEventListener('click', e => {
            e.stopPropagation();
            closeCodeThemeMenu();
            setCodeTheme(lang, t.id);
        });
        menu.append(item);
    }
    menu.append(el('div', 'code-menu-foot', 'Todos los lenguajes, en Configuración → Código.'));
    menu.addEventListener('mousedown', e => e.stopPropagation());
    document.body.append(menu);
    const r = anchor.getBoundingClientRect();
    menu.style.top = `${Math.max(8, Math.min(r.bottom + 6, window.innerHeight - menu.offsetHeight - 8))}px`;
    menu.style.left = `${Math.max(8, Math.min(r.right - menu.offsetWidth, window.innerWidth - menu.offsetWidth - 8))}px`;
    state.codeMenu = menu;
}

/** Cierra el menú de temas del código, si está abierto. */
function closeCodeThemeMenu() {
    if (!state.codeMenu) return;
    state.codeMenu.remove();
    state.codeMenu = null;
}

/** Un pintor de líneas de un lenguaje: colorea una línea detrás de otra, y lo que sigue abierto al acabar una (un
 * comentario de varias líneas, una cadena literal, una etiqueta o un bloque de C# en Razor) sigue en la siguiente. */
function wfPainter(lang) {
    const st = {};
    return text => wfPaint(String(text), lang, st);
}

/** Una línea suelta de código con los colores de su lenguaje. */
function wfHighlight(text, lang) {
    return wfPaint(String(text), lang, {});
}

/** Pinta una línea con el motor de su lenguaje. Solo crea elementos de texto: nada de lo leído se interpreta. */
function wfPaint(text, lang, st) {
    const line = wfIndented(text);
    if (!lang || lang.engine === 'plain') line.append(text);
    else if (lang.engine === 'md') wfMarkdownLine(text, line);
    else if (lang.engine === 'markup') wfMarkup(text, lang, st, line, null);
    else if (lang.engine === 'razor') wfRazor(text, lang, st, line);
    else wfCodeLine(text, lang, st, line);
    return line;
}

/** Añade un trozo de texto, en un color si lo tiene. */
function wfPut(out, cls, text) {
    if (!text) return;
    out.append(cls ? el('span', cls, text) : text);
}

/** La expresión que separa los trozos de una línea de un lenguaje de código: su texto se hace una vez por lenguaje y
 * cada línea lleva la suya, porque una cadena interpolada pinta su interior con otra a mitad de línea.
 *
 * Por orden: comentario de línea, comentario de bloque, cadena literal de C# (@"…"), cadena interpolada ($"…"),
 * cadenas, variables con signo, números y palabras. */
function wfCodeRule(lang) {
    if (lang.ruleSource) return new RegExp(lang.ruleSource, 'g');
    const esc = s => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const never = '(\\b\\B)';
    lang.ruleSource = [
        lang.comment ? `(${esc(lang.comment)}.*$)` : never,
        lang.block ? '(\\/\\*)' : never,
        lang.verbatim ? '(\\$?@"|@\\$")' : never,
        lang.verbatim ? '(\\$"(?:[^"\\\\]|\\\\.)*"?)' : never,
        '("(?:[^"\\\\]|\\\\.)*"?|\'(?:[^\'\\\\]|\\\\.)*\'?|`(?:[^`\\\\]|\\\\.)*`?)',
        lang.sigil ? `(\\${lang.sigil}[\\w:]+)` : never,
        '(\\b0x[0-9a-fA-F]+\\b|\\b\\d[\\d_]*(?:\\.\\d+)?(?:[eE][+-]?\\d+)?[fFdDmMlLuU]*\\b)',
        lang.dash ? '(-?[A-Za-z_][\\w-]*)' : '([A-Za-z_]\\w*)',
    ].join('|');
    return new RegExp(lang.ruleSource, 'g');
}

/** Una línea de un lenguaje de código: comentarios (los «///» de C#, con sus etiquetas aparte, como Visual Studio),
 * preprocesador, cadenas, números, palabras clave (las de control aparte), métodos, tipos y variables. */
function wfCodeLine(text, lang, st, out) {
    let i = 0;
    // Lo que sigue abierto de la línea anterior: un comentario de bloque o una cadena literal.
    if (st.block) {
        const end = text.indexOf('*/');
        if (end < 0) return wfPut(out, 'tok-com', text);
        wfPut(out, 'tok-com', text.slice(0, end + 2));
        i = end + 2;
        st.block = false;
    } else if (st.verbatim) {
        const end = wfVerbatimEnd(text, 0);
        if (end < 0) return wfPut(out, 'tok-str', text);
        wfPut(out, 'tok-str', text.slice(0, end));
        i = end;
        st.verbatim = false;
    } else {
        const lead = /^\s*/.exec(text)[0];
        if (lang.doc && text.startsWith(lang.doc, lead.length)) {
            out.append(lead);
            wfPut(out, 'tok-doc', lang.doc);
            for (const part of text.slice(lead.length + lang.doc.length).split(/(<[^>]*>?)/)) wfPut(out, part.startsWith('<') ? 'tok-doc' : 'tok-com', part);
            return;
        }
        if (lang.preproc && /^#\s*[a-z]/.test(text.slice(lead.length))) return wfPut(out, 'tok-pre', text);
    }
    const rule = wfCodeRule(lang);
    rule.lastIndex = i;
    let last = i;
    let m;
    while ((m = rule.exec(text))) {
        if (m.index > last) out.append(text.slice(last, m.index));
        last = m.index + m[0].length;
        if (m[1]) wfPut(out, 'tok-com', m[0]);
        else if (m[2]) {
            const end = text.indexOf('*/', m.index + 2);
            if (end < 0) {
                st.block = true;
                return wfPut(out, 'tok-com', text.slice(m.index));
            }
            wfPut(out, 'tok-com', text.slice(m.index, end + 2));
            last = rule.lastIndex = end + 2;
        } else if (m[3]) {
            const end = wfVerbatimEnd(text, m.index + m[0].length);
            if (end < 0) {
                st.verbatim = true;
                return wfPut(out, 'tok-str', text.slice(m.index));
            }
            wfPut(out, 'tok-str', text.slice(m.index, end));
            last = rule.lastIndex = end;
        } else if (m[4]) wfInterpolated(m[0], lang, out);
        else if (m[5]) wfPut(out, 'tok-str', m[0]);
        else if (m[6]) wfPut(out, 'tok-var', m[0]);
        else if (m[7]) wfPut(out, 'tok-num', m[0]);
        else wfPut(out, wfWordClass(text, m.index, m[0], lang), m[0]);
    }
    if (last < text.length) out.append(text.slice(last));
}

/** Dónde acaba una cadena literal de C# que empieza en una posición: tras su comilla de cierre (las dobles, «""», son una
 * comilla escrita); -1 si sigue en la línea siguiente. */
function wfVerbatimEnd(text, from) {
    for (let i = from; i < text.length; i++) {
        if (text[i] !== '"') continue;
        if (text[i + 1] === '"') i++;
        else return i + 1;
    }
    return -1;
}

/** Una cadena interpolada de C#: el texto como cadena y lo que va entre llaves, como código. */
function wfInterpolated(s, lang, out) {
    let text = '';
    const flush = () => {
        wfPut(out, 'tok-str', text);
        text = '';
    };
    for (let i = 0; i < s.length;) {
        if (s[i] === '{' && s[i + 1] === '{') {
            text += '{{';
            i += 2;
        } else if (s[i] === '{' && i > 1) {
            let depth = 1;
            let j = i + 1;
            for (; j < s.length && depth; j++) {
                if (s[j] === '{') depth++;
                else if (s[j] === '}') depth--;
            }
            flush();
            out.append('{');
            wfCodeLine(s.slice(i + 1, depth ? j : j - 1), lang, {}, out);
            if (!depth) out.append('}');
            i = j;
        } else text += s[i++];
    }
    flush();
}

/** El color de una palabra por lo que es y por lo que la rodea: palabra clave, de control, método, tipo o nada. */
function wfWordClass(text, at, word, lang) {
    const w = lang.nocase ? word.toLowerCase() : word;
    if (lang.control.has(w)) return 'tok-ctl';
    if (lang.keys.has(w)) return 'tok-key';
    const before = text.slice(0, at);
    const after = text.slice(at + word.length);
    const typed = lang.types !== false;
    const prev = (/(\w+)\s*$/.exec(before) || [])[1];
    if (typed && prev && WF_TYPE_AFTER.has(prev)) return 'tok-type';
    // Un atributo de C#, «[Fact]», o uno de una lista, «[Theory, InlineData(1)]», es un tipo aunque lleve paréntesis.
    if (typed && lang.verbatim && /^\s*\[(?:[^\]]*,\s*)?$/.test(before)) return 'tok-type';
    if (lang.methods !== false && /^\s*(?:<[\w\s,.<>?[\]]*>)?\s*\(/.test(after)) return 'tok-fn';
    // Un nombre que se declara o se usa como valor («Limite = 100», «Take(Limite)») no es un tipo, aunque vaya con mayúscula.
    if (/^\s*(?:=(?!=)|;|,|\)|=>)/.test(after)) return null;
    // Con mayúscula y sin punto delante, un tipo («List<string>», «Path.Combine»); detrás de un punto, un miembro.
    return typed && /^[A-Z]/.test(word) && /[a-z]/.test(word) && !/\.\s*$/.test(before) ? 'tok-type' : null;
}

/** Una línea de un lenguaje de marcas (HTML, XML, XAML): delimitadores, etiquetas, atributos, valores y comentarios, con
 * las etiquetas y los comentarios que siguen en la línea siguiente. En Razor, cada «@» del contenido lo atiende onAt. */
function wfMarkup(text, lang, st, out, onAt) {
    let i = 0;
    while (i < text.length) {
        if (st.comment) {
            const end = text.indexOf(st.comment, i);
            const stop = end < 0 ? text.length : end + st.comment.length;
            wfPut(out, 'tok-com', text.slice(i, stop));
            if (end >= 0) st.comment = null;
            i = stop;
            continue;
        }
        if (st.tag) {
            const piece = /\s+|\/?>|=|"[^"]*"?|'[^']*'?|[\w:.@-]+|./y;
            piece.lastIndex = i;
            const t = piece.exec(text)[0];
            if (t === '>' || t === '/>') {
                wfPut(out, 'tok-delim', t);
                st.tag = false;
            } else if (t === '=') wfPut(out, 'tok-delim', t);
            else if (t[0] === '"' || t[0] === "'") wfPut(out, 'tok-attrval', t);
            else if (/^[\w:.@-]/.test(t)) wfPut(out, 'tok-attr', t);
            else out.append(t);
            i += t.length;
            continue;
        }
        const lt = text.indexOf('<', i);
        const at = onAt ? text.indexOf('@', i) : -1;
        const next = Math.min(lt < 0 ? text.length : lt, at < 0 ? text.length : at);
        if (next > i) out.append(text.slice(i, next));
        i = next;
        if (i >= text.length) break;
        if (i === at) {
            i = onAt(text, i, out);
            continue;
        }
        if (text.startsWith('<!--', i)) {
            st.comment = '-->';
            continue;
        }
        const open = /<\/?[A-Za-z_][\w:.-]*|<[!?][\w-]*/y;
        open.lastIndex = i;
        const tag = open.exec(text);
        // Un «<» suelto, como el de una comparación, no abre ninguna etiqueta.
        if (!tag) {
            out.append('<');
            i++;
            continue;
        }
        const delim = /^<\/?|^<[!?]/.exec(tag[0])[0];
        wfPut(out, 'tok-delim', delim);
        wfPut(out, 'tok-tag', tag[0].slice(delim.length));
        st.tag = true;
        i += tag[0].length;
    }
}

/** Una línea de Razor: HTML con sus «@», y dentro de un bloque de C# («@{ }», «@if { }», «@code { }»), C#, salvo las
 * líneas que empiezan por una etiqueta, que vuelven a ser HTML. */
function wfRazor(text, lang, st, out) {
    const cs = WF_LANGS[lang.code || 'cs'] || WF_LANGS.other;
    st.cs = st.cs || {};
    const trimmed = text.trimStart();
    // La llave de un «@if (…)» puede ir en la línea siguiente, y un «else» o un «catch» siguen el bloque que acaba de cerrar.
    const opens = st.pending && trimmed.startsWith('{');
    const follows = !st.depth && /^(?:else|catch|finally)\b/.test(trimmed);
    st.pending = false;
    if (!st.comment && !st.tag && (opens || follows || (st.depth > 0 && !trimmed.startsWith('<')))) {
        // Dentro de un bloque, «@foreach» o «@if» llevan su «@» de Razor delante del C#.
        const lead = text.length - trimmed.length;
        if (trimmed.startsWith('@') && !trimmed.startsWith('@@')) {
            out.append(text.slice(0, lead));
            wfPut(out, 'tok-razor', '@');
            wfRazorCode(text, lead + 1, cs, st, out);
        } else wfRazorCode(text, 0, cs, st, out);
        return;
    }
    wfMarkup(text, lang, st, out, (t, at, o) => wfRazorAt(t, at, cs, st, o));
}

/** Pinta como C# desde una posición hasta el final de la línea y lleva la cuenta de las llaves del bloque de Razor. */
function wfRazorCode(text, from, cs, st, out) {
    const code = text.slice(from);
    wfCodeLine(code, cs, st.cs, out);
    const bare = code.replace(/"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|\/\/.*$/g, '');
    for (const c of bare) {
        if (c === '{') st.depth = (st.depth || 0) + 1;
        else if (c === '}') st.depth = Math.max(0, (st.depth || 0) - 1);
    }
    // Un «if (…)», un «foreach (…)» o un «else» sin su llave la llevan en la línea siguiente.
    st.pending = /^\s*(?:(?:if|foreach|for|while|switch|using|lock|catch)\b.*\)|else(?:\s+if\b.*\))?|try|finally|do)\s*$/.test(bare);
}

/** Lo que empieza en una «@» de Razor: un comentario «@* *@», un bloque o una directiva (el resto de la línea, como C#),
 * una expresión «@(…)» o «@Model.Nombre», o nada si es un correo o «@@». Devuelve dónde sigue la línea. */
function wfRazorAt(text, at, cs, st, out) {
    if (at > 0 && /\w/.test(text[at - 1])) {
        out.append('@');
        return at + 1;
    }
    const rest = text.slice(at + 1);
    if (rest.startsWith('@')) {
        out.append('@@');
        return at + 2;
    }
    if (rest.startsWith('*')) {
        st.comment = '*@';
        return at;
    }
    wfPut(out, 'tok-razor', '@');
    const word = (/^[A-Za-z_]\w*/.exec(rest) || [])[0];
    if (rest.startsWith('{') || (word && RAZOR_BLOCKS.has(word))) {
        wfRazorCode(text, at + 1, cs, st, out);
        return text.length;
    }
    if (word && RAZOR_KEYWORDS.has(word)) {
        wfPut(out, 'tok-key', word);
        wfRazorCode(text, at + 1 + word.length, cs, st, out);
        return text.length;
    }
    if (rest.startsWith('(')) {
        let depth = 0;
        let j = at + 1;
        for (; j < text.length; j++) {
            if (text[j] === '(') depth++;
            else if (text[j] === ')' && --depth === 0) {
                j++;
                break;
            }
        }
        wfCodeLine(text.slice(at + 1, j), cs, {}, out);
        return j;
    }
    const expr = /^[A-Za-z_]\w*(?:\??\.[A-Za-z_]\w*|\([^()]*\)|\[[^\]]*\])*/.exec(rest);
    if (!expr) return at + 1;
    wfCodeLine(expr[0], cs, {}, out);
    return at + 1 + expr[0].length;
}

// Cómo son las líneas de mentira del código que se está leyendo: su sangría, en niveles, y su largo, en % del ancho;
// un largo 0 es una línea en blanco. Se repiten hasta llenar el sitio.
const WF_SKELETON = [[0, 34], [0, 0], [0, 52], [0, 18], [1, 46], [1, 0], [1, 62], [1, 14], [2, 58], [2, 40], [2, 66], [1, 12],
    [1, 0], [1, 54], [1, 14], [2, 48], [3, 36], [3, 60], [2, 12], [1, 10], [0, 8]];

/** Lo que se ve mientras llega un fichero: su ventana, con su nombre, su lenguaje y su tema, y dentro unas líneas de
 * código de mentira que brillan, tantas como caben en el sitio que hay, que se vuelve a medir si cambia. */
function wfCodeLoading(path, lang, note) {
    const view = wfCodeView('wf-code wf-code-loading', lang);
    const win = wfFileWin(wfFile(path, lang, note), [view]);
    win.classList.add('loading');
    let shown = -1;
    const fill = () => {
        if (!win.isConnected) return;
        const body = win.querySelector('.wf-filewin-body');
        const room = Math.max(8, Math.min(120, Math.floor((body.clientHeight || 320) / 18)));
        if (room === shown) return;
        shown = room;
        view.replaceChildren();
        for (let i = 0; i < room; i++) {
            const [depth, width] = WF_SKELETON[i % WF_SKELETON.length];
            const row = el('div', 'wf-code-line');
            const bar = el('span', 'wf-skel');
            bar.style.marginLeft = `${depth * 2}em`;
            bar.style.width = `${width}%`;
            bar.style.animationDelay = `${(i % 7) * 0.08}s`;
            row.append(el('span', 'wf-code-num', String(i + 1)), width ? bar : '');
            view.append(row);
        }
    };
    requestAnimationFrame(fill);
    // El sitio cambia al abrir el detalle a toda la vista o al mover el panel: se vuelve a llenar, hasta que llega el fichero.
    const watch = new ResizeObserver(() => (win.isConnected ? fill() : watch.disconnect()));
    requestAnimationFrame(() => watch.observe(win));
    return win;
}

/** Una línea de Markdown con sus colores: títulos, listas, citas, negritas, código y enlaces. */
function wfMarkdownLine(text, line) {
    const head = /^(\s*(?:#{1,6}\s|[-*+]\s|\d+\.\s|>\s?))/.exec(text);
    if (head && /#/.test(head[1])) {
        line.append(el('span', 'tok-key', text));
        return line;
    }
    let rest = text;
    if (head) {
        line.append(el('span', 'tok-num', head[1]));
        rest = text.slice(head[1].length);
    }
    let last = 0;
    for (const m of rest.matchAll(/(`[^`]+`)|(\*\*[^*]+\*\*|__[^_]+__)|(\[[^\]]+\]\([^)]+\))/g)) {
        if (m.index > last) line.append(rest.slice(last, m.index));
        line.append(el('span', m[1] ? 'tok-str' : m[2] ? 'tok-type' : 'tok-tag', m[0]));
        last = m.index + m[0].length;
    }
    if (last < rest.length) line.append(rest.slice(last));
    return line;
}

/** Lo que dice alguien, en una tarjeta de su color: quién, cuándo y con qué modelo arriba, y el texto con su formato;
 * un prompt, con sus imágenes; lo que piensa Claude, en cursiva y con borde discontinuo. */
function wfSay(s, lane, bare) {
    const who = s.kind === 'prompt' || s.kind === 'command' ? 'user' : lane || 'main';
    const card = paint(el('div', `wf-say ${s.kind}` + (bare ? ' bare' : '')), who);
    // Solo, el detalle ya dice quién y cuándo en su cabecera: aquí, el texto.
    const words = s.images ? withoutImageMarks(s.text || '') : s.text || s.title || '';
    if (bare) {
        const text = el('div', 'wf-say-text');
        text.append(markdown(plain(words)));
        card.append(text);
        if (s.images) card.append(gallery(s));
        return card;
    }
    const head = el('div', 'wf-say-head');
    const icon = el('span', 'wf-say-icon');
    icon.append(flowIcon(s.kind === 'thinking' ? 'think' : who === 'user' ? 'prompt' : 'reply', 'flow-icon'));
    const name = s.queued ? `${state.user} · mientras trabaja` : s.kind === 'thinking' ? `${actorName(who)} piensa` : actorName(who);
    head.append(icon, el('span', 'wf-say-name', name), el('span', 'wf-say-meta', [modelLabel(s.model, s.effort), clock(s.start)].filter(Boolean).join(' · ')));
    const text = el('div', 'wf-say-text');
    text.append(markdown(plain(words)));
    if (s.images) text.append(gallery(s));
    card.append(head, text);
    return card;
}

/** La ficha de un agente: su dibujo, su nombre y su ronda; lo que es, su modelo, cuándo empezó y cómo va; y su encargo
 * y lo que devolvió, que vienen de la llamada que lo lanzó. */
/** La ficha de un agente: su encargo y lo que devolvió de verdad, o que aún no ha devuelto nada si sigue trabajando.
 * Con `bare`, sin su cabecera, porque ya la lleva la ficha del directo que la enseña.
 *
 * Lanzado en segundo plano, el resultado de su llamada solo dice que arrancó: lo que devolvió es su entrega o, si no la
 * hay, su última respuesta, en cuanto ha terminado. */
function wfAgentCard(lane, bare) {
    const a = agentOf(lane);
    const card = paint(el('div', 'wf-agent' + (bare ? ' bare' : '')), lane);
    const done = Boolean(a.endUtc) && flowDone(a, flowMoment(selectedSteps()));
    if (!bare) {
        const head = el('div', 'wf-agent-head');
        const icon = el('span', 'wf-agent-icon');
        icon.append(flowIcon('agent', 'flow-icon'));
        const title = el('div', 'wf-agent-title');
        const label = roundLabel(a.description);
        const name = el('div', 'wf-agent-name');
        if (label) name.append(roundTag({ label }), ' ');
        name.append(actorName(lane));
        head.append(icon, title);
        title.append(name, wfAgentMeta(a, done));
        card.append(head);
    }
    const call = callOf(lane);
    if (!call) return card;
    if (call.hasDetail && !state.details.has(call.order)) post({ type: 'detail', order: call.order });
    const detail = state.details.get(call.order);
    const input = callInput(call);
    const block = (label, note, text, cls) => {
        const part = el('div', `wf-agent-part ${cls}`);
        const head = el('div', 'wf-agent-label', label);
        if (note) head.append(el('span', 'wf-agent-note', note));
        part.append(head);
        if (typeof text === 'string') {
            const md = el('div', 'wf-md');
            md.append(markdown(cut(plain(text), 6000)));
            part.append(md);
        } else part.append(text);
        card.append(part);
    };
    if (input && input.prompt) block('Encargo', 'lo que le pidió Claude', String(input.prompt), 'ask');
    else if (call.hasDetail && !detail) card.append(el('div', 'wf-agent-part faint', 'Cargando el encargo…'));
    const last = done ? [...state.trace.steps].reverse().find(s => s.agentId === lane && s.kind === 'reply' && s.text) : null;
    const back = agentReturn(lane) || (last && last.text);
    if (back) block('Lo que devolvió', `su respuesta final${a.endUtc ? ` · llegó a las ${clock(a.endUtc)}` : ''}`, String(back), 'back');
    else if (!done) block('Lo que devolvió', null, el('div', 'wf-agent-wait', `Aún no ha devuelto nada · lleva ${duration(between(a.startUtc, new Date().toISOString()))}`), 'back');
    else if (!call.hasDetail || detail) block('Lo que devolvió', null, el('div', 'wf-agent-wait', 'Terminó sin mensaje final.'), 'back');
    return card;
}

/** La fila de un agente en la ficha de un lanzamiento: qué se le encargó, cómo es y cómo va; al pulsarla abre su ficha,
 * que lleva «‹ Lanzamiento» para volver. Sin su nodo en el lienzo no hay ficha que abrir, y la fila no se pulsa. */
function wfLaunchRow(v, launch, a, moment) {
    const call = callOf(a.id);
    if (call && call.hasDetail && !state.details.has(call.order)) post({ type: 'detail', order: call.order });
    const input = call && callInput(call);
    const done = Boolean(a.endUtc) && flowDone(a, moment);
    const target = `a:${a.id}`;
    const row = paint(el('button', 'wf-launch-row ' + (done ? 'done' : 'run')), a.id);
    row.disabled = !(v.nodeData && v.nodeData.get(target));
    const main = el('span', 'wf-launch-main');
    main.append(el('span', 'wf-launch-name', actorName(a.id)));
    const ask = input && input.prompt ? plain(String(input.prompt)).split('\n').find(line => line.trim()) : a.description;
    if (ask && ask.trim() !== actorName(a.id)) main.append(el('span', 'wf-launch-ask', `Encargo: ${ask.trim()}`));
    const chips = el('span', 'wf-launch-chips');
    for (const text of [a.type, a.background ? 'segundo plano' : ''].filter(Boolean)) chips.append(el('span', 'wf-agent-chip', text));
    if (a.model) chips.append(modelBadge(a.model, a.effort));
    main.append(chips);
    const side = el('span', 'wf-launch-side');
    side.append(el('span', 'wf-launch-state', done ? `✓ ${duration(between(a.startUtc, a.endUtc))}` : `Trabajando · ${duration(between(a.startUtc, new Date().toISOString()))}`));
    if (!row.disabled) side.append(el('span', 'wf-launch-open', 'Abrir ›'));
    row.append(el('span', 'wf-launch-dot'), main, side);
    row.dataset.tip = `m:${a.id}`;
    row.dataset.tipActor = a.id;
    row.addEventListener('click', () => {
        v.backTo = launch;
        v.backFor = target;
        v.detail = target;
        wfDetail(v, target);
    });
    return row;
}

/** Cómo es y cómo va un agente, en una línea: tipo, modelo, si va en segundo plano y su estado (✓ y lo que tardó, o
 * trabajando). */
function wfAgentMeta(a, done) {
    const meta = el('div', 'wf-agent-chips');
    if (a.type) meta.append(el('span', 'wf-agent-chip', a.type));
    if (a.model) meta.append(modelBadge(a.model, a.effort));
    if (a.background) meta.append(el('span', 'wf-agent-chip', 'segundo plano'));
    meta.append(el('span', 'wf-agent-state ' + (done ? 'ok' : 'run'),
        done ? `✓ ${duration(between(a.startUtc, a.endUtc))}` : `● trabajando · ${duration(between(a.startUtc, new Date().toISOString()))}`));
    return meta;
}

/** Unos pasos en dos columnas: a la izquierda su lista, como una tabla (qué, sobre qué, a qué hora y cuánto tardó); a la
 * derecha, el elegido, con el aspecto de lo que es. Se ve uno a la vez; sin elegir, el primero.
 *
 * Con un agente, la lista empieza por su ficha (su encargo y lo que devolvió), que es lo que se ve sin elegir. */
function wfCalls(v, key, steps, title, grouped, agent) {
    const grid = el('div', 'wf-calls' + (grouped ? ' grouped' : ''));
    const list = el('div', 'wf-calls-list');
    list.append(el('div', 'wf-calls-title', title));
    const card = Boolean(agent) && !steps.some(s => s.order === state.wfPick.get(key));
    if (agent) {
        const row = el('button', 'wf-row card' + (card ? ' on' : ''));
        const what = el('span', 'wf-row-what');
        what.append(el('span', 'wf-row-name', 'Encargo y lo que devolvió'), el('span', 'wf-row-detail', [agentOf(agent).type, agentOf(agent).model].filter(Boolean).join(' · ')));
        row.append(flowIcon('agent', 'wf-row-icon'), what);
        row.addEventListener('click', () => {
            state.wfPick.delete(key);
            wfDetail(v, key);
        });
        list.append(row);
    }
    const picked = card ? null : steps.find(s => s.order === state.wfPick.get(key)) || steps[0];
    for (const s of steps) {
        const [label, detail] = seqLabel({ step: s });
        const kind = s.kind === 'tool' ? toolIcon(s.title.split(' · ')[0]) : s.kind === 'skill' ? 'skill' : s.kind === 'reply' ? 'reply' : s.kind === 'thinking' ? 'think' : s.kind === 'agent' ? 'agent' : 'tool';
        const says = s.kind === 'reply' || s.kind === 'thinking' || s.kind === 'prompt';
        const row = el('button', 'wf-row' + (s === picked ? ' on' : '') + (s.isError ? ' error' : '') + (grouped ? (says ? ' say' : ' sub') : '') + (s.kind === 'thinking' ? ' think' : ''));
        const what = el('span', 'wf-row-what');
        // Una MCP, por lo que hace y su servidor, no por su nombre interno («mcp__playwright__browser_click»).
        const mcp = s.kind === 'tool' && mcpServer(s.title.split(' · ')[0]);
        const shownName = mcp ? `${mcp.tool.replace(/_/g, ' ')} · ${mcp.server}` : label.replace(/^\S+\s/, '');
        if (grouped && says) what.append(el('span', 'wf-row-say', clip(oneLine(plain(s.text || '')), 220)));
        else what.append(el('span', 'wf-row-name', shownName), el('span', 'wf-row-detail', clip(oneLine(plain(detail || '')), 120)));
        const time = between(s.start, s.end);
        row.append(flowIcon(kind, 'wf-row-icon'), what, el('span', 'wf-row-time', clock(s.start)), el('span', 'wf-row-dur', time ? duration(time) : ''));
        row.addEventListener('click', () => {
            state.wfPick.set(key, s.order);
            wfDetail(v, key);
        });
        list.append(row);
    }
    const view = el('div', 'wf-calls-view');
    if (card) view.append(wfAgentCard(agent, true));
    else if (picked) {
        if (picked.hasDetail && !state.details.has(picked.order)) post({ type: 'detail', order: picked.order });
        if (picked.kind === 'tool' || picked.kind === 'skill') view.append(wfCall(picked));
        else if (picked.kind === 'agent' && picked.childAgentId && agentOf(picked.childAgentId)) view.append(wfAgentCard(picked.childAgentId));
        else view.append(wfSay(picked, picked.agentId || 'main'));
        view.classList.toggle('says', !(picked.kind === 'tool' || picked.kind === 'skill'));
    }
    grid.append(list, view);
    // La lista y lo elegido se desplazan por separado: elegir otra no la mueve.
    requestAnimationFrame(() => {
        const on = list.querySelector('.wf-row.on');
        if (on && (on.offsetTop < list.scrollTop || on.offsetTop > list.scrollTop + list.clientHeight)) on.scrollIntoView({ block: 'nearest' });
    });
    return grid;
}

/** Cierra el detalle y quita la marca del nodo. */
function wfCloseDetail(v) {
    reviewPopClose();
    if (fileFind.open && fileFind.bar && v.drawer.contains(fileFind.bar)) fileFindClose();
    if (v.dock) $('panes').classList.remove('dock-open');
    v.detail = null;
    delete v.backTo;
    delete v.backFor;
    v.drawer.hidden = true;
    v.drawer.classList.remove('tall');
    v.root.classList.remove('detail-file');
    // Si era la de un fichero, su fila deja de verse elegida en el panel de cambios.
    if (v.drawer.dataset.sig) renderChanges();
    delete v.drawer.dataset.sig;
    v.nodes.forEach(node => node.classList.remove('picked'));
    // Una pieza no tiene lienzo que volver a encuadrar.
    if (!v.piece) wfRestoreView(v);
}

/** Deja el lienzo como se miraba antes de abrir el detalle, con las filas repartidas otra vez en todo el ancho:
 * siguiendo o viéndolo entero, si era así, ya en todo lo libre; si no, en el mismo sitio. Lo que la persona haya movido
 * o pedido con el detalle abierto se respeta. */
function wfRestoreView(v) {
    const before = v.beforeDetail;
    v.beforeDetail = null;
    if (!before || !v.root.isConnected) return;
    const back = !v.movedInDetail;
    if (back) {
        v.follow = before.follow;
        v.fit = before.fit;
        v.autoZoom = before.autoZoom;
        if (!v.follow && !v.fit) v.cam = before.cam;
        $('flow-follow').classList.toggle('on', v.follow);
    } else wfAnchorView(v);
    wfPlaceOverlays(v);
    renderDiagram(false);
    if (back && !v.follow && !v.fit) wfCamera(v, true);
}

/** Anota que la persona ha movido el lienzo o ha pedido seguir o verlo todo, y deja de encajar solo lo que se veía
 * entero. Con el detalle abierto, al cerrarlo, se queda así. */
function wfMovedByHand(v) {
    if (!v) return;
    v.fit = null;
    if (v.beforeDetail) v.movedInDetail = true;
}

/** Cambia la parte de la sesión que enseña el lienzo, la recuerda y lo pinta desde cero, siguiendo lo último. */
function setFlow(change) {
    Object.assign(state, change);
    if (change.flowScope) remember('flowScope', change.flowScope);
    state.wf = null;
    renderDiagram(false);
}

/** Cajas que trabajan en el momento que se ve: la última de cada agente que sigue y, si la sesión trabaja, la última de Claude. */
function flowActive(lanes, moment) {
    const active = new Set();
    if (!moment.working && !moment.replaying) return active;
    for (const [lane, nodes] of lanes) {
        const last = nodes[nodes.length - 1];
        if (!last || last.kind === 'end') continue;
        if (lane === 'main' ? !moment.now.agentId || moment.now.kind === 'agent' : !flowDone(agentOf(lane), moment)) active.add(last);
    }
    return active;
}

/** Qué hace Claude en el momento que se ve, por su último paso. */
function flowDoing(m) {
    const s = m.now;
    switch (s.kind) {
        case 'prompt':
        case 'command': return 'Leyendo el prompt';
        case 'thinking': return 'Pensando…';
        case 'reply': return 'Contestando';
        case 'agent': return 'Lanzando agentes';
        case 'skill': return `Cargando ${s.title}`;
        case 'tool': return `Usando ${s.title.split(' · ')[0]}`;
        default: return 'Trabajando';
    }
}

// CHAT

/** Pinta el chat de la interacción elegida, o de toda la sesión con separadores. */
function renderChat(scrollToStart) {
    const t = state.trace;
    const box = $('chat');
    const anchor = chatAnchor(box);
    const follow = state.chatPinned && following();
    state.chatStale = false;
    box.replaceChildren();
    state.chatItems = null;
    // Se vuelve a decidir al final, ya pintado y en su sitio; vacío, no tiene adónde bajar.
    $('to-bottom').hidden = true;
    if (!t) return;
    const all = state.interaction === 'all';
    const label = $('chat-label');
    const where = el('span', 'chat-title');
    where.append(agentLogo(t.agent), placeOf(t) || 'Chat');
    label.replaceChildren(where, el('span', 'chat-sub', all ? 'Toda la sesión' : `Interacción ${state.interaction}`));
    label.title = label.textContent;

    const items = chatItems();
    if (items.length === 0) {
        box.append(el('div', 'empty', 'Esta interacción no tiene pasos que enseñar.'));
        return;
    }
    for (const item of items) box.append(item.make());
    markText(box);
    state.chatItems = items;
    state.chatView = chatView();
    if (scrollToStart === true) {
        box.scrollTop = all ? box.scrollHeight : 0;
        state.chatPinned = all;
    } else if (follow) box.scrollTop = box.scrollHeight;
    else restoreChatAnchor(box, anchor);
    noteChatNews(follow);
}

/** El primer elemento del chat que se ve y cuánto asoma: para dejarlo donde estaba tras repintar. */
function chatAnchor(box) {
    const top = box.getBoundingClientRect().top;
    for (const node of box.children) {
        const r = node.getBoundingClientRect();
        if (r.bottom > top) return { order: node.dataset.order, text: node.textContent, offset: r.top - top, scrollTop: box.scrollTop };
    }
    return { scrollTop: box.scrollTop };
}

/**
 * Vuelve a dejar a la vista lo que se veía antes de repintar el chat.
 *
 * Se coloca por el elemento, no por los píxeles: recién pintado, el chat aún no tiene los «Ver más» de
 * las respuestas largas, y lo de arriba mide distinto hasta que aparecen.
 */
function restoreChatAnchor(box, anchor) {
    const node = anchor.order !== undefined
        ? box.querySelector(`:scope > [data-order="${anchor.order}"]`)
        : [...box.children].find(n => n.dataset.order === undefined && n.textContent === anchor.text);
    if (!node) {
        box.scrollTop = anchor.scrollTop;
        return;
    }
    box.scrollTop += node.getBoundingClientRect().top - box.getBoundingClientRect().top - anchor.offset;
}

/**
 * Pone al día el chat con lo nuevo de la sesión en directo: conserva los pasos que no han cambiado y
 * vuelve a pintar desde el primero distinto.
 *
 * Volver a montar el chat entero en cada novedad cuesta lo mismo que abrir la sesión. Si ha cambiado lo
 * que se enseña (otra interacción, otro paso señalado, las herramientas), se pinta entero.
 */
function patchChat() {
    const t = state.trace;
    const old = state.chatItems;
    if (!t || !old || state.chatView !== chatView()) {
        renderChat(false);
        return;
    }
    const items = chatItems();
    if (items.length === 0) {
        renderChat(false);
        return;
    }
    state.chatStale = false;
    let same = 0;
    while (same < old.length && same < items.length && old[same].key === items[same].key && old[same].sig === items[same].sig) same++;
    if (same === old.length && same === items.length) return;

    const box = $('chat');
    const follow = state.chatPinned && following();
    while (box.children.length > same) box.lastElementChild.remove();
    for (const item of items.slice(same)) {
        const node = item.make();
        box.append(node);
        markText(node);
    }
    state.chatItems = items;
    if (follow) box.scrollTop = box.scrollHeight;
    noteChatNews(follow);
}

/** Lo que enseña el chat, en orden: un separador por interacción si se ve toda la sesión y cada paso, con su clave, su firma y cómo pintarlo. */
function chatItems() {
    const t = state.trace;
    const wait = agentsWait();
    const all = state.interaction === 'all';
    const items = [];
    let current = null;
    for (const s of selectedSteps().filter(inChat)) {
        // Un agente en segundo plano puede seguir hablando ya entrada la interacción siguiente: no vuelve a abrir la suya.
        if (all && (current === null || s.interaction > current)) {
            const number = s.interaction;
            const i = t.interactions.find(x => x.number === number);
            const text = number === 0 ? 'Antes del primer prompt' : `Interacción ${number} · ${hour(i && i.startUtc)}`;
            items.push({ key: `d${number}`, sig: text, make: () => el('div', 'divider', text) });
            current = number;
        }
        items.push({ key: String(s.order), sig: JSON.stringify(s), make: () => step(s) });
    }
    // Mientras espera a sus agentes, al final: se va cuando la sesión retoma.
    if (wait && (all || state.interaction === Math.max(...t.interactions.map(i => i.number))))
        items.push({ key: 'agentswait', sig: JSON.stringify([wait.agents, agoShort(wait.since)]), make: () => chatWaitNote(wait) });
    return items;
}

/** Lo que decide qué pasos salen en el chat, aparte de los datos de la sesión. */
function chatView() {
    return `${state.trace && state.trace.id}|${state.interaction}|${state.showTools}|${state.focus}`;
}

/** En directo, si llega algo nuevo mientras se lee más arriba, lo avisa en el botón de ir al final. */
function noteChatNews(follow) {
    const t = state.trace;
    const items = state.chatItems;
    if (!t || !items || items.length === 0) return;
    const last = Number(items[items.length - 1].key);
    const seen = state.chatSeen;
    if (seen.id !== t.id) state.chatNews = false;
    if (t.live && seen.id === t.id && seen.interaction === state.interaction && last > seen.last && !follow) state.chatNews = true;
    state.chatSeen = { id: t.id, interaction: state.interaction, last };
    updateToBottom();
}

// Lo que hay que subir el chat, en píxeles, para que salga el botón de ir al final.
const TO_BOTTOM_GAP = 300;

// Hasta cuántos píxeles del final se considera que el chat está abajo del todo.
const PIN_GAP = 30;

/** Píxeles que faltan para llegar al final del chat. */
function fromBottom(box) {
    return box.scrollHeight - box.clientHeight - box.scrollTop;
}

/** Si el chat sigue lo que va llegando: solo en directo y fuera de la repetición. */
function following() {
    return Boolean(state.trace && state.trace.live) && state.replay.index === null;
}

/** Al desplazar el chat: queda anclado al final si se llega abajo y se suelta si se sube; mientras baja solo hacia el final, no se suelta. */
function onChatScroll() {
    const gap = fromBottom($('chat'));
    if (state.chatGliding) {
        if (gap <= PIN_GAP) state.chatGliding = false;
    } else state.chatPinned = gap <= PIN_GAP;
    updateToBottom();
}

/** Si el chat está anclado, lo vuelve a llevar al final: tras cambiar su tamaño o crecer lo que ya estaba pintado. */
function pinChat() {
    if (!state.chatPinned || !following()) return;
    const box = $('chat');
    if (fromBottom(box) > 1) box.scrollTop = box.scrollHeight;
}

/** Enseña el botón de ir al final si el chat se ha subido bastante o hay algo nuevo debajo; al llegar abajo, quita el aviso. */
function updateToBottom() {
    const box = $('chat');
    const gap = fromBottom(box);
    if (gap <= PIN_GAP) state.chatNews = false;
    const button = $('to-bottom');
    button.hidden = !(gap >= TO_BOTTOM_GAP || (state.chatNews && gap > PIN_GAP));
    button.classList.toggle('news', state.chatNews);
    button.title = state.chatNews ? 'Hay mensajes nuevos: ir al final del chat' : 'Ir al final del chat';
}

/** Lleva el chat al final con un desplazamiento suave y lo deja anclado allí. */
function scrollChatToBottom() {
    const box = $('chat');
    state.chatNews = false;
    state.chatPinned = true;
    state.chatGliding = true;
    box.scrollTo({ top: box.scrollHeight, behavior: 'smooth' });
    updateToBottom();
}

/** Si un paso sale en el chat: las herramientas, solo con «Herramientas» encendido o cuando es el paso señalado; las preguntas a la persona, siempre. */
function inChat(s) {
    return s.kind !== 'tool' || Boolean(s.questions) || state.showTools || s.order === state.focus;
}

/** Enciende o apaga las herramientas en el chat y lo recuerda. */
function setShowTools(show) {
    state.showTools = show;
    const button = $('chat-tools');
    button.classList.toggle('on', show);
    button.title = show ? 'Ocultar las herramientas en el chat' : 'Ver también las herramientas en el chat';
    remember('showTools', show);
}

/** Si hay texto seleccionado dentro del chat, que no se debe perder al repintarlo. */
function selectingInChat() {
    const sel = window.getSelection();
    return Boolean(sel && !sel.isCollapsed && sel.toString().trim() && $('chat').contains(sel.anchorNode));
}

/** Pasa la marca de paso señalado en el chat sin repintarlo, para no perder lo seleccionado. */
function markFocus() {
    $('chat').querySelectorAll('.focus').forEach(n => n.classList.remove('focus'));
    const node = state.focusMark && $('chat').querySelector(`[data-order="${state.focus}"]`);
    if (node) node.classList.add('focus');
}

/** Elemento de un paso según su tipo, marcado con su orden para casarlo con el diagrama. */
function step(s) {
    let node;
    switch (s.kind) {
        case 'prompt': node = message(s, false); break;
        case 'command': node = message(s, true); break;
        case 'reply': node = message(s, false); break;
        case 'thinking': node = thought(s); break;
        case 'notification': node = el('div', 'notice', `🔔 ${s.text || s.title}`); break;
        default:
            if (!s.questions) return event(s);
            node = ask(s);
    }
    node.dataset.order = s.order;
    if (state.focusMark && state.focus === s.order) node.classList.add('focus');
    if (state.find.matches.includes(s.order)) node.classList.add('match');
    node.addEventListener('click', e => {
        e.stopPropagation();
        // Soltar un arrastre también es un clic: si ha dejado texto seleccionado, se está copiando, no señalando.
        if (selectingInChat()) return;
        focusStep(s.order, 'chat');
    });
    return node;
}

/** Razonamiento como bocadillo de pensamiento: nube con burbujas que suben desde el avatar de quien piensa. */
function thought(s) {
    const who = speaker(s);
    const box = paint(el('div', 'msg thought'), who);
    const cloud = el('div', 'bubble');
    const head = el('div', 'who');
    head.append(el('strong', null, actorName(who)), ' piensa');
    head.append(` · ${hour(s.start)}`);
    cloud.append(head, ...longText(s, el('div', 'text', s.text || '')));
    box.append(paint(el('span', 'avatar', actorIcon(who)), who), el('span', 'puffs'), cloud);
    return box;
}

/** Un comando de consola como en una terminal: la marca «!» aparte y el comando en monoespaciada, sin su salida. */
function shellLine(text) {
    const line = el('div', 'shell-line');
    line.append(el('span', 'shell-mark', '!'), el('span', 'shell-command', (text || '').replace(/^!\s*/, '')));
    return line;
}

/** Mensaje tipo Telegram con avatar: el de la persona a la derecha en verde; el de Claude o de un agente a la izquierda, en su color. */
function message(s, command) {
    const who = speaker(s);
    const ink = s.shell ? 'shell' : who;
    const box = paint(el('div', 'msg' + (who === 'user' ? ' me' : '')), ink);
    const bubble = el('div', 'bubble' + (command ? ' command' : '') + (s.shell ? ' shell' : ''));
    const head = el('div', 'who');
    head.append(el('strong', null, actorName(who)));
    if (s.shell) head.append(' · ', flowIcon('terminal', 'shell-icon'), 'comando de consola');
    if (s.model) head.append(` · ${s.model}`);
    head.append(` · ${hour(s.start)}`);
    if (s.secrets) head.append(secretTag(s));
    if (s.hookError) head.append(hookTag(s));
    bubble.append(head);
    if (s.shell) bubble.append(shellLine(s.text));
    else if (s.kind === 'reply') bubble.append(...longText(s, markdown(s.text || '')));
    else if (s.kind === 'prompt') {
        bubble.append(promptBody(s.images ? withoutImageMarks(s.text || '') : s.text || ''));
        if (s.images) bubble.append(gallery(s));
    }
    else bubble.append(el('div', 'text', s.text || ''));
    box.append(paint(el('span', 'avatar', actorIcon(who)), ink), bubble);
    return box;
}

/** Preguntas a la persona: la tarjeta de quien pregunta, con las opciones y la elegida marcada, y debajo la respuesta como mensaje suyo. */
function ask(s) {
    const box = el('div', 'ask-step');
    const who = speaker(s);
    const card = paint(el('div', 'msg'), who);
    const bubble = el('div', 'bubble ask');
    const head = el('div', 'who');
    head.append(el('strong', null, actorName(who)), ` pregunta · ${hour(s.start)}`);
    bubble.append(head);
    for (const q of s.questions) {
        const block = el('div', 'question');
        const title = el('div', 'q-title');
        if (q.header) title.append(el('span', 'tag', q.header), ' ');
        title.append(el('strong', null, q.question));
        block.append(title);
        const chosen = chosenLabels(q);
        const options = el('div', 'options');
        for (const o of q.options || []) {
            const picked = chosen.has(o.label);
            const row = el('div', 'option' + (picked ? ' chosen' : ''));
            row.append(el('span', 'mark', q.multiSelect ? (picked ? '☑' : '☐') : (picked ? '●' : '○')));
            const words = el('div');
            words.append(el('div', 'opt-label', o.label));
            if (o.description) words.append(el('div', 'opt-desc', o.description));
            row.append(words);
            options.append(row);
        }
        block.append(options);
        bubble.append(block);
    }
    card.append(paint(el('span', 'avatar', actorIcon(who)), who), bubble);
    box.append(card, askReply(s));
    return box;
}

/** Opciones que eligió la persona: la etiqueta que respondió, o varias separadas por comas si la pregunta admitía más de una. */
function chosenLabels(q) {
    if (!q.answer) return new Set();
    const labels = (q.options || []).map(o => o.label);
    if (labels.includes(q.answer)) return new Set([q.answer]);
    return new Set(q.multiSelect ? labels.filter(l => q.answer.split(', ').includes(l)) : []);
}

/** Respuesta de la persona a las preguntas: lo que eligió o escribió, con sus notas; o si no llegó a responder. */
function askReply(s) {
    const answered = s.questions.filter(q => q.answer);
    if (answered.length === 0) {
        const text = !s.end ? '⏳ Esperando respuesta…' : '✋ Sin respuesta: las rechazó o la sesión se cortó';
        return el('div', 'notice', text);
    }
    const reply = paint(el('div', 'msg me'), 'user');
    const bubble = el('div', 'bubble');
    const head = el('div', 'who');
    head.append(el('strong', null, actorName('user')), ` · ${hour(s.end)}`);
    bubble.append(head);
    for (const q of answered) {
        const line = el('div', 'answer');
        // Con varias preguntas, cada respuesta lleva el rótulo de la suya.
        if (s.questions.length > 1) line.append(el('span', 'tag', q.header || clip(q.question, 40)), ' ');
        const own = chosenLabels(q).size === 0;
        line.append(el('span', own ? 'own' : null, s.images ? withoutAttached(q.answer) : q.answer));
        bubble.append(line);
        if (q.note) bubble.append(el('div', 'note', `📝 ${q.note}`));
    }
    // Lo que pegó al contestar, como en un prompt: sus miniaturas, que se amplían al pulsarlas.
    if (s.images) bubble.append(gallery(s));
    reply.append(paint(el('span', 'avatar', actorIcon('user')), 'user'), bubble);
    return reply;
}

/** Quita de una respuesta la marca «(Image attached)» que pone Claude Code al pegar una imagen: la imagen ya se enseña. */
function withoutAttached(text) {
    return String(text || '').replace(/\s*\(Image attached\)/g, '').trim();
}

/** Texto de las preguntas y sus respuestas, para el buscador y los rótulos. */
function askText(s) {
    return (s.questions || []).map(q => `${q.question} ${q.answer || ''} ${q.note || ''}`).join(' ');
}

// Lo pegado en un prompt, entre las etiquetas con que lo guarda Claude Code; sin cierre si el texto se cortó.
const PASTED = /<pasted_content[^>]*>\n?([\s\S]*?)(?:\n?<\/pasted_content[^>]*>|$)/g;

/** Prompt de la persona: lo escrito como texto y cada trozo pegado en su bloque, con su botón de copiar. */
function promptBody(text) {
    const box = el('div', 'prompt-body');
    let last = 0;
    for (const m of text.matchAll(PASTED)) {
        const before = text.slice(last, m.index).trim();
        if (before) box.append(linkedText(before));
        const pasted = el('div', 'pasted');
        pasted.append(el('div', 'pasted-label', '📋 Texto pegado'), codeBlock(m[1], null));
        box.append(pasted);
        last = m.index + m[0].length;
    }
    const after = text.slice(last).trim();
    if (after) box.append(linkedText(after));
    return box;
}

/** Bloque de texto de un prompt con sus direcciones web como enlaces. */
function linkedText(text) {
    const box = el('div', 'text');
    box.append(linked(text));
    return box;
}

/** Quita del texto de un prompt las marcas de sus imágenes: «[Image #N]» de Claude Code y la línea «[imagen]» de la app.
 *
 * Solo para el chat, donde las miniaturas ya las enseñan; en las listas la marca es lo único que avisa de la imagen. */
function withoutImageMarks(text) {
    return text.replace(/[ \t]*\[Image #\d+\]/g, '').replace(/^\[imagen\]$/gm, '').trim();
}

/** Miniaturas de las imágenes de un paso; las que aún no han llegado se piden a la app una vez y se rellenan al llegar. */
function gallery(s) {
    const box = el('div', 'gallery');
    for (let index = 0; index < s.images; index++) {
        const key = `${s.order}:${index}`;
        const img = el('img', 'thumb');
        img.dataset.key = key;
        img.alt = `Imagen ${index + 1}`;
        const src = state.images.get(key);
        if (src) img.src = src;
        else if (!state.images.has(key)) {
            // Marcada como pedida, para no volver a pedirla en cada repintado.
            state.images.set(key, null);
            post({ type: 'image', order: s.order, index });
        }
        img.addEventListener('click', e => {
            e.stopPropagation();
            const all = [...box.querySelectorAll('img.thumb')].filter(i => i.src);
            if (img.src) openLightbox(all.map(i => i.src), all.indexOf(img));
        });
        box.append(img);
    }
    return box;
}

/** Pone una imagen recién llegada en sus miniaturas, sin repintar el chat. */
function receiveImage(m) {
    const key = `${m.order}:${m.index}`;
    // Solo direcciones «data:» de imagen: la página no carga nada de fuera.
    if (typeof m.src !== 'string' || !/^data:image\/(png|jpeg|gif|webp);base64,/.test(m.src)) return;
    state.images.set(key, m.src);
    document.querySelectorAll(`img.thumb[data-key="${key}"]`).forEach(img => { img.src = m.src; });
}

/** Enseña una imagen a tamaño completo sobre la vista. */
function openLightbox(srcs, index) {
    state.lightbox = { srcs: Array.isArray(srcs) ? srcs : [srcs], index: Math.max(0, index || 0) };
    showLightbox();
    $('lightbox').hidden = false;
    // La página solo cubre Sesiones: la ventana oscurece el resto, para que la imagen se vea sobre toda la app.
    post({ type: 'dim', on: true });
}

/** Enseña en el visor la imagen que toca, con cuántas hay y las flechas que llevan a la anterior y la siguiente. */
function showLightbox() {
    const lb = state.lightbox;
    if (!lb) return;
    $('lightbox-img').src = lb.srcs[lb.index];
    const many = lb.srcs.length > 1;
    $('lb-prev').hidden = !many;
    $('lb-next').hidden = !many;
    $('lb-count').hidden = !many;
    $('lb-count').textContent = `${lb.index + 1} / ${lb.srcs.length}`;
}

/** Pasa a la imagen anterior o la siguiente del visor; tras la última, vuelve a la primera. */
function stepLightbox(delta) {
    const lb = state.lightbox;
    if (!lb || lb.srcs.length < 2) return;
    lb.index = (lb.index + delta + lb.srcs.length) % lb.srcs.length;
    showLightbox();
}

/** Cierra el visor de imágenes. */
function closeLightbox() {
    if ($('lightbox').hidden) return;
    $('lightbox').hidden = true;
    $('lightbox-img').removeAttribute('src');
    state.lightbox = null;
    post({ type: 'dim', on: false });
}

/** Texto de un prompt sin las etiquetas de lo pegado, para los rótulos de una línea. */
function plain(text) {
    return (text || '').replace(/<\/?pasted_content[^>]*>/g, ' ');
}

// Altura fija de una respuesta en el chat; la misma que «max-height» de .clamp en la hoja de estilos.
const CLAMP_H = 190;

/** Respuesta con altura fija y «Ver más» si no cabe; abierta si se pidió o si lleva algo de lo buscado. */
function longText(s, content) {
    const clamp = el('div', 'clamp');
    clamp.append(content);
    const more = el('button', 'more');
    const searched = state.find.matches.includes(s.order);
    const set = open => {
        clamp.classList.toggle('open', open);
        more.textContent = open ? 'Ver menos' : 'Ver más';
    };
    set(state.longOpen.has(s.order) || searched);
    more.addEventListener('click', e => {
        e.stopPropagation();
        const open = !clamp.classList.contains('open');
        if (open) state.longOpen.add(s.order);
        else state.longOpen.delete(s.order);
        set(open);
    });
    // Solo se ve el botón si la respuesta no cabe; se mide una vez pintada.
    if (pendingClamps.length === 0) requestAnimationFrame(measureClamps);
    pendingClamps.push({ clamp, more });
    more.hidden = true;
    return [clamp, more];
}

// Respuestas pintadas que aún no se han medido para saber si llevan «Ver más».
const pendingClamps = [];

/** Vuelve a medir todas las respuestas del chat, para que cada una lleve su «Ver más» si no cabe; se hace cuando el chat
 * vuelve a verse tras estar escondido, o cambia de ancho, que es cuando una medida de antes puede haber quedado mal. */
function remeasureClamps() {
    const box = $('chat');
    if (box.clientHeight === 0) return;
    state.clampsStale = false;
    box.querySelectorAll('.clamp').forEach(clamp => {
        const more = clamp.nextElementSibling;
        const cut = clamp.scrollHeight > CLAMP_H + 4;
        clamp.classList.toggle('cut', cut);
        if (more && more.classList.contains('more')) more.hidden = !cut;
    });
}

/**
 * Enseña «Ver más» en las respuestas pintadas que no caben y, si el chat estaba anclado al final, lo deja ahí.
 *
 * Primero se mide todo y después se cambia: medir tras cada cambio obligaría a recolocar el chat una vez
 * por respuesta. El botón aparece después de bajar al final, y sin volver a bajar lo dejaría a medias.
 */
function measureClamps() {
    const batch = pendingClamps.splice(0);
    const cuts = batch.map(({ clamp }) => clamp.isConnected && clamp.scrollHeight > CLAMP_H + 4);
    batch.forEach(({ clamp, more }, i) => {
        clamp.classList.toggle('cut', cuts[i]);
        more.hidden = !cuts[i];
    });
    // Lo que no se ha podido medir, porque el chat estaba escondido, se mide al volver a verse.
    if ($('chat').clientHeight === 0) state.clampsStale = true;
    const box = $('chat');
    if (state.chatPinned && state.replay.index === null && fromBottom(box) > 1) box.scrollTop = box.scrollHeight;
    updateToBottom();
}

const EventIcon = { tool: '🔧', skill: '🧙', agent: '🤖' };
const EventLabel = { tool: 'Herramienta', skill: 'Skill', agent: 'Agente' };

/** Paso plegable: herramienta, skill o lanzamiento de agente, con la raya del color de quien actúa; al abrirlo pide su detalle. */
function event(s) {
    const open = state.expanded.has(s.order);
    const who = s.kind === 'agent' && s.childAgentId ? s.childAgentId : speaker(s);
    const box = paint(el('div', `event ${s.kind}` + (open ? ' open' : '') + (state.focusMark && state.focus === s.order ? ' focus' : '')
        + (state.find.matches.includes(s.order) ? ' match' : '')), who);
    box.dataset.order = s.order;
    const head = el('button', 'event-head');
    head.append(el('span', 'chev', open ? '▾' : '▸'), el('span', null, EventIcon[s.kind] || '•'));

    const name = el('span', 'name');
    name.append(el('strong', null, s.kind === 'tool' ? s.title.split(' · ')[0] : EventLabel[s.kind]));
    const rest = s.kind === 'tool' ? s.title.split(' · ').slice(1).join(' · ') : s.title;
    if (rest) name.append(` · ${rest}`);
    head.append(name);

    const agent = s.kind === 'agent' ? agentOf(s.childAgentId) : null;
    if (agent && agent.background) head.append(el('span', 'tag', 'segundo plano'));
    if (s.skill && s.kind !== 'skill') head.append(el('span', 'tag', `🧙 ${s.skill}`));
    if (s.isError) head.append(el('span', 'tag error', 'Error'));
    if (s.secrets) head.append(secretTag(s));
    if (s.hookError) head.append(hookTag(s));
    const ms = agent ? between(agent.startUtc, agent.endUtc) : between(s.start, s.end);
    head.append(el('span', 'meta', [duration(ms), hour(s.start)].filter(Boolean).join(' · ')));
    head.addEventListener('click', e => {
        e.stopPropagation();
        state.focus = s.order;
        state.focusMark = false;
        toggle(s);
        markFocus();
        decorate();
        scrollDiagramTo(s.order);
    });
    box.append(head);

    if (open) box.append(body(s));
    return box;
}

/** Contenido de un paso abierto: entrada y resultado, o el encargo del agente y lo que devolvió. */
function body(s) {
    const box = el('div', 'event-body');
    const detail = state.details.get(s.order);
    if (s.hasDetail && !detail) {
        box.append(el('div', 'loading', 'Cargando…'));
        return box;
    }
    const text = detail && detail.text;
    const result = detail && detail.result;
    if (s.secrets) box.append(secretBox([s]));

    // Lo que dice el agente mientras trabaja ya sale en el chat, en su color: aquí, solo el encargo y la vuelta.
    if (s.kind === 'agent') {
        if (text) box.append(part('Encargo', text, false));
        if (result) box.append(part('Lo que devolvió', result, false));
        if (!text && !result) box.append(el('div', 'faint', 'Sin detalle.'));
    } else if (!text && !result && !s.images) box.append(el('div', 'faint', 'Sin detalle.'));
    // Cada herramienta con su aspecto: una terminal, el código con sus líneas, el diff de un editor, la imagen.
    else box.append(wfCall(s));
    return box;
}

/** Un bloque con su rótulo: código en monoespaciada, con su botón de copiar, o texto corrido. */
function part(label, text, code) {
    const box = el('div', 'part');
    box.append(el('div', 'part-label', label), code ? codeBlock(text, 'code') : el('div', 'prose', text));
    return box;
}

/** Bloque de código con un botón que copia solo su contenido. */
function codeBlock(text, cls) {
    const box = el('div', 'code-block');
    const copy = el('button', 'copy', 'Copiar');
    copy.title = 'Copiar este código';
    copy.addEventListener('click', async e => {
        e.stopPropagation();
        const ok = await copyText(text);
        copy.textContent = ok ? 'Copiado ✓' : 'No se pudo copiar';
        copy.classList.add('done');
        setTimeout(() => {
            copy.textContent = 'Copiar';
            copy.classList.remove('done');
        }, 1500);
    });
    box.append(el('pre', cls, text), copy);
    return box;
}

/** Copia un texto al portapapeles; si el navegador no deja, lo intenta a la antigua. */
async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        const area = el('textarea');
        area.value = text;
        area.style.position = 'fixed';
        area.style.opacity = '0';
        document.body.append(area);
        area.select();
        const ok = document.execCommand('copy');
        area.remove();
        return ok;
    }
}

/** Abre o cierra un paso; la primera vez pide su detalle a la app. */
function toggle(s) {
    if (state.expanded.has(s.order)) state.expanded.delete(s.order);
    else {
        state.expanded.add(s.order);
        if (s.hasDetail && !state.details.has(s.order)) {
            // Si ya la había pedido el tooltip, al llegar sí hay que repintar el paso.
            state.tipAsks.delete(s.order);
            post({ type: 'detail', order: s.order });
        }
    }
    repaintStep(s);
}

/**
 * Vuelve a pintar un solo paso del chat, en su sitio, sin tocar el resto.
 *
 * Abrir o cerrar un paso solo cambia ese paso: repintar el chat entero movía lo que se estaba leyendo.
 * Se pinta entero si el paso no está en el chat o si cambian los pasos que salen, que con las
 * herramientas ocultas depende del señalado.
 */
function repaintStep(s) {
    const old = $('chat').querySelector(`:scope > [data-order="${s.order}"]`);
    if (!old || !state.chatItems || (!state.showTools && state.chatView !== chatView())) {
        renderChat(false);
        return;
    }
    const node = step(s);
    old.replaceWith(node);
    markText(node);
    markFocus();
    state.chatView = chatView();
}

// RESPUESTA FINAL CON FORMATO: la del directo, con sus secciones, tablas, código, comandos y lo que le toca a la persona

// El estado de una sección por cómo empieza su título; lo que no casa, neutra. Solo da el color: la sección sale
// siempre en su sitio y con su título. Se mira el arranque y no palabras sueltas: «Decisiones que tomé sin preguntarte»
// no es algo que le toque a la persona.
const FF_KINDS = [
    [/^(lo que |qué )?(no (he|está|están|se ha|se han)|falta|queda)\b|^(sin (comprobar|probar|verificar|ver))\b|^(pendientes?|riesgos?|ojo|cuidado)\b/i, 'warn'],
    [/^(lo que |qué )?te toca\b|^(tus |mis )?(dudas|preguntas)\b|^(lo que )?necesito (de ti|que)\b|^(para ti|tu turno)\b/i, 'ask'],
    [/^(lo )?(que )?(he )?(hecho|cambiado|probado|comprobado)\b|^(hecho|listo|resultado|cambios|cómo lo he probado)\b/i, 'done'],
];
// Un párrafo que es una etiqueta: «**Título:**», solo o con algo detrás en la misma línea.
const FF_LABEL = /^\*\*([^*]+?):?\*\*:?\s*([\s\S]*)$/;
// Lo que pide algo a la persona sin preguntar: «Dime…», «Mira…», «Pruébalo…».
const FF_ASKS = /^(dime|mira|cuéntame|confirma|elige|decide|revisa|prueba|pruébalo|avísame)\b/i;
// Un aviso: «**Ojo:**», «**Nota:**»…
const FF_CALLOUT = /^\*\*(Ojo|Cuidado|Atención|Importante|Aviso|Nota)[:.]?\*\*:?\s*([\s\S]*)$/i;
// Una estrella de cuatro puntas, la ✦ de su ficha de ayuda: «pintar con formato».
const FF_ICON = 'M8 1.2C8.6 5.5 10.5 7.4 14.8 8C10.5 8.6 8.6 10.5 8 14.8C7.4 10.5 5.5 8.6 1.2 8C5.5 7.4 7.4 5.5 8 1.2Z';

// La última respuesta reconocida: la ficha se vuelve a pintar con cada novedad del directo.
let ffLast = { text: null, shape: null };

/** Parte una respuesta en bloques: títulos con su nivel, párrafos, listas con un nivel dentro, tablas, citas y código.
 *
 * Una raya de separación («---») no es un bloque: las tarjetas y los capítulos ya marcan el corte. Lo que va sangrado bajo un punto de una lista (un código, una línea de «✅», otro párrafo) es de ese punto y va en sus
 * bloques; una lista numerada guarda el número con que empieza, para que unos pasos partidos por un código no vuelvan a 1. */
function ffParse(src) {
    const lines = src.replace(/\r/g, '').split('\n');
    const blocks = [];
    let i = 0;
    const isRule = l => /^ {0,3}([-*_])( *\1){2,} *$/.test(l);
    const isItem = l => !isRule(l) && /^\s*([-*+]|\d+[.)])\s+/.test(l);
    const isRow = l => /^\s*\|.*\|\s*$/.test(l);
    const starts = l => /^\s*(```|~~~|#{1,6}\s|>)/.test(l) || isItem(l) || isRow(l) || isRule(l);
    while (i < lines.length) {
        const line = lines[i];
        if (!line.trim() || isRule(line)) { i++; continue; }
        const open = fenceOpen(line);
        if (open) {
            const lang = (line.match(/^\s*(?:`{3,}|~{3,})\s*([\w#+.-]*)/) || [])[1] || null;
            const block = readFence(lines, i, open);
            blocks.push({ t: 'code', lang, text: block.code });
            i = block.next;
            continue;
        }
        const h = line.match(/^(#{1,6})\s+(.*)$/);
        if (h) { blocks.push({ t: 'h', level: h[1].length, text: h[2] }); i++; continue; }
        if (/^\s*>/.test(line)) {
            const quote = [];
            while (i < lines.length && /^\s*>/.test(lines[i])) quote.push(lines[i++].replace(/^\s*>\s?/, ''));
            blocks.push({ t: 'quote', text: quote.join(' ') });
            continue;
        }
        if (isRow(line) && i + 1 < lines.length && /^\s*\|?\s*:?-{2,}/.test(lines[i + 1])) {
            const cells = row => row.trim().replace(/^\||\|$/g, '').split('|').map(c => c.trim());
            const head = cells(line);
            const rows = [];
            i += 2;
            while (i < lines.length && isRow(lines[i])) rows.push(cells(lines[i++]));
            blocks.push({ t: 'table', head, rows });
            continue;
        }
        if (isItem(line)) {
            const items = [];
            const ordered = /^\s*\d/.test(line);
            const start = ordered ? parseInt(line.trim(), 10) : 1;
            const indentOf = l => l.match(/^\s*/)[0].length;
            const nextFilled = from => {
                let j = from;
                while (j < lines.length && !lines[j].trim()) j++;
                return j;
            };
            while (i < lines.length) {
                // Una línea en blanco no corta la lista si lo siguiente es otro punto de ella.
                if (!lines[i].trim()) {
                    const j = nextFilled(i);
                    if (j < lines.length && isItem(lines[j]) && /^\s*\d/.test(lines[j]) === ordered && indentOf(lines[j]) === indentOf(line)) i = j;
                    else break;
                }
                if (!isItem(lines[i])) break;
                const m = lines[i++].match(/^(\s*)([-*+]|\d+[.)])\s+(.*)$/);
                if (m[1].length >= 2 && items.length) items[items.length - 1].children.push(m[3]);
                else items.push({ text: m[3], children: [], more: [], indent: m[1].length + m[2].length + 1 });
                const item = items[items.length - 1];
                // Lo sangrado que sigue al punto, con sus líneas en blanco y sus códigos enteros, es de él.
                while (i < lines.length) {
                    const j = nextFilled(i);
                    if (j >= lines.length || isItem(lines[j]) || indentOf(lines[j]) < Math.min(item.indent, 3)) break;
                    if (j > i && item.more.length) item.more.push('');
                    const open = fenceOpen(lines[j]);
                    const end = open ? readFence(lines, j, open).next : j + 1;
                    item.more.push(...lines.slice(j, end));
                    i = end;
                }
            }
            for (const item of items) {
                const cut = new RegExp(`^ {0,${item.indent}}`);
                item.blocks = item.more.length ? ffParse(item.more.map(l => l.replace(cut, '')).join('\n')) : [];
                delete item.more;
            }
            blocks.push({ t: 'list', ordered, start, items });
            continue;
        }
        const para = [];
        while (i < lines.length && lines[i].trim() && !(para.length && starts(lines[i]))) para.push(lines[i++]);
        blocks.push({ t: 'p', text: para.join('\n') });
    }
    return blocks;
}

/** Si un bloque son comandos «!» para que la persona los copie: un bloque de código con una línea «!» por comando, o un
 * párrafo que solo lleva comandos «!» en línea (`! …`). */
function ffIsCommand(b) {
    if (b.t === 'p') return /^(?:`!\s+[^`]+`[\s,;.]*)+$/.test(b.text.trim());
    return b.t === 'code' && b.text.trim().length > 0 && b.text.trim().split('\n').every(l => /^!\s/.test(l));
}

/** Los comandos «!» que van en línea, entre comillas de código, en el texto de unos bloques, sin el «!» y en su orden. */
function ffInlineCommands(blocks) {
    const texts = blocks.flatMap(b =>
        b.t === 'p' || b.t === 'quote' ? [b.text]
        : b.t === 'list' ? b.items.flatMap(it => [it.text, ...it.children, ...(it.blocks || []).flatMap(x => x.t === 'p' || x.t === 'quote' ? [x.text] : [])])
        : b.t === 'table' ? [...b.head, ...b.rows.flat()]
        : []);
    const found = [];
    for (const text of texts)
        for (const m of text.matchAll(/`!\s+([^`]+?)\s*`/g))
            if (!found.includes(m[1])) found.push(m[1]);
    return found;
}

/** Lo que se sabe pintar de una respuesta final, o null si no trae nada: entonces se ve tal cual.
 *
 * Todo sale en el orden del mensaje y con sus palabras: ni se mueve un bloque de su sección, ni se juntan puntos de
 * varias, ni se pone un título que el mensaje no traiga. Solo cambia el aspecto.
 *
 * Si la respuesta usa títulos de más de un nivel, los del nivel más alto son capítulos: un título suelto que encabeza
 * las secciones que le siguen, en vez de una sección vacía al lado de ellas. */
function ffShape(text) {
    if (ffLast.text === text) return ffLast.shape;
    const blocks = ffParse(text);
    const kindOf = title => (FF_KINDS.find(([re]) => re.test(title.replace(/[*_`]/g, '').trim())) || [null, 'plain'])[1];
    const levels = new Set(blocks.filter(b => b.t === 'h').map(b => b.level));
    const chapter = levels.size > 1 ? Math.min(...levels) : 0;
    const shape = { lead: null, parts: [] };
    const commands = [];
    let section = null;
    blocks.forEach((b, i) => {
        for (const command of ffInlineCommands([b]))
            if (!commands.includes(command)) commands.push(command);
        if (i === 0 && b.t === 'p' && !ffIsCommand(b)) { shape.lead = b.text; return; }
        if (b.t === 'h' && b.level === chapter) {
            section = null;
            shape.parts.push({ t: 'chap', title: b.text });
            return;
        }
        if (b.t === 'h') {
            section = { t: 'sec', title: b.text, sub: null, kind: kindOf(b.text), blocks: [] };
            shape.parts.push(section);
            return;
        }
        // Un bloque de comandos «!» se pinta en su sitio como tarjeta de copiar, una fila por comando.
        if (ffIsCommand(b) && b.t === 'code') {
            const lines = b.text.trim().split('\n').map(l => l.trim().replace(/^!\s+/, ''));
            for (const command of lines) if (!commands.includes(command)) commands.push(command);
            (section ? section.blocks : shape.parts).push({ t: 'cmd', lines });
            return;
        }
        // Una etiqueta («**Prototipo:**») seguida de una lista o una tabla abre una sección con ese mismo texto de título,
        // aunque la respuesta no lleve títulos; el primer párrafo que llega después la cierra.
        const label = b.t === 'p' && !b.text.includes('\n') && !FF_CALLOUT.test(b.text) && /^\*\*[^*]+:\*\*|^\*\*[^*]+\*\*:/.test(b.text) && b.text.match(FF_LABEL);
        const next = blocks[i + 1];
        // Dentro de una sección con título, la etiqueta se queda como texto de ella: abrir otra dejaría el título vacío.
        if (label && next && (next.t === 'list' || next.t === 'table') && !/^\d+\./.test(label[1]) && (!section || section.fromLabel)) {
            section = { t: 'sec', title: label[1], sub: label[2] || null, kind: kindOf(label[1]), blocks: [], fromLabel: true };
            shape.parts.push(section);
            return;
        }
        if (section && section.fromLabel && b.t === 'p') section = null;
        (section ? section.blocks : shape.parts).push(b);
    });
    const askSections = shape.parts.filter(p => p.t === 'sec' && p.kind === 'ask');
    shape.counts = {
        sections: shape.parts.filter(p => p.t === 'sec' && p.blocks.length).length,
        tables: blocks.filter(b => b.t === 'table').length,
        code: blocks.filter(b => b.t === 'code' && !ffIsCommand(b)).length,
        commands: commands.length,
        asks: askSections.reduce((n, s) => n + s.blocks.filter(b => b.t === 'list').reduce((m, b) => m + b.items.length, 0), 0),
    };
    const any = Object.values(shape.counts).some(Boolean);
    ffLast = { text, shape: any ? shape : null };
    return ffLast.shape;
}

/** «**Término:** texto» o «**Término**, texto» al principio de un punto; una negrita que sigue la frase sin puntuación
 * («**La vista** tiene…») no es un término. */
function ffTerm(text) {
    const m = text.match(/^\*\*(.+?)\*\*\s*([:,.]?)\s*([\s\S]*)$/);
    if (!m || !(m[2] || /[:,.]\s*$/.test(m[1]))) return null;
    const punct = m[2] || m[1].match(/([:,.])\s*$/)[1];
    return { term: m[1].replace(/[:,.]\s*$/, ''), punct, rest: m[3] };
}

/** Un punto de lista: si empieza por un término, el término en negrita con su puntuación de siempre y el resto detrás. */
function ffPoint(text) {
    const t = ffTerm(text);
    if (!t) return inline(text);
    const frag = document.createDocumentFragment();
    const strong = el('strong', 'ff-term');
    strong.append(inline(`${t.term}${t.punct}`));
    frag.append(strong, ' ', inline(t.rest));
    return frag;
}

/** Una lista con rayas cortas en vez del punto de siempre; las de dentro, más cortas y apagadas. */
function ffBullets(items, sub) {
    const ul = el('ul', sub ? 'ff-list sub' : 'ff-list');
    for (const it of items) {
        const li = el('li');
        li.append(ffPoint(typeof it === 'string' ? it : it.text));
        if (it.children && it.children.length) li.append(ffBullets(it.children, true));
        if (it.blocks && it.blocks.length) ffBlocks(it.blocks, li);
        ul.append(li);
    }
    return ul;
}

/** Una lista: pasos con su número si es numerada; si no, rayas. */
function ffList(b) {
    if (!b.ordered) return ffBullets(b.items);
    const ol = el('ol', 'ff-steps');
    // Los pasos siguen con su número aunque un código los haya partido en varias listas.
    if (b.start > 1) ol.style.counterReset = `ff-step ${b.start - 1}`;
    for (const it of b.items) {
        const li = el('li');
        const text = el('span');
        text.append(ffPoint(it.text));
        if (it.children.length) text.append(ffBullets(it.children, true));
        if (it.blocks && it.blocks.length) ffBlocks(it.blocks, text);
        li.append(text);
        ol.append(li);
    }
    return ol;
}

/** Una tabla: cabecera apagada y filas separadas; una primera columna de claves cortas («A», «1») va como ficha. */
function ffTable(b) {
    const box = el('div', 'ff-table');
    const table = el('table');
    const head = el('tr');
    b.head.forEach(c => { const th = el('th'); th.append(inline(c)); head.append(th); });
    table.append(head);
    const keys = b.rows.length > 0 && b.rows.every(r => /^[A-Z0-9]{1,3}$/.test(r[0] || ''));
    for (const r of b.rows) {
        const tr = el('tr');
        r.forEach((c, k) => {
            const td = el('td');
            if (k === 0 && keys) {
                td.className = 'key';
                const tag = el('span');
                tag.append(inline(c));
                td.append(tag);
            } else {
                td.append(inline(c));
                ffBreakable(td);
            }
            // Para la tabla apilada, cada celda lleva el nombre de su columna.
            td.dataset.label = String(b.head[k] || '').replace(/[*`_]/g, '');
            tr.append(td);
        });
        table.append(tr);
    }
    box.append(table);
    // Si ni así cabe (una ficha estrecha con varias columnas de código), se apila: una ficha por fila. Solo cuenta el ancho:
    // apilarla cambia su alto, y volver a medir por eso no acabaría nunca.
    let measured = -1;
    new ResizeObserver(([entry]) => {
        const w = Math.round(entry.contentRect.width);
        if (w === measured) return;
        measured = w;
        box.classList.remove('tight');
        box.classList.toggle('tight', table.offsetWidth > box.clientWidth + 1);
    }).observe(box);
    return box;
}

/** Deja partir el código en línea de una celda por sus puntos, barras, paréntesis y comas, como se leería.
 *
 * Sin ello, una tabla con rutas o nombres largos no cabía y pedía moverse de lado; y partida por cualquier letra,
 * «TomarAsync» quedaba en «Toma» y «rAsync». */
function ffBreakable(cell) {
    for (const code of cell.querySelectorAll('.md-code')) {
        for (const t of [...code.childNodes].filter(x => x.nodeType === 3)) {
            const parts = t.textContent.split(/(?<=[./\\(,])/);
            if (parts.length < 2) continue;
            const frag = document.createDocumentFragment();
            parts.forEach((p, i) => {
                frag.append(p);
                if (i < parts.length - 1) frag.append(document.createElement('wbr'));
            });
            t.replaceWith(frag);
        }
    }
}

/** Botón «Copiar» de un código o un comando. */
function ffCopy(text) {
    const copy = el('button', 'ff-copy', 'Copiar');
    copy.addEventListener('click', async e => {
        e.stopPropagation();
        copy.textContent = (await copyText(text)) ? 'Copiado ✓' : 'No se pudo copiar';
        setTimeout(() => (copy.textContent = 'Copiar'), 1500);
    });
    return copy;
}

/** Un bloque de código con su lenguaje y «Copiar»; sin barra de desplazamiento, la línea se parte donde haga falta. */
function ffCode(b) {
    const box = el('div', 'ff-code');
    const head = el('div', 'ff-code-head', b.lang || 'código');
    head.append(ffCopy(b.text));
    box.append(head, el('pre', null, b.text));
    return box;
}

/** Un párrafo: un aviso («Ojo:», «Nota:») o texto apagado con sus negritas a la vista. */
function ffParagraph(text) {
    const callout = text.match(FF_CALLOUT);
    if (callout) {
        const box = el('div', /nota/i.test(callout[1]) ? 'ff-callout note' : 'ff-callout');
        const body = el('div');
        body.append(inline(callout[2]));
        box.append(el('span', 'ff-callout-tag', callout[1]), body);
        return box;
    }
    const p = el('p', 'ff-prose');
    p.append(inline(text));
    return p;
}

/** Pinta unos bloques; una lista que sigue a un punto numerado en negrita va dentro de él. */
function ffBlocks(blocks, into) {
    let item = null;
    for (const b of blocks) {
        const numbered = b.t === 'p' && b.text.match(/^\*\*(\d+)\.\s*([\s\S]+?)\*\*\s*([\s\S]*)$/);
        if (numbered) {
            item = el('div', 'ff-item');
            item.append(el('span', 'ff-num', numbered[1]));
            const title = el('div', 'ff-item-title');
            title.append(inline(numbered[2].replace(/[:.]\s*$/, '')));
            item.append(title);
            if (numbered[3]) {
                const text = el('div', 'ff-item-text');
                text.append(inline(numbered[3].replace(/^[:.]\s*/, '')));
                item.append(text);
            }
            into.append(item);
            continue;
        }
        if (b.t === 'list' && item) { item.append(ffList(b)); continue; }
        item = null;
        if (b.t === 'cmd') into.append(ffCommands(b.lines));
        else if (b.t === 'list') into.append(ffList(b));
        else if (b.t === 'table') into.append(ffTable(b));
        else if (b.t === 'code') into.append(ffCode(b));
        else if (b.t === 'quote') { const q = el('p', 'ff-quote'); q.append(inline(b.text)); into.append(q); }
        else if (b.t === 'sec') into.append(ffSection(b));
        else into.append(ffParagraph(b.text));
    }
}

/** Una sección: su título tal cual, en el color de su estado, y su contenido en su orden.
 *
 * La que le toca a la persona («Qué te toca») lleva el aspecto de su caja, con cuántos puntos tiene, y sus listas como
 * pasos numerados. */
function ffSection(s) {
    const ask = s.kind === 'ask';
    const sec = el('div', ask ? 'ff-ask' : `ff-sec ${s.kind}`);
    const head = el('div', 'ff-head');
    head.append(inline(s.title));
    if (s.sub) {
        const sub = el('span', 'ff-sub');
        sub.append(inline(s.sub));
        head.append(sub);
    }
    const items = s.blocks.filter(b => b.t === 'list').reduce((n, b) => n + b.items.length, 0);
    if (ask && items) head.append(el('span', 'ff-count', String(items)));
    sec.append(head);
    if (!ask) {
        ffBlocks(s.blocks, sec);
        return sec;
    }
    for (const b of s.blocks) {
        if (b.t === 'list') sec.append(ffAskList(b.items));
        else ffBlocks([b], sec);
    }
    return sec;
}

/** Los puntos de lo que le toca a la persona, numerados: el término, si lo trae, de título, y lo que pide destacado. */
function ffAskList(items) {
    const ol = el('ol');
    for (const it of items) {
        const t = ffTerm(it.text);
        const li = el('li');
        const title = el('div', 'ff-q-title');
        title.append(inline(t ? t.term : it.text));
        li.append(title);
        if (t && t.rest) {
            const text = el('div', 'ff-q-text');
            ffMark(text, t.rest);
            li.append(text);
        }
        if (it.children.length) li.append(ffBullets(it.children, true));
        if (it.blocks && it.blocks.length) ffBlocks(it.blocks, li);
        ol.append(li);
    }
    return ol;
}

/** Un bloque de comandos «!» en su sitio: una fila por comando, con «Copiar». */
function ffCommands(lines) {
    const box = el('div', 'ff-cmds bare');
    for (const c of lines) {
        const row = el('div', 'ff-cmd bare');
        row.append(el('div', 'ff-cmd-line', `! ${c}`), ffCopy(`! ${c}`));
        box.append(row);
    }
    return box;
}

/** Pone un «Copiar» pequeño junto a cada comando «!» que va en línea, dentro de una frase, un punto o una tabla. */
function ffInlineCopies(root) {
    for (const code of root.querySelectorAll('.md-code')) {
        const text = code.textContent.trim();
        if (!/^!\s+\S/.test(text)) continue;
        const copy = ffCopy(text);
        copy.classList.add('inline');
        code.after(copy);
    }
}

/** Una frase que pregunta o pide algo, destacada; el resto, apagado. */
function ffMark(into, text) {
    for (const part of text.split(/(?<=[.?!])\s+/)) {
        if (!part) continue;
        const node = /\?$/.test(part) || FF_ASKS.test(part) ? el('mark') : el('span');
        node.append(inline(`${part} `));
        into.append(node);
    }
}

/** La respuesta con el formato de Arkko: todo en el orden del mensaje, con sus títulos; los capítulos, como títulos
 * sueltos sobre sus secciones; el texto que no va en una sección, en tarjetas neutras entre ellas. */
function ffFormatted(shape) {
    const root = el('div', 'ff');
    // Lo que no va en una sección, la primera frase incluida, se agrupa en tarjetas neutras entre las secciones.
    // Un título sin nada debajo también va: del mensaje no se pierde nada.
    const parts = [...(shape.lead ? [{ t: 'p', text: shape.lead }] : []), ...shape.parts];
    let loose = null;
    for (const part of parts) {
        if (part.t === 'chap') {
            loose = null;
            const title = el('div', 'ff-chap');
            title.append(inline(part.title));
            root.append(title);
            continue;
        }
        if (part.t === 'sec') {
            loose = null;
            root.append(ffSection(part));
            continue;
        }
        if (!loose) {
            loose = el('div', 'ff-loose');
            root.append(loose);
        }
        ffBlocks([part], loose);
    }
    ffInlineCopies(root);
    ffRows(root);
    return root;
}

/** Junta en una fila las secciones seguidas que se leen solas, sin tabla ni código.
 *
 * En una ficha ancha, la fila las reparte en columnas y la última crece hasta el borde, para no dejar huecos; en una
 * estrecha, siguen una debajo de otra. Lo que necesita el ancho (tablas, código, comandos y «Te toca») queda fuera. */
function ffRows(root) {
    const half = node => node.classList.contains('ff-sec') && !node.querySelector('.ff-table, .ff-code');
    let run = [];
    const close = () => {
        if (run.length > 1) {
            const row = el('div', 'ff-row');
            run[0].before(row);
            row.append(...run);
        }
        run = [];
    };
    for (const node of [...root.children]) {
        if (half(node)) run.push(node);
        else close();
    }
    close();
}

/** La respuesta final en su ficha: con el formato de Arkko si está encendido y hay algo que pintar; si no, como siempre. */
function ffSay(s, lane) {
    const words = s.images ? withoutImageMarks(s.text || '') : s.text || '';
    const shape = state.finalFormat ? ffShape(plain(words)) : null;
    if (!shape) return wfSay(s, lane, true);
    const card = paint(el('div', 'wf-say reply bare ff-card'), lane || 'main');
    const text = el('div', 'wf-say-text');
    text.append(ffFormatted(shape));
    card.append(text);
    if (s.images) card.append(gallery(s));
    return card;
}

/** El botón de la cabecera de la ficha que enciende o apaga el formato; deshabilitado si no hay nada que formatear. */
function ffButton(v, s) {
    const words = s.images ? withoutImageMarks(s.text || '') : s.text || '';
    const shape = ffShape(plain(words));
    const on = Boolean(shape) && Boolean(state.finalFormat);
    // Deshabilitado, el botón no recibe el ratón: su ficha la saca el envoltorio.
    const wrap = el('span', 'ff-btn-wrap');
    const button = el('button', `pbtn small wf-drawer-btn ff-btn${on ? ' on' : ''}`);
    const icon = svg('svg', { class: 'fit-icon', viewBox: '0 0 16 16', 'aria-hidden': 'true' });
    icon.append(svg('path', { d: FF_ICON }));
    button.append(icon);
    button.disabled = !shape;
    button.setAttribute('aria-pressed', String(on));
    button.setAttribute('aria-label', 'Formato de Arkko');
    if (shape) {
        const c = shape.counts;
        const seen = [
            [c.sections, 'sección', 'secciones'], [c.tables, 'tabla', 'tablas'], [c.code, 'bloque de código', 'bloques de código'],
            [c.commands, 'comando', 'comandos'],
        ].filter(([n]) => n).map(([n, one, many]) => `${n} ${n === 1 ? one : many}`);
        if (c.asks) seen.push(`te toca ${c.asks}`);
        hint(wrap, '✦', on ? 'Formato de Arkko, activado' : 'Ver con el formato de Arkko', 'se recuerda',
            `${seen.join(' · ')}. ${on ? 'Pulsa para verla como la escribió Claude.' : 'Pulsa para pintarla con sus secciones, tablas y comandos, en su orden y con sus títulos.'}`);
    } else {
        hint(wrap, '✦', 'Nada que formatear', null,
            'Esta respuesta no tiene secciones, tablas, código, comandos ni preguntas que Arkko sepa pintar, y se ve tal cual. El botón se activa cuando la respuesta trae alguno.');
    }
    button.addEventListener('click', () => {
        state.finalFormat = !state.finalFormat;
        post({ type: 'finalFormat', on: state.finalFormat });
        const key = v.detail || v.drawer.dataset.key;
        if (key) wfDetail(v, key);
    });
    wrap.append(button);
    return wrap;
}

/** Vuelve a pintar la ficha abierta del directo, tras cambiar el formato desde la app. */
function ffRefresh() {
    const v = state.wf;
    const key = v && (v.detail || v.drawer.dataset.key);
    if (key && !v.drawer.hidden) wfDetail(v, key);
}

// MARKDOWN: un intérprete propio que solo crea elementos de texto; nada del mensaje se ejecuta ni se carga

/** Texto con formato de una respuesta: títulos, listas, código, citas, tablas, separadores y párrafos. */
function markdown(src) {
    const root = el('div', 'md');
    const lines = src.replace(/\r/g, '').split('\n');
    let i = 0;
    const isTableSep = line => /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/.test(line);
    const tableStart = k => /^\s*\|.*\|\s*$/.test(lines[k]) && k + 1 < lines.length && isTableSep(lines[k + 1]);
    const blockStart = line => /^\s*(```|~~~|#{1,6}\s|>|[-*+]\s|\d+[.)]\s|(---|\*\*\*|___)\s*$)/.test(line);
    while (i < lines.length) {
        const line = lines[i];
        if (/^\s*$/.test(line)) { i++; continue; }

        const open = fenceOpen(line);
        if (open) {
            const block = readFence(lines, i, open);
            root.append(codeBlock(block.code, null));
            i = block.next;
            continue;
        }
        const h = line.match(/^(#{1,6})\s+(.*)$/);
        if (h) {
            const head = el('div', `md-h md-h${h[1].length}`);
            head.append(inline(h[2]));
            root.append(head);
            i++;
            continue;
        }
        if (/^\s*(---|\*\*\*|___)\s*$/.test(line)) {
            root.append(el('hr'));
            i++;
            continue;
        }
        if (/^\s*>/.test(line)) {
            const quote = [];
            while (i < lines.length && /^\s*>/.test(lines[i])) quote.push(lines[i++].replace(/^\s*>\s?/, ''));
            const q = el('blockquote');
            q.append(markdown(quote.join('\n')));
            root.append(q);
            continue;
        }
        if (tableStart(i)) {
            const cells = row => row.trim().replace(/^\||\|$/g, '').split('|').map(c => c.trim());
            const table = el('table');
            const head = el('tr');
            cells(line).forEach(c => { const th = el('th'); th.append(inline(c)); head.append(th); });
            table.append(head);
            i += 2;
            while (i < lines.length && /^\s*\|/.test(lines[i])) {
                const tr = el('tr');
                cells(lines[i++]).forEach(c => { const td = el('td'); td.append(inline(c)); tr.append(td); });
                table.append(tr);
            }
            root.append(table);
            continue;
        }
        if (/^\s*([-*+]|\d+[.)])\s+/.test(line)) {
            const items = [];
            while (i < lines.length && (/^\s*([-*+]|\d+[.)])\s+/.test(lines[i]) || (/^\s{2,}\S/.test(lines[i]) && items.length))) {
                const m = lines[i].match(/^(\s*)([-*+]|\d+[.)])\s+(.*)$/);
                const open = !m && fenceOpen(lines[i]);
                if (m) {
                    items.push({ indent: m[1].length, ordered: /\d/.test(m[2]), parts: [m[3]] });
                    i++;
                } else if (open) {
                    const block = readFence(lines, i, open);
                    items[items.length - 1].parts.push({ code: block.code });
                    i = block.next;
                } else {
                    const parts = items[items.length - 1].parts;
                    if (typeof parts[parts.length - 1] === 'string') parts[parts.length - 1] += '\n' + lines[i].trim();
                    else parts.push(lines[i].trim());
                    i++;
                }
            }
            root.append(list(items));
            continue;
        }

        const para = [];
        // Una tabla corta el párrafo aunque no haya línea en blanco antes, como en GitHub.
        while (i < lines.length && !/^\s*$/.test(lines[i]) && !(para.length && (blockStart(lines[i]) || tableStart(i)))) para.push(lines[i++]);
        const p = el('p');
        para.forEach((l, k) => {
            if (k) p.append(el('br'));
            p.append(inline(l));
        });
        root.append(p);
    }
    return root;
}

/** Apertura de un bloque de código, con tres o más ` o ~; null si la línea no abre ninguno. */
function fenceOpen(line) {
    const m = line.match(/^(\s*)(`{3,}|~{3,})/);
    // Solo cierra una línea con la misma marca, al menos igual de larga y nada detrás: así un bloque de ```` puede enseñar ``` dentro.
    return m ? { indent: m[1].length, close: new RegExp(`^\\s*${m[2][0]}{${m[2].length},}\\s*$`) } : null;
}

/** Lee un bloque de código desde su apertura: su texto, sin la sangría de la apertura, y la línea que sigue al cierre. */
function readFence(lines, start, open) {
    const cut = new RegExp(`^ {0,${open.indent}}`);
    const code = [];
    let i = start + 1;
    while (i < lines.length && !open.close.test(lines[i])) code.push(lines[i++].replace(cut, ''));
    return { code: code.join('\n'), next: i + 1 };
}

/** Lista con sus sublistas según la sangría de cada elemento. */
function list(items) {
    const make = ordered => el(ordered ? 'ol' : 'ul');
    const root = make(items[0].ordered);
    const stack = [{ indent: items[0].indent, el: root }];
    for (const item of items) {
        while (stack.length > 1 && item.indent < stack[stack.length - 1].indent) stack.pop();
        let top = stack[stack.length - 1];
        if (item.indent > top.indent && top.el.lastElementChild) {
            const sub = make(item.ordered);
            top.el.lastElementChild.append(sub);
            stack.push({ indent: item.indent, el: sub });
            top = stack[stack.length - 1];
        }
        const li = el('li');
        item.parts.forEach(part => {
            if (typeof part !== 'string') {
                li.append(codeBlock(part.code, null));
                return;
            }
            part.split('\n').forEach((t, k) => {
                if (k) li.append(el('br'));
                li.append(inline(t));
            });
        });
        top.el.append(li);
    }
    return root;
}

/** Un texto con Markdown en una sola línea: sin marcas de bloque (títulos, citas, viñetas, tablas, bloques de código), con las celdas separadas por «·» y los saltos como espacios. */
function oneLine(src) {
    return src.replace(/\r/g, '').split('\n')
        .filter(line => !/^\s*(```|~~~)/.test(line) && !/^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/.test(line))
        .map(line => line
            .replace(/^\s*\|(.*)\|\s*$/, (_, cells) => cells.split('|').map(c => c.trim()).join(' · '))
            .replace(/^\s*(#{1,6}\s+|>\s?|[-*+]\s+)/, '')
            .trim())
        .filter(Boolean)
        .join(' ');
}

/** Formato de una línea para una fila de la secuencia: como «inline», pero los enlaces quedan como texto, porque la fila entera es un botón. */
function flatInline(text) {
    const frag = inline(text);
    frag.querySelectorAll('a').forEach(a => a.replaceWith(el('span', 'md-link', a.textContent)));
    return frag;
}

/** Formato dentro de una línea: código, negrita, cursiva, tachado y enlaces; solo se pueden abrir los web. */
function inline(text) {
    const frag = document.createDocumentFragment();
    const re = /(`+)([\s\S]*?)\1|\*\*([\s\S]+?)\*\*|__([\s\S]+?)__|~~([\s\S]+?)~~|(?<![\w*])\*(?!\s)([^*]+?)\*(?![\w*])|(?<![\w_])_(?!\s)([^_]+?)_(?![\w_])|\[([^\]]+)\]\(([^)\s]+)\)/g;
    let last = 0;
    let m;
    while ((m = re.exec(text))) {
        if (m.index > last) frag.append(linked(text.slice(last, m.index)));
        if (m[1]) frag.append(el('code', 'md-code', m[2]));
        else if (m[3] || m[4]) { const b = el('strong'); b.append(inline(m[3] || m[4])); frag.append(b); }
        else if (m[5]) { const d = el('del'); d.append(inline(m[5])); frag.append(d); }
        else if (m[6] || m[7]) { const em = el('em'); em.append(inline(m[6] || m[7])); frag.append(em); }
        else if (m[8]) {
            const a = WEB_LINK.test(m[9]) ? link(m[9]) : el('span', 'md-link');
            a.append(inline(m[8]));
            // Un enlace no lleva otro dentro: «[https://…](https://…)» es muy habitual.
            a.querySelectorAll('a').forEach(x => x.replaceWith(x.textContent));
            a.title = m[9];
            frag.append(a);
        }
        last = re.lastIndex;
    }
    if (last < text.length) frag.append(linked(text.slice(last)));
    return frag;
}

// Solo se abren direcciones web: ni ficheros, ni esquemas que lancen otros programas.
const WEB_LINK = /^https?:\/\/[^\s]+$/i;
const BARE_LINK = /https?:\/\/[^\s<>"'`]+/gi;

/** Enlace que se abre en el navegador; sin texto, que lo pone quien lo usa. */
function link(url) {
    const a = el('a', 'md-link');
    a.href = url;
    return a;
}

/** Texto con sus direcciones web sueltas convertidas en enlaces; la puntuación que las cierra se queda fuera. */
function linked(text) {
    const frag = document.createDocumentFragment();
    let last = 0;
    for (const m of text.matchAll(BARE_LINK)) {
        let url = m[0].replace(/[.,;:!?]+$/, '');
        if (url.endsWith(')') && !url.includes('(')) url = url.slice(0, -1);
        if (m.index > last) frag.append(text.slice(last, m.index));
        const a = link(url);
        a.textContent = url;
        frag.append(a);
        last = m.index + url.length;
    }
    if (last < text.length) frag.append(text.slice(last));
    return frag;
}

/** Abre en el navegador un enlace del chat, a través de la app, en lugar de navegar la página. */
function openLink(e) {
    const a = e.target.closest ? e.target.closest('a.md-link') : null;
    if (!a) return;
    e.preventDefault();
    e.stopPropagation();
    post({ type: 'link', url: a.href });
}

// BUSCADOR DENTRO DE LA SESIÓN

/** Busca en los pasos de lo elegido, sin mayúsculas ni acentos, y vuelve a pintar las marcas. */
function refreshFind() {
    const f = state.find;
    const q = norm(f.query.trim());
    f.matches = q ? selectedSteps().filter(s => norm(`${s.title} ${s.text || ''} ${askText(s)}`).includes(q)).map(s => s.order) : [];
    if (f.index >= f.matches.length) f.index = f.matches.length - 1;
    $('find-count').textContent = !q ? '' : f.matches.length === 0 ? 'sin resultados'
        : f.index < 0 ? `${f.matches.length}` : `${f.index + 1} de ${f.matches.length}`;
}

/** Va a la coincidencia siguiente o anterior y la señala en el diagrama y en el chat. */
function findStep(delta) {
    const f = state.find;
    if (f.matches.length === 0) return;
    f.index = f.index < 0 ? (delta > 0 ? 0 : f.matches.length - 1) : (f.index + delta + f.matches.length) % f.matches.length;
    refreshFind();
    focusStep(f.matches[f.index], 'diagram');
    scrollDiagramTo(f.matches[f.index]);
}

/** Marca dentro del chat el texto buscado, sin tocar el resto del contenido. */
function markText(box) {
    const q = state.find.query.trim();
    if (!q) return;
    const re = new RegExp(q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'gi');
    const walker = document.createTreeWalker(box, NodeFilter.SHOW_TEXT, {
        acceptNode: node => (node.parentElement.closest('button') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT),
    });
    const nodes = [];
    while (walker.nextNode()) {
        re.lastIndex = 0;
        if (re.test(walker.currentNode.nodeValue)) nodes.push(walker.currentNode);
    }
    for (const node of nodes) {
        const frag = document.createDocumentFragment();
        const value = node.nodeValue;
        let last = 0;
        re.lastIndex = 0;
        let m;
        while ((m = re.exec(value))) {
            frag.append(value.slice(last, m.index), el('mark', null, m[0]));
            last = re.lastIndex;
        }
        frag.append(value.slice(last));
        node.replaceWith(frag);
    }
}

// SINCRONÍA ENTRE DIAGRAMA Y CHAT

/** Señala un paso en el diagrama y en el chat; desde el diagrama, abre los agentes que lo contienen y lleva el chat hasta él. */
function focusStep(order, from) {
    const s = stepAt(order);
    if (!s) return;
    // Repitiendo, señalar un paso es llevar la repetición hasta él.
    const index = state.replay.index !== null ? selectedSteps().findIndex(x => x.order === order) : -1;
    if (index >= 0) {
        goTo(index);
        if (state.replay.playing) schedule();
        return;
    }
    state.focus = order;
    state.focusMark = from === 'diagram';
    if (from === 'diagram') {
        reveal(s);
        renderChat(false);
        const node = $('chat').querySelector(`[data-order="${order}"]`);
        if (node) node.scrollIntoView({ block: 'center' });
        decorate();
    } else {
        // Sin repintar el chat: un doble clic o un arrastre sobre el texto conservan su selección.
        markFocus();
        decorate();
        scrollDiagramTo(order);
    }
}

/** Despliega en la secuencia los agentes que contienen un paso. */
function reveal(s) {
    for (const call of ancestors(s)) {
        if (state.folded.delete(call.childAgentId) && state.seqView) renderDiagram(false);
    }
}

// REPETICIÓN Y BOCADILLOS

/** Va a un paso de la repetición: lo marca en el diagrama y en el chat y saca su bocadillo. */
function goTo(index) {
    const steps = selectedSteps();
    if (steps.length === 0) return;
    const r = state.replay;
    r.index = Math.max(0, Math.min(steps.length - 1, index));
    const s = steps[r.index];
    state.focus = s.order;
    state.focusMark = true;
    document.querySelector('.app').classList.add('replaying');
    reveal(s);
    renderChat(false);
    const node = $('chat').querySelector(`[data-order="${s.order}"]`);
    if (node) node.scrollIntoView({ block: 'center' });
    // El mapa cambia con cada paso (aparecen y se apagan agentes); la secuencia solo se decora.
    if (state.tab === 'map' && !state.seqView) renderDiagram(false);
    else decorate();
    scrollDiagramTo(s.order);
    updatePlayer(steps);
}

/** Pone al día la barra: botones, posición, rótulo y el botón de reproducir o pausar. */
function updatePlayer(steps) {
    const r = state.replay;
    const none = steps.length === 0;
    ['p-first', 'p-prev', 'p-play', 'p-next', 'p-last'].forEach(id => { $(id).disabled = none; });
    const range = $('p-range');
    range.disabled = none;
    range.max = Math.max(0, steps.length - 1);
    range.value = r.index ?? 0;
    const play = $('p-play');
    play.textContent = r.playing ? '⏸' : '▶';
    play.title = r.playing ? 'Pausa (espacio)' : 'Reproducir (espacio)';
    $('p-label').textContent = none ? 'Sin pasos que repetir'
        : r.index === null ? `${steps.length} pasos · repítelos aquí`
        : `Paso ${r.index + 1} de ${steps.length} · ${clock(steps[r.index].start)}`;
}

/** Cuánto se queda un paso a la vista al reproducir: los mensajes, según lo largos que son; las herramientas, poco. */
function dwell(s) {
    const talks = ['prompt', 'command', 'reply', 'thinking', 'notification'].includes(s.kind);
    const length = talks ? (s.text || '').length : s.questions ? askText(s).length : 0;
    const ms = s.kind === 'tool' && !s.questions ? 700 : Math.min(4500, 1100 + length * 20);
    return ms / state.replay.speed;
}

/** Reproduce desde el paso actual, o desde el principio si no había empezado o ya estaba al final. */
function play() {
    const r = state.replay;
    const steps = selectedSteps();
    if (steps.length === 0) return;
    if (r.index === null || r.index >= steps.length - 1) goTo(0);
    r.playing = true;
    updatePlayer(steps);
    schedule();
}

/** Programa el paso siguiente según lo que tarda el actual; al llegar al final, se para. */
function schedule() {
    const r = state.replay;
    clearTimeout(r.timer);
    if (!r.playing) return;
    const steps = selectedSteps();
    r.timer = setTimeout(() => {
        if (r.index >= steps.length - 1) {
            pause();
            return;
        }
        goTo(r.index + 1);
        schedule();
    }, dwell(steps[r.index]));
}

/** Pausa la reproducción, dejando el paso a la vista. */
function pause() {
    const r = state.replay;
    r.playing = false;
    clearTimeout(r.timer);
    updatePlayer(selectedSteps());
}

/** Sale de la repetición: todo vuelve a verse entero y sin bocadillo. */
function exitReplay() {
    pause();
    const was = state.replay.index !== null;
    state.replay.index = null;
    document.querySelector('.app').classList.remove('replaying');
    if (was && state.trace) renderDiagram(false);
    updatePlayer(selectedSteps());
}

/** Mueve la repetición unos pasos adelante o atrás; si no había empezado, empieza. */
function stepReplay(delta) {
    const r = state.replay;
    goTo(r.index === null ? (delta > 0 ? 0 : selectedSteps().length - 1) : r.index + delta);
    if (r.playing) schedule();
}

/** Lo que dice el bocadillo de un paso. */
function bubbleText(s) {
    if (s.questions) return `❓ ${clip(s.questions.map(q => q.question).join(' · '), 400)}`;
    if (s.shell) return `! ${clip(shellCommand(s), 400)}`;
    switch (s.kind) {
        case 'prompt':
        case 'command':
        case 'reply':
            return clip(plain(s.text), 400);
        case 'thinking': return clip(s.text, 400) || 'Pensando…';
        case 'tool': return `🔧 ${s.title}${s.isError ? '\n❌ Ha fallado' : ''}`;
        case 'skill': return `🧙 Cargo la skill ${s.title}`;
        case 'agent': return `🤖 Lanzo ${s.title}`;
        case 'notification': return `🔔 ${clip(s.text || s.title, 400)}`;
        default: return s.title;
    }
}

/** Bocadillo tipo cómic sobre quien actúa en el paso: encima de su fila o su tarjeta, o debajo si no cabe. */
function bubble(s) {
    const geo = state.geo;
    const stage = $('diagram').querySelector('.stage');
    if (!geo || !stage) return;
    const who = speaker(s);
    let x;
    let y;
    if (geo.kind === 'sequence') {
        x = geo.x(who);
        y = geo.rowY.get(s.order);
    } else {
        const p = geo.card(who);
        if (p) {
            x = p.x + 40;
            y = p.y;
        }
    }
    if (x === undefined || y === undefined) return;

    const thinks = s.kind === 'thinking';
    const box = el('div', 'comic' + (s.isError ? ' error' : '') + (thinks ? ' thought' : ''), bubbleText(s));
    stage.append(box);
    const w = box.offsetWidth;
    const h = box.offsetHeight;
    const left = Math.max(8, Math.min(x - 28, stage.clientWidth - w - 8));
    const above = y - h - 16 >= 4;
    const top = above ? y - h - 16 : y + 18;
    box.style.left = `${left}px`;
    box.style.top = `${top}px`;

    const tail = el('div', 'comic-tail' + (above ? '' : ' up') + (thinks ? ' puff' : ''));
    tail.style.left = `${x - 7}px`;
    // El pico se mete bajo el borde del bocadillo; las burbujas del pensamiento quedan sueltas, fuera de él.
    const gap = thinks ? (above ? h + 1 : -13) : (above ? h - 8 : -6);
    tail.style.top = `${top + gap}px`;
    stage.append(tail);
}

// DIVISOR Y CHAT MINIMIZADO

/** Lee una preferencia de la vista guardada en este equipo; si no se puede, la de siempre. */
function remembered(key, fallback) {
    try {
        const value = localStorage.getItem(`sesiones.${key}`);
        return value === null ? fallback : JSON.parse(value);
    } catch {
        return fallback;
    }
}

/** Guarda una preferencia de la vista en este equipo; si no se puede, no pasa nada. */
function remember(key, value) {
    try {
        localStorage.setItem(`sesiones.${key}`, JSON.stringify(value));
    } catch {
        // Sin almacenamiento, la vista funciona igual con los tamaños de siempre.
    }
}

/** Pone el ancho del chat, entre un mínimo legible y dos tercios del espacio. */
function setChatWidth(px) {
    const panes = $('panes');
    // Con el panel escondido aún no hay ancho que medir: se respeta el guardado.
    const width = panes.clientWidth ? Math.max(300, Math.min(panes.clientWidth * 0.68, px)) : Math.max(300, px);
    panes.style.setProperty('--chat-w', `${width}px`);
    return width;
}

/** Minimiza el chat a una pestaña fina, o lo vuelve a abrir; si se pide suave, deslizándose. */
function setChatOpen(open, smooth) {
    const panes = $('panes');
    const pane = $('chat-pane');
    const turn = (state.chatTurn || 0) + 1;
    state.chatTurn = turn;
    // El ancho cambia de una vez y lo que se anima es solo la capa del chat: animar el ancho recolocaría en cada
    // fotograma el chat entero y el lienzo, y se movía a trompicones.
    const apply = () => {
        if (state.chatTurn !== turn) return;
        panes.classList.toggle('chat-closed', !open);
        if (open && smooth) pane.animate([{ opacity: 0, transform: 'translateX(32px)' }, { opacity: 1, transform: 'none' }],
            { duration: 240, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)' });
        // El lienzo se recoloca ya, en un solo deslizamiento, y no cuando acabe de notar el cambio de tamaño.
        if (smooth && state.wf) {
            wfAnchorView(state.wf);
            renderDiagram(false);
        }
    };
    if (!open && smooth && !panes.classList.contains('chat-closed')) {
        // Se queda en su último fotograma hasta esconder el chat: si no, asomaría entero un instante.
        const out = pane.animate([{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateX(32px)' }],
            { duration: 170, easing: 'ease-in', fill: 'forwards' });
        out.onfinish = () => {
            apply();
            out.cancel();
        };
    } else apply();
    if (open) chatUnseen(0);
    chatReading();
    remember('chatOpen', open);
}

/** Dice a la app si la persona lee la sesión, con el chat a la vista: solo entonces la da por revisada sola. */
function chatReading() {
    post({ type: 'reading', on: !$('panes').classList.contains('chat-closed') });
}

/** Pone el aviso de la barra del chat minimizado: cuántos mensajes nuevos hay sin leer; con 0, lo quita. */
function chatUnseen(count) {
    state.chatUnseen = count;
    const tab = $('chat-tab');
    tab.classList.toggle('unseen', count > 0);
    tab.querySelector('.chat-tab-icon').dataset.count = count > 9 ? '9+' : String(count);
}

/** Engancha el divisor: al arrastrarlo cambia el ancho del chat, que se recuerda al soltar. */
function initSplitter() {
    const splitter = $('splitter');
    splitter.addEventListener('pointerdown', e => {
        splitter.setPointerCapture(e.pointerId);
        splitter.classList.add('drag');
        const move = ev => setChatWidth($('panes').getBoundingClientRect().right - ev.clientX);
        const up = ev => {
            splitter.releasePointerCapture(ev.pointerId);
            splitter.classList.remove('drag');
            splitter.removeEventListener('pointermove', move);
            splitter.removeEventListener('pointerup', up);
            remember('chatWidth', setChatWidth($('panes').getBoundingClientRect().right - ev.clientX));
        };
        splitter.addEventListener('pointermove', move);
        splitter.addEventListener('pointerup', up);
    });
    setChatWidth(remembered('chatWidth', 420));
    setChatOpen(remembered('chatOpen', true));
    setShowTools(remembered('showTools', false));
    $('chat-tools').addEventListener('click', () => {
        setShowTools(!state.showTools);
        renderChat(false);
    });
    document.addEventListener('selectionchange', () => {
        if (state.chatStale && !selectingInChat()) renderChat(false);
    });
    $('chat-min').addEventListener('click', () => setChatOpen(false, true));
    $('chat-tab').addEventListener('click', () => setChatOpen(true, true));
    $('chat-tab').querySelector('.chat-tab-icon').append(flowIcon('reply', 'flow-icon'));
    $('chat-head-icon').append(flowIcon('reply', 'flow-icon'));
}

// LOS CAMBIOS DE LA SESIÓN

// Cada cuánto se vuelve a mirar git en directo, aunque no llegue nada nuevo.
const CHANGES_EVERY = 10000;

/** Si se enseña lo de git: con una sesión elegida, en cualquier vista, y solo en la ventana nueva, que es la que lo lee. */
function changesOn() {
    return Boolean(window.invokeCSharpAction) && Boolean(state.trace);
}

/** Si el panel de cambios está abierto, no minimizado a su pestaña. */
function changesOpen() {
    return !$('panes').classList.contains('changes-closed');
}

/** Pone o quita lo de git según la vista que se ve (el panel, su pestaña y la rama de arriba) y, si se ve, lo pide. */
function syncChanges() {
    showChanges();
    if (changesOn()) requestChanges(true);
}

/** Enseña o esconde el panel de cambios, su pestaña y la rama de arriba, sin pedir nada. La rama va sobre el lienzo, así
 * que solo se ve en las vistas que lo tienen (el Flujo y el Diagrama). */
function showChanges() {
    const on = changesOn();
    $('panes').classList.toggle('changes-on', on);
    $('flow-git').hidden = !on || !['live', 'diagram'].includes(currentView());
    $('flow-git').classList.toggle('in-stamp', currentView() === 'diagram');
}

/** Abre o minimiza el panel de cambios; si se pide suave, su capa se desliza, como la del chat. */
function setChangesOpen(open, smooth) {
    const panes = $('panes');
    const pane = $('changes-pane');
    const apply = () => {
        panes.classList.toggle('changes-closed', !open);
        if (open && smooth) pane.animate([{ opacity: 0, transform: 'translateX(32px)' }, { opacity: 1, transform: 'none' }],
            { duration: 240, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)' });
        if (smooth && state.wf) {
            wfAnchorView(state.wf);
            renderDiagram(false);
        }
    };
    if (!open && smooth && !panes.classList.contains('changes-closed')) {
        const out = pane.animate([{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateX(32px)' }],
            { duration: 170, easing: 'ease-in', fill: 'forwards' });
        out.onfinish = () => {
            apply();
            out.cancel();
        };
    } else apply();
    remember('changesOpen', open);
    if (open) requestChanges(true);
}

/** Pide a la app lo cambiado: siempre lo pendiente, que se ve arriba con la rama; todos los ficheros si el panel está en
 * Repositorio; y toda la rama si los cambios no se limitan a lo sin commitear. Al momento o, si llegan muchas novedades
 * seguidas, al medio segundo de la última. */
function requestChanges(now) {
    clearTimeout(state.changes.timer);
    if (!changesOn()) return;
    const go = () => {
        post({ type: 'changes', scope: 'pending' });
        if (changesOpen() && state.changes.view === 'repo') post({ type: 'changes', scope: 'repo' });
        if (!changesOpen() || state.changes.scope !== 'branch') return;
        post({ type: 'changes', scope: 'branch' });
        const round = reviewRound();
        if (round) {
            state.changes.sinceAsked = round.base;
            post({ type: 'changes', scope: `since:${round.base}` });
        }
        // La PR se busca una vez por sesión, al ver toda la rama: habla con el remoto.
        if (!state.changes.prAsked) {
            state.changes.prAsked = true;
            post({ type: 'reviewPr' });
        }
    };
    if (now) go();
    else state.changes.timer = setTimeout(go, 500);
}

/** Cambia los cambios del panel entre toda la rama y solo lo sin commitear, lo recuerda y lo pide. */
function setChangesScope(scope) {
    state.changes.scope = scope === 'pending' ? 'pending' : 'branch';
    remember('changesWipOnly', state.changes.scope === 'pending');
    renderChanges();
    requestChanges(true);
}

/** Cambia el panel entre la pestaña de los cambios y la del repositorio entero; cada una conserva por dónde iba. */
function setChangesView(view) {
    const next = view === 'repo' ? 'repo' : 'changes';
    if (next === state.changes.view) return;
    const box = $('changes');
    state.changes.tops[state.changes.view] = box.scrollTop;
    state.changes.view = next;
    remember('changesView', next);
    renderChanges();
    box.scrollTop = state.changes.tops[next] || 0;
    requestChanges(true);
}

/** El alcance de lo que enseña el panel: el repositorio entero, toda la rama o lo sin commitear. Las notas abiertas aparte
 * siempre van con los cambios. */
function panelScope() {
    return state.changes.view === 'repo' && PIECE !== 'review' ? 'repo' : state.changes.scope;
}

/** Las rutas de lo que está sin commitear, para marcarlas con su punto; se vuelven a sacar solo si ha llegado otro pendiente. */
function uncommitted() {
    const d = state.changes.sets.pending;
    if (!state.changes.wip || state.changes.wip.from !== d)
        state.changes.wip = { from: d, paths: new Set(d && d.files ? d.files.map(f => f.path) : []) };
    return state.changes.wip.paths;
}

/** El punto de un fichero sin commitear, en el hueco de la flecha de las carpetas de su nivel, para que los nombres no se
 * muevan lleven punto o no. */
function wipDot(left) {
    const dot = el('span', 'chg-wip');
    dot.style.left = `${left}px`;
    hint(dot, '●', 'Sin commitear', null, 'Tiene cambios que aún no están en ningún commit.');
    return dot;
}

/** Clave con que se guarda el diff de un fichero (contra su base) o el fichero entero. */
function changeKey(kind, base, path) {
    return kind === 'changeDiff' ? `changeDiff:${base || 'HEAD'}:${path}` : `changeFile::${path}`;
}

/** Recibe lo cambiado de un alcance: olvida el diff y el fichero de lo que ha cambiado desde la vez anterior, y pinta lo
 * que lo enseña. Lo cambiado desde la base de una ronda va con toda la rama, a la que sustituye mientras se ve la ronda. */
function receiveChanges(m) {
    const slot = String(m.scope).startsWith('since:') ? 'since' : m.scope;
    if (!state.trace || m.id !== state.trace.id || !(slot in state.changes.sets)) return;
    const scope = slot === 'since' ? 'branch' : slot;
    const old = state.changes.sets[slot];
    const sig = f => `${f.added}|${f.deleted}|${f.status}`;
    const before = new Map(((old && old.scope === m.scope && old.files) || []).map(f => [f.path, sig(f)]));
    // Tras un aviso de la carpeta, el fichero de la ficha abierta se vuelve a pedir: mientras, sigue el que había.
    const open = m.fresh ? changesOpenPath(scope) : null;
    for (const f of m.files || []) {
        if (before.get(f.path) === sig(f) || f.path === open) continue;
        for (const key of [...state.changes.texts.keys()]) if (key.endsWith(`:${f.path}`)) state.changes.texts.delete(key);
    }
    state.changes.sets[slot] = m;
    if (m.fresh) changesReread(m, scope);
    if (slot === 'repo' && finder.box && finder.mode === 'files') finderRun();
    if (slot === 'pending') renderGit();
    // Lo pendiente pone y quita los puntos de lo sin commitear en cualquiera de las dos pestañas.
    if (scope === panelScope() || slot === 'pending') renderChanges();
    const v = state.wf;
    if (v && v.detail && v.detail.startsWith(`file:${scope}:`) && !v.drawer.hidden) wfDetail(v, v.detail);
}

/** La ruta del fichero cuya ficha está abierta, si es de un alcance; null si no hay ninguna. */
function changesOpenPath(scope) {
    const v = state.wf;
    const prefix = `file:${scope}:`;
    return v && v.detail && v.detail.startsWith(prefix) && !v.drawer.hidden ? v.detail.slice(prefix.length) : null;
}

/** Tras un aviso de la carpeta, lo leído de cada fichero puede ser viejo aunque cuente las mismas líneas: se olvida, y el
 * de la ficha abierta se vuelve a pedir; mientras llega, se sigue viendo el que había, sin pasar por «leyendo». */
function changesReread(m, scope) {
    const open = changesOpenPath(scope);
    for (const key of [...state.changes.texts.keys()]) if (!open || !key.endsWith(`:${open}`)) state.changes.texts.delete(key);
    const f = open && (m.files || []).find(x => x.path === open);
    if (!f) return;
    if (f.status !== 'untracked') post({ type: 'changeDiff', path: open, base: m.base, oldPath: f.oldPath });
    if (f.status !== 'deleted') post({ type: 'changeFile', path: open, base: m.base, oldPath: f.oldPath });
}

/** Recibe el diff o el fichero entero que se pidió y, si su ficha está abierta, la pinta con él; si es igual al que ya
 * se tenía, no se vuelve a pintar. */
function receiveChangeText(m) {
    if (!state.trace || m.id !== state.trace.id) return;
    const key = changeKey(m.type, m.base, m.path);
    const old = state.changes.texts.get(key);
    if (old && old.text === m.text && old.error === m.error && old.truncated === m.truncated) return;
    m.seq = ++state.changes.textSeq;
    state.changes.texts.set(key, m);
    const v = state.wf;
    if (v && v.detail && v.detail.endsWith(`:${m.path}`) && v.detail.startsWith('file:') && !v.drawer.hidden) wfDetail(v, v.detail);
    if (m.type === 'changeFile') {
        // Ya ha llegado: si después se olvida (porque el fichero cambia), la vista previa lo vuelve a pedir.
        finder.asked.delete(m.path);
        if (finder.box) finderPreview();
    }
}

// Letra de cada estado de un fichero, como la de git, y cómo se dice.
const CHANGE_STATUS = {
    added: ['A', 'Nuevo'], untracked: ['A', 'Nuevo, sin añadir a git'], modified: ['M', 'Cambiado'], deleted: ['D', 'Borrado'],
    renamed: ['R', 'Renombrado'], copied: ['C', 'Copiado'], type: ['T', 'Cambia de tipo'], tracked: ['', 'En el repositorio'],
};

/** Las líneas que entran y salen, en verde y rojo; «binario» si git no las cuenta. */
function changeNums(added, deleted, binary) {
    const nums = el('span', 'chg-num');
    if (binary) nums.textContent = 'binario';
    else nums.append(el('span', 'plus', `+${added || 0}`), el('span', 'minus', `−${deleted || 0}`));
    return nums;
}

/** Suma de las líneas que entran y salen de unos ficheros. */
function changeTotals(files) {
    return {
        added: files.reduce((sum, f) => sum + (f.added || 0), 0),
        deleted: files.reduce((sum, f) => sum + (f.deleted || 0), 0),
    };
}

/** Fila de un fichero cambiado: lo que le pasó, su nombre y sus líneas; en revisión, su casilla, si ha cambiado desde que
 * se revisó y cuántas notas lleva. Al pulsarla se abre su ficha.
 * @param {number} [dotAt] En el árbol del panel, dónde va el punto si está sin commitear; sin él, no lleva punto. */
function changeRow(f, scope, indent, dotAt) {
    const v = state.wf;
    const review = reviewing(scope) ? reviewMarkOf(f.path) : null;
    const row = el('div', 'chg-file' + (v && v.detail === `file:${scope}:${f.path}` ? ' on' : '') + (review ? review.reviewed ? ' reviewed' : ' left' : ''));
    row.dataset.path = f.path;
    if (indent !== undefined) row.style.paddingLeft = `${indent}px`;
    if (dotAt !== undefined && uncommitted().has(f.path)) row.append(wipDot(dotAt));
    const line = el('div', 'chg-line');
    if (review) line.append(reviewCheck(f.path, review));
    const [letter, word] = CHANGE_STATUS[f.status] || ['M', 'Cambiado'];
    const mark = el('span', `chg-status ${f.status}`, letter);
    hint(mark, letter, word, null, f.oldPath ? `Antes: ${f.oldPath}` : null);
    const cut = f.path.lastIndexOf('/');
    line.append(mark, el('span', 'chg-name' + (f.status === 'deleted' ? ' gone' : ''), cut >= 0 ? f.path.slice(cut + 1) : f.path));
    if (review && review.changed) {
        const changed = el('span', 'rv-changed', '↺');
        hint(changed, '↺', 'Ha cambiado desde que se revisó', null, 'Estaba revisado, pero su contenido es otro: vuelve a estar sin revisar.');
        line.append(changed);
    }
    const round = review ? reviewRoundOf(f.path) : 0;
    if (round) {
        const mark = el('span', 'rv-round', `R${round}`);
        hint(mark, '↻', `Cambiado en la ronda ${round}`, null,
            `Lo tocó /opsx:review en su ronda ${round}. «Ronda ${round}» en la tarjeta de la revisión enseña solo lo cambiado desde entonces.`);
        line.append(mark);
    }
    const notes = review ? reviewOf().notes.filter(n => n.file === f.path).length : 0;
    if (notes) line.append(el('span', 'rv-count', `✎ ${notes}`));
    const eye = openedEye(scope, f.path);
    if (eye) line.append(eye);
    line.append(changeNums(f.added, f.deleted, f.added === null && f.deleted === null));
    row.append(line);
    row.addEventListener('click', () => openChange(scope, f.path));
    row.addEventListener('contextmenu', e => {
        if (changesInCork()) changeOpenMenu(e, scope, f.path);
    });
    return row;
}

/** El ojo de un fichero que ya tiene su panel, con dónde está; null si no lo tiene. */
function openedEye(scope, path) {
    const opened = openFileOf(scope, path);
    if (!opened) return null;
    const eye = svg('svg', { class: 'chg-open', viewBox: '0 0 24 24', 'aria-hidden': 'true' });
    eye.append(svg('path', { d: 'M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z' }), svg('circle', { cx: 12, cy: 12, r: 3 }));
    hint(eye, '👁', 'Abierto', opened.place ? `En ${opened.place}` : null, 'Ya tiene su panel: pulsa el fichero para ir a él.');
    return eye;
}

// ICONOS DE FICHERO

// Piezas de los dibujos, de 16×16 y de trazo, como los de FLOW_ICONS.
const FT_SHEET = '<path d="M4 1.5h5.5l3 3v10h-8.5zM9.5 1.5v3h3"/>';
const ftTile = (t, size) => `<rect x="1.5" y="1.5" width="13" height="13" rx="2"/><text x="14" y="13" font-size="${size}" text-anchor="end">${t}</text>`;
const ftLetters = (t, size) => `<text x="8" y="11.6" font-size="${size}" text-anchor="middle">${t}</text>`;

// Cada tipo: cómo se llama en la ficha, el color que usa (su variable --ft-…) y su dibujo.
const FILE_TYPES = {
    cs: ['C#', 'cs', ftLetters('C#', 7.6)],
    razor: ['Razor', 'razor', ftLetters('@', 10)],
    markup: ['Marcado', 'markup', FT_SHEET + '<path d="M6.8 8.3L5.5 9.8l1.3 1.5M9.8 8.3l1.3 1.5l-1.3 1.5"/>'],
    js: ['JavaScript', 'js', ftLetters('JS', 8.2)],
    ts: ['TypeScript', 'ts', ftTile('TS', 6.6)],
    vue: ['Vue', 'vue', '<path d="M1.3 2.5L8 14L14.7 2.5M4.6 2.5L8 8.3l3.4-5.8"/>'],
    css: ['Estilos', 'css', '<path d="M8 1.5a6.5 6.5 0 1 0 0 13c1 0 1.4-.8 1-1.6c-.5-.9.1-1.9 1.1-1.9h1.4a3 3 0 0 0 3-3C14.5 4.4 11.6 1.5 8 1.5z"/><circle class="ft-fill" cx="5" cy="7" r="1"/><circle class="ft-fill" cx="8" cy="4.6" r="1"/><circle class="ft-fill" cx="11" cy="6.4" r="1"/>'],
    json: ['JSON', 'json', '<path d="M5.5 2.5c-1.4 0-2 .6-2 2v1.4c0 1-.5 1.6-1.5 2.1c1 .5 1.5 1.1 1.5 2.1v1.4c0 1.4.6 2 2 2M10.5 2.5c1.4 0 2 .6 2 2v1.4c0 1 .5 1.6 1.5 2.1c-1 .5-1.5 1.1-1.5 2.1v1.4c0 1.4-.6 2-2 2"/><circle class="ft-fill" cx="8" cy="8" r=".9"/>'],
    yaml: ['YAML', 'yaml', ftTile('YML', 5)],
    table: ['Tabla', 'table', FT_SHEET + '<path d="M5.8 7.5h5v5h-5zM5.8 10h5M8.3 7.5v5"/>'],
    md: ['Markdown', 'md', ftTile('MD', 5.8)],
    readme: ['Léeme', 'md', '<path d="M2 3.5c2-1 4-1 6 .5c2-1.5 4-1.5 6-.5v9c-2-1-4-1-6 .5c-2-1.5-4-1.5-6-.5zM8 4v9"/>'],
    agent: ['Instrucciones del agente', 'agent', '<path d="M3.5 5.5h9v7h-9zM8 2.5v3M5.8 8.5h.4M9.8 8.5h.4M6 10.5h4M2 8v2M14 8v2"/>'],
    text: ['Texto', 'conf', FT_SHEET + '<path d="M6 8h4.5M6 10.5h4.5"/>'],
    script: ['Script', 'script', '<path d="M2.5 4l4 4l-4 4M8 12.5h5.5"/>'],
    py: ['Python', 'py', ftTile('PY', 6.6)],
    proj: ['Proyecto', 'proj', '<path d="M8 2l6.5 3.2L8 8.4L1.5 5.2zM1.5 8L8 11.2L14.5 8M1.5 10.8L8 14l6.5-3.2"/>'],
    pkg: ['Paquete', 'proj', '<path d="M2 5l6-3l6 3v6.5l-6 3l-6-3z"/><path d="M2 5l6 3l6-3M8 8v6.5M5 3.5l6 3"/>'],
    lock: ['Bloqueo de versiones', 'bin', '<rect x="3" y="7" width="10" height="7.5" rx="1.5"/><path d="M5.3 7V5a2.7 2.7 0 0 1 5.4 0v2M8 10v1.6"/>'],
    conf: ['Configuración', 'conf', '<circle cx="8" cy="8" r="2"/><circle cx="8" cy="8" r="4.4"/><path d="M8 3.6V1.6M8 12.4v2M3.6 8h-2M12.4 8h2M4.9 4.9L3.5 3.5M11.1 11.1l1.4 1.4M11.1 4.9l1.4-1.4M4.9 11.1l-1.4 1.4"/>'],
    git: ['Git', 'git', '<circle cx="4.5" cy="3.5" r="1.6"/><circle cx="4.5" cy="12.5" r="1.6"/><circle cx="11.5" cy="5.5" r="1.6"/><path d="M4.5 5.1v5.8M11.5 7.1c0 2.4-2 3-5.6 4.4"/>'],
    img: ['Imagen', 'img', '<rect x="1.5" y="2.5" width="13" height="11" rx="1.5"/><path d="M1.5 11.5l3.8-3.8l3 3l2-2l4.2 4.2"/><circle cx="10.8" cy="5.8" r="1.2"/>'],
    font: ['Fuente', 'font', ftTile('Aa', 6.4)],
    pdf: ['PDF', 'pdf', ftTile('PDF', 5)],
    sql: ['SQL', 'sql', '<ellipse cx="8" cy="3.8" rx="5.5" ry="2.3"/><path d="M2.5 3.8v8.4c0 1.3 2.5 2.3 5.5 2.3s5.5-1 5.5-2.3V3.8M2.5 8c0 1.3 2.5 2.3 5.5 2.3s5.5-1 5.5-2.3"/>'],
    bin: ['Binario', 'bin', '<rect x="3.5" y="3.5" width="9" height="9" rx="1.5"/><path d="M6 1.5v2M10 1.5v2M6 12.5v2M10 12.5v2M1.5 6h2M1.5 10h2M12.5 6h2M12.5 10h2M6.3 6.3h3.4v3.4H6.3z"/>'],
    zip: ['Comprimido', 'bin', '<rect x="2.5" y="1.5" width="11" height="13" rx="1.5"/><path d="M8 1.5v1.5M7 4h1M8 5h1M7 6h1M8 7h1"/><rect x="6.6" y="8.5" width="2.8" height="3" rx=".6"/>'],
    web: ['Petición HTTP', 'web', '<circle cx="8" cy="8" r="6.5"/><path d="M1.5 8h13M8 1.5c2.2 2 2.2 11 0 13M8 1.5c-2.2 2-2.2 11 0 13"/>'],
    test: ['Test', 'test', '<path d="M6 1.5h4M6.6 1.5v4.3L2.4 12.6c-.5.9.1 1.9 1.1 1.9h9c1 0 1.6-1 1.1-1.9L9.4 5.8V1.5M4.3 9.5h7.4"/>'],
    license: ['Licencia', 'conf', '<circle cx="8" cy="6" r="4.3"/><path d="M5.4 9.4L4.3 14.5L8 12.8l3.7 1.7l-1.1-5.1"/><circle cx="8" cy="6" r="1.6"/>'],
    docker: ['Docker', 'web', '<path d="M1.5 8.5h11.3c.8-1.4 1.2-2.4 1.7-2.4M1.5 8.5c0 3 2.4 5 6 5c3.3 0 5.7-2.1 6.5-5.4"/><path d="M3.5 6.2h2.2v2.3H3.5zM5.7 6.2h2.2v2.3H5.7zM7.9 6.2h2.2v2.3H7.9zM5.7 3.9h2.2v2.3H5.7z"/>'],
    plain: ['Sin extensión', 'conf', FT_SHEET],
    unknown: ['Tipo sin icono', 'unknown', '<path d="M4 1.5h5.5l3 3v10h-8.5zM9.5 1.5v3h3" stroke-dasharray="1.6 1.4"/><path d="M6.6 7.3a1.55 1.55 0 1 1 2.1 1.4c-.45.2-.65.5-.65.95v.35M8.05 12.1v.1"/>'],
};

// Nombres que tienen su propio icono, ganen a su extensión.
const FILE_TYPE_NAMES = {
    'readme.md': 'readme', 'claude.md': 'agent', 'agents.md': 'agent', 'package.json': 'pkg', 'package-lock.json': 'lock',
    license: 'license', 'license.md': 'license', 'license.txt': 'license', dockerfile: 'docker',
};

// Extensiones de cada tipo.
const FILE_TYPE_EXTS = Object.fromEntries(Object.entries({
    cs: 'cs csx', razor: 'razor cshtml', markup: 'axaml xaml html htm xml resx manifest', js: 'js mjs cjs jsx', ts: 'ts tsx mts cts',
    vue: 'vue', css: 'css scss less', json: 'json jsonl jsonc', yaml: 'yaml yml toml', table: 'csv tsv xlsx xls', md: 'md mdx',
    text: 'txt log', script: 'ps1 psm1 psd1 sh bash bat cmd', py: 'py', proj: 'csproj sln slnx props targets', lock: 'lock',
    conf: 'editorconfig npmrc env config ini', git: 'gitignore gitattributes gitkeep gitmodules', img: 'png jpg jpeg gif webp ico svg bmp',
    font: 'woff2 woff ttf otf', pdf: 'pdf', sql: 'sql', bin: 'dll exe', zip: 'zip 7z gz tgz rar', web: 'http rest',
}).flatMap(([type, exts]) => exts.split(' ').map(ext => [ext, type])));

// Carpetas conocidas: el emblema que llevan dentro y el color de su tipo.
const FOLDER_TYPES = {
    src: 'src', lib: 'src', docs: 'docs', doc: 'docs', openspec: 'docs', scripts: 'scripts', tools: 'scripts', tests: 'tests', test: 'tests',
    assets: 'assets', images: 'assets', wwwroot: 'assets', '.github': 'git', '.githooks': 'git', '.claude': 'agent',
};
const FOLDER_EMBLEMS = {
    src: ['ts', '<path d="M6.6 7.6L5.3 9.2l1.3 1.6M9.4 7.6l1.3 1.6l-1.3 1.6"/>'],
    docs: ['md', '<path d="M5.5 8.2h5M5.5 10.4h3.4"/>'],
    scripts: ['script', '<path d="M5.4 7.8l1.6 1.4l-1.6 1.4M8.6 10.6h2.2"/>'],
    tests: ['test', '<path d="M5.6 9.3l1.5 1.4l3-3"/>'],
    assets: ['img', '<path d="M4.8 11l2-2l1.6 1.6l1.2-1.2l1.6 1.6"/>'],
    git: ['git', '<circle cx="6" cy="9.2" r="1"/><circle cx="10" cy="9.2" r="1"/><path d="M7 9.2h2"/>'],
    agent: ['agent', '<path d="M8 7.2v4M6 9.2h4M6.6 7.8l2.8 2.8M9.4 7.8l-2.8 2.8"/>'],
};
// Carpetas que genera la compilación o el gestor de paquetes: salen atenuadas.
const GENERATED_FOLDERS = new Set(['bin', 'obj', 'node_modules', 'dist', 'build', '.vs']);

/** Tipo de un fichero por su nombre: primero el nombre exacto, después los tests y luego la extensión; «plain» si no tiene
 * extensión y «unknown» si su extensión no está en la tabla. */
function fileTypeOf(name) {
    const lower = name.toLowerCase();
    if (FILE_TYPE_NAMES[lower]) return FILE_TYPE_NAMES[lower];
    if (/\.(test|spec)\.[a-z]+$/.test(lower) || /tests?\.cs$/.test(lower)) return 'test';
    const dot = lower.lastIndexOf('.');
    if (dot < 0) return 'plain';
    return FILE_TYPE_EXTS[lower.slice(dot + 1)] || 'unknown';
}

/** Dibujo de un tipo como SVG, con la clase de su color. Las piezas son constantes de este fichero, no texto de fuera. */
function fileTypeSvg(body, color, cls) {
    const icon = svg('svg', { class: `${cls || 'flow-icon'} ft-icon ft-${color}`, viewBox: '0 0 16 16', 'aria-hidden': 'true' });
    icon.innerHTML = body;
    return icon;
}

/** Icono del tipo de un fichero, con la ficha que dice el tipo y la extensión al pasar el ratón. */
function fileTypeIcon(name, cls) {
    const type = fileTypeOf(name);
    const [label, color, body] = FILE_TYPES[type];
    const dot = name.lastIndexOf('.');
    const ext = dot >= 0 ? name.slice(dot) : 'sin extensión';
    return hint(fileTypeSvg(body, color, cls), '📄', label, ext,
        type === 'unknown' ? 'Arkko todavía no tiene icono para esta extensión.' : null);
}

/** Icono de una carpeta del repositorio: abierta o cerrada, y las conocidas con su emblema y su color. */
function folderTypeIcon(name, open) {
    const emblem = FOLDER_EMBLEMS[FOLDER_TYPES[name.toLowerCase()]];
    const body = open
        ? '<path d="M1.5 12.5v-9h4.3l1.5 1.5h5.7v2"/><path d="M1.5 12.5l2.1-5.5h11l-2.1 5.5z"/>'
        : '<path d="M1.5 3.5h4.3l1.5 1.5h7.2v8h-13z"/>' + (emblem ? emblem[1] : '');
    return fileTypeSvg(body, emblem ? emblem[0] : 'dir');
}

/** Fila de un fichero del repositorio entero: su icono y su nombre, con el punto si está sin commitear. Al pulsarla se abre
 * su ficha, con el fichero entero. */
function repoRow(f, indent, dotAt) {
    const v = state.wf;
    const row = el('div', 'chg-file chg-repo' + (v && v.detail === `file:repo:${f.path}` ? ' on' : ''));
    row.dataset.path = f.path;
    row.style.paddingLeft = `${indent}px`;
    if (uncommitted().has(f.path)) row.append(wipDot(dotAt));
    const line = el('div', 'chg-line');
    const cut = f.path.lastIndexOf('/');
    const name = cut >= 0 ? f.path.slice(cut + 1) : f.path;
    line.append(fileTypeIcon(name), el('span', 'chg-name', name));
    const eye = openedEye('repo', f.path);
    if (eye) line.append(eye);
    row.append(line);
    row.addEventListener('click', () => openChange('repo', f.path));
    row.addEventListener('contextmenu', e => {
        if (changesInCork()) changeOpenMenu(e, 'repo', f.path);
    });
    return row;
}

/** Si los cambios van en un corcho, en su propio recuadro: ahí un fichero se puede abrir en varios sitios. */
function changesInCork() {
    return !!(state.wf && state.wf.dock && document.body.classList.contains('in-cork') && window.invokeCSharpAction);
}

/** El menú del clic derecho sobre un fichero de los cambios de un corcho: abrirlo aquí, en una pestaña nueva del recuadro
 * de los cambios; con los demás ficheros (lo que hace pulsarlo); en un recuadro nuevo a la derecha; o en una ventana
 * suelta. */
function changeOpenMenu(e, scope, path) {
    e.preventDefault();
    e.stopPropagation();
    closeCodeThemeMenu();
    hideTip();
    const menu = el('div', 'code-menu pin-menu');
    menu.append(el('div', 'code-menu-title', path.split('/').pop()));
    menu.append(el('div', 'pin-menu-group', 'Abrir'));
    const item = (icon, text, note, where) => {
        const button = el('button', 'code-menu-item');
        button.append(pinIcon(icon), el('span', 'code-menu-name', text));
        if (note) button.append(el('span', 'pin-menu-note', note));
        button.addEventListener('click', ev => {
            ev.stopPropagation();
            closeCodeThemeMenu();
            openChange(scope, path, where);
        });
        menu.append(button);
    };
    item('tabs', 'Aquí', 'en una pestaña nueva', 'tab');
    item('split', 'Con los demás ficheros', 'al pulsarlo', 'beside');
    item('plus', 'En un recuadro nuevo', 'a la derecha', 'box');
    item('window', 'En una ventana suelta', null, 'window');
    menu.addEventListener('mousedown', ev => ev.stopPropagation());
    document.body.append(menu);
    menu.style.left = `${Math.max(8, Math.min(e.clientX, window.innerWidth - menu.offsetWidth - 8))}px`;
    menu.style.top = `${Math.max(8, Math.min(e.clientY, window.innerHeight - menu.offsetHeight - 8))}px`;
    state.codeMenu = menu;
}

/** La ficha de un fichero de la sesión abierta en algún panel, con dónde está; null si no lo está. */
function openFileOf(scope, path) {
    return (state.openFiles || []).find(f => f.scope === scope && f.path === path) || null;
}

/** Abre en la ficha del directo un fichero cambiado de un alcance; en la ventana de solo los cambios, en su hueco, al
 * lado de la lista; y si esa ventana va en un corcho, con las demás fichas de ficheros del corcho (o en un recuadro nuevo
 * a su derecha, si no hay). Si ya está abierto en un panel, lleva a ese panel, esté donde esté.
 * @param {string} [where] En un corcho, dónde, elegido en el clic derecho: «tab» (una pestaña nueva del recuadro de los
 * cambios), «beside» (con los demás ficheros, lo de siempre), «box» (un recuadro nuevo) o «window» (una ventana suelta).
 * @param {object} [extra] Lo que se añade al mensaje cuando el fichero se abre en otro panel: la línea a la que llevarlo y
 * lo que buscar en ella. */
function openChange(scope, path, where, extra) {
    const name = path.split('/').pop();
    const file = { type: 'pieceFile', scope, path, tab: scope === 'repo' ? 'file' : state.changes.tab, round: pieceRound(scope), title: `${name} · ${sessionName(state.trace)}`, ...extra };
    // Las notas abiertas aparte no tienen sitio para el fichero: en un corcho va con las demás fichas, y si no, a su panel.
    if (PIECE === 'review') {
        post(document.body.classList.contains('in-cork') ? { ...file, where: where || 'beside' } : file);
        return;
    }
    // Las vistas sin lienzo no tienen ficha: el fichero se abre en su propio panel.
    if (!state.wf) {
        post(file);
        return;
    }
    if (window.invokeCSharpAction && openFileOf(scope, path)) {
        post(file);
        return;
    }
    if (changesInCork()) {
        post({ ...file, where: where || 'beside' });
        return;
    }
    if (state.wf.dock) $('panes').classList.add('dock-open');
    state.wf.detail = `file:${scope}:${path}`;
    wfDetail(state.wf, state.wf.detail);
    renderChanges();
    renderGit();
}

/** El árbol de carpetas de unos ficheros: cada carpeta con sus subcarpetas, sus ficheros, cuántos lleva (y cuántos están
 * revisados) y la suma de sus líneas. Una carpeta sin ficheros y con una sola subcarpeta se junta con ella
 * («src/App/Web»), como en un editor. */
function changeTree(files, reviewed) {
    const folder = (name, path) => ({ name, path, dirs: new Map(), files: [], count: 0, done: 0, added: 0, deleted: 0 });
    const root = folder('', '');
    for (const f of files) {
        let node = root;
        const count = n => {
            n.count++;
            if (reviewed && reviewed.has(f.path)) n.done++;
            n.added += f.added || 0;
            n.deleted += f.deleted || 0;
        };
        count(node);
        for (const part of f.path.split('/').slice(0, -1)) {
            if (!node.dirs.has(part)) node.dirs.set(part, folder(part, node.path ? `${node.path}/${part}` : part));
            node = node.dirs.get(part);
            count(node);
        }
        node.files.push(f);
    }
    const compact = node => {
        for (const [key, child] of node.dirs) {
            let joined = child;
            while (joined.files.length === 0 && joined.dirs.size === 1) {
                const only = [...joined.dirs.values()][0];
                only.name = `${joined.name}/${only.name}`;
                joined = only;
            }
            node.dirs.set(key, joined);
            compact(joined);
        }
    };
    compact(root);
    return root;
}

/** Las rutas de todos los ficheros de una carpeta del árbol, también los de sus subcarpetas. */
function changeTreePaths(dir) {
    return [...dir.files.map(f => f.path), ...[...dir.dirs.values()].flatMap(changeTreePaths)];
}

/** Pinta unos ficheros cambiados, o los del repositorio entero, como un árbol de carpetas plegables. */
function changeTreeRows(files, scope) {
    const repo = scope === 'repo';
    const rows = [];
    // Cada pestaña recuerda sus carpetas abiertas.
    const openDirs = repo ? state.changes.repoDirs : state.changes.openDirs;
    // En revisión, cada carpeta dice cuántos de sus ficheros están revisados.
    const reviewed = reviewing(scope) ? reviewedPaths() : null;
    const walk = (node, depth) => {
        for (const dir of [...node.dirs.values()].sort((a, b) => a.name.localeCompare(b.name))) {
            const done = reviewed && dir.done === dir.count;
            // En revisión, una carpeta con algo por revisar sale abierta y una ya revisada, cerrada, salvo que la persona
            // la haya abierto o cerrado a mano.
            const open = reviewed
                ? !state.changes.closedDirs.has(dir.path) && (!done || openDirs.has(dir.path))
                : openDirs.has(dir.path);
            const row = el('div', 'chg-folder' + (open ? ' open' : '') + (done ? ' done' : reviewed ? ' left' : '')
                + (repo && GENERATED_FOLDERS.has(dir.name.toLowerCase()) ? ' generated' : ''));
            row.style.paddingLeft = `${8 + depth * 14}px`;
            row.append(el('span', 'chg-caret', open ? '▾' : '▸'));
            if (reviewed) {
                const paths = changeTreePaths(dir);
                const box = reviewBox(done ? 'on' : dir.done ? 'some' : 'off', () => reviewMark(paths, !done));
                hint(box, '✓', done ? 'Carpeta revisada' : 'Marcar la carpeta', `${dir.done}/${dir.count}`,
                    done ? 'Pulsa para devolver todos sus ficheros a sin revisar.' : 'Pulsa para dar por revisados todos sus ficheros.');
                row.append(box);
            }
            row.append(repo ? folderTypeIcon(dir.name, open) : flowIcon('folder', 'flow-icon'), el('span', 'chg-folder-name', dir.name),
                el('span', 'chg-folder-count', reviewed ? `${dir.done}/${dir.count}` : String(dir.count)));
            if (!repo) row.append(changeNums(dir.added, dir.deleted));
            row.addEventListener('click', () => {
                if (open) {
                    openDirs.delete(dir.path);
                    if (reviewed) state.changes.closedDirs.add(dir.path);
                } else {
                    openDirs.add(dir.path);
                    state.changes.closedDirs.delete(dir.path);
                }
                remember(repo ? 'repoDirs' : 'changesDirs', [...openDirs]);
                remember('changesClosedDirs', [...state.changes.closedDirs]);
                renderChanges();
            });
            rows.push(row);
            if (open) walk(dir, depth + 1);
        }
        // El punto de lo sin commitear, centrado bajo la flecha de las carpetas de su nivel; en el repositorio, el icono del
        // fichero se pone bajo el de esas carpetas.
        const dotAt = 10 + depth * 14;
        for (const f of node.files) rows.push(repo ? repoRow(f, 24 + depth * 14, dotAt) : changeRow(f, scope, 22 + depth * 14, dotAt));
    };
    walk(changeTree(files, reviewed), 0);
    return rows;
}

/** Pinta lo de arriba del panel: las pestañas Cambios y Repositorio, cada una con su icono y cuántos ficheros lleva; y el
 * total de lo que se ve, con sus líneas en los cambios y el botón para dejar solo lo sin commitear. */
function renderChangesHead(scope, files) {
    const repo = scope === 'repo';
    for (const [key, label, icon] of [['changes', 'Cambios', 'diff'], ['repo', 'Repositorio', 'folder']]) {
        const tab = $('changes-scope').querySelector(`[data-cview="${key}"]`);
        const set = key === 'repo' ? state.changes.sets.repo : changeSet(state.changes.scope);
        tab.classList.toggle('on', key === (repo ? 'repo' : 'changes'));
        tab.replaceChildren(flowIcon(icon, 'flow-icon'), el('span', 'changes-tab-label', label));
        if (set && !set.error) tab.append(el('span', 'changes-tab-count', String(set.files.length)));
    }
    const line = $('changes-total');
    const parts = [];
    if (files.length) {
        const sum = el('span', 'changes-total-sum', `${files.length} fichero${files.length === 1 ? '' : 's'}`);
        if (!repo) {
            const total = changeTotals(files);
            sum.append(changeNums(total.added, total.deleted));
        }
        parts.push(sum);
    }
    // Las notas abiertas aparte enseñan la revisión, que es de toda la rama: ahí no se filtra.
    if (!repo && PIECE !== 'review') parts.push(wipToggle(scope));
    line.replaceChildren(...parts);
}

/** El botón que deja en los cambios solo lo sin commitear, con su punto y cuántos ficheros lo están. */
function wipToggle(scope) {
    const on = scope === 'pending';
    const pending = state.changes.sets.pending;
    const button = el('button', 'changes-wip' + (on ? ' on' : ''));
    button.append(el('span', 'changes-wip-dot'), el('span', null, 'Solo sin commitear'));
    if (pending && !pending.error) button.append(el('span', 'changes-wip-count', String(pending.files.length)));
    hint(button, '●', on ? 'Solo lo sin commitear' : 'Toda la rama', null,
        on ? 'Pulsa para volver a ver toda la rama.' : 'Pulsa para dejar solo lo que aún no está en ningún commit, con su diff contra el último.');
    button.addEventListener('click', () => setChangesScope(on ? 'branch' : 'pending'));
    return button;
}

/** Pinta el panel: la rama y lo que se ve en su cabecera, las pestañas y el total, y los ficheros de los cambios o del
 * repositorio entero como un árbol de carpetas plegables, con su buscador. */
function renderChanges() {
    if (PIECE === 'file') return;
    const scope = panelScope();
    const d = changeSet(scope);
    const round = scope === 'branch' ? reviewRound() : null;
    const base = !d || d.error ? '' : scope === 'repo' ? 'todos los ficheros' : scope === 'pending' ? 'sin commitear'
        : round ? `desde la ronda ${round.round}` : d.baseLabel ? `contra ${d.baseLabel}` : 'lo no commiteado';
    $('changes-label').replaceChildren(el('span', null, d && d.branch ? `⑂ ${d.branch}` : 'Cambios'), el('span', 'faint', base || 'de la carpeta de la sesión'));
    const box = $('changes');
    const files = d && !d.error ? d.files : [];
    renderChangesHead(scope, files);
    if (!d) {
        box.replaceChildren(changesSkeleton(scope));
        return;
    }
    if (d.error) {
        box.replaceChildren(el('div', 'changes-note error', d.error));
        return;
    }
    if (scope === 'repo') {
        const parts = [];
        if (!files.length) parts.push(el('div', 'changes-note', 'El repositorio aún no tiene ficheros.'));
        if (d.truncated) parts.push(el('div', 'changes-note', `Solo se ven los ${files.length} primeros ficheros.`));
        parts.push(...changeTreeRows(files, scope));
        const top = box.scrollTop;
        box.replaceChildren(...parts);
        box.scrollTop = top;
        return;
    }
    if (PIECE === 'review') {
        const top = box.scrollTop;
        box.replaceChildren(reviewOf() ? reviewNotesBlock(true)
            : el('div', 'changes-note', 'Esta rama aún no tiene revisión: se empieza en Cambios, en toda la rama.'));
        box.scrollTop = top;
        return;
    }
    const parts = [];
    if (scope === 'branch' && (files.length || round)) parts.push(...reviewHead());
    if (d.note) parts.push(el('div', 'changes-note', d.note));
    if (!files.length) parts.push(el('div', 'changes-note', scope === 'pending' ? 'Nada sin commitear: todo está en commits.'
        : round ? `Nada cambiado desde la ronda ${round.round}.` : 'Nada cambiado en la rama.'));
    if (d.truncated) parts.push(el('div', 'changes-note', `Solo se ven los ${files.length} primeros ficheros.`));
    // En revisión, se puede dejar a la vista solo lo que falta por revisar.
    const left = reviewing(scope) && state.changes.onlyUnreviewed ? files.filter(f => !(reviewMarkOf(f.path) || {}).reviewed) : files;
    if (left !== files && !left.length) parts.push(el('div', 'changes-note', 'Todo revisado.'));
    parts.push(...changeTreeRows(left, scope));
    // Se conserva por dónde iba la lista: se repinta con cada novedad del directo.
    const top = box.scrollTop;
    box.replaceChildren(...parts);
    box.scrollTop = top;
}

// Las filas de mentira de Cambios mientras se mira lo cambiado: su sangría y su largo en %; las de nivel 0 son carpetas.
const CHG_SKELETON = [[0, 22], [1, 40], [1, 30], [1, 46], [0, 18], [1, 36], [2, 28], [2, 42], [0, 24], [1, 32], [1, 44]];

/** Lo que se ve en Cambios mientras la app mira lo cambiado: unas filas de mentira con la forma de la lista (carpetas y
 * ficheros, con su marca y sus líneas) que brillan y, en la rama, la tarjeta de la revisión. */
function changesSkeleton(scope) {
    const box = el('div', 'chg-skeleton');
    box.setAttribute('aria-label', 'Mirando lo cambiado…');
    if (scope === 'branch') box.append(el('div', 'chg-skel chg-skel-card'));
    CHG_SKELETON.forEach(([depth, width], i) => {
        const row = el('div', 'chg-skel-row');
        row.style.paddingLeft = `${8 + depth * 16}px`;
        row.style.setProperty('--d', `${(i % 6) * 0.1}s`);
        const bar = el('span', 'chg-skel chg-skel-bar');
        bar.style.width = `${width}%`;
        row.append(el('span', 'chg-skel ' + (depth ? 'chg-skel-mark' : 'chg-skel-dir')), bar);
        if (depth) row.append(el('span', 'chg-skel chg-skel-num'));
        box.append(row);
    });
    return box;
}

/** Pinta arriba a la derecha del directo la rama, los commits sin subir y lo pendiente con sus líneas, y debajo, si está
 * desplegada, la lista de lo pendiente; y las líneas de lo pendiente en la pestaña de los cambios minimizados. */
function renderGit() {
    if (PIECE === 'file') return;
    const d = state.changes.sets.pending;
    const files = d && !d.error ? d.files : [];
    const total = changeTotals(files);
    const branch = (d && d.branch) || (state.trace && state.trace.branch) || '';
    const head = $('flow-git-head');
    head.replaceChildren(flowIcon('branch', 'flow-icon'), el('span', 'flow-git-name', branch || 'Sin rama'));
    if (d && d.ahead) head.append(el('span', 'flow-git-ahead', `↑${d.ahead}`));
    else if (d && !d.error && d.branch && !d.upstream) head.append(el('span', 'flow-git-ahead faint', 'sin publicar'));
    if (files.length) head.append(changeNums(total.added, total.deleted));
    else if (d && !d.error) head.append(el('span', 'flow-git-clean', '✓'));
    head.append(el('span', 'flow-git-caret', state.changes.listOpen ? '▴' : '▾'));
    const pushed = d && d.upstream
        ? (d.ahead ? `${d.ahead} commit${d.ahead === 1 ? '' : 's'} sin subir a ${d.upstream}.` : `Al día con ${d.upstream}.`)
        : 'La rama aún no se ha publicado.';
    const pending = d && d.error ? d.error
        : files.length ? `${files.length} fichero${files.length === 1 ? '' : 's'} sin commitear: +${total.added} −${total.deleted}.` : 'Nada sin commitear.';
    hint(head, '⑂', branch || 'Rama de git', null, `${pending} ${pushed} Pulsa para ver lo pendiente.`);
    dgStampGit();
    const list = $('flow-git-list');
    list.hidden = !state.changes.listOpen || !files.length;
    if (!list.hidden) {
        const top = list.scrollTop;
        list.replaceChildren(...files.map(f => changeRow(f, 'pending')));
        list.scrollTop = top;
    }
    const tab = $('changes-tab-num');
    tab.replaceChildren(...(files.length ? [el('span', 'plus', `+${total.added}`), el('span', 'minus', `−${total.deleted}`)] : []));
}

/** La ficha de un fichero cambiado («alcance:ruta»): su diff desde la base de su alcance o el fichero entero como está
 * ahora, con las líneas cambiadas marcadas. Lo que falta se pide a la app y se pinta al llegar. */
function wfFileDetail(v, key) {
    const cut = key.indexOf(':');
    const scope = key.slice(0, cut);
    const path = key.slice(cut + 1);
    const d = changeSet(scope);
    const round = scope === 'branch' ? reviewRound() : null;
    const f = d && d.files ? d.files.find(x => x.path === path) : null;
    const drawer = v.drawer;
    if (!f) {
        // Mientras llega, o si ya no está cambiado (se ha commiteado o descartado), la ficha lo dice en lugar de cerrarse.
        const sig = `${key}|${round ? round.base : ''}|${!d ? 'esperando' : d.error || 'sin cambios'}`;
        if (!drawer.hidden && drawer.dataset.sig === sig) return;
        drawer.dataset.sig = sig;
        drawer.hidden = false;
        const head = el('div', 'wf-drawer-head');
        const badge = el('span', 'flow-badge');
        badge.append(flowIcon('diff', 'flow-icon'));
        head.append(badge, el('strong', 'wf-drawer-title', path), ...wfFileButtons(v, scope, path));
        const body = el('div', 'wf-drawer-body');
        if (reviewRoundsOn(scope)) body.append(reviewRoundBand());
        body.append(el('div', 'wf-drawer-meta', !d ? 'Mirando lo cambiado…' : d.error
            || (scope === 'pending' ? 'Ya no tiene nada pendiente: se ha commiteado o descartado.'
                : scope === 'repo' ? 'Ya no está en el repositorio: se ha borrado o lo deja fuera el .gitignore.'
                : round ? `No ha cambiado desde la ronda ${round.round}.` : 'No ha cambiado en la rama.')));
        drawer.replaceChildren(head, body);
        fileFindAttach(drawer, true);
        return;
    }
    // Un fichero del repositorio entero no tiene diff que enseñar: se ve entero, con lo sin commitear marcado.
    const repo = scope === 'repo';
    const tab = f.status === 'deleted' ? 'diff' : repo ? 'file' : state.changes.tab;
    const kind = tab === 'diff' && f.status !== 'untracked' ? 'changeDiff' : 'changeFile';
    const base = changeBase(scope, path, d);
    const againstMerged = base !== d.base;
    const text = state.changes.texts.get(changeKey(kind, base, path));
    const diffText = f.status === 'untracked' ? null : state.changes.texts.get(changeKey('changeDiff', base, path));
    // En directo se vuelve a abrir con cada novedad: si no ha cambiado nada, se deja como está, con su desplazamiento.
    const sig = [key, tab, base, f.added, f.deleted, text ? text.seq : 'n', diffText ? diffText.seq : 'n', reviewSig(scope, path)].join('|');
    if (!drawer.hidden && drawer.dataset.sig === sig) {
        // Sin nada nuevo que pintar, aún puede tocar ir a la línea de una nota o de un resultado de búsqueda.
        if (reviewing(scope)) reviewFocus(drawer);
        repoGoToLine(drawer, key);
        return;
    }
    drawer.dataset.sig = sig;
    // Si se vuelve a pintar lo mismo (una nota nueva, una casilla, una novedad del directo), el código no salta arriba.
    const look = `${key}|${tab}|${base}`;
    const oldBody = !drawer.hidden && drawer.dataset.look === look ? drawer.querySelector('.wf-filewin-body') : null;
    const keptTop = oldBody ? oldBody.scrollTop : 0;
    drawer.dataset.look = look;
    if (!text) post({ type: kind, path, base, oldPath: f.oldPath });
    // El fichero entero marca lo que cambió: hace falta también su diff.
    if (tab === 'file' && f.status !== 'untracked' && !diffText) post({ type: 'changeDiff', path, base, oldPath: f.oldPath });

    v.nodes.forEach(node => node.classList.remove('picked'));
    drawer.hidden = false;
    paint(drawer, 'main');
    const head = el('div', 'wf-drawer-head');
    const badge = el('span', 'flow-badge');
    const typeIcon = repo ? fileTypeIcon(path.slice(path.lastIndexOf('/') + 1)) : null;
    badge.append(typeIcon || flowIcon('diff', 'flow-icon'));
    // La insignia toma el color del tipo para teñir su fondo.
    if (typeIcon) badge.classList.add('ft-badge', [...typeIcon.classList].find(c => c.startsWith('ft-') && c !== 'ft-icon'));
    // En el repositorio, si está sin commitear, se dice con las líneas de lo pendiente.
    const wip = repo && state.changes.sets.pending && (state.changes.sets.pending.files || []).find(x => x.path === path);
    const shownAs = wip ? { ...wip, status: 'wip' } : f;
    const [, word] = shownAs.status === 'wip' ? ['', 'Sin commitear'] : CHANGE_STATUS[f.status] || ['M', 'Cambiado'];
    head.append(badge, el('strong', 'wf-drawer-title', path));
    const when = el('span', 'wf-drawer-when');
    // Sin líneas contadas (un binario, o un fichero del repositorio), la app las manda nulas o no las manda.
    when.append(el('span', null, shownAs.added == null && shownAs.deleted == null ? word : `${word} · +${shownAs.added || 0} −${shownAs.deleted || 0}`));
    head.append(when);
    if (f.status !== 'deleted' && !repo) {
        const tabs = el('div', 'segmented wf-file-tabs');
        for (const [name, label] of [['diff', 'Cambios'], ['file', 'Fichero']]) {
            const chip = el('button', 'chip' + (tab === name ? ' on' : ''), label);
            chip.addEventListener('click', () => {
                state.changes.tab = name;
                wfDetail(v, v.detail);
            });
            tabs.append(chip);
        }
        head.append(tabs);
    }
    if (reviewing(scope)) head.append(...reviewTools(path));
    head.append(...wfFileButtons(v, scope, path));

    const body = el('div', 'wf-drawer-body');
    const lang = wfLang(path);
    if (reviewRoundsOn(scope)) body.append(reviewRoundBand());
    if (reviewing(scope)) body.append(...reviewFileNotes(path, Boolean(round) && tab === 'diff'));
    if (!text) body.append(wfCodeLoading(path, lang, tab === 'diff' ? 'leyendo los cambios…' : 'leyendo el fichero…'));
    else if (text.error) body.append(wfError(text.error));
    else {
        const note = text.truncated ? 'cortado: es muy largo' : tab === 'file' ? 'como está ahora'
            : againstMerged ? 'también lo cambió develop: contra lo que se mergeó'
            : round ? `desde la ronda ${round.round}` : d.baseLabel ? `desde ${d.baseLabel}` : 'sin commitear';
        const view = tab === 'diff'
            ? (f.status === 'untracked' ? wfDiff('', text.text || '', lang) : wfUnified(text.text || '', lang))
            : wfWholeFile(text.text || '', lang, diffText && !diffText.error ? wfAddedLines(diffText.text || '') : null);
        // Las notas de lo quitado cuentan sus líneas en la base de la rama, no en la de la ronda: en la ronda, lo quitado
        // no se anota ni lleva notas, que salen sobre el código, con las del fichero entero.
        if (round) view.querySelectorAll('.wf-code-line[data-old]').forEach(row => delete row.dataset.old);
        if (reviewing(scope)) reviewDecorate(view, path);
        body.append(wfFileWin(wfFile(path, lang, note), [view]));
    }
    const finding = fileFindFocused();
    drawer.replaceChildren(head, body);
    const scroller = body.querySelector('.wf-filewin-body');
    if (keptTop && scroller) scroller.scrollTop = keptTop;
    fileFindAttach(drawer, true, finding);
    if (reviewing(scope)) reviewFocus(drawer);
    repoGoToLine(drawer, key);
    reviewPopPlace();
}

/** Los botones de la cabecera de la ficha de un fichero: clavarlo en el corcho y cerrarlo y, en el lienzo, cambiar su
 * alto. En su propio panel, ninguno: de eso se encarga el panel. */
function wfFileButtons(v, scope, path) {
    if (v.piece && !v.dock) return [];
    if (!v.dock) return wfDrawerButtons(v);
    const close = el('button', 'pbtn small wf-drawer-close', '✕');
    close.addEventListener('click', () => wfCloseDetail(v));
    return [close];
}

/** Los botones de la ficha de un fichero en el lienzo: el de cambiar su alto y su cierre. */
function wfDrawerButtons(v) {
    const close = el('button', 'pbtn small wf-drawer-close', '✕');
    close.addEventListener('click', () => wfCloseDetail(v));
    return [...(v.piece || STEP ? [] : [wfDockButton(v)]), wfSizeButton(v.drawer), close];
}

/** Un diff de git, como el de un editor: cada trozo con su línea de inicio, sus números de línea, lo quitado en rojo y lo
 * nuevo en verde, con el trozo que cambia resaltado en una línea tocada. */
function wfUnified(text, lang) {
    const rows = [];
    let oldNo = 0;
    let newNo = 0;
    for (const line of String(text).split('\n')) {
        const hunk = /^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@(.*)$/.exec(line);
        if (hunk) {
            oldNo = Number(hunk[1]);
            newNo = Number(hunk[2]);
            rows.push({ op: '@', text: `línea ${newNo}${hunk[3] ? ` · ${hunk[3].trim()}` : ''}` });
            continue;
        }
        // Antes del primer trozo va la cabecera de git (diff, index, ---, +++); «\ No newline» no es una línea.
        if (!rows.length || line.startsWith('\\')) continue;
        if (line.startsWith('+')) rows.push({ op: '+', text: line.slice(1), n: newNo++ });
        else if (line.startsWith('-')) rows.push({ op: '-', text: line.slice(1), n: oldNo++ });
        else if (line.startsWith(' ')) {
            rows.push({ op: ' ', text: line.slice(1), n: newNo++ });
            oldNo++;
        }
    }
    const view = wfCodeView('wf-code wf-diff', lang);
    if (!rows.length) {
        view.append(el('div', 'wf-code-more', /Binary files/.test(text) ? 'Es un fichero binario: no hay líneas que comparar.' : 'Sin cambios en el contenido: solo se ha renombrado o cambiado de permisos.'));
        return view;
    }
    // Cada trozo empieza con su pintor: lo que quedó abierto en el anterior no sigue en este.
    let paint = wfPainter(lang);
    for (const d of wfPairs(rows).slice(0, WF_FILE_LINES)) {
        if (d.op === '@') {
            view.append(el('div', 'wf-code-line hunk', `   ⋯ ${d.text}`));
            paint = wfPainter(lang);
            continue;
        }
        const row = el('div', `wf-code-line ${d.op === '+' ? 'add' : d.op === '-' ? 'del' : ''}`);
        row.append(el('span', 'wf-code-num', String(d.n)), d.mark ? wfMarked(d.text, d.mark, d.op) : paint(d.text));
        // Las líneas que siguen en el fichero llevan su número de ahora; las quitadas, el suyo en la base. En las dos se
        // puede anotar.
        if (d.op !== '-') row.dataset.line = String(d.n);
        else row.dataset.old = String(d.n);
        view.append(row);
    }
    if (rows.length > WF_FILE_LINES) view.append(el('div', 'wf-code-more', `… ${rows.length - WF_FILE_LINES} líneas más`));
    return view;
}

// Líneas que se pintan como mucho de un diff o un fichero entero.
const WF_FILE_LINES = 5000;

/** Los números de línea que entran nuevos en un diff de git, para marcarlos en el fichero entero. */
function wfAddedLines(text) {
    const lines = new Set();
    let n = 0;
    let inHunk = false;
    for (const line of String(text).split('\n')) {
        const hunk = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@/.exec(line);
        if (hunk) {
            n = Number(hunk[1]);
            inHunk = true;
        } else if (!inHunk || line.startsWith('\\') || line.startsWith('-')) continue;
        else if (line.startsWith('+')) lines.add(n++);
        else n++;
    }
    return lines;
}

/** Un fichero entero con sus números de línea y los colores de su lenguaje, con las líneas nuevas marcadas al margen. */
function wfWholeFile(text, lang, added) {
    const view = wfCodeView('wf-code', lang);
    const paint = wfPainter(lang);
    const lines = String(text).replace(/\n$/, '').split('\n');
    lines.slice(0, WF_FILE_LINES).forEach((line, i) => {
        const row = el('div', 'wf-code-line' + (added && added.has(i + 1) ? ' mark' : ''));
        row.append(el('span', 'wf-code-num', String(i + 1)), paint(line));
        row.dataset.line = String(i + 1);
        view.append(row);
    });
    if (lines.length > WF_FILE_LINES) view.append(el('div', 'wf-code-more', `… ${lines.length - WF_FILE_LINES} líneas más`));
    return view;
}

// BUSCAR EN EL FICHERO

// La búsqueda de la ficha de un fichero. Sigue abierta mientras se mira el mismo fichero, aunque la ficha se vuelva a
// pintar con una novedad o al cambiar entre «Cambios» y «Fichero».
const fileFind = { open: false, key: null, query: '', index: -1, hits: [], bar: null, timer: null };
// Más coincidencias no se resaltan: buscar una letra en un fichero enorme frenaría la ficha.
const FILE_FIND_MAX = 5000;

/** La ficha de fichero abierta en la página, con su código ya pintado, o null. */
function fileFindDrawer() {
    return [...document.querySelectorAll('.wf-drawer')].find(d => !d.hidden && d.isConnected
        && (d.dataset.key || '').startsWith('file:') && d.querySelector('.wf-filewin-body .wf-code-line')) || null;
}

/** Atiende las teclas de la búsqueda en el fichero y dice si eran suyas: Ctrl+F la abre, F3 y Mayús+F3 van a la
 * siguiente y a la anterior, y Esc la cierra. */
function fileFindKey(e) {
    if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && e.key.toLowerCase() === 'f') {
        const drawer = fileFindDrawer();
        if (!drawer) return false;
        e.preventDefault();
        fileFindOpen(drawer);
        return true;
    }
    if (!fileFind.open || !fileFind.bar || !fileFind.bar.isConnected) return false;
    if (e.key === 'F3') {
        e.preventDefault();
        fileFindStep(e.shiftKey ? -1 : 1);
        return true;
    }
    if (e.key === 'Escape') {
        e.preventDefault();
        fileFindClose();
        return true;
    }
    return false;
}

/** Abre la barra de búsqueda en una ficha de fichero, con lo seleccionado en el código ya escrito, y deja el cursor en
 * ella. */
function fileFindOpen(drawer) {
    const key = drawer.dataset.key.slice(5);
    if (fileFind.key !== key) fileFind.index = -1;
    fileFind.key = key;
    fileFind.open = true;
    const sel = window.getSelection();
    const picked = sel ? String(sel) : '';
    if (picked.trim() && !picked.includes('\n') && picked.length <= 200 && drawer.contains(sel.anchorNode) && !fileFind.bar?.contains(sel.anchorNode)) {
        fileFind.query = picked;
        fileFind.index = -1;
        if (fileFind.bar) fileFind.bar.querySelector('input').value = picked;
    }
    fileFindAttach(drawer, false);
    const input = fileFind.bar && fileFind.bar.querySelector('input');
    if (input) {
        input.focus();
        input.select();
    }
}

/** Si la búsqueda tiene el cursor: hay que devolvérselo al volver a pintar la ficha. */
function fileFindFocused() {
    const input = fileFind.bar && fileFind.bar.querySelector('input');
    return input && document.activeElement === input ? { start: input.selectionStart, end: input.selectionEnd } : null;
}

/** Pone la barra sobre el código de la ficha y vuelve a buscar, si la búsqueda está abierta en ese mismo fichero; si la
 * ficha es de otro, la cierra.
 *
 * Al volver a pintar se queda en la misma coincidencia y no mueve el código; «focus» devuelve el cursor a la barra. */
function fileFindAttach(drawer, keep, focus) {
    const key = (drawer.dataset.key || '').slice(5);
    if (fileFind.key !== key) {
        if (fileFind.open) fileFindClose();
        return;
    }
    if (!fileFind.open) return;
    const win = drawer.querySelector('.wf-filewin:not(.loading)');
    if (!win) return;
    if (!fileFind.bar) fileFind.bar = fileFindBar();
    const head = win.querySelector(':scope > .wf-file');
    fileFind.bar.style.top = `${(head ? head.offsetHeight : 0) + 8}px`;
    win.classList.add('finding');
    win.append(fileFind.bar);
    fileFindRun(keep);
    if (focus) {
        const input = fileFind.bar.querySelector('input');
        input.focus();
        input.setSelectionRange(focus.start, focus.end);
    }
}

/** La barra de búsqueda: el texto, cuántas hay y en cuál se está, las opciones de mayúsculas y palabra entera, y
 * anterior, siguiente y cerrar. */
function fileFindBar() {
    const bar = el('div', 'ffind');
    const input = el('input', 'ffind-q');
    input.type = 'text';
    input.placeholder = 'Buscar en el fichero';
    input.spellcheck = false;
    input.autocomplete = 'off';
    input.value = fileFind.query;
    input.addEventListener('input', () => {
        fileFind.query = input.value;
        fileFind.index = -1;
        clearTimeout(fileFind.timer);
        fileFind.timer = setTimeout(() => fileFindRun(false), 120);
    });
    input.addEventListener('keydown', e => {
        if (e.key !== 'Enter') return;
        e.preventDefault();
        clearTimeout(fileFind.timer);
        if (fileFind.index === -1) fileFindRun(false);
        else fileFindStep(e.shiftKey ? -1 : 1);
    });
    const option = (label, key, title, body) => {
        const button = el('button', 'ffind-opt' + (remembered(key, false) === true ? ' on' : ''), label);
        button.dataset.opt = key;
        hint(button, label, title, null, body);
        button.addEventListener('click', () => {
            button.classList.toggle('on');
            remember(key, button.classList.contains('on'));
            fileFind.index = -1;
            fileFindRun(false);
        });
        return button;
    };
    const step = (label, d, title, keys) => {
        const button = el('button', 'ffind-btn', label);
        hint(button, label, title, keys, null);
        button.addEventListener('click', () => fileFindStep(d));
        return button;
    };
    const close = el('button', 'ffind-btn', '✕');
    hint(close, '✕', 'Cerrar la búsqueda', 'Esc', null);
    close.addEventListener('click', fileFindClose);
    bar.append(input, el('span', 'ffind-count'),
        option('Aa', 'fileFindCase', 'Distinguir mayúsculas', '«Lista» no encuentra «lista».'),
        option('ab', 'fileFindWord', 'Palabra entera', '«List» no encuentra «ListAll».'),
        step('↑', -1, 'Anterior', 'Mayús+Intro · Mayús+F3'), step('↓', 1, 'Siguiente', 'Intro · F3'), close);
    return bar;
}

/** Lo que se busca, como expresión, con las opciones de la barra; null si no hay nada escrito. */
function fileFindRegex() {
    if (!fileFind.query) return null;
    const text = fileFind.query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const word = remembered('fileFindWord', false) === true;
    const exact = remembered('fileFindCase', false) === true;
    return new RegExp(word ? `(?<![\\p{L}\\p{N}_])${text}(?![\\p{L}\\p{N}_])` : text, `gu${exact ? '' : 'i'}`);
}

/** Busca en las líneas pintadas de la ficha, resalta todas las coincidencias y la actual, y las marca en la regla.
 *
 * Con «keep», se queda en la misma coincidencia sin mover el código; si no, va a la primera que se ve o que viene
 * después. */
function fileFindRun(keep) {
    const bar = fileFind.bar;
    const drawer = bar && bar.closest('.wf-drawer');
    fileFind.hits = [];
    const re = fileFindRegex();
    if (re && drawer) {
        for (const row of drawer.querySelectorAll('.wf-filewin-body .wf-code-line:not(.hunk)')) {
            const parts = [];
            let text = '';
            const walker = document.createTreeWalker(row, NodeFilter.SHOW_TEXT, {
                acceptNode: n => (n.parentElement.closest('.wf-code-num') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT),
            });
            for (let n = walker.nextNode(); n; n = walker.nextNode()) {
                parts.push([n, text.length]);
                text += n.data;
            }
            // El punto del texto de la línea en su nodo: un final cae al final de un nodo, un principio al empezar el siguiente.
            const point = (pos, end) => {
                for (const [n, s] of parts) if (end ? pos <= s + n.data.length : pos < s + n.data.length) return [n, pos - s];
                const [n] = parts[parts.length - 1];
                return [n, n.data.length];
            };
            re.lastIndex = 0;
            for (let m = re.exec(text); m && fileFind.hits.length < FILE_FIND_MAX; m = re.exec(text)) {
                if (!m[0]) {
                    re.lastIndex++;
                    continue;
                }
                const range = document.createRange();
                range.setStart(...point(m.index, false));
                range.setEnd(...point(m.index + m[0].length, true));
                fileFind.hits.push({ range, row });
            }
            if (fileFind.hits.length >= FILE_FIND_MAX) break;
        }
    }
    const n = fileFind.hits.length;
    if (!n) fileFind.index = -1;
    else if (keep && fileFind.index >= 0) fileFind.index = Math.min(fileFind.index, n - 1);
    else {
        // Como en un editor: la primera desde lo que se está viendo; si no queda ninguna debajo, la primera del todo.
        const scroller = fileFind.hits[0].row.closest('.wf-filewin-body');
        const top = scroller ? scroller.getBoundingClientRect().top : -Infinity;
        const from = fileFind.hits.findIndex(h => h.row.getBoundingClientRect().bottom > top);
        fileFind.index = Math.max(0, from);
    }
    fileFindPaint();
    if (n && !keep) fileFindReveal(fileFind.hits[fileFind.index]);
}

/** Resalta las coincidencias, la actual más fuerte, y pone la cuenta y la regla de la derecha. */
function fileFindPaint() {
    const bar = fileFind.bar;
    if (!bar) return;
    const n = fileFind.hits.length;
    const count = bar.querySelector('.ffind-count');
    count.textContent = !fileFind.query ? '' : !n ? 'Sin resultados' : `${fileFind.index + 1} de ${n}${n >= FILE_FIND_MAX ? '+' : ''}`;
    bar.classList.toggle('none', Boolean(fileFind.query) && !n);
    if (window.CSS && CSS.highlights) {
        CSS.highlights.set('file-find', new Highlight(...fileFind.hits.map(h => h.range)));
        CSS.highlights.set('file-find-now', new Highlight(...(n ? [fileFind.hits[fileFind.index].range] : [])));
    }
    const win = bar.closest('.wf-filewin');
    if (!win) return;
    win.querySelector(':scope > .ffind-ruler')?.remove();
    const body = win.querySelector('.wf-filewin-body');
    if (!n || !body || !body.scrollHeight) return;
    const ruler = el('div', 'ffind-ruler');
    ruler.style.top = `${body.offsetTop}px`;
    const marks = new Set();
    const bodyTop = body.getBoundingClientRect().top - body.scrollTop;
    for (const [i, h] of fileFind.hits.entries()) {
        const at = Math.round((1000 * (h.row.getBoundingClientRect().top - bodyTop)) / body.scrollHeight) / 10;
        const now = i === fileFind.index;
        if (marks.has(at) && !now) continue;
        marks.add(at);
        const mark = el('i', now ? 'now' : null);
        mark.style.top = `${Math.min(at, 99.4)}%`;
        ruler.append(mark);
    }
    win.append(ruler);
}

/** Desplaza el código para que la coincidencia quede a la vista, en medio, si no lo estaba ya; sin meterla bajo la barra. */
function fileFindReveal(hit) {
    const scroller = hit && hit.row.closest('.wf-filewin-body');
    if (!scroller) return;
    const r = hit.range.getBoundingClientRect();
    const s = scroller.getBoundingClientRect();
    const barBottom = fileFind.bar ? fileFind.bar.getBoundingClientRect().bottom : s.top;
    if (r.top < Math.max(s.top, barBottom) + 4 || r.bottom > s.bottom - 4) scroller.scrollTop += r.top - s.top - scroller.clientHeight / 2;
}

/** Va a la coincidencia siguiente o a la anterior, dando la vuelta al llegar al final. */
function fileFindStep(d) {
    const n = fileFind.hits.length;
    if (!n) return;
    fileFind.index = ((fileFind.index < 0 ? (d > 0 ? -1 : 0) : fileFind.index) + d + n) % n;
    fileFindPaint();
    fileFindReveal(fileFind.hits[fileFind.index]);
}

/** Cierra la búsqueda: quita la barra, los resaltados y la regla, y deja el código donde estaba. */
function fileFindClose() {
    clearTimeout(fileFind.timer);
    fileFind.open = false;
    fileFind.hits = [];
    const win = fileFind.bar && fileFind.bar.closest('.wf-filewin');
    if (win) {
        win.classList.remove('finding');
        win.querySelector(':scope > .ffind-ruler')?.remove();
    }
    if (fileFind.bar) fileFind.bar.remove();
    if (window.CSS && CSS.highlights) {
        CSS.highlights.delete('file-find');
        CSS.highlights.delete('file-find-now');
    }
}

// BUSCAR EN EL REPOSITORIO

// La paleta «Buscar en todo»: su caja y sus partes, el modo («files», ir a un fichero; «text», texto en todo el
// repositorio; «uses», dónde se usa un nombre), lo escrito en cada modo, lo que se puede elegir y cuál está elegido. Al
// cerrarla se queda como estaba: volver a abrirla trae lo mismo.
const finder = {
    box: null, scrim: null, input: null, opts: null, modes: null, sum: null, list: null, preview: null,
    mode: 'text', query: { files: '', text: '', uses: '' }, items: [], sel: 0, timer: null, asked: new Set(),
    // En texto y usos, los ficheros plegados (por su ruta, o «Definición»); se despliegan con cada búsqueda nueva.
    closed: new Set(),
};
// Ficheros que enseña como mucho el modo de ficheros, y abiertos hace poco que recuerda.
const FINDER_FILES_MAX = 50;
const REPO_RECENT_MAX = 10;
// Líneas que enseña la vista previa por encima de la elegida; por debajo, el doble.
const FINDER_CONTEXT = 10;
// Por debajo de este ancho la paleta no cabe con la vista previa al lado, y va sin ella.
const FINDER_NARROW = 680;
// Los modos, en el orden en que los recorre Tab: su nombre y su atajo.
const FINDER_MODES = [['files', 'Ficheros', 'Ctrl+P'], ['text', 'Texto', 'Ctrl+Mayús+F'], ['uses', 'Usos', 'Ctrl+clic']];
// Lo último que se ha pedido a la app al buscar texto o usos, con su número para quedarse con la última respuesta, lo
// que contestó y si se espera respuesta.
const repoGrep = { asked: null, askedOptions: null, seq: 0, result: null, busy: false };
// Cómo se buscan los usos de un nombre: la palabra entera y tal cual, sin tocar las opciones recordadas del texto.
const REPO_USES_OPTIONS = { matchCase: true, wholeWord: true, regex: false };

/** Si esta página puede buscar en el repositorio: la de Sesiones; en un corcho o una ventana suelta, el panel de Cambios
 * y la ficha de un fichero; y la propia paleta abierta aparte. Con una sesión. */
function repoSearchHere() {
    return changesOn() && (!PIECE || PIECE === 'changes' || PIECE === 'file' || PIECE === 'finder');
}

/** La página de la paleta abierta aparte: la app la pone en su propia ventana, centrada sobre el corcho o la ventana
 * suelta desde la que se pide, por encima de todos sus paneles. */
function startFinder() {
    document.body.classList.add('piece-mode', 'piece-finder');
    document.body.append($('tip'));
    document.addEventListener('mouseover', trackTip);
    document.addEventListener('keydown', e => repoSearchKey(e));
    // Pulsar fuera (en el corcho, en otra ventana) le quita el teclado a la página: entonces se cierra.
    window.addEventListener('blur', () => setTimeout(() => {
        if (!document.hasFocus()) finderClose(true);
    }, 150));
    if (host) host.addEventListener('message', e => receivePiece(e.data));
    window.agentPulseReceive = receivePiece;
    window.agentPulseHello = () => post({ type: 'ready' });
    post({ type: 'ready' });
}

/** La paleta aparte recibe de qué sesión es: si es otra, olvida lo buscado en la anterior y pide sus ficheros. */
function finderSession(m) {
    if (state.trace && state.trace.id === m.id) return;
    repoSearchReset();
    state.changes.sets = { pending: null, branch: null, since: null, repo: null };
    state.changes.texts.clear();
    state.trace = { id: m.id, piece: true };
    post({ type: 'changes', scope: 'repo' });
}

/** Atiende Ctrl+P, Ctrl+Mayús+F y Mayús+F12, y dice si eran suyas: abren la paleta en su modo. */
function repoSearchKey(e) {
    if (e.altKey || !repoSearchHere()) return false;
    if (e.key === 'F12' && e.shiftKey && !e.ctrlKey && !e.metaKey) {
        const word = repoUsesSelected();
        if (!word) return false;
        e.preventDefault();
        finderOpen('uses', word);
        return true;
    }
    if (!(e.ctrlKey || e.metaKey)) return false;
    const key = e.key.toLowerCase();
    if (key === 'p' && !e.shiftKey) {
        e.preventDefault();
        finderOpen('files');
        return true;
    }
    if (key === 'f' && e.shiftKey) {
        e.preventDefault();
        finderOpen('text', repoSelectedText());
        return true;
    }
    return false;
}

/** Cierra la paleta y olvida lo buscado, al cambiar de sesión. */
function repoSearchReset() {
    finderClose();
    clearTimeout(finder.timer);
    finder.query = { files: '', text: '', uses: '' };
    finder.items = [];
    finder.asked.clear();
    finder.closed.clear();
    Object.assign(repoGrep, { asked: null, askedOptions: null, result: null, busy: false });
}

/** Apunta un fichero como abierto hace poco, el primero de la lista. */
function repoRecentAdd(path) {
    const recent = remembered('repoRecent', []).filter(p => p !== path);
    remember('repoRecent', [path, ...recent].slice(0, REPO_RECENT_MAX));
}

/** Abre un fichero del repositorio en su ficha; con línea, la lleva a ella y, con «find», abre la barra de Ctrl+F ya
 * buscando eso. */
function repoOpenAt(path, line, find) {
    repoRecentAdd(path);
    // La ficha de un fichero del repositorio necesita la lista entera, que solo se pide al ver «Repositorio».
    if (!state.changes.sets.repo) post({ type: 'changes', scope: 'repo' });
    state.changes.goTo = line ? { key: `repo:${path}`, line, find: find || null } : null;
    // Si el fichero se abre en otro panel (en un corcho o una ventana suelta), la línea va con el mensaje a la app.
    const goTo = line ? { line, findQuery: find ? find.query : null, findCase: Boolean(find && find.matchCase), findWord: Boolean(find && find.wholeWord) } : {};
    // Desde la ficha de un fichero suelta, el otro se abre como pestaña de su mismo recuadro, sin cambiar este; desde la
    // paleta aparte, la app lo coloca según el panel desde el que se pidió.
    if (PIECE === 'file' || PIECE === 'finder') {
        post({ type: 'pieceFile', scope: 'repo', path, tab: 'file', title: path.split('/').pop(), where: PIECE === 'file' ? 'tab' : 'auto', ...goTo });
        return;
    }
    openChange('repo', path, undefined, goTo);
}

/** Lleva la ficha a la línea pedida al abrir un resultado, una vez, y abre allí la búsqueda en el fichero si se pidió.
 * Mientras se lee el código no hay filas: se hace al llegar. */
function repoGoToLine(drawer, key) {
    const go = state.changes.goTo;
    if (!go || go.key !== key) return;
    const rows = [...drawer.querySelectorAll('.wf-filewin-body .wf-code-line[data-line]')];
    if (!rows.length) return;
    state.changes.goTo = null;
    const row = rows.find(r => Number(r.dataset.line) === go.line) || rows.find(r => Number(r.dataset.line) >= go.line);
    if (!row) return;
    requestAnimationFrame(() => {
        row.scrollIntoView({ block: 'center' });
        row.classList.add('goto-now');
        setTimeout(() => row.classList.remove('goto-now'), 1600);
        if (!go.find) return;
        // La barra se rehace para que sus opciones sean las de la búsqueda que se abre.
        fileFindClose();
        fileFind.bar = null;
        remember('fileFindCase', go.find.matchCase);
        remember('fileFindWord', go.find.wholeWord);
        Object.assign(fileFind, { key, open: true, query: go.find.query, index: -1 });
        fileFindAttach(drawer, false);
        const at = fileFind.hits.findIndex(h => h.row === row);
        if (at >= 0) {
            fileFind.index = at;
            fileFindPaint();
        }
    });
}

/** En la ficha de un fichero suelta: la app pide llevarla a una línea, al abrirla desde un resultado. */
function repoGoToReceive(m) {
    const key = `${m.scope}:${m.path}`;
    const find = m.findQuery ? { query: m.findQuery, matchCase: Boolean(m.findCase), wholeWord: Boolean(m.findWord) } : null;
    state.changes.goTo = { key, line: m.line, find };
    const v = state.wf;
    if (v && v.drawer && !v.drawer.hidden && v.detail === `file:${key}`) repoGoToLine(v.drawer, key);
}

/** Si en una posición de un nombre empieza una palabra: al principio, tras «.», «_», «-» o un espacio, o al pasar de
 * minúscula a mayúscula. */
function finderWordStart(text, i) {
    return i === 0 || /[/._\- ]/.test(text[i - 1]) || (/\p{Lu}/u.test(text[i]) && /[\p{Ll}\p{N}]/u.test(text[i - 1]));
}

/** Las posiciones de un trozo de texto, de «from» y tantas letras. */
function finderSpan(from, length) {
    return Array.from({ length }, (_, k) => from + k);
}

/** Cómo casa lo escrito con un fichero, por niveles, del más claro al más suelto; null si no casa en el nivel pedido.
 *
 * 1. Lo escrito tal cual: dentro del nombre (más si empieza el nombre o una palabra) o, si no, en la ruta. Varias palabras
 *    separadas por espacios tienen que estar todas.
 * 2. Por palabras: cada letra sigue a la anterior o empieza una palabra del nombre, como «gitch» en «GitChanges».
 * 3. Las letras en orden, aunque estén sueltas, solo en el nombre: lo último, cuando no casa nada de lo anterior. */
function finderScore(query, path, level) {
    const q = query.toLowerCase();
    const name = path.slice(path.lastIndexOf('/') + 1);
    const lowerName = name.toLowerCase();
    const off = path.length - name.length;
    if (level === 1) {
        const words = q.split(/\s+/).filter(Boolean);
        if (words.length > 1) {
            const at = [];
            let score = 0;
            for (const w of words) {
                const i = lowerName.indexOf(w);
                const j = i >= 0 ? off + i : path.toLowerCase().indexOf(w);
                if (j < 0) return null;
                score += i >= 0 ? 30 : 10;
                at.push(...finderSpan(j, w.length));
            }
            return { score: score - path.length * 0.01, at };
        }
        const i = lowerName.indexOf(q);
        if (i >= 0) return { score: 100 + (i === 0 ? 40 : finderWordStart(name, i) ? 30 : 10) - name.length * 0.1 - path.length * 0.005, at: finderSpan(off + i, q.length) };
        const j = path.toLowerCase().indexOf(q);
        return j >= 0 ? { score: 60 - path.length * 0.01, at: finderSpan(j, q.length) } : null;
    }
    if (level === 2) {
        // Se busca la forma de casar letra a letra en que cada una sigue a la anterior o empieza palabra.
        const seen = new Map();
        const walk = (qi, from, last) => {
            if (qi === q.length) return [];
            const key = `${qi}:${from}:${last}`;
            if (seen.has(key)) return seen.get(key);
            let found = null;
            for (let j = from; j < name.length && !found; j++) {
                if (lowerName[j] !== q[qi] || !(j === last + 1 || finderWordStart(name, j))) continue;
                const rest = walk(qi + 1, j + 1, j);
                if (rest) found = [j, ...rest];
            }
            seen.set(key, found);
            return found;
        };
        const hit = walk(0, 0, -2);
        if (!hit) return null;
        const runs = hit.filter((j, k) => k && j === hit[k - 1] + 1).length;
        return { score: 50 + runs * 3 - name.length * 0.1, at: hit.map(j => off + j) };
    }
    const at = [];
    let from = 0;
    for (const ch of q) {
        const j = lowerName.indexOf(ch, from);
        if (j < 0) return null;
        at.push(off + j);
        from = j + 1;
    }
    return { score: 10 - name.length * 0.1, at };
}

/** Un texto con las letras que casan resaltadas; «offset» es dónde empieza el texto en la ruta. */
function finderMarked(cls, text, at, offset) {
    const span = el('span', cls);
    const marks = new Set(at.map(i => i - offset));
    let plain = '';
    for (const [i, ch] of [...text].entries()) {
        if (!marks.has(i)) {
            plain += ch;
            continue;
        }
        if (plain) span.append(plain);
        plain = '';
        span.append(el('mark', null, ch));
    }
    if (plain) span.append(plain);
    return span;
}

/** Abre la paleta en un modo, con el cursor en ella; con «word», lo pone como lo buscado. En un panel de un corcho o de
 * una ventana suelta, la pide a la app, que la abre en su propia ventana centrada sobre todos los paneles. */
function finderOpen(mode, word, here) {
    if ((PIECE === 'changes' || PIECE === 'file') && !here) {
        post({ type: 'finderOpen', mode, word: word || null });
        return;
    }
    finder.mode = FINDER_MODES.some(([k]) => k === mode) ? mode : 'text';
    if (word) finder.query[finder.mode] = word;
    if (!state.changes.sets.repo) post({ type: 'changes', scope: 'repo' });
    if (!finder.box) finderBuild();
    finderPlace();
    finderSync();
    finder.input.focus();
    finder.input.select();
    if (finder.mode === 'files') finderRun();
    else repoGrepAsk();
}

/** Monta la paleta: el campo con sus opciones, los modos con lo encontrado, la lista con la vista previa al lado y las
 * teclas. Un velo detrás la separa de la página y la cierra al pulsarlo. */
function finderBuild() {
    const scrim = el('div', 'fx-scrim');
    scrim.addEventListener('mousedown', finderClose);
    const box = el('div', 'fx');
    const field = el('label', 'fx-field');
    const input = el('input');
    Object.assign(input, { type: 'text', spellcheck: false, autocomplete: 'off' });
    input.addEventListener('input', finderInput);
    input.addEventListener('keydown', finderKey);
    const opts = el('span', 'fx-opts');
    field.append(flowIcon('search', 'flow-icon'), input, opts);
    const modes = el('div', 'fx-modes');
    for (const [key, label, keys] of FINDER_MODES) {
        const button = el('button', 'fx-mode');
        button.dataset.mode = key;
        button.append(el('span', null, label), el('span', 'fx-key', keys));
        button.addEventListener('mousedown', e => e.preventDefault());
        button.addEventListener('click', () => finderSetMode(key));
        modes.append(button);
    }
    const sum = el('span', 'fx-sum');
    const top = el('div', 'fx-top');
    const row = el('div', 'fx-row');
    row.append(modes, sum);
    top.append(field, row);
    const list = el('div', 'fx-list');
    list.addEventListener('mousedown', e => e.preventDefault());
    const preview = el('div', 'fx-preview');
    const body = el('div', 'fx-body');
    body.append(list, preview);
    const foot = el('div', 'fx-foot');
    for (const [keys, what] of [['↑ ↓', 'elegir'], ['Intro', 'abrir'], ['Tab', 'cambiar de modo'], ['Esc', 'cerrar']]) foot.append(el('b', null, keys), el('span', null, what));
    box.append(top, body, foot);
    Object.assign(finder, { box, scrim, input, opts, modes, sum, list, preview });
    // Aparte, la paleta es toda la ventana: no hay nada detrás que tapar.
    if (PIECE !== 'finder') document.body.append(scrim);
    document.body.append(box);
    window.addEventListener('resize', finderPlace);
}

/** Coloca la paleta en el centro de la ventana; aparte, ocupa toda la suya. Si no cabe con la vista previa al lado, va
 * sin ella. */
function finderPlace() {
    if (!finder.box) return;
    const whole = PIECE === 'finder';
    const width = whole ? innerWidth : Math.min(940, innerWidth - 32);
    const height = whole ? innerHeight : Math.min(600, innerHeight - 32);
    Object.assign(finder.box.style, { left: `${(innerWidth - width) / 2}px`, top: `${(innerHeight - height) / 2}px`, width: `${width}px`, height: `${height}px` });
    finder.box.classList.toggle('narrow', width < FINDER_NARROW);
}

/** Cierra la paleta sin olvidar lo buscado. «outside» dice que se cierra porque se ha pulsado fuera: aparte, la app no
 * devuelve entonces el teclado a la ventana de detrás, que ya lo tiene quien se ha pulsado. */
function finderClose(outside) {
    if (!finder.box) return;
    finder.box.remove();
    finder.scrim.remove();
    Object.assign(finder, { box: null, scrim: null, input: null, opts: null, modes: null, sum: null, list: null, preview: null });
    window.removeEventListener('resize', finderPlace);
    if (window.CSS && CSS.highlights) CSS.highlights.delete('finder-hit');
    // Aparte, cerrar la paleta es esconder su ventana.
    if (PIECE === 'finder') post({ type: 'finderClose', outside: outside === true });
}

/** Pone el campo, las opciones y los modos como pide el modo elegido. */
function finderSync() {
    const mode = finder.mode;
    finder.input.value = finder.query[mode];
    finder.input.placeholder = { files: 'Ir a un fichero por su nombre… (Nombre:120 para ir a la línea)', text: 'Buscar texto en todos los ficheros…', uses: 'Nombre del que ver dónde se usa…' }[mode];
    for (const button of finder.modes.children) button.classList.toggle('on', button.dataset.mode === mode);
    finder.opts.replaceChildren();
    if (mode === 'text') {
        for (const [label, key, title, body] of [
            ['Aa', 'repoGrepCase', 'Distinguir mayúsculas', '«Lista» no encuentra «lista».'],
            ['ab', 'repoGrepWord', 'Palabra entera', '«List» no encuentra «ListAll».'],
            ['.*', 'repoGrepRegex', 'Expresión regular', 'Lo escrito es una expresión regular extendida, como «Get(Name|Id)».'],
        ]) {
            const button = el('button', 'fx-opt' + (remembered(key, false) === true ? ' on' : ''), label);
            hint(button, label, title, null, body);
            button.addEventListener('mousedown', e => e.preventDefault());
            button.addEventListener('click', e => {
                e.preventDefault();
                remember(key, !button.classList.contains('on'));
                button.classList.toggle('on');
                repoGrepAsk();
            });
            finder.opts.append(button);
        }
    } else if (mode === 'uses') {
        const fixed = el('span', 'fx-fixed', 'Aa · ab');
        hint(fixed, 'Aa', 'Palabra entera y tal cual', null, 'Los usos se buscan siempre así, sin cambiar las opciones del modo Texto.');
        finder.opts.append(fixed);
    }
}

/** Cambia de modo sin cerrar la paleta; cada modo conserva lo que tenía escrito. */
function finderSetMode(mode) {
    finder.mode = mode;
    finder.sel = 0;
    finderSync();
    finder.input.focus();
    if (mode === 'files') finderRun();
    else repoGrepAsk();
}

/** Lo escrito en la paleta: en ficheros busca al momento; en texto y usos, al dejar de escribir. */
function finderInput() {
    finder.query[finder.mode] = finder.input.value;
    finder.sel = 0;
    clearTimeout(finder.timer);
    if (finder.mode === 'files') finderRun();
    else finder.timer = setTimeout(repoGrepAsk, 300);
}

/** Las teclas de la paleta: flechas para elegir, Intro para abrir, Tab para cambiar de modo y Esc para cerrar. */
function finderKey(e) {
    const n = finder.items.length;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        if (n) finder.sel = finderStep(e.key === 'ArrowDown' ? 1 : -1);
        finderPaint();
    } else if (e.key === 'Enter') {
        e.preventDefault();
        clearTimeout(finder.timer);
        if (finder.mode !== 'files' && finder.query[finder.mode] !== (repoGrep.result && repoGrep.result.query)) repoGrepAsk();
        else finderPick(finder.items[finder.sel]);
    } else if (e.key === 'Tab') {
        e.preventDefault();
        const at = FINDER_MODES.findIndex(([k]) => k === finder.mode);
        finderSetMode(FINDER_MODES[(at + (e.shiftKey ? -1 : 1) + FINDER_MODES.length) % FINDER_MODES.length][0]);
    } else if (e.key === 'Escape') {
        e.preventDefault();
        e.stopPropagation();
        finderClose();
    }
}

/** Lo que se puede elegir en el modo actual: los ficheros que casan (o los abiertos hace poco) o las líneas encontradas,
 * con las que parecen la definición primero en los usos. */
function finderRun() {
    if (!finder.box) return;
    if (finder.mode === 'files') {
        const d = state.changes.sets.repo;
        const files = d && !d.error ? d.files.map(f => f.path) : [];
        const raw = finder.query.files.trim();
        const lineAt = raw.match(/^(.*?):(\d+)$/);
        const query = lineAt ? lineAt[1] : raw;
        const line = lineAt ? Number(lineAt[2]) : null;
        if (!query) {
            const known = new Set(files);
            finder.items = remembered('repoRecent', []).filter(p => known.has(p)).map(path => ({ path, at: [], line, recent: true }));
        } else {
            // Solo se baja de nivel si el anterior no encuentra nada: lo claro no se mezcla con lo suelto.
            for (const level of [1, 2, 3]) {
                finder.items = files.map(path => {
                    const hit = finderScore(query, path, level);
                    return hit && { path, ...hit, line };
                }).filter(Boolean).sort((a, b) => b.score - a.score).slice(0, FINDER_FILES_MAX);
                finder.level = level;
                if (finder.items.length) break;
            }
        }
    } else {
        const r = repoGrep.result;
        const hits = r && !r.error && r.query === finder.query[finder.mode] ? r.hits || [] : [];
        const word = finder.mode === 'uses' ? r && r.query : null;
        const items = hits.map(h => ({ path: h.path, line: h.line, text: h.text, def: Boolean(word) && repoIsDefinition(h.text, word) }));
        // Las definiciones van primero; el resto, en el orden de git, que ya va por fichero.
        finder.items = [...items.filter(i => i.def), ...items.filter(i => !i.def)];
    }
    finder.sel = Math.min(finder.sel, Math.max(0, finder.items.length - 1));
    finderPaint();
}

/** Pinta lo encontrado: cuánto, la lista (los ficheros, o las líneas por fichero con lo buscado resaltado) y la vista
 * previa de lo elegido. */
function finderPaint() {
    if (!finder.box) return;
    const mode = finder.mode;
    const r = repoGrep.result;
    const query = finder.query[mode];
    const rows = [];
    let note = null;
    if (mode === 'files') {
        const d = state.changes.sets.repo;
        if (!d) note = 'Leyendo los ficheros del repositorio…';
        else if (d.error) note = d.error;
        else if (!finder.items.length) note = query.trim() ? 'Ningún fichero con esas letras.' : 'Escribe parte del nombre de un fichero.';
        finder.sum.textContent = finder.items.length && !finder.items[0].recent ? `${finder.items.length}${finder.items.length === FINDER_FILES_MAX ? '+' : ''} ficheros` : '';
        if (finder.items.length && finder.items[0].recent) rows.push(el('div', 'fx-sec', 'Abiertos hace poco'));
        else if (finder.items.length && finder.level === 3) rows.push(el('div', 'fx-sec', `Ninguno contiene «${query.trim()}»: parecidos, por letras sueltas`));
        finder.items.forEach((item, i) => {
            const cut = item.path.lastIndexOf('/');
            const name = item.path.slice(cut + 1);
            const row = el('div', 'fx-item fx-file' + (i === finder.sel ? ' on' : ''));
            row.append(fileTypeIcon(name), finderMarked('fx-name', name, item.at, cut + 1), finderMarked('fx-dir', cut > 0 ? item.path.slice(0, cut) : '', item.at, 0));
            if (item.line) row.append(el('span', 'fx-line', `línea ${item.line}`));
            finderItemEvents(row, i);
            rows.push(row);
        });
    } else {
        const fresh = r && r.query === query;
        if (query.trim().length < 2) note = mode === 'uses' ? 'Escribe un nombre, o haz Ctrl+clic en uno del código.' : 'Escribe al menos dos letras.';
        else if (!fresh) note = 'Buscando…';
        else if (r.error) note = r.error;
        else if (!finder.items.length) note = `Nada con «${r.query}» en el repositorio.`;
        const files = new Set(finder.items.map(i => i.path)).size;
        const defs = finder.items.filter(i => i.def).length;
        finder.sum.textContent = !fresh || !finder.items.length ? '' : `${finder.items.length}${r.truncated ? '+' : ''} ${mode === 'uses' ? 'uso' : 'resultado'}${finder.items.length === 1 ? '' : 's'} en ${files} fichero${files === 1 ? '' : 's'}`
            + (defs ? ` · ${defs} definici${defs === 1 ? 'ón' : 'ones'}` : '');
        finder.sum.classList.toggle('busy', repoGrep.busy);
        const re = fresh ? repoGrepRegex(r.query, repoGrep.askedOptions || {}) : null;
        const counts = new Map();
        for (const item of finder.items) counts.set(finderGroupOf(item), (counts.get(finderGroupOf(item)) || 0) + 1);
        let group = null;
        finder.items.forEach((item, i) => {
            const head = finderGroupOf(item);
            const closed = finder.closed.has(head);
            if (head !== group) {
                group = head;
                const cut = item.path.lastIndexOf('/');
                const g = el('div', 'fx-group' + (item.def ? ' def' : ''));
                g.append(el('span', 'fx-caret', closed ? '▸' : '▾'));
                if (item.def) g.append(el('span', 'fx-name', 'Definición'));
                else g.append(fileTypeIcon(item.path.slice(cut + 1)), el('span', 'fx-name', item.path.slice(cut + 1)), el('span', 'fx-dir', cut > 0 ? item.path.slice(0, cut) : ''));
                g.append(el('span', 'fx-count', String(counts.get(head))));
                g.addEventListener('click', () => finderFold(head));
                rows.push(g);
            }
            if (closed) return;
            const row = el('div', 'fx-item fx-hit' + (i === finder.sel ? ' on' : ''));
            if (item.def) row.append(el('span', 'fx-where', `${item.path.slice(item.path.lastIndexOf('/') + 1)}:${item.line}`));
            else row.append(el('span', 'fx-no', String(item.line)));
            row.append(repoGrepLine(item.text.trim(), re));
            finderItemEvents(row, i);
            rows.push(row);
        });
        if (fresh && r.truncated) rows.push(el('div', 'fx-note', `Solo se ven los ${finder.items.length} primeros: afina la búsqueda.`));
    }
    if (note) rows.unshift(el('div', 'fx-note', note));
    finder.list.replaceChildren(...rows);
    finder.list.querySelector('.fx-item.on')?.scrollIntoView({ block: 'nearest' });
    finderPreview();
}

/** El grupo de una línea encontrada en la lista: «Definición» o la ruta de su fichero. */
function finderGroupOf(item) {
    return item.def ? 'Definición' : item.path;
}

/** Pliega o despliega un fichero de la lista; si lo elegido queda dentro, pasa a la primera línea que se vea. */
function finderFold(group) {
    if (finder.closed.has(group)) finder.closed.delete(group);
    else finder.closed.add(group);
    const sel = finder.items[finder.sel];
    if (sel && finder.closed.has(finderGroupOf(sel))) finder.sel = Math.max(0, finder.items.findIndex(i => !finder.closed.has(finderGroupOf(i))));
    finderPaint();
    finder.input.focus();
}

/** Lo siguiente o lo anterior que se ve en la lista, saltando lo que está plegado; da la vuelta al llegar al final. */
function finderStep(d) {
    const n = finder.items.length;
    for (let k = 1; k <= n; k++) {
        const i = (finder.sel + d * k + n * n) % n;
        if (finder.mode === 'files' || !finder.closed.has(finderGroupOf(finder.items[i]))) return i;
    }
    return finder.sel;
}

/** Los clics de una fila de la lista: uno la elige y enseña su vista previa; dos la abren. */
function finderItemEvents(row, i) {
    row.addEventListener('click', () => {
        finder.sel = i;
        finderPaint();
    });
    row.addEventListener('dblclick', () => finderPick(finder.items[i]));
}

/** La vista previa de lo elegido: las líneas de su fichero alrededor de la encontrada, con sus colores y lo buscado
 * resaltado. El fichero se pide a la app la primera vez y se pinta al llegar. */
function finderPreview() {
    const box = finder.preview;
    if (!box || finder.box.classList.contains('narrow')) return;
    const item = finder.items[finder.sel];
    if (window.CSS && CSS.highlights) CSS.highlights.delete('finder-hit');
    if (!item) {
        box.replaceChildren();
        return;
    }
    const cut = item.path.lastIndexOf('/');
    const head = el('div', 'fx-pv-head');
    head.append(fileTypeIcon(item.path.slice(cut + 1)), el('b', null, item.path.slice(cut + 1)), el('span', null, `${cut > 0 ? item.path.slice(0, cut) : ''}${item.line ? ` · línea ${item.line}` : ''}`));
    const got = state.changes.texts.get(changeKey('changeFile', null, item.path));
    if (!got) {
        if (!finder.asked.has(item.path)) {
            finder.asked.add(item.path);
            post({ type: 'changeFile', path: item.path });
        }
        box.replaceChildren(head, el('div', 'fx-note', 'Leyendo el fichero…'));
        return;
    }
    if (got.error || got.text === null) {
        box.replaceChildren(head, el('div', 'fx-note', got.error || 'No se puede enseñar.'));
        return;
    }
    const lang = wfLang(item.path);
    const view = wfCodeView('wf-code fx-code', lang);
    const paint = wfPainter(lang);
    const lines = String(got.text).replace(/\n$/, '').split('\n');
    const at = item.line || 1;
    const from = Math.max(1, at - FINDER_CONTEXT);
    const to = Math.min(lines.length, at + FINDER_CONTEXT * 2);
    // El pintor lleva el estado de un comentario o una cadena de varias líneas: se le pasan también las de antes.
    for (let n = Math.max(1, from - 40); n < from; n++) paint(lines[n - 1]);
    for (let n = from; n <= to; n++) {
        const row = el('div', 'wf-code-line' + (item.line && n === at ? ' now' : ''));
        row.append(el('span', 'wf-code-num', String(n)), paint(lines[n - 1]));
        view.append(row);
    }
    box.replaceChildren(head, view);
    view.querySelector('.now')?.scrollIntoView({ block: 'center' });
    finderPreviewMarks(view);
}

/** Resalta en la vista previa lo buscado, con las opciones con que se buscó. */
function finderPreviewMarks(view) {
    const r = repoGrep.result;
    if (finder.mode === 'files' || !r || r.query !== finder.query[finder.mode] || !window.CSS || !CSS.highlights) return;
    const re = repoGrepRegex(r.query, repoGrep.askedOptions || {});
    if (!re) return;
    const ranges = [];
    for (const row of view.querySelectorAll('.wf-code-line')) {
        const walker = document.createTreeWalker(row, NodeFilter.SHOW_TEXT, {
            acceptNode: n => (n.parentElement.closest('.wf-code-num') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT),
        });
        for (let n = walker.nextNode(); n; n = walker.nextNode()) {
            re.lastIndex = 0;
            for (let m = re.exec(n.data); m; m = re.exec(n.data)) {
                if (!m[0]) {
                    re.lastIndex++;
                    continue;
                }
                const range = document.createRange();
                range.setStart(n, m.index);
                range.setEnd(n, m.index + m[0].length);
                ranges.push(range);
            }
        }
    }
    CSS.highlights.set('finder-hit', new Highlight(...ranges));
}

/** Abre lo elegido en la paleta y la cierra: un fichero (en su línea, si se escribió) o una línea encontrada, con la barra
 * de Ctrl+F buscando lo mismo (salvo con expresión regular). */
function finderPick(item) {
    if (!item) return;
    finderClose();
    if (finder.mode === 'files') {
        repoOpenAt(item.path, item.line, null);
        return;
    }
    const r = repoGrep.result;
    const opts = repoGrep.askedOptions || {};
    repoOpenAt(item.path, item.line, !r || opts.regex ? null : { query: r.query, matchCase: Boolean(opts.matchCase), wholeWord: Boolean(opts.wholeWord) });
}

/** Las opciones de la búsqueda de texto, recordadas. */
function repoGrepOptions() {
    return {
        matchCase: remembered('repoGrepCase', false) === true,
        wholeWord: remembered('repoGrepWord', false) === true,
        regex: remembered('repoGrepRegex', false) === true,
    };
}

/** Pide a la app que busque lo escrito en el modo de texto o de usos, si ha cambiado y tiene al menos dos letras. */
function repoGrepAsk() {
    clearTimeout(finder.timer);
    const query = finder.query[finder.mode];
    const options = finder.mode === 'uses' ? REPO_USES_OPTIONS : repoGrepOptions();
    const asked = JSON.stringify([query, options]);
    if (query.trim().length >= 2 && asked !== repoGrep.asked) {
        repoGrep.asked = asked;
        repoGrep.askedOptions = options;
        repoGrep.busy = true;
        repoGrep.seq++;
        post({ type: 'repoGrep', query, seq: repoGrep.seq, ...options });
    }
    finderRun();
}

/** Recoge lo que ha encontrado la app, si es la última búsqueda pedida de esta sesión. */
function receiveRepoGrep(m) {
    if (!state.trace || m.id !== state.trace.id || m.seq !== repoGrep.seq) return;
    repoGrep.result = m;
    repoGrep.busy = false;
    finder.sel = 0;
    finder.closed.clear();
    finderRun();
}

/** La expresión con que se resalta lo encontrado, con las opciones con que se buscó; null si no vale. */
function repoGrepRegex(query, options) {
    try {
        const text = options.regex ? query : query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        const word = options.wholeWord ? `(?<![\\p{L}\\p{N}_])(?:${text})(?![\\p{L}\\p{N}_])` : text;
        return new RegExp(word, `gu${options.matchCase ? '' : 'i'}`);
    } catch {
        return null;
    }
}

/** Una línea encontrada con lo buscado resaltado. */
function repoGrepLine(text, re) {
    const span = el('span', 'fx-text');
    if (!re) {
        span.textContent = text;
        return span;
    }
    let last = 0;
    re.lastIndex = 0;
    for (let m = re.exec(text); m; m = re.exec(text)) {
        if (!m[0]) {
            re.lastIndex++;
            continue;
        }
        if (m.index > last) span.append(text.slice(last, m.index));
        span.append(el('mark', null, m[0]));
        last = m.index + m[0].length;
    }
    if (last < text.length) span.append(text.slice(last));
    return span;
}

/** Si una línea parece declarar un nombre: por su palabra clave (class, enum, function, def, const…), por un tipo seguido
 * del nombre y de «(», «{», «=» o «;» (un método, un campo o una propiedad), o por «nombre = function» o «nombre: (».
 *
 * Es una aproximación por cómo empieza la línea, sin entender el lenguaje: vale para los más comunes y puede equivocarse. */
function repoIsDefinition(text, name) {
    const n = name.replace(/\$/g, '\\$');
    return new RegExp(`\\b(?:class|interface|struct|record|enum|namespace|function|def|const|let|var|type|delegate|event)\\s+${n}\\b`).test(text)
        || new RegExp(`^\\s*(?:(?:public|private|protected|internal|static|readonly|const|volatile|async|override|virtual|sealed|partial|abstract|required|unsafe|extern|new)\\s+)*[\\w<>\\[\\],?.]+\\s+${n}\\s*(?:[({=;]|=>)`).test(text)
        || new RegExp(`^\\s*${n}\\s*[:=]\\s*(?:async\\s*)?(?:function\\b|\\()`).test(text)
        // Un método con un tipo de vuelta compuesto («Task<(A, B)> Nombre(»): empieza por modificadores y el nombre va
        // seguido de «(» o «<» antes de ningún «=».
        || new RegExp(`^\\s*(?:(?:public|private|protected|internal|static|async|override|virtual|sealed|abstract|extern|unsafe|new)\\s+)+[^=]*?\\b${n}\\s*[(<]`).test(text);
}

/** El nombre del código que hay bajo un punto de la pantalla, en una ficha de fichero: letras, números, «_» y «$»; null
 * si no hay ninguno, si es de una letra o si es un número. Devuelve también su trozo de texto, para subrayarlo. */
function codeWordAt(x, y) {
    const caret = document.caretRangeFromPoint && document.caretRangeFromPoint(x, y);
    const node = caret && caret.startContainer;
    if (!node || node.nodeType !== Node.TEXT_NODE) return null;
    const line = node.parentElement.closest('.wf-filewin-body .wf-code-line');
    if (!line || node.parentElement.closest('.wf-code-num')) return null;
    const text = node.data;
    const isWord = ch => /[\p{L}\p{N}_$]/u.test(ch);
    let start = caret.startOffset;
    let end = start;
    while (start > 0 && isWord(text[start - 1])) start--;
    while (end < text.length && isWord(text[end])) end++;
    const word = text.slice(start, end);
    if (word.length < 2 || /^\p{N}/u.test(word)) return null;
    const range = document.createRange();
    range.setStart(node, start);
    range.setEnd(node, end);
    // El cursor cae en el nombre más cercano aunque el puntero esté en un hueco: solo vale si está encima.
    const box = range.getBoundingClientRect();
    if (x < box.left - 1 || x > box.right + 1 || y < box.top - 1 || y > box.bottom + 1) return null;
    return { word, range };
}

/** Lo seleccionado en el código de una ficha, si es una sola línea corta; null si no. */
function repoSelectedText() {
    const sel = window.getSelection();
    const text = sel ? String(sel) : '';
    const inCode = sel && sel.anchorNode && sel.anchorNode.parentElement && sel.anchorNode.parentElement.closest('.wf-filewin-body');
    return inCode && text.trim() && !text.includes('\n') && text.length <= 200 ? text.trim() : null;
}

/** El nombre seleccionado en el código de una ficha, si lo seleccionado es un nombre; null si no. */
function repoUsesSelected() {
    const text = repoSelectedText();
    return text && text.length >= 2 && /^[\p{L}_$][\p{L}\p{N}_$]*$/u.test(text) ? text : null;
}

// Si hay un nombre subrayado: sin él, mover el ratón sin Ctrl no toca nada.
let repoUsesHovering = false;

/** Subraya el nombre bajo el puntero mientras se tiene Ctrl pulsado sobre el código, para que se vea que se puede pulsar. */
function repoUsesHover(e) {
    const hit = (e.ctrlKey || e.metaKey) && repoSearchHere() ? codeWordAt(e.clientX, e.clientY) : null;
    if (!hit && !repoUsesHovering) return;
    repoUsesHovering = Boolean(hit);
    document.body.classList.toggle('uses-hover', Boolean(hit));
    if (!window.CSS || !CSS.highlights) return;
    if (hit) CSS.highlights.set('uses-hover', new Highlight(hit.range));
    else CSS.highlights.delete('uses-hover');
}

/** Ctrl+clic en un nombre del código: abre la paleta con sus usos en vez de lo que haría el clic. */
function repoUsesClick(e) {
    if (!(e.ctrlKey || e.metaKey) || e.shiftKey || e.altKey || !repoSearchHere()) return;
    const hit = codeWordAt(e.clientX, e.clientY);
    if (!hit) return;
    e.preventDefault();
    e.stopPropagation();
    repoUsesHover({ ctrlKey: false });
    finderOpen('uses', hit.word);
}

/** Engancha lo de git: la pestaña y el botón de minimizar del panel, el alcance, la lupa de buscar en el repositorio, la
 * rama de arriba con su lista, y el divisor con el chat, que se recuerda. */
function initChanges() {
    $('changes-tab').querySelector('.chat-tab-icon').append(flowIcon('diff', 'flow-icon'));
    $('changes-head-icon').append(flowIcon('diff', 'flow-icon'));
    $('changes-min').addEventListener('click', () => setChangesOpen(false, true));
    $('changes-tab').addEventListener('click', () => setChangesOpen(true, true));
    $('changes-scope').querySelectorAll('[data-cview]').forEach(chip => chip.addEventListener('click', () => setChangesView(chip.dataset.cview)));
    const find = $('changes-find-btn');
    find.append(flowIcon('search', 'flow-icon'));
    hint(find, '⌕', 'Buscar en el repositorio', 'Ctrl+Mayús+F',
        'Texto en todos los ficheros. En la misma paleta: Ctrl+P para ir a un fichero por su nombre y Ctrl+clic en un nombre del código para ver dónde se usa.');
    find.addEventListener('click', () => finderOpen(finder.mode));
    // Con clave nueva: «Pendiente» era antes una pestaña, y quien la dejó elegida no ha pedido el filtro.
    state.changes.scope = remembered('changesWipOnly', false) === true ? 'pending' : 'branch';
    state.changes.view = remembered('changesView', 'changes') === 'repo' ? 'repo' : 'changes';
    state.changes.openDirs = new Set(remembered('changesDirs', []));
    state.changes.repoDirs = new Set(remembered('repoDirs', []));
    state.changes.closedDirs = new Set(remembered('changesClosedDirs', []));
    state.changes.reviewHidden = remembered('reviewHidden', false) === true;
    state.changes.listOpen = remembered('gitListOpen', false) === true;
    $('panes').classList.toggle('changes-closed', !remembered('changesOpen', true));
    $('flow-git-head').addEventListener('click', () => {
        state.changes.listOpen = !state.changes.listOpen;
        remember('gitListOpen', state.changes.listOpen);
        renderGit();
    });
    $('flow-git').addEventListener('mouseover', trackTip);
    $('changes-pane').addEventListener('mouseover', trackTip);
    $('changes-pane').addEventListener('mouseleave', hideTip);
    const side = $('side');
    const split = $('side-split');
    const setSplit = value => {
        const share = Math.max(0.3, Math.min(1.7, value));
        side.style.setProperty('--side-split', String(share));
        return share;
    };
    // El divisor reparte la altura: 1 es mitad y mitad; más, más chat.
    const shareAt = y => {
        const r = side.getBoundingClientRect();
        return ((y - r.top) / r.height) * 2;
    };
    split.addEventListener('pointerdown', e => {
        split.setPointerCapture(e.pointerId);
        split.classList.add('drag');
        const move = ev => setSplit(shareAt(ev.clientY));
        const up = ev => {
            split.releasePointerCapture(ev.pointerId);
            split.classList.remove('drag');
            split.removeEventListener('pointermove', move);
            split.removeEventListener('pointerup', up);
            remember('sideSplit', setSplit(shareAt(ev.clientY)));
        };
        split.addEventListener('pointermove', move);
        split.addEventListener('pointerup', up);
    });
    setSplit(remembered('sideSplit', 1));
    renderChanges();
    renderGit();
    setInterval(() => {
        if (state.trace && state.trace.live && document.visibilityState === 'visible') requestChanges(true);
    }, CHANGES_EVERY);
}

// LA REVISIÓN

// Tipos de nota, por su clave en el fichero de revisión, con cómo se dicen.
const REVIEW_KINDS = [['correccion', 'Corrección'], ['mejora', 'Mejora'], ['duda', 'Duda'], ['bien', 'Bien']];

/** La revisión de la rama de la sesión que se ve, si se ha empezado; null si no. */
function reviewOf() {
    return (state.changes.review && state.changes.review.review) || null;
}

/** Lo que hizo /opsx:review con una nota, de su fichero de lo aplicado; null si está pendiente. */
function outcomeOf(n) {
    const applied = state.changes.review && state.changes.review.applied;
    return (applied && applied[n.id]) || null;
}

// Cómo se dice cada resultado de /opsx:review y cómo se llama lo que cuenta su resumen.
const REVIEW_OUTCOMES = {
    aplicada: { name: 'Aplicada', summary: 'Qué se hizo' },
    contestada: { name: 'Contestada', summary: 'Respuesta' },
    tarea: { name: 'Tarea', summary: 'Por qué va aparte' },
    vista: { name: 'Vista', summary: 'Comentario' },
};

/** Si se está revisando lo que enseña un alcance: hay revisión, no se ha pedido ver la rama sin ella y es toda la rama, que
 * es lo que enseña la PR. */
function reviewing(scope) {
    return scope === 'branch' && Boolean(reviewOf()) && !state.changes.reviewHidden;
}

/** La ronda de la revisión que se ve aparte en toda la rama, con su base y sus ficheros; null si se ve toda la rama. */
function reviewRound() {
    const r = state.changes.review;
    if (!state.changes.round || !reviewing('branch') || !r || !r.rounds) return null;
    return r.rounds.find(x => x.round === state.changes.round) || null;
}

/** Lo cambiado que enseña un alcance. En toda la rama, con una ronda a la vista, lo cambiado desde la base de esa ronda
 * que es de la rama (lo que tocaron ella y las siguientes, y lo que aún no se ha commiteado), sin lo que entró con un
 * merge de develop; null mientras llega. */
function changeSet(scope) {
    const round = scope === 'branch' ? reviewRound() : null;
    if (!round) return state.changes.sets[scope];
    const since = state.changes.sets.since;
    if (!since || since.scope !== `since:${round.base}`) return null;
    if (!since.files) return since;
    const ours = new Set(state.changes.review.rounds.filter(x => x.round >= round.round).flatMap(x => x.files));
    const pending = state.changes.sets.pending;
    for (const p of pending && pending.files ? pending.files : []) ours.add(p.path);
    return { ...since, files: since.files.filter(x => ours.has(x.path)) };
}

/** Con qué se compara un fichero en su ficha: la base de su alcance o, en una ronda, si también lo cambió un merge de
 * develop, lo que se mergeó, para no enseñar como de la ronda lo que hizo develop. */
function changeBase(scope, path, d) {
    const round = scope === 'branch' ? reviewRound() : null;
    if (!round) return d.base;
    const merged = state.changes.review.rounds.filter(x => x.round >= round.round && x.merged && x.merged.includes(path) && x.mergedBase);
    return merged.length ? merged[merged.length - 1].mergedBase : d.base;
}

/** La última ronda de la revisión que tocó un fichero; 0 si ninguna. */
function reviewRoundOf(path) {
    const r = state.changes.review;
    return r && r.rounds ? r.rounds.filter(x => x.files.includes(path)).reduce((last, x) => Math.max(last, x.round), 0) : 0;
}

/** Los ficheros que cuenta una ronda en su selector: los que tocó ella y las rondas siguientes. */
function reviewRoundCount(round) {
    const later = state.changes.review.rounds.filter(x => x.round >= round.round);
    return new Set(later.flatMap(x => x.files)).size;
}

/** Si la última ronda de la revisión aún no se ha abierto en esta máquina: lleva un punto hasta verla. */
function reviewRoundIsNew(round) {
    const r = state.changes.review;
    const last = r.rounds[r.rounds.length - 1];
    return round === last && (remembered('reviewRoundsSeen', {})[r.path] || 0) < last.round;
}

/** Ve aparte una ronda de la revisión, o toda la rama con null: la apunta como vista, la pide a la app y repinta la lista
 * y la ficha abierta. Las demás páginas de la misma revisión pasan a la misma ronda, salvo si la elección viene de una de
 * ellas (con «shared»).
 *
 * En la pieza de un fichero no hay lista que la pida: lo cambiado desde la ronda se pide aquí, y la ronda se le dice al
 * panel, que la conserva cuando la página se recarga al cambiarlo de sitio. La ficha de un fichero tiene su propia ronda:
 * ni la reparte ni la recibe. */
function setReviewRound(n, shared) {
    state.changes.round = n;
    if (!shared && PIECE !== 'file') post({ type: 'reviewShow', round: n });
    const r = state.changes.review;
    if (n && r && r.path) {
        const seen = remembered('reviewRoundsSeen', {});
        if ((seen[r.path] || 0) < n) remember('reviewRoundsSeen', { ...seen, [r.path]: n });
    }
    if (PIECE === 'file') {
        post({ type: 'pieceRound', round: n });
        const round = reviewRound();
        if (round) {
            state.changes.sinceAsked = round.base;
            post({ type: 'changes', scope: `since:${round.base}` });
        }
    }
    renderChanges();
    requestChanges(true);
    const v = state.wf;
    if (v && v.detail && v.detail.startsWith('file:branch:') && !v.drawer.hidden) wfDetail(v, v.detail);
}

/** Lleva a lo que cambió una ronda en el fichero de una nota: ve la ronda aparte y abre el diff del fichero en las líneas
 * de la nota. Una nota general solo cambia la lista a la ronda. */
function reviewGoRound(round, n) {
    if (!n.file) {
        setReviewRound(round);
        return;
    }
    setReviewRound(round);
    state.changes.tab = 'diff';
    state.changes.focusLine = n.line && !n.lost ? n.line : null;
    openChange('branch', n.file);
}

/** La franja que avisa, en la lista, de que se ve solo una ronda: desde cuándo, cuántos ficheros y sus líneas, y el
 * enlace para volver a toda la rama. */
function reviewRoundStrip(round) {
    const d = changeSet('branch');
    const strip = el('div', 'rv-since');
    strip.append(el('span', null, 'Desde la '), el('strong', null, `ronda ${round.round}`));
    if (d && !d.error) {
        const total = changeTotals(d.files);
        strip.append(el('span', 'rv-since-count', `· ${d.files.length} fichero${d.files.length === 1 ? '' : 's'}`), changeNums(total.added, total.deleted));
    }
    hint(strip, '↻', `Solo la ronda ${round.round} y lo que vino después`, `contra ${round.base.slice(0, 7)}`,
        'Comparado con el commit de la rama justo antes de la ronda: sus ficheros y sus diffs son solo lo que ha cambiado desde entonces.');
    const back = el('button', 'rv-since-back', 'Ver toda la rama');
    back.addEventListener('click', () => setReviewRound(null));
    strip.append(back);
    return strip;
}

/** Si la ficha de un fichero de un alcance puede cambiar entre toda la rama y las rondas: se revisa toda la rama y hay
 * alguna ronda con código. */
function reviewRoundsOn(scope) {
    const r = state.changes.review;
    return reviewing(scope) && Boolean(r && r.rounds && r.rounds.length);
}

/** La banda de la ficha de un fichero para cambiar entre toda la rama y cada ronda, como la fila «Ver» de la tarjeta;
 * viendo una ronda, en lila. */
function reviewRoundBand() {
    const band = el('div', 'rv-round-band' + (reviewRound() ? ' on' : ''));
    band.append(el('span', 'rv-see-label', 'Ver'), reviewRoundChips(state.changes.review, false));
    return band;
}

/** La ronda con que se abre la ficha de un fichero en su propio panel: la que se ve, si es de toda la rama; null si no. */
function pieceRound(scope) {
    return scope === 'branch' && reviewRound() ? state.changes.round : null;
}

// Lo que tarda como mucho en confirmarse una marca: si la app no la confirma en ese rato, manda lo que diga ella.
const REVIEW_PENDING_MS = 10000;

/** La marca de un fichero en la revisión: si está revisado y si ha cambiado desde entonces.
 *
 * Lo que se acaba de marcar manda hasta que la app lo confirma: marcando deprisa, una respuesta anterior (la de otra
 * casilla o la del repaso del directo) llegaba después y quitaba la marca un momento, o del todo. */
function reviewMarkOf(path) {
    const review = reviewOf();
    if (!review) return null;
    const mark = review.files.find(f => f.path === path) || { path, reviewed: false };
    const pending = state.changes.pendingMarks.get(path);
    return pending && Date.now() - pending.at < REVIEW_PENDING_MS ? { ...mark, reviewed: pending.want, changed: false } : mark;
}

/** Los ficheros revisados de la revisión, contando lo que se acaba de marcar. */
function reviewedPaths() {
    const review = reviewOf();
    return new Set(review ? review.files.map(f => f.path).filter(p => reviewMarkOf(p).reviewed) : []);
}

/** Recibe cómo está la revisión, con su PR si se ha buscado, y pinta lo que la enseña. */
function receiveReview(m) {
    if (!state.trace || m.id !== state.trace.id) return;
    const before = state.changes.review || {};
    state.changes.review = {
        review: m.review || null, path: m.path, error: m.error,
        pr: m.prChecked ? m.pr ?? null : m.review && m.review.pr ? m.review.pr : before.pr,
        prError: m.prChecked ? m.prError || null : before.prError,
        applied: m.applied || {}, appliedWarning: m.appliedWarning || null,
        rounds: m.rounds || [], roundsWarning: m.roundsWarning || null,
    };
    // La ronda que se veía puede haber desaparecido (un rebase); si sigue y aún no se ha pedido su base, se pide.
    if (state.changes.round && !state.changes.review.rounds.some(x => x.round === state.changes.round)) state.changes.round = null;
    const round = reviewRound();
    if (round && state.changes.sinceAsked !== round.base && (changesOpen() || PIECE === 'file')) {
        state.changes.sinceAsked = round.base;
        post({ type: 'changes', scope: `since:${round.base}` });
    }
    // Lo que la app ya confirma deja de estar pendiente; lo que lleva demasiado sin confirmarse, también.
    for (const [path, pending] of state.changes.pendingMarks) {
        const mark = m.review && m.review.files.find(f => f.path === path);
        if (!mark || mark.reviewed === pending.want || Date.now() - pending.at >= REVIEW_PENDING_MS) state.changes.pendingMarks.delete(path);
    }
    renderChanges();
    const v = state.wf;
    if (v && v.detail && v.detail.startsWith('file:branch:') && !v.drawer.hidden) wfDetail(v, v.detail);
    // Con el fichero de la revisión a la vista, se vuelve a leer: puede haber cambiado con lo que acaba de llegar.
    if (reviewFileShown()) post({ type: 'reviewFile' });
}

/** Casilla de «revisado», con una zona de pulsar más grande que el cuadro: llena, a medias («–», en una carpeta con
 * parte revisada) o vacía. */
function reviewBox(state3, onToggle) {
    const box = el('button', `rv-check ${state3}`);
    box.append(el('span', 'rv-box', state3 === 'on' ? '✓' : state3 === 'some' ? '–' : ''));
    // Al pulsar deprisa, el segundo clic llega como doble clic: también cuenta, y no abre la ficha de detrás.
    box.addEventListener('click', e => {
        e.stopPropagation();
        onToggle();
    });
    box.addEventListener('dblclick', e => e.stopPropagation());
    return box;
}

/** Casilla de «revisado» de un fichero: marca o desmarca al momento y se lo pide a la app, que guarda su huella. */
function reviewCheck(path, mark) {
    const box = reviewBox(mark.reviewed ? 'on' : 'off', () => reviewMark([path], !reviewMarkOf(path).reviewed));
    hint(box, '✓', mark.reviewed ? 'Revisado' : 'Sin revisar', null, mark.reviewed
        ? 'Pulsa para devolverlo a sin revisar. Si cambia, vuelve solo.'
        : 'Pulsa para darlo por revisado. Si después cambia, vuelve a sin revisar.');
    return box;
}

/** Da unos ficheros por revisados o los devuelve a «sin revisar»: se ve al momento y la app los guarda con su huella. */
function reviewMark(paths, reviewed) {
    for (const path of paths) state.changes.pendingMarks.set(path, { want: reviewed, at: Date.now() });
    // Revisar un fichero da por leídas las respuestas de sus notas: lo guarda la app al marcarlo, y aquí se ve ya, con
    // un aviso para deshacerlo.
    const review = reviewOf();
    const fresh = reviewed && review ? review.notes.filter(n => n.file && paths.includes(n.file) && outcomeOf(n) && !noteRead(n)) : [];
    if (fresh.length) {
        for (const n of fresh) n.read = outcomeOf(n).round || 0;
        state.changes.readUndo = { ids: fresh.map(n => n.id), files: [...new Set(fresh.map(n => n.file))] };
        clearTimeout(state.changes.readUndoTimer);
        state.changes.readUndoTimer = setTimeout(() => {
            state.changes.readUndo = null;
            renderChanges();
        }, REVIEW_UNDO_MS);
    }
    post({ type: 'reviewMark', paths, reviewed });
    renderChanges();
    const v = state.wf;
    if (v && v.detail && paths.some(p => v.detail === `file:branch:${p}`) && !v.drawer.hidden) wfDetail(v, v.detail);
}

/** La PR de la rama, para la tarjeta de la revisión: buscándola, su número, que no hay, o que el remoto no ha contestado,
 * con un enlace para volver a preguntar. */
function reviewPr(r, review) {
    if (review && review.pr) return [el('span', 'rv-pr-num', `PR #${review.pr}`)];
    if (r.pr === undefined) return [el('span', null, 'buscando la PR…')];
    if (r.pr) return [el('span', 'rv-pr-num', `PR #${r.pr}`)];
    if (!r.prError) return [el('span', null, 'sin PR')];
    const failed = el('span', 'rv-pr-failed', 'PR sin comprobar');
    hint(failed, '⚠', 'No se ha podido mirar la PR', null, r.prError);
    const retry = el('button', 'rv-retry', 'Reintentar');
    retry.addEventListener('click', () => {
        state.changes.review = { ...r, pr: undefined, prError: null };
        post({ type: 'reviewPr' });
        renderChanges();
    });
    return [failed, retry];
}

/** Lo de arriba del panel en toda la rama, en una tarjeta: sin revisión, qué se va a revisar y el botón para empezar; con
 * ella, cuánto va revisado con su barra, el filtro de lo que falta y las notas y, si se piden, su lista con la de una nota
 * general. */
function reviewHead() {
    const r = state.changes.review || {};
    const review = reviewOf();
    const branch = state.changes.sets.branch;
    const card = el('div', 'rv-card' + (review ? ' on' : ''));
    const icon = el('span', 'rv-icon');
    icon.append(flowIcon('review', 'flow-icon'));
    const sub = el('div', 'rv-sub');
    sub.append(...reviewPr(r, review));
    if (!review) {
        const files = branch && branch.files ? branch.files.length : 0;
        sub.append(el('span', null, `· ${files} fichero${files === 1 ? '' : 's'}`));
        const start = el('button', 'rv-start', r.starting ? 'Empezando…' : 'Empezar');
        hint(start, '✓', 'Empezar la revisión', null,
            'Cada fichero de la rama tendrá su casilla de revisado, y podrás anotar correcciones, mejoras, dudas y lo que está bien. Se guarda en docs/reviews/, fuera de git.');
        start.addEventListener('click', () => {
            state.changes.review = { ...r, starting: true };
            post({ type: 'reviewStart' });
            renderChanges();
        });
        card.append(icon, el('div', 'rv-title', 'Revisar la rama'), start, sub);
        if (r.error) card.append(el('div', 'rv-error', r.error));
        return [card];
    }
    const done = reviewedPaths().size;
    const total = review.files.length;
    const left = total - done;
    sub.append(left
        ? el('span', 'rv-left', `· faltan ${left}`)
        : el('span', 'rv-all', '· todo revisado'));
    const count = el('span', 'rv-count-big', `${done}/${total}`);
    hint(count, '✓', `${done} de ${total} ficheros revisados`, null, r.path ? `La revisión se guarda en ${r.path}, fuera de git.` : null);
    // Sin la revisión a la vista, la tarjeta solo recuerda que hay una a medias y deja seguir con ella.
    if (state.changes.reviewHidden) {
        const resume = el('button', 'rv-start', 'Seguir revisión');
        resume.addEventListener('click', () => setReviewHidden(false));
        card.append(icon, el('div', 'rv-title', 'Revisión en curso'), resume, sub);
        return [card];
    }
    const progress = el('div', 'rv-progress');
    const fill = el('i');
    fill.style.width = `${total ? (done / total) * 100 : 0}%`;
    progress.append(fill);
    const only = el('button', 'chip rv-filter' + (state.changes.onlyUnreviewed ? ' on' : ''), 'Sin revisar');
    only.addEventListener('click', () => {
        state.changes.onlyUnreviewed = !state.changes.onlyUnreviewed;
        renderChanges();
    });
    const pending = review.notes.filter(n => !outcomeOf(n)).length;
    const unread = review.notes.filter(n => outcomeOf(n) && !noteRead(n)).length;
    const aside = notesAsideNow();
    const inline = state.changes.showNotes && !aside;
    const counts = [pending ? `${pending} pendiente${pending === 1 ? '' : 's'}` : '', unread ? `${unread} sin leer` : ''].filter(Boolean);
    const notes = el('button', 'chip rv-filter' + (inline && state.changes.notesView === 'notes' ? ' on' : ''),
        `Notas (${[String(review.notes.length), ...counts].join(' · ')})${aside ? ' ↗' : ''}`);
    if (aside) hint(notes, '↗', 'Ir a las notas', null, 'Están abiertas aparte, en su propio panel.');
    else if (review.notes.length > pending) hint(notes, '✓', counts.join(' y ') || 'Todo leído', null,
        'Lo resuelto lo marca /opsx:review al cerrar cada ronda, en su fichero de lo aplicado. Lo que ya has leído va plegado al final.');
    notes.addEventListener('click', () => {
        if (aside) return showNotesAside('notes');
        state.changes.showNotes = !(inline && state.changes.notesView === 'notes');
        state.changes.notesView = 'notes';
        renderChanges();
    });
    const preview = el('button', 'chip rv-filter' + (inline && state.changes.notesView === 'file' ? ' on' : ''), `Ver fichero${aside ? ' ↗' : ''}`);
    hint(preview, '✎', 'Ver el fichero de la revisión', null, 'Cómo queda docs/reviews/…-review.md, el que leerá /opsx:review: escrito o tal cual.');
    preview.addEventListener('click', () => {
        if (aside) return showNotesAside('file');
        const on = inline && state.changes.notesView === 'file';
        state.changes.showNotes = !on;
        state.changes.notesView = 'file';
        if (!on) post({ type: 'reviewFile' });
        renderChanges();
    });
    const plain = el('button', 'rv-plain', 'Ver la rama sin revisión');
    hint(plain, '👁', 'Ver la rama como siempre', null, 'Sin casillas ni notas. La revisión sigue guardada: «Seguir revisión» la vuelve a enseñar donde estaba.');
    plain.addEventListener('click', () => setReviewHidden(true));
    const tools = el('div', 'rv-tools');
    tools.append(only, notes, preview, plain);
    card.append(icon, el('div', 'rv-title', 'Revisión'), count, sub, progress, tools);
    if (r.rounds && r.rounds.length) card.append(reviewRoundPicker(r));
    if (r.error) card.append(el('div', 'rv-error', r.error));
    if (r.appliedWarning) {
        const warn = el('div', 'rv-applied-warn', `⚠ ${r.appliedWarning}`);
        hint(warn, '⚠', 'Lo aplicado no se ha tenido en cuenta', null, 'Lo escribe /opsx:review al cerrar cada ronda; la app solo lo lee. La revisión funciona igual, con las notas como pendientes.');
        card.append(warn);
    }
    if (r.roundsWarning) {
        const warn = el('div', 'rv-applied-warn', `⚠ ${r.roundsWarning}`);
        hint(warn, '⚠', 'Una ronda no se puede ver aparte', null, 'Sus notas siguen marcadas como resueltas; sus cambios se ven en toda la rama.');
        card.append(warn);
    }
    const parts = [card];
    if (state.changes.readUndo) parts.push(reviewUndoBar());
    const round = reviewRound();
    if (round) parts.push(reviewRoundStrip(round));
    if (inline) parts.push(reviewNotesBlock(false));
    return parts;
}

/** Si las notas de la revisión están abiertas aparte, en su propio panel: entonces no se despliegan aquí. */
function notesAsideNow() {
    return Boolean(state.changes.notesAside) && PIECE !== 'review';
}

/** El nombre del panel de las notas abiertas aparte. */
function notesTitle() {
    return `Notas de la revisión · ${sessionName(state.trace)}`;
}

/** Lleva a las notas abiertas aparte, con sus notas o el fichero de la revisión a la vista. */
function showNotesAside(view) {
    post({ type: 'reviewShow', view });
    post({ type: 'pieceOpen', piece: 'review', title: notesTitle() });
}

/** Si se ve el fichero de la revisión en el sitio de las notas, para volver a pedirlo cuando cambia. */
function reviewFileShown() {
    return state.changes.notesView === 'file' && (PIECE === 'review' || (state.changes.showNotes && !notesAsideNow()));
}

// Lo que se queda el aviso de lo dado por leído al revisar un fichero, con su «Deshacer».
const REVIEW_UNDO_MS = 10000;

/** Si ya se ha leído lo que contestó /opsx:review a una nota en su última ronda: si otra ronda la vuelve a tratar, su
 * respuesta es otra y vuelve a estar sin leer. */
function noteRead(n) {
    const o = outcomeOf(n);
    return Boolean(o) && n.read != null && n.read >= (o.round || 0);
}

/** Da por leídas las respuestas de unas notas resueltas, o las devuelve a «sin leer»: se ve al momento y la app lo guarda
 * en la revisión. */
function reviewRead(ids, read) {
    const review = reviewOf();
    if (!review || !ids.length) return;
    for (const n of review.notes) if (ids.includes(n.id)) n.read = read && outcomeOf(n) ? outcomeOf(n).round || 0 : null;
    post({ type: 'reviewRead', ids, read });
    renderChanges();
}

/** El aviso de lo que se acaba de dar por leído al revisar un fichero, con su «Deshacer». */
function reviewUndoBar() {
    const u = state.changes.readUndo;
    const count = u.ids.length;
    const what = u.files.length === 1 ? u.files[0].split('/').pop() : `${u.files.length} ficheros`;
    const bar = el('div', 'rv-undo');
    bar.append(el('span', null, `${count === 1 ? '1 nota resuelta, leída' : `${count} notas resueltas, leídas`} al revisar ${what}`));
    const undo = el('button', 'rv-undo-btn', 'Deshacer');
    undo.addEventListener('click', () => {
        clearTimeout(state.changes.readUndoTimer);
        state.changes.readUndo = null;
        reviewRead(u.ids, false);
    });
    bar.append(undo);
    return bar;
}

/** Las notas de la revisión, iguales dentro de Cambios y abiertas aparte. Arriba, si se ven las notas o el fichero de la
 * revisión; debajo, las que faltan por tratar o por leer y, plegadas al final, las ya leídas. Con una ronda a la vista,
 * solo lo que resolvió esa ronda. Aparte llevan su propio selector de rondas; dentro de Cambios, en un corcho, el botón
 * para abrirlas aparte. */
function reviewNotesBlock(inPane) {
    const review = reviewOf();
    const r = state.changes.review;
    const list = el('div', 'rv-notes' + (inPane ? ' aside' : ''));
    const top = el('div', 'rv-notes-top');
    const seg = el('div', 'segmented rv-notes-view');
    for (const [key, label] of [['notes', 'Notas'], ['file', 'Fichero']]) {
        const chip = el('button', 'chip' + (state.changes.notesView === key ? ' on' : ''), label);
        chip.addEventListener('click', () => {
            if (state.changes.notesView === key) return;
            state.changes.notesView = key;
            if (key === 'file') post({ type: 'reviewFile' });
            renderChanges();
        });
        seg.append(chip);
    }
    top.append(seg);
    const round = reviewRound();
    if (state.changes.notesView === 'notes' && !round) {
        const add = el('button', 'rv-add', '+ Nota general');
        add.addEventListener('click', () => reviewCompose());
        top.append(add);
    }
    if (!inPane && changesInCork()) {
        const apart = el('button', 'rv-apart', '⧉ Abrir aparte');
        hint(apart, '⧉', 'Abrir las notas aparte', null, 'En su propio recuadro del corcho, junto a Cambios. Al cerrarlo, vuelven aquí.');
        apart.addEventListener('click', () => post({ type: 'pieceOpen', piece: 'review', title: notesTitle(), where: 'beside' }));
        top.append(apart);
    }
    list.append(top);
    if (state.changes.notesView === 'file') {
        list.append(reviewFileView());
        return list;
    }
    if (inPane && r && r.rounds && r.rounds.length) list.append(reviewRoundBand());
    if (state.changes.compose && state.changes.compose.file === null) list.append(reviewComposer());
    const inRound = n => !round || (outcomeOf(n) && outcomeOf(n).round === round.round);
    const shown = review.notes.filter(inRound).sort(reviewNoteOrder);
    const open = shown.filter(n => !noteRead(n));
    const read = shown.filter(n => noteRead(n));
    if (!review.notes.length) list.append(el('div', 'changes-note', 'Aún no hay notas: selecciona una o varias líneas en la ficha de un fichero para anotarlas.'));
    else if (round && !shown.length) list.append(el('div', 'changes-note', `La ronda ${round.round} no resolvió ninguna nota.`));
    else if (!open.length) list.append(el('div', 'changes-note', round ? `Lo de la ronda ${round.round}, todo leído.` : 'Nada pendiente ni por leer.'));
    for (const n of open) list.append(reviewNoteCard(n, true));
    if (read.length) {
        const fold = el('button', 'rv-read-fold' + (state.changes.readOpen ? ' open' : ''));
        fold.append(el('span', 'rv-read-chev', '›'), el('span', null, `Leídas · ${read.length}`), el('span', 'rv-read-rule'));
        hint(fold, '✓', 'Las notas ya leídas', null, 'Sus respuestas ya las has leído. Si otra ronda vuelve a tratar una, sale otra vez arriba.');
        fold.addEventListener('click', () => {
            state.changes.readOpen = !state.changes.readOpen;
            renderChanges();
        });
        list.append(fold);
        if (state.changes.readOpen) for (const n of read) list.append(reviewNoteCard(n, true));
    }
    return list;
}

/** La fila «Ver» de la tarjeta de la revisión: toda la rama o cada ronda de /opsx:review con código, con los ficheros que
 * tocó; la última, con un punto hasta abrirla. */
function reviewRoundPicker(r) {
    const row = el('div', 'rv-see');
    row.append(el('span', 'rv-see-label', 'Ver'), reviewRoundChips(r, true));
    return row;
}

/** Los botones para elegir entre toda la rama y cada ronda de la revisión, con los ficheros que cuenta cada una si se
 * piden; la última ronda, con un punto hasta abrirla. */
function reviewRoundChips(r, counts) {
    const seg = el('div', 'segmented rv-see-seg');
    const all = el('button', 'chip' + (reviewRound() ? '' : ' on'), 'Toda la rama');
    const branch = state.changes.sets.branch;
    hint(all, '⑂', 'Toda la rama', branch && branch.baseLabel ? `contra ${branch.baseLabel}` : null,
        'Todo lo cambiado en la rama, como lo enseña la PR.');
    all.addEventListener('click', () => setReviewRound(null));
    seg.append(all);
    for (const round of r.rounds) {
        const label = counts ? `Ronda ${round.round} · ${reviewRoundCount(round)}` : `Ronda ${round.round}`;
        const chip = el('button', 'chip rv-see-round' + (state.changes.round === round.round ? ' on' : ''), label);
        if (reviewRoundIsNew(round)) chip.append(el('span', 'rv-see-new'));
        hint(chip, '↻', `Ronda ${round.round} de /opsx:review`, `contra ${round.base.slice(0, 7)}`,
            'Solo lo cambiado desde que empezó la ronda hasta ahora: sus ficheros y sus diffs. Las casillas y las notas siguen igual.');
        chip.addEventListener('click', () => setReviewRound(round.round));
        seg.append(chip);
    }
    return seg;
}

/** El orden de las notas en su lista: las generales, y después por fichero y, en cada uno, las del fichero entero (y las
 * que perdieron su línea) antes que las de sus líneas, de la primera a la última. Las empatadas, como se escribieron. */
function reviewNoteOrder(a, b) {
    if (!a.file !== !b.file) return a.file ? 1 : -1;
    if (a.file !== b.file) return a.file.localeCompare(b.file);
    const line = n => (n.line && !n.lost ? n.line : 0);
    return line(a) - line(b) || (a.oldLine || 0) - (b.oldLine || 0);
}

/** El fichero de la revisión en el sitio de las notas: escrito, como se lee, o su Markdown tal cual. Se pone al día al
 * marcar o anotar. */
function reviewFileView() {
    const r = state.changes.reviewText;
    const tab = state.changes.reviewTab || 'view';
    const box = el('div', 'rv-file-view');
    const tabs = el('div', 'segmented rv-file-tabs');
    for (const [key, label] of [['view', 'Vista'], ['code', 'Markdown']]) {
        const chip = el('button', 'chip' + (tab === key ? ' on' : ''), label);
        chip.addEventListener('click', () => {
            state.changes.reviewTab = key;
            renderChanges();
        });
        tabs.append(chip);
    }
    box.append(tabs);
    const path = (state.changes.review && state.changes.review.path) || 'docs/reviews/';
    const name = path.replace(/\\/g, '/').split('/').slice(-3).join('/');
    if (!r) box.append(el('div', 'changes-note', 'Leyendo la revisión…'));
    else if (r.error) box.append(el('div', 'changes-note error', r.error));
    else if (tab === 'code') box.append(wfFileWin(wfFile(name, WF_LANGS.md, 'tal cual'), [wfWholeFile(r.text || '', WF_LANGS.md, null)]));
    else {
        // Escrito, sin lo que es solo para la app (los comentarios con sus datos), y con las casillas como marcas: el
        // Markdown de la página no tiene listas de tareas.
        const md = el('div', 'wf-md rv-preview');
        md.append(markdown((r.text || '').replace(/^<!--[\s\S]*?-->\s*$/gm, '').replace(/ ?<!--[\s\S]*?-->/g, '')
            .replace(/^- \[x\] /gm, '- ✓ ').replace(/^- \[ \] /gm, '- ○ ')));
        box.append(md);
    }
    return box;
}

/** Enseña la rama como siempre, sin la revisión, o vuelve a ella; la revisión sigue guardada igual. */
function setReviewHidden(hidden) {
    state.changes.reviewHidden = hidden;
    remember('reviewHidden', hidden);
    renderChanges();
    const v = state.wf;
    if (v && v.detail && v.detail.startsWith('file:branch:') && !v.drawer.hidden) wfDetail(v, v.detail);
}

/** Huella de lo que la revisión enseña en la ficha de un fichero, para no repintarla si no ha cambiado.
 *
 * Las líneas que se están anotando no cuentan: se marcan sobre la ficha tal cual, sin repintarla, para que no salte. */
function reviewSig(scope, path) {
    if (!reviewing(scope)) return '';
    const mark = reviewMarkOf(path);
    const notes = reviewOf().notes.filter(n => n.file === path)
        .map(n => `${n.id}:${n.kind}:${n.line}-${n.endLine}:${n.lost}:${outcomeOf(n) ? 'r' : ''}`).join(',');
    return [mark.reviewed, mark.changed, notes].join('/');
}

/** Lo de la revisión en la cabecera de la ficha de un fichero: una nota al fichero entero, que se abre con el mismo menú
 * flotante que las de unas líneas, y su casilla de revisado. */
function reviewTools(path) {
    const mark = reviewMarkOf(path);
    const add = el('button', 'chip rv-file-add');
    add.append(flowIcon('edit', 'flow-icon'), 'Nota al fichero');
    hint(add, '✎', 'Nota al fichero entero', null, 'Para unas líneas, selecciónalas en el código o arrastra sobre sus números.');
    add.addEventListener('click', e => {
        e.stopPropagation();
        reviewPick({ file: path, line: null, endLine: null }, { x: e.clientX, y: e.clientY, sel: '.rv-file-add' });
    });
    const check = el('button', 'chip rv-reviewed' + (mark.reviewed ? ' on' : ''), mark.reviewed ? '✓ Revisado' : 'Marcar revisado');
    check.addEventListener('click', () => {
        const done = !mark.reviewed;
        reviewMark([path], done);
        // Revisado, el fichero ya no hace falta a la vista: su panel se vacía, para el siguiente, o se cierra el detalle que lo enseña.
        if (!done) return;
        if (PIECE === 'file' && window.invokeCSharpAction) post({ type: 'pieceEmpty' });
        else if (state.wf && state.wf.detail === `file:branch:${path}`) wfCloseDetail(state.wf);
    });
    return [add, check];
}

/** Las notas de un fichero que no van en una línea (las del fichero entero y las que perdieron la suya), en una franja
 * sobre el código: cada una, una marca de su color con el principio de su texto; al pasar el ratón, su ficha. Viendo una
 * ronda, también las que son solo de lo quitado, que en su diff no tienen dónde ir. */
function reviewFileNotes(path, inRound) {
    const notes = reviewOf().notes.filter(n => n.file === path && (!n.line || n.lost || (inRound && n.anchor)));
    if (!notes.length) return [];
    const strip = el('div', 'rv-file-strip');
    strip.append(el('span', 'rv-file-strip-label', 'Al fichero'));
    for (const n of notes) {
        const chip = el('span', `rv-file-chip kind-${n.kind}` + (outcomeOf(n) ? ' resolved' : ''));
        chip.dataset.id = n.id;
        chip.append(reviewKindMark(n.kind), el('span', 'rv-file-chip-text', n.text));
        if (n.lost) chip.append(el('span', 'rv-file-chip-tag', 'sin línea'));
        else if (n.anchor && n.line) chip.append(el('span', 'rv-file-chip-tag', 'lo quitado'));
        chip.addEventListener('mouseenter', e => reviewHoverEnter(path, [n], { x: e.clientX, y: e.clientY, sel: `.rv-file-chip[data-id="${n.id}"]` }));
        chip.addEventListener('mouseleave', () => reviewHoverLeave());
        strip.append(chip);
    }
    return [strip];
}

/** El texto de una fila de código, sin su número. */
function reviewRowText(row) {
    const num = row.querySelector('.wf-code-num');
    return row.textContent.slice(num ? num.textContent.length : 0);
}

/** Pone la revisión en el código de una ficha: las líneas de cada nota, en el color de su tipo, y deja elegir líneas para
 * anotarlas y ver la nota de una línea al pasar el ratón por ella. */
function reviewDecorate(view, path) {
    const rows = [...view.querySelectorAll(REVIEW_ROWS)];
    for (const row of rows) row.querySelector('.wf-code-num').classList.add('rv-num');
    reviewPaint(rows, path);
    reviewSelectable(view, path, rows);
    reviewHoverable(view, path);
}

// Las filas de código en las que se puede anotar: las del fichero de ahora y, en el diff, las quitadas.
const REVIEW_ROWS = '.wf-code-line[data-line], .wf-code-line[data-old]';

/** Las notas de un fichero que caen en una fila de código: por su línea de ahora o, si es quitada, por su línea en la
 * base. Una nota solo de lo quitado cae también en la línea que la sitúa, salvo en el diff, donde se ve lo quitado. */
function reviewNotesAt(path, row) {
    const notes = reviewOf().notes.filter(n => n.file === path && n.line && !n.lost);
    if (row.dataset.old) return notes.filter(n => reviewCovers(n, row));
    const line = Number(row.dataset.line);
    const diff = Boolean(row.closest('.wf-diff'));
    return notes.filter(n => line >= n.line && line <= (n.endLine || n.line) && !(n.anchor && diff));
}

/** Si una fila de código cae en lo que se está anotando, o en una nota. */
function reviewCovers(n, row) {
    if (row.dataset.old) return Boolean(n.oldLine) && Number(row.dataset.old) >= n.oldLine && Number(row.dataset.old) <= (n.oldEndLine || n.oldLine);
    return Boolean(n.line) && !n.anchor && Number(row.dataset.line) >= n.line && Number(row.dataset.line) <= (n.endLine || n.line);
}

/** La clave de una fila de código, para volver a encontrarla al repintar: su línea de ahora o la quitada. */
function reviewRowKey(row) {
    return row.dataset.old ? `o${row.dataset.old}` : `n${row.dataset.line}`;
}

/** La fila de código de una clave, en la ficha abierta; null si no está, como una quitada vista en «Fichero». */
function reviewRowOf(drawer, key) {
    return drawer.querySelector(key[0] === 'o' ? `.wf-code-line[data-old="${key.slice(1)}"]` : `.wf-code-line[data-line="${key.slice(1)}"]`);
}

// Las clases con el color de cada tipo de nota, para quitarlas todas antes de poner la que toca.
const REVIEW_KIND_CLASSES = REVIEW_KINDS.map(([key]) => `kind-${key}`);

/** Marca en unas filas de código las líneas con nota y las que se están anotando: el texto, como pasado con un
 * subrayador del color de su tipo, y junto al número una barra por nota.
 *
 * Una línea con varias notas se subraya del color de la primera pendiente y lleva sus barras una al lado de otra, cada
 * una de su color; las resueltas salen más tenues, para que destaquen las que aún tienen algo por hacer. */
function reviewPaint(rows, path) {
    const c = state.changes.compose;
    const picking = c && c.file === path && c.line ? c : null;
    const p = state.changes.pop;
    const shown = new Set(p && p.mode === 'show' && p.path === path && p.ids ? p.ids.split(',') : []);
    for (const row of rows) {
        const notes = reviewNotesAt(path, row);
        const picked = picking && reviewCovers(picking, row);
        const ordered = [...notes.filter(n => !outcomeOf(n)), ...notes.filter(n => outcomeOf(n))];
        const kind = picked ? picking.kind : ordered.length && ordered[0].kind;
        row.classList.remove(...REVIEW_KIND_CLASSES);
        if (kind) row.classList.add(`kind-${kind}`);
        // Lo que se está anotando tapa las notas que ya hubiera en esas líneas, hasta guardarlo o descartarlo.
        const noted = !picked && notes.length > 0;
        row.classList.toggle('rv-picked', Boolean(picked));
        row.classList.toggle('rv-noted', noted);
        row.classList.toggle('rv-noted-done', noted && notes.every(n => outcomeOf(n)));
        row.classList.toggle('rv-hot', noted && notes.some(n => shown.has(n.id)));
        // La línea que sitúa una nota solo de lo quitado lleva su barra, pero no se subraya: la nota no es de ella.
        const inked = noted && notes.some(n => reviewCovers(n, row));
        row.classList.toggle('rv-anchor', noted && !inked);
        if (picked || inked) reviewInk(row);
        const bars = picked ? [picking.kind ? `var(--kc-${picking.kind})` : 'var(--accent)']
            : ordered.map(n => outcomeOf(n) ? `color-mix(in srgb, var(--kc-${n.kind}) 40%, transparent)` : `var(--kc-${n.kind})`);
        row.querySelector('.wf-code-num').style.backgroundImage = reviewBars(bars);
    }
}

// El ancho de cada barra junto al número de una línea anotada y el hueco que la separa de la siguiente.
const REVIEW_BAR = 3;
const REVIEW_BAR_GAP = 1;

/** Las barras de unas notas junto al número de línea, de dentro afuera: un degradado con cortes secos, que deja el
 * hueco entre ellas transparente. */
function reviewBars(colors) {
    if (!colors.length) return '';
    const step = REVIEW_BAR + REVIEW_BAR_GAP;
    const stops = colors.flatMap((color, i) => [`${color} ${i * step}px ${i * step + REVIEW_BAR}px`, `transparent ${i * step + REVIEW_BAR}px ${(i + 1) * step}px`]);
    return `linear-gradient(to left, ${stops.join(', ')}, transparent ${colors.length * step}px)`;
}

/** Envuelve el texto de una fila de código en el trazo del subrayador, sin la sangría, como lo deja quien subraya. Una
 * vez por fila; una línea en blanco no lleva trazo. */
function reviewInk(row) {
    const text = row.querySelector('.wf-code-num').nextElementSibling;
    if (!text || text.querySelector('.rv-ink')) return;
    let first = text.firstChild;
    while (first && first.nodeType === Node.TEXT_NODE && !first.data.trim()) first = first.nextSibling;
    if (!first) return;
    if (first.nodeType === Node.TEXT_NODE) {
        const indent = first.data.length - first.data.trimStart().length;
        if (indent) first = first.splitText(indent);
    }
    const ink = el('span', 'rv-ink');
    while (first) {
        const next = first.nextSibling;
        ink.append(first);
        first = next;
    }
    text.append(ink);
}

/** Vuelve a marcar las líneas de la ficha abierta, sin repintarla. */
function reviewRepaint() {
    const v = state.wf;
    if (!v || v.drawer.hidden || !v.detail || !v.detail.startsWith('file:branch:') || !reviewing('branch')) return;
    reviewPaint([...v.drawer.querySelectorAll(REVIEW_ROWS)], v.detail.slice('file:branch:'.length));
}

/** Deja elegir líneas para anotarlas: arrastrando sobre los números, pulsando uno o seleccionando el código como para
 * copiarlo. Al soltar, sale junto al cursor el menú de tipos de nota. Se eligen filas seguidas, como se ven: en el diff,
 * también las quitadas. */
function reviewSelectable(view, path, rows) {
    const index = new Map(rows.map((r, i) => [r, i]));
    let drag = null;
    const paint = () => {
        const [from, to] = drag ? [Math.min(drag.from, drag.to), Math.max(drag.from, drag.to)] : [0, -1];
        rows.forEach((row, i) => row.classList.toggle('rv-selecting', i >= from && i <= to));
    };
    const pick = (a, b, x, y) => {
        const target = reviewTarget(path, rows, Math.min(a, b), Math.max(a, b));
        if (target) reviewPick(target, { x, y, key: reviewRowKey(rows[b]), down: b > a });
    };
    view.addEventListener('mousedown', e => {
        const num = e.target.closest('.rv-num');
        if (!num || e.button !== 0) return;
        e.preventDefault();
        e.stopPropagation();
        const at = index.get(num.parentElement);
        drag = { from: at, to: at };
        paint();
        const up = m => {
            document.removeEventListener('mouseup', up);
            const done = drag;
            drag = null;
            paint();
            if (done) pick(done.from, done.to, m.clientX, m.clientY);
        };
        document.addEventListener('mouseup', up);
    });
    view.addEventListener('mouseover', e => {
        if (!drag) return;
        const row = e.target.closest(REVIEW_ROWS);
        if (row && index.has(row)) {
            drag.to = index.get(row);
            paint();
        }
    });
    // La selección del código se deja hasta elegir el tipo: mientras sale el menú, aún se puede copiar.
    view.addEventListener('mouseup', e => {
        if (drag) return;
        setTimeout(() => {
            const sel = window.getSelection();
            if (!sel || sel.isCollapsed || !view.contains(sel.anchorNode) || !view.contains(sel.focusNode)) return;
            const rowOf = node => index.get((node.nodeType === 1 ? node : node.parentElement).closest(REVIEW_ROWS));
            const a = rowOf(sel.anchorNode);
            const b = rowOf(sel.focusNode);
            if (a === undefined || b === undefined) return;
            pick(a, b, e.clientX, e.clientY);
        });
    });
}

/** Lo que se anota al elegir unas filas seguidas: sus líneas de ahora y, si hay quitadas, sus líneas en la base y su
 * texto. Si solo hay quitadas, la nota se sitúa en la línea que las sigue o, al final del fichero, en la de antes. */
function reviewTarget(path, rows, from, to) {
    const span = rows.slice(from, to + 1);
    const now = span.filter(r => r.dataset.line).map(r => Number(r.dataset.line));
    const gone = span.filter(r => r.dataset.old);
    const target = { file: path, line: null, endLine: null, anchor: false };
    if (now.length) {
        target.line = Math.min(...now);
        const last = Math.max(...now);
        target.endLine = last > target.line ? last : null;
    } else {
        const next = rows.slice(to + 1).find(r => r.dataset.line) || rows.slice(0, from).reverse().find(r => r.dataset.line);
        if (!next) return null;
        Object.assign(target, { line: Number(next.dataset.line), anchor: true });
    }
    target.lineText = reviewRowText(rows.find(r => r.dataset.line === String(target.line)));
    if (gone.length) Object.assign(target, {
        oldLine: Number(gone[0].dataset.old),
        oldEndLine: gone.length > 1 ? Number(gone[gone.length - 1].dataset.old) : null,
        removed: gone.map(reviewRowText).join('\n'),
    });
    return target;
}

/** Deja ver las notas de una línea al pasar el ratón por ella. No sale mientras se eligen líneas o se escribe una nota. */
function reviewHoverable(view, path) {
    view.addEventListener('mouseover', e => {
        if (e.buttons) return;
        const row = e.target.closest('.wf-code-line.rv-noted');
        if (!row) {
            reviewHoverLeave();
            return;
        }
        // Por debajo del principio de lo marcado, la ficha sale debajo del ratón: encima taparía lo de arriba.
        const prev = row.previousElementSibling;
        reviewHoverEnter(path, reviewNotesAt(path, row),
            { x: e.clientX, y: e.clientY, key: reviewRowKey(row), down: Boolean(prev && prev.classList.contains('rv-noted')) });
    });
    view.addEventListener('mouseleave', () => reviewHoverLeave());
}

// Lo que espera la ficha de una nota antes de salir al pasar el ratón por sus líneas, y antes de irse al dejarlas: lo
// bastante para no parpadear al cruzar el código y para llegar a ella con el ratón.
const REVIEW_HOVER_IN = 180;
const REVIEW_HOVER_OUT = 240;

/** El ratón ha llegado a unas notas, en sus líneas o en la cabecera del fichero: sale su ficha junto a él, o se queda la
 * que ya las enseña. Mientras se eligen líneas o se escribe una nota, no sale. */
function reviewHoverEnter(path, notes, cursor) {
    const h = state.changes.hover;
    const p = state.changes.pop;
    if (p && p.mode !== 'show') return;
    clearTimeout(h.out);
    const ids = notes.map(n => n.id).join(',');
    if (p && p.ids === ids) return;
    // La ficha sale donde esté el ratón al cumplirse la espera, no donde entró en la línea.
    h.cursor = cursor;
    if (h.in && h.ids === ids) return;
    clearTimeout(h.in);
    h.ids = ids;
    // Con otra ficha a la vista, se cambia casi al momento: ya se está leyendo notas.
    h.in = setTimeout(() => {
        h.in = null;
        reviewPopShow(path, notes, h.cursor);
    }, p && p.mode === 'show' ? 60 : REVIEW_HOVER_IN);
}

/** El ratón ha dejado las líneas con notas o su ficha: la ficha se va, si no vuelve a tiempo. */
function reviewHoverLeave() {
    const h = state.changes.hover;
    clearTimeout(h.in);
    h.in = null;
    clearTimeout(h.out);
    h.out = setTimeout(() => {
        const p = state.changes.pop;
        if (p && p.mode === 'show') reviewPopRemove();
    }, REVIEW_HOVER_OUT);
}

/** Elige unas líneas para anotarlas: se quedan marcadas y sale junto al cursor el menú de tipos de nota.
 *
 * Con una nota a medio escribir, se sigue con ella: elegir otras líneas no se lleva lo escrito. */
function reviewPick(target, cursor) {
    const c = state.changes.compose;
    if (reviewWriting()) {
        state.changes.pop.box.querySelector('textarea').focus();
        return;
    }
    reviewPopClose();
    const general = c && c.file === null;
    state.changes.compose = { ...target, kind: null, text: '', cursor };
    if (general) renderChanges();
    reviewRepaint();
    reviewPopKinds();
}

// Cómo se ve cada tipo de nota en el menú flotante: su marca y lo que pide la caja.
const REVIEW_KIND_LOOK = {
    correccion: ['!', 'Qué hay que corregir…'],
    mejora: ['↑', 'Qué se puede mejorar…'],
    duda: ['?', 'Qué quieres preguntar…'],
    bien: ['✓', 'Qué está bien…'],
};

/** Dónde va una nota o lo que se está anotando: «Línea 12», «Líneas 12–18», «Quitada 10», «Líneas 12–13 · quitadas
 * 10–11» o, sin línea, «Todo el fichero». */
function reviewLines(n) {
    if (n.lost) return 'Su línea ya no está';
    if (!n.line) return 'Todo el fichero';
    const range = (a, b, word) => (b && b !== a ? `${word}s ${a}–${b}` : `${word} ${a}`);
    const parts = [];
    if (!n.anchor) parts.push(range(n.line, n.endLine, 'Línea'));
    if (n.oldLine) parts.push(range(n.oldLine, n.oldEndLine, parts.length ? 'quitada' : 'Quitada'));
    return parts.join(' · ');
}

/** La marca de un tipo de nota: un cuadro en su color con su signo. */
function reviewKindMark(kind) {
    return el('span', 'rv-pop-mark', (REVIEW_KIND_LOOK[kind] || ['•'])[0]);
}

/** El menú de tipos de nota que sale encima de las líneas elegidas: al pulsar uno, las líneas toman su color y se abre
 * la caja para escribir la nota. */
function reviewPopKinds() {
    const c = state.changes.compose;
    const box = el('div', 'rv-pop-kinds');
    const list = el('div', 'rv-pop-kind-list');
    for (const [key, name] of REVIEW_KINDS) {
        const item = el('button', `rv-pop-kind kind-${key}` + (c.kind === key ? ' on' : ''));
        item.append(reviewKindMark(key), el('span', 'rv-hl', name));
        item.addEventListener('click', () => {
            c.kind = key;
            window.getSelection().removeAllRanges();
            reviewRepaint();
            reviewPopWrite();
        });
        list.append(item);
    }
    box.append(el('div', 'rv-pop-where', `Anotar · ${reviewLines(c)}`), list);
    reviewPopOpen('kinds', box, c.file, c.line, c.endLine || c.line, c.cursor);
}

/** La caja de la nota de unas líneas, junto a ellas y en el color de su tipo. Pulsar el tipo vuelve al menú para
 * cambiarlo sin perder lo escrito; Ctrl+Intro guarda y Esc descarta. */
function reviewPopWrite() {
    const c = state.changes.compose;
    const box = el('div', `rv-pop-write kind-${c.kind}`);
    const head = el('div', 'rv-pop-head');
    const kind = el('button', 'rv-pop-kind-tag');
    kind.append(reviewKindMark(c.kind), el('span', 'rv-hl', REVIEW_KINDS.find(([key]) => key === c.kind)[1]), el('span', 'rv-pop-caret', '▾'));
    kind.addEventListener('click', () => reviewPopKinds());
    head.append(kind, el('span', 'rv-pop-where', reviewLines(c)));
    const area = el('textarea', 'rv-pop-text');
    area.placeholder = REVIEW_KIND_LOOK[c.kind][1];
    area.value = c.text || '';
    const save = el('button', 'rv-pop-save', 'Guardar');
    const cancel = el('button', 'rv-cancel', 'Descartar');
    const ready = () => { save.disabled = !area.value.trim(); };
    area.addEventListener('input', () => {
        c.text = area.value;
        ready();
    });
    const keep = () => {
        if (!area.value.trim()) return;
        post({
            type: 'reviewNote', kind: c.kind, file: c.file, line: c.line || null, endLine: c.endLine || null, lineText: c.lineText || null, text: area.value,
            oldLine: c.oldLine || null, oldEndLine: c.oldEndLine || null, removed: c.removed ?? null, anchor: Boolean(c.anchor),
        });
        reviewPopClose();
    };
    save.addEventListener('click', keep);
    cancel.addEventListener('click', () => reviewPopClose());
    // Lo que se escribe es de la caja: Esc la descarta sin cerrar la ficha.
    area.addEventListener('keydown', e => {
        e.stopPropagation();
        if (e.key === 'Escape') reviewPopClose();
        else if (e.key === 'Enter' && e.ctrlKey) keep();
    });
    const foot = el('div', 'rv-pop-foot');
    foot.append(el('span', 'rv-pop-keys', 'Ctrl+Intro para guardar'), cancel, save);
    box.append(head, area, foot);
    ready();
    reviewPopOpen('write', box, c.file, c.line, c.endLine || c.line, c.cursor);
    requestAnimationFrame(() => area.focus());
}

/** La ficha de unas notas, al pasar el ratón por sus líneas o por su marca en la cabecera del fichero: cada una con su
 * tipo, dónde va, su texto, lo quitado si es de eso, lo que hizo /opsx:review si ya la resolvió y «Eliminar nota». */
function reviewPopShow(path, notes, cursor) {
    if (!notes.length) return;
    const box = el('div', 'rv-pop-show');
    for (const n of notes) box.append(reviewPopNote(n));
    box.addEventListener('mouseenter', () => clearTimeout(state.changes.hover.out));
    box.addEventListener('mouseleave', () => reviewHoverLeave());
    const placed = notes.filter(n => n.line && !n.lost);
    const from = placed.length ? Math.min(...placed.map(n => n.line)) : null;
    const to = placed.length ? Math.max(...placed.map(n => n.endLine || n.line)) : null;
    reviewPopOpen('show', box, path, from, to, cursor);
    state.changes.pop.ids = notes.map(n => n.id).join(',');
    reviewRepaint();
}

/** Una nota en la ficha que sale al pasar el ratón por sus líneas. */
function reviewPopNote(n) {
    const name = (REVIEW_KINDS.find(([key]) => key === n.kind) || [n.kind, n.kind])[1];
    const outcome = outcomeOf(n);
    const card = el('div', `rv-pop-note kind-${n.kind}` + (outcome ? ' resolved' : ''));
    const head = el('div', 'rv-pop-head');
    const kind = el('span', 'rv-pop-kind-tag');
    kind.append(reviewKindMark(n.kind), el('span', 'rv-hl', name));
    head.append(kind);
    if (outcome) head.append(reviewOutcomeBadge(outcome, n));
    head.append(el('span', 'rv-pop-where', reviewLines(n)));
    card.append(head, el('div', 'rv-pop-says', n.text));
    if (n.removed) {
        const gone = el('div', 'rv-pop-gone');
        gone.append(el('span', 'rv-pop-label', 'Lo quitado'));
        // Sin la sangría que comparten, como se leen sueltas.
        const lines = n.removed.split('\n');
        const indent = Math.min(...lines.filter(l => l.trim()).map(l => l.length - l.trimStart().length));
        for (const text of lines) gone.append(el('div', 'rv-pop-gone-line', Number.isFinite(indent) ? text.slice(indent) : text));
        card.append(gone);
    }
    if (outcome && outcome.summary) {
        const said = el('div', 'rv-pop-outcome');
        said.append(el('span', 'rv-pop-label', (REVIEW_OUTCOMES[outcome.outcome] || { summary: 'Qué se hizo' }).summary), outcome.summary);
        card.append(said);
    }
    const remove = el('button', 'rv-pop-remove');
    remove.append(trash('Eliminar nota'), 'Eliminar nota');
    remove.addEventListener('click', () => {
        post({ type: 'reviewNoteRemove', id: n.id });
        reviewPopRemove();
    });
    const foot = el('div', 'rv-pop-foot');
    foot.append(remove);
    card.append(foot);
    return card;
}

/** Abre el menú flotante de la revisión junto al cursor, sobre unas líneas de un fichero; el que hubiera abierto se quita.
 * El cursor es dónde estaba el ratón ({x, y}), en qué fila de código (key) o junto a qué de la cabecera (sel), y si se
 * eligió hacia abajo (down). */
function reviewPopOpen(mode, box, path, from, to, cursor) {
    reviewPopRemove();
    box.classList.add('rv-pop');
    // Lo que se pulsa dentro no cuenta como pulsar fuera.
    box.addEventListener('mousedown', e => e.stopPropagation());
    document.body.append(box);
    state.changes.pop = { mode, box, path, from, to, cursor };
    reviewPopPlace();
}

// Lo que se separa el menú flotante del cursor por arriba y por abajo (por abajo, el cursor ocupa su alto) y lo que se
// adelanta a su izquierda.
const REVIEW_POP_ABOVE = 12;
const REVIEW_POP_BELOW = 20;
const REVIEW_POP_LEFT = 20;

/** Coloca el menú flotante junto al cursor, del lado contrario a las líneas para no taparlas: debajo si se eligieron
 * hacia abajo, encima si hacia arriba o es una sola; si ahí no cabe, del otro lado. Va con la línea del cursor al
 * desplazar el código: si sale de la vista, se esconde; si ya no está en la ficha, se cierra, salvo una nota a medio
 * escribir, que espera a que vuelva. */
function reviewPopPlace() {
    const p = state.changes.pop;
    if (!p) return;
    const v = state.wf;
    const open = Boolean(v) && !v.drawer.hidden && v.detail === `file:branch:${p.path}` && reviewing('branch');
    const lost = () => {
        if (open && p.mode === 'write') p.box.style.visibility = 'hidden';
        else reviewPopClose();
    };
    if (p.cursor.sel) {
        const target = open ? v.drawer.querySelector(p.cursor.sel) : null;
        if (target) reviewPopBeside(p, target.getBoundingClientRect());
        else lost();
        return;
    }
    let anchor = open ? reviewRowOf(v.drawer, p.cursor.key) : null;
    const exact = Boolean(anchor);
    // Al cambiar entre «Cambios» y «Fichero», la fila del cursor puede no estar: vale la primera de las suyas que se vea.
    if (open && !anchor && p.from) anchor = [...v.drawer.querySelectorAll('.wf-code-line[data-line]')]
        .find(r => Number(r.dataset.line) >= p.from && Number(r.dataset.line) <= p.to);
    const scroller = anchor && anchor.closest('.wf-filewin-body');
    if (!scroller) {
        lost();
        return;
    }
    const view = scroller.getBoundingClientRect();
    const row = anchor.getBoundingClientRect();
    const out = row.bottom <= view.top || row.top >= view.bottom;
    if (out && p.mode === 'show') {
        reviewPopRemove();
        return;
    }
    p.box.style.visibility = out ? 'hidden' : '';
    if (out) return;
    // La altura del cursor dentro de su línea se toma al abrirlo; desde ahí, sigue a la línea.
    if (exact && p.cursor.dy === undefined) p.cursor.dy = Math.max(0, Math.min(row.height, p.cursor.y - row.top));
    const y0 = row.top + (exact ? p.cursor.dy : row.height / 2);
    const w = p.box.offsetWidth;
    const h = p.box.offsetHeight;
    const up = y0 - h - REVIEW_POP_ABOVE;
    const down = y0 + REVIEW_POP_BELOW;
    const fitsUp = up >= 8;
    const fitsDown = down + h <= window.innerHeight - 8;
    const below = p.cursor.down ? fitsDown || !fitsUp : !fitsUp && fitsDown;
    const y = Math.max(8, Math.min(below ? down : up, window.innerHeight - h - 8));
    const x = Math.max(8, Math.min(p.cursor.x - REVIEW_POP_LEFT, window.innerWidth - w - 8));
    p.box.style.left = `${x}px`;
    p.box.style.top = `${y}px`;
    p.box.classList.toggle('below', below);
}

/** Coloca el menú flotante bajo algo de la cabecera de la ficha, alineado con su borde izquierdo; encima, si no cabe. */
function reviewPopBeside(p, r) {
    p.box.style.visibility = '';
    const w = p.box.offsetWidth;
    const h = p.box.offsetHeight;
    const below = r.bottom + 6 + h <= window.innerHeight - 8 || r.top - 6 - h < 8;
    p.box.style.left = `${Math.max(8, Math.min(r.left, window.innerWidth - w - 8))}px`;
    p.box.style.top = `${below ? r.bottom + 6 : r.top - 6 - h}px`;
    p.box.classList.toggle('below', below);
}

/** Si hay una nota a medio escribir en la caja flotante: pulsar fuera no la cierra, ni a la ficha en la que está. */
function reviewWriting() {
    const p = state.changes.pop;
    return Boolean(p && p.mode === 'write' && (state.changes.compose.text || '').trim());
}

/** Al pulsar fuera del menú flotante, se cierra, salvo una nota a medio escribir: así no se pierde. */
function reviewPopOutside() {
    if (!state.changes.pop || reviewWriting()) return;
    reviewPopClose();
}

/** Cierra el menú flotante; si se estaba eligiendo el tipo o escribiendo, las líneas dejan de estar elegidas.
 *
 * La selección del código se va con él: otra selección que empezara encima arrastraría el texto en vez de elegir. */
function reviewPopClose() {
    const p = state.changes.pop;
    if (!p) return;
    reviewPopRemove();
    if (p.mode === 'show') return;
    state.changes.compose = null;
    const sel = window.getSelection();
    const at = sel && sel.anchorNode && (sel.anchorNode.nodeType === 1 ? sel.anchorNode : sel.anchorNode.parentElement);
    if (at && at.closest('.wf-drawer')) sel.removeAllRanges();
    reviewRepaint();
}

/** Quita el menú flotante, si está, sin tocar lo que se esté anotando. */
function reviewPopRemove() {
    const p = state.changes.pop;
    if (!p) return;
    p.box.remove();
    state.changes.pop = null;
    if (p.mode === 'show') reviewRepaint();
}

/** Abre la caja para escribir una nota general, en la lista de notas; lo que se estuviera anotando en un fichero se cierra. */
function reviewCompose() {
    reviewPopClose();
    state.changes.compose = { file: null };
    state.changes.composerEl = null;
    renderChanges();
}

/** La caja para escribir una nota general: su tipo, su texto y guardar o descartar.
 *
 * Es la misma mientras siga abierta: en directo el panel se repinta con cada novedad, y rehacerla le quitaba el cursor a
 * quien estaba escribiendo. */
function reviewComposer() {
    const c = state.changes.compose;
    if (state.changes.composerEl) return state.changes.composerEl;
    const box = el('div', 'rv-compose');
    state.changes.composerEl = box;
    const kinds = el('div', 'segmented rv-kinds');
    c.kind = c.kind || 'correccion';
    for (const [key, name] of REVIEW_KINDS) {
        const chip = el('button', `chip kind-${key}` + (c.kind === key ? ' on' : ''), name);
        chip.addEventListener('click', e => {
            e.stopPropagation();
            c.kind = key;
            kinds.querySelectorAll('.chip').forEach(x => x.classList.toggle('on', x === chip));
        });
        kinds.append(chip);
    }
    const area = el('textarea', 'rv-text');
    area.placeholder = 'Qué hay que corregir, mejorar o preguntar…';
    area.value = c.text || '';
    area.addEventListener('input', () => { c.text = area.value; });
    const save = el('button', 'rv-save', 'Guardar');
    const cancel = el('button', 'rv-cancel', 'Descartar');
    const close = () => {
        state.changes.compose = null;
        state.changes.composerEl = null;
        renderChanges();
    };
    const keep = () => {
        if (!area.value.trim()) return;
        post({ type: 'reviewNote', kind: c.kind, file: null, line: null, endLine: null, lineText: null, text: area.value });
        close();
    };
    save.addEventListener('click', e => {
        e.stopPropagation();
        keep();
    });
    cancel.addEventListener('click', e => {
        e.stopPropagation();
        close();
    });
    // Lo que se escribe es de la caja: Esc la descarta sin cerrar la ficha y Ctrl+Intro la guarda.
    area.addEventListener('keydown', e => {
        e.stopPropagation();
        if (e.key === 'Escape') close();
        else if (e.key === 'Enter' && e.ctrlKey) keep();
    });
    box.addEventListener('click', e => e.stopPropagation());
    const foot = el('div', 'rv-compose-foot');
    foot.append(el('span', 'rv-where', 'Nota general'), cancel, save);
    box.append(kinds, area, foot);
    requestAnimationFrame(() => area.focus());
    return box;
}

/** Tarjeta de una nota: su tipo en su color, dónde va (en la lista) y su texto; × la quita y, en la lista, pulsarla lleva a
 * su línea. Si /opsx:review ya la resolvió, lleva su marca, sale atenuada y deja desplegar lo que contó. */
function reviewNoteCard(n, listed) {
    const name = (REVIEW_KINDS.find(([key]) => key === n.kind) || [n.kind, n.kind])[1];
    const outcome = outcomeOf(n);
    const card = el('div', `rv-note kind-${n.kind}` + (listed && n.file ? ' link' : '') + (outcome ? ' resolved' : '') + (noteRead(n) ? ' read' : ''));
    const head = el('div', 'rv-note-head');
    head.append(el('span', 'rv-kind', name));
    if (outcome) head.append(reviewOutcomeBadge(outcome, n));
    if (listed) {
        const cut = n.file ? n.file.lastIndexOf('/') : -1;
        const file = n.file ? (cut >= 0 ? n.file.slice(cut + 1) : n.file) : 'general';
        head.append(el('span', 'rv-note-where', n.file && n.line && !n.lost
            ? `${file}:${n.line}${n.endLine ? `–${n.endLine}` : ''}${n.removed ? (n.anchor ? ' · lo quitado' : ' · y lo quitado') : ''}` : file));
    } else if (n.lost) head.append(el('span', 'rv-note-where', 'su línea ya no está'));
    head.append(outcome ? reviewReadButton(n) : reviewRemoveButton(n));
    card.append(head, el('div', 'rv-note-text', n.text));
    if (outcome && outcome.summary) card.append(reviewOutcomeSummary(n, outcome));
    if (listed && n.file) card.addEventListener('click', () => {
        state.changes.tab = 'file';
        state.changes.focusLine = n.line && !n.lost ? n.line : null;
        openChange('branch', n.file);
    });
    return card;
}

/** El botón de quitar una nota que aún no ha tratado /opsx:review.
 *
 * Una resuelta no se quita: se perdería lo que contestó el agente, y para quitarla de en medio está «Leída». */
function reviewRemoveButton(n) {
    const remove = el('button', 'rv-remove', '×');
    hint(remove, '×', 'Quitar la nota', null, null);
    remove.addEventListener('click', e => {
        e.stopPropagation();
        post({ type: 'reviewNoteRemove', id: n.id });
    });
    return remove;
}

/** El botón de dar por leída la respuesta de una nota resuelta, o de devolverla a «sin leer» si ya se leyó. */
function reviewReadButton(n) {
    const read = noteRead(n);
    const button = el('button', 'rv-remove rv-read' + (read ? ' on' : ''), read ? '↺' : '✓');
    hint(button, read ? '↺' : '✓', read ? 'Volver a sin leer' : 'Marcar como leída', null, read
        ? 'Vuelve arriba, con lo que falta por leer.'
        : 'Pasa a «Leídas», plegado al final. Si otra ronda vuelve a tratarla, sale otra vez arriba.');
    button.addEventListener('click', e => {
        e.stopPropagation();
        reviewRead([n.id], !read);
    });
    return button;
}

/** La marca de una nota resuelta: «Aplicada · ronda 1 · a1b2c3d», «Contestada · ronda 1», «Tarea DEVLAB-130 · ronda 2» o
 * «Vista», en su color. La clave de la tarea abre la tarea en el Tablero, y el commit, lo que cambió su ronda en el
 * fichero de la nota. */
function reviewOutcomeBadge(o, n) {
    const kind = REVIEW_OUTCOMES[o.outcome] || { name: o.outcome };
    const badge = el('span', `rv-outcome out-${o.outcome}`);
    badge.append(el('span', 'rv-outcome-dot'));
    if (o.outcome === 'tarea' && o.task) {
        badge.append(`${kind.name} `);
        const key = el('button', 'rv-outcome-task', o.task);
        key.addEventListener('click', e => {
            e.stopPropagation();
            post({ type: 'task', key: o.task });
        });
        badge.append(key);
    } else badge.append(kind.name);
    if (o.round && o.outcome !== 'vista') badge.append(` · ronda ${o.round}`);
    const r = state.changes.review;
    const viewable = o.outcome === 'aplicada' && o.commit && r && r.rounds && r.rounds.some(x => x.round === o.round) && reviewing('branch');
    if (o.outcome === 'aplicada' && o.commit) {
        badge.append(' · ');
        const commit = el(viewable ? 'button' : 'span', 'rv-outcome-commit', o.commit.slice(0, 7));
        if (viewable) commit.addEventListener('click', e => {
            e.stopPropagation();
            reviewGoRound(o.round, n);
        });
        badge.append(commit);
    }
    const facts = [o.round ? `Ronda ${o.round} de /opsx:review` : 'De /opsx:review', o.commit ? `commit ${o.commit}` : '', o.task ? `pasa a ${o.task}` : ''].filter(Boolean).join(' · ');
    const tip = viewable ? `Pulsa el commit para ver solo lo cambiado en la ronda ${o.round}.` : null;
    hint(badge, '✓', kind.name, facts, [o.summary, tip || (o.outcome === 'tarea' ? 'Pulsa la clave para abrir la tarea en el Tablero.' : null)].filter(Boolean).join(' ') || null);
    return badge;
}

/** Lo que contó /opsx:review de una nota, plegado bajo su texto; se despliega al pulsar y se recuerda mientras se revisa. */
function reviewOutcomeSummary(n, o) {
    const key = `${n.id}|${n.createdUtc}`;
    const open = state.changes.openOutcomes.has(key);
    const box = el('div', 'rv-outcome-summary' + (open ? ' open' : ''));
    const toggle = el('button', 'rv-outcome-toggle', `${open ? '▾' : '▸'} ${(REVIEW_OUTCOMES[o.outcome] || { summary: 'Qué se hizo' }).summary}`);
    toggle.addEventListener('click', e => {
        e.stopPropagation();
        if (open) state.changes.openOutcomes.delete(key);
        else state.changes.openOutcomes.add(key);
        renderChanges();
        const v = state.wf;
        if (v && v.detail && v.detail.startsWith('file:branch:') && !v.drawer.hidden) wfDetail(v, v.detail);
    });
    box.append(toggle);
    if (open) box.append(el('div', 'rv-outcome-text', o.summary));
    return box;
}

/** Lleva la ficha a la línea que se pidió (la de una nota o la que se está anotando), una vez; si el diff no la enseña,
 * a la primera que enseña después de ella. */
function reviewFocus(drawer) {
    const line = state.changes.focusLine;
    const rows = [...drawer.querySelectorAll('.wf-code-line[data-line]')];
    // Mientras se lee el código no hay filas: se va a la línea al llegar.
    if (!line || !rows.length) return;
    state.changes.focusLine = null;
    const row = rows.find(r => Number(r.dataset.line) === line) || rows.find(r => Number(r.dataset.line) >= line);
    if (row) requestAnimationFrame(() => row.scrollIntoView({ block: 'center' }));
}

// MENSAJES DE LA APP

/** Atiende lo que manda la app. */
function receive(m) {
    switch (m.type) {
        case 'host':
            setHost(m);
            break;
        case 'theme':
            applyTheme(m);
            // Los agentes llevan un color de la paleta del diseño: al cambiarlo, se recalculan y se repinta.
            if (state.design !== m.design) {
                state.design = m.design;
                state.colorCache = null;
                if (state.trace) {
                    renderLegend();
                    renderDiagram(false);
                    renderChat(false);
                }
            }
            break;
        case 'user':
            state.user = m.name || 'User';
            if (state.trace) {
                renderLegend();
                renderDiagram(false);
                renderChat(false);
            }
            break;
        case 'tab':
            setView(m.tab, true);
            break;
        case 'reveal':
            // Ya tiene el tema, la vista y las activas: la app puede enseñarla sin que cambie nada al aparecer.
            post({ type: 'revealed' });
            break;
        case 'deleted':
            sessionDeleted(m.id);
            break;
        case 'sessions':
            state.sessions = m.items;
            renderSessions();
            break;
        case 'active': {
            const closed = state.selectedId && state.active.some(a => a.id === state.selectedId) && !m.items.some(a => a.id === state.selectedId);
            state.active = m.items;
            // Una pieza sigue enseñando su sesión aunque se cierre: es de esa sesión, no de la que se elija.
            if (closed && !PIECE) clearView();
            renderActive();
            renderContext();
            // Lo que se ve al momento cambia con el estado de la sesión (trabaja, pregunta, espera a sus agentes).
            if (state.tab === 'map' && (state.wf && (state.mapLayout === 'live' || state.mapLayout === 'diagram') || (state.mapLayout === 'now' || state.mapLayout === 'obra' || state.mapLayout === 'parte' && state.parteMode !== 'steps') && state.trace)) renderDiagram(false);
            break;
        }
        case 'groups':
            state.groupColors = m.colors || {};
            state.palette = m.palette || [];
            renderActive();
            break;
        case 'image':
            receiveImage(m);
            break;
        case 'mcpFile':
            receiveMcpFile(m);
            break;
        case 'tasks':
            if (state.trace && state.trace.id === m.id) {
                state.trace.task = m.task;
                renderTasks();
            }
            break;
        case 'obraPlan':
            obPlan(m);
            break;
        case 'tracePatch':
            // Solo llegan los pasos desde «from»: se completan con los que ya había y se sigue como con la sesión entera.
            // Si la página no tiene esa sesión (o tiene menos pasos), se pide entera.
            if (!state.trace || state.trace.id !== m.id || state.trace.steps.length < m.from) {
                post({ type: 'open', id: m.id });
                break;
            }
            receive({ ...m, type: 'trace', steps: state.trace.steps.slice(0, m.from).concat(m.steps) });
            break;
        case 'trace': {
            const old = state.trace;
            const same = old && old.id === m.id;
            const numbers = m.interactions.map(i => i.number);
            // Otra sesión: lo que avisaba la barra del chat era de la de antes.
            if (!same) chatUnseen(0);
            if (same) {
                // Con el chat minimizado, su barra cuenta las respuestas nuevas de Claude; lo que escribe la persona ya lo ha leído.
                const said = m.steps.slice(old.steps.length).filter(s => !s.agentId && s.kind === 'reply').length;
                if ($('panes').classList.contains('chat-closed') && said) chatUnseen((state.chatUnseen || 0) + said);
                // Lo que estaba en curso y ya tiene resultado: su detalle guardado se quedó sin él.
                const pendingSteps = new Set(old.steps.filter(s => !s.end).map(s => s.order));
                for (const s of m.steps.filter(x => pendingSteps.has(x.order) && x.end)) {
                    state.details.delete(s.order);
                    if (state.expanded.has(s.order) && s.hasDetail) post({ type: 'detail', order: s.order });
                }
                // En directo, quien miraba la última interacción sigue a la nueva cuando llega otro prompt.
                const oldLast = old.interactions.length ? old.interactions[old.interactions.length - 1].number : null;
                if (state.interaction === oldLast && numbers.length) state.interaction = numbers[numbers.length - 1];
            }
            // Se ha cerrado mientras se miraba en directo: no se queda a la vista ni pasa a «Viendo».
            if (same && old.live && !m.live && !PIECE) {
                clearView();
                break;
            }
            // Llega tarde la de una sesión que se cerró mientras se miraba y ya se limpió.
            if (m.id === state.dropped) break;
            if (m.id === state.loadingId) state.loadingId = null;
            state.trace = m;
            noteViewing(m);
            // Al abrir una sesión se ve entera, con el chat en lo último; si la interacción elegida ya no existe, también.
            if (!same || (state.interaction !== 'all' && !numbers.includes(state.interaction))) {
                state.interaction = 'all';
                pause();
                state.replay.index = null;
                document.querySelector('.app').classList.remove('replaying');
                state.showDone = false;
            }
            renderInteractions();
            updateNav();
            refreshFind();
            renderLegend();
            if (!same) {
                state.changes.sets = { pending: null, branch: null, since: null, repo: null };
                state.changes.texts.clear();
                repoSearchReset();
                reviewPopRemove();
                Object.assign(state.changes, { review: null, prAsked: false, compose: null, composerEl: null, focusLine: null, reviewText: null, round: null, sinceAsked: null });
                state.changes.pendingMarks.clear();
                renderChanges();
                renderGit();
            }
            renderDiagram(!same);
            // En directo, lo nuevo espera a que se suelte lo seleccionado en el chat.
            if (same && selectingInChat()) state.chatStale = true;
            else if (same) patchChat();
            else renderChat(true);
            if (state.focusSecret) focusSecret();
            if (!same) {
                fadeIn($('diagram'));
                fadeIn($('chat'));
            }
            // Lo cambiado se vuelve a mirar al abrir otra sesión y, en directo, poco después de cada novedad.
            showChanges();
            if (!same) requestChanges(true);
            else if (m.steps.length !== old.steps.length) requestChanges(false);
            if (!same || m.steps.length !== old.steps.length) historyRequest();
            break;
        }
        case 'changes':
            receiveChanges(m);
            break;
        case 'finderOpen':
            finderOpen(m.mode, m.word, true);
            break;
        case 'review':
            receiveReview(m);
            break;
        case 'code':
            receiveCode(m);
            break;
        case 'finalFormat':
            state.finalFormat = Boolean(m.on);
            ffRefresh();
            break;
        case 'corks': {
            state.corks = m.list || [];
            state.profiles = m.profiles || [];
            state.templates = m.templates || [];
            // Las fichas de fichero abiertas en algún panel, que la lista de cambios marca con un ojo, y si las notas de la
            // revisión están abiertas aparte, que entonces no se despliegan en la lista.
            const open = JSON.stringify(m.openFiles || []);
            const aside = Boolean(m.notesAside);
            if (open !== JSON.stringify(state.openFiles || []) || aside !== state.changes.notesAside) {
                state.openFiles = m.openFiles || [];
                state.changes.notesAside = aside;
                renderChanges();
                historyRequest();
            }
            break;
        }
        case 'fileHistory':
            historyRender(m);
            break;
        case 'reviewFile':
            if (!state.trace || m.id !== state.trace.id) break;
            state.changes.reviewText = m;
            if (reviewFileShown()) renderChanges();
            break;
        case 'reviewShow':
            // Lo que se ha elegido ver en otra página de la misma revisión: la ronda, o sus notas o su fichero.
            if (!state.trace || m.id !== state.trace.id || PIECE === 'file') break;
            if (m.view) {
                state.changes.notesView = m.view === 'file' ? 'file' : 'notes';
                if (state.changes.notesView === 'file') post({ type: 'reviewFile' });
            }
            if (m.hasRound && (m.round || null) !== state.changes.round) setReviewRound(m.round || null, true);
            else renderChanges();
            break;
        case 'changeDiff':
        case 'changeFile':
            receiveChangeText(m);
            break;
        case 'repoGrep':
            receiveRepoGrep(m);
            break;
        case 'detail': {
            state.details.set(m.order, m);
            const s = state.trace && state.trace.steps.find(x => x.order === m.order);
            if (!state.tipAsks.delete(m.order) && s) repaintStep(s);
            if (state.tip.key === `q:${m.order}`) refreshTip();
            if (state.wf && state.wf.detail && !state.wf.drawer.hidden) wfDetail(state.wf, state.wf.detail);
            // El razonamiento del paso que se está repitiendo llega después: se rehace su bocadillo.
            if (state.replay.index !== null && state.focus === m.order) decorate();
            break;
        }
        case 'refresh':
            requestList();
            // Sin reabrir cuando la app va a pedir otra sesión justo después.
            if (state.selectedId && m.reopen !== false) openSession(state.selectedId, true);
            break;
        case 'secretSpot':
            receiveSecretSpot(m);
            break;
        case 'select':
            setListsOpen(false);
            // Desde Seguridad, con el secreto cuyo paso hay que enseñar al llegar la sesión.
            state.focusSecret = m.secret || null;
            if (state.focusSecret && state.trace && state.trace.id === m.id) focusSecret();
            else openSession(m.id, false);
            break;
        case 'closeLightbox':
            closeLightbox();
            break;
        case 'error':
            state.loadingId = null;
            $('diagram').replaceChildren(el('div', 'empty', m.message));
            $('chat').replaceChildren(el('div', 'empty', m.message));
            break;
    }
}

/** Pone en la página los colores del tema que manda la app, si es claro y su diseño. */
function applyTheme(m) {
    for (const [name, value] of Object.entries(m.vars)) document.documentElement.style.setProperty(name, value);
    document.documentElement.dataset.theme = m.light ? 'light' : 'dark';
    document.documentElement.dataset.design = m.design || '';
}

// HISTORIA DEL FICHERO

/** El fichero que sigue la historia: la última ficha de fichero abierta de la sesión; null si no hay ninguna. */
function historyFile() {
    const files = state.openFiles || [];
    return files.length ? files[files.length - 1] : null;
}

/** En la pieza de la historia, pide a la app la del fichero abierto; sin ninguno abierto, pinta el aviso de vacío. */
function historyRequest() {
    if (PIECE !== 'history' || !state.trace) return;
    const file = historyFile();
    if (file) post({ type: 'fileHistory', path: file.path });
    else historyRender(null);
}

/** La hora de un instante, con el día delante si no es hoy: «08:24» o «11 nov 16:02». */
function historyTime(at) {
    if (!at) return '';
    const d = new Date(at);
    const time = d.toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' });
    return d.toDateString() === new Date().toDateString() ? time : `${d.toLocaleDateString('es-ES', { day: 'numeric', month: 'short' }).replace('.', '')} ${time}`;
}

/** Pinta la historia del fichero abierto: por ronda de la revisión si se conocen, y dentro, por el prompt que lo pidió,
 * cada vez que se tocó, con quién, cómo, cuántas líneas y lo que dijo Claude después. */
function historyRender(m) {
    let box = $('history');
    if (!box) {
        box = el('div', 'history');
        box.id = 'history';
        document.body.append(box);
    }
    box.replaceChildren();
    if (!m) {
        const empty = el('div', 'history-empty');
        empty.append(el('b', null, 'Ningún fichero abierto'), el('span', null, 'Abre un fichero de los cambios y aquí sale cómo llegó a estar así: qué prompt lo pidió, quién lo editó y cuántas líneas cambió.'));
        box.append(empty);
        return;
    }
    const slash = m.path.lastIndexOf('/');
    const head = el('div', 'history-head');
    head.append(el('b', null, m.path.slice(slash + 1)), el('span', null, slash > 0 ? m.path.slice(0, slash) : ''));
    box.append(head);
    if (!m.touches.length) {
        const empty = el('div', 'history-empty');
        empty.append(el('b', null, 'Esta sesión no ha cambiado este fichero'), el('span', null, 'Si lo cambió otra sesión, un comando que no lo nombra o una persona a mano, no sale aquí.'));
        box.append(empty);
        return;
    }
    const prompts = new Map((state.trace.interactions || []).map(i => [i.number, i]));
    let round, interaction;
    m.touches.forEach((t, i) => {
        if (m.rounds && t.round !== round) {
            round = t.round;
            interaction = undefined;
            box.append(el('div', 'history-round', round ? `Ronda ${round}` : 'Antes de la revisión'));
        }
        if (t.interaction !== interaction) {
            interaction = t.interaction;
            const p = prompts.get(interaction);
            const row = el('div', 'history-ev prompt');
            row.append(el('span', 'history-who a', 'A'), el('span', 'history-what', p ? `«${firstLines(p.prompt, 1, 120)}»` : `Prompt ${interaction}`), el('span', 'history-at', historyTime(p && p.startUtc)));
            box.append(row);
        }
        const row = el('div', 'history-ev');
        const what = el('span', 'history-what');
        const line = el('span', 'history-verb', t.verb === 'Comando' ? 'Quizá cambiado por un comando' : t.verb === 'Escribe' ? 'Escribe el fichero entero' : 'Edita');
        what.append(line);
        if (t.added || t.removed) {
            const lines = el('span', 'history-lines');
            if (t.added) lines.append(el('i', null, `+${t.added}`));
            if (t.removed) lines.append(el('u', null, `−${t.removed}`));
            what.append(lines);
        }
        // Varias ediciones seguidas antes de que Claude diga nada llevan la misma frase: va solo en la última.
        const next = m.touches[i + 1];
        if (t.reply && !(next && next.interaction === t.interaction && next.reply === t.reply)) what.append(el('span', 'history-reply', t.reply));
        const who = el('span', 'history-who' + (t.who === 'B' ? '' : ' agent'), t.who);
        row.append(who, what, el('span', 'history-at', historyTime(t.at)));
        box.append(row);
    });
}

// PIEZA APARTE

// Las partes de una sesión que se clavan en un corcho: la cabecera donde se hace clic derecho y cómo se llaman.
const PIECES = {
    live: { head: '#flow-dock .flow-tools', name: 'Flujo' },
    chat: { head: '#chat-pane .pane-head', name: 'Chat' },
    changes: { head: '#changes-pane .pane-head', name: 'Cambios' },
};

/** Engancha lo de clavar una parte en el corcho: el clic derecho en su cabecera, en la pestaña del chat o de los cambios
 * plegados, o en la barra de arriba, que clava la vista que se ve. En una pieza, en cambio, deja a la vista lo que
 * enseña: el directo o una vista solos, o la ficha de un paso sobre el directo, sin pedir toda la rama ni preguntar al
 * remoto por la PR; o los cambios, abiertos aunque la sección los tenga minimizados, con su hueco para el fichero. */
function initPieces() {
    if (DIAGRAM_PIECE) $('panes').classList.add('changes-closed', 'chat-closed');
    if (PIECE === 'changes' || PIECE === 'review') $('panes').classList.remove('changes-closed');
    if (PIECE === 'changes') initChangesDock();
    // Las notas abiertas aparte son las de la revisión, que es de toda la rama.
    if (PIECE === 'review') Object.assign(state.changes, { scope: 'branch', showNotes: true });
    if (PIECE || !window.invokeCSharpAction) return;
    const pinPart = (target, piece, name) => target.addEventListener('contextmenu', e => {
        if (!state.trace || e.target.closest('input, textarea')) return;
        const part = typeof piece === 'function' ? piece() : piece;
        const title = `${typeof name === 'function' ? name(part) : name} · ${sessionName(state.trace)}`;
        pinMenu(e, { type: 'pieceOpen', piece: part, title }, title);
    });
    // Sin botones que se repitan en cada parte: el clic derecho en su cabecera.
    for (const [piece, look] of Object.entries(PIECES)) pinPart(document.querySelector(look.head), piece, look.name);
    pinPart($('chat-tab'), 'chat', PIECES.chat.name);
    pinPart($('changes-tab'), 'changes', PIECES.changes.name);
    pinPart(document.querySelector('.toolbar .topbar'), currentView, view => VIEWS[view].name);
}

/** Marca si la pieza va en un recuadro del corcho, que le pone la cabecera: así se pega a ella, sin margen propio, y
 * sigue su marco con el color de la sesión. */
function setHost(m) {
    document.body.classList.toggle('in-cork', !!m.cork);
    if (m.edge) document.documentElement.style.setProperty('--cork-edge', m.edge);
    else document.documentElement.style.removeProperty('--cork-edge');
}

/** Engancha el clic derecho en la cabecera de una ficha para clavarla: la de un fichero o la de un paso. En la pieza de
 * una de ellas no, que ya lo está. */
function pinnableDrawer(v) {
    if (!window.invokeCSharpAction || PIECE === 'file' || PIECE === 'step') return;
    v.drawer.addEventListener('contextmenu', e => {
        if (!state.trace || !e.target.closest('.wf-drawer-head') || e.target.closest('input, textarea')) return;
        const key = v.drawer.dataset.key || '';
        if (key.startsWith('file:')) {
            const [, scope, ...rest] = key.split(':');
            const file = rest.join(':');
            const name = file.split('/').pop();
            pinMenu(e, { type: 'pieceFile', scope, path: file, tab: state.changes.tab, round: pieceRound(scope), title: `${name} · ${sessionName(state.trace)}` }, name);
        } else if (key) {
            const name = `${v.drawer.dataset.title || 'Paso'} · ${sessionName(state.trace)}`;
            pinMenu(e, { type: 'pieceOpen', piece: 'step', key, interaction: v.drawer.dataset.interaction, title: name }, name);
        }
    });
}

/** El menú del clic derecho para clavar algo de la sesión: en cada corcho abierto, por su nombre y con cuántos paneles
 * tiene, en uno nuevo, o flotante, en su propia ventana. Si ya estaba abierto en otro sitio, se lleva allí. También un
 * corcho nuevo montado con una plantilla y lleno con esta sesión: las guardadas y las de la app, todas en el desplegable
 * «Plantillas». */
function pinMenu(e, message, name) {
    e.preventDefault();
    e.stopPropagation();
    closeCodeThemeMenu();
    hideTip();
    const menu = el('div', 'code-menu pin-menu');
    menu.append(el('div', 'code-menu-title', name));
    const item = (icon, text, note, sent, parent = menu) => {
        const button = el('button', 'code-menu-item');
        button.append(pinIcon(icon), el('span', 'code-menu-name', text));
        if (note) button.append(el('span', 'pin-menu-note', note));
        button.addEventListener('click', ev => {
            ev.stopPropagation();
            closeCodeThemeMenu();
            post(sent);
        });
        parent.append(button);
    };
    menu.append(el('div', 'pin-menu-group', 'Clavar en el corcho'));
    for (const cork of state.corks || [])
        item('pin', cork.name, cork.count === 0 ? 'vacío' : cork.count === 1 ? '1 panel' : `${cork.count} paneles`, { ...message, where: String(cork.id) });
    item('plus', 'Un corcho nuevo', null, { ...message, where: 'new' });
    const profiles = state.profiles || [];
    const templates = state.templates || [];
    if (profiles.length || templates.length) {
        const fold = el('button', 'code-menu-item');
        const arrow = el('span', 'pin-menu-note', 'en un corcho nuevo ▸');
        fold.append(pinIcon('grid'), el('span', 'code-menu-name', 'Plantillas'), arrow);
        const list = el('div', 'pin-menu-sub');
        list.hidden = true;
        for (const profile of profiles) item('grid', profile, null, { type: 'pieceProfile', profile }, list);
        if (templates.length) {
            if (profiles.length) list.append(el('div', 'pin-menu-group', 'De la app'));
            for (const profile of templates) item('grid', profile, null, { type: 'pieceProfile', profile }, list);
        }
        fold.addEventListener('click', ev => {
            ev.stopPropagation();
            list.hidden = !list.hidden;
            arrow.textContent = list.hidden ? 'en un corcho nuevo ▸' : 'en un corcho nuevo ▾';
            placeMenu(menu, e);
        });
        menu.append(fold, list);
    }
    menu.append(el('div', 'pin-menu-sep'));
    item('window', 'Flotante', 'en su propia ventana', { ...message, where: 'window' });
    menu.addEventListener('mousedown', ev => ev.stopPropagation());
    document.body.append(menu);
    placeMenu(menu, e);
    state.codeMenu = menu;
}

/** Coloca un menú junto al ratón sin que se salga de la ventana; se vuelve a llamar si crece al desplegar algo. */
function placeMenu(menu, e) {
    menu.style.left = `${Math.max(8, Math.min(e.clientX, window.innerWidth - menu.offsetWidth - 8))}px`;
    menu.style.top = `${Math.max(8, Math.min(e.clientY, window.innerHeight - menu.offsetHeight - 8))}px`;
}

/** Un icono de los menús de clavar y de abrir, dibujado con trazos como los del corcho: la chincheta, el más, una
 * ventana, los recuadros de un perfil, un recuadro partido o unas pestañas. */
function pinIcon(name) {
    const shapes = {
        pin: 'M9 4h6l-1 6 4 3v2H6v-2l4-3z M12 15v6',
        plus: 'M12 5v14 M5 12h14',
        window: 'M5 5h14a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M3 9h18',
        grid: 'M4 5h16v14H4z M11 5v14 M11 12h9',
        split: 'M4 5h16v14H4z M12 5v14',
        tabs: 'M3 9h18v10H3z M3 9V6h7v3 M10 9V6h6v3',
    };
    const icon = svg('svg', { class: 'pin-menu-icon', viewBox: '0 0 24 24', 'aria-hidden': 'true' });
    icon.append(svg('path', { d: shapes[name] }));
    return icon;
}

/** En la ventana de los cambios, monta a la derecha de la lista el hueco donde se abre el fichero que se pulsa, con un
 * separador para repartir el ancho, que se recuerda. */
function initChangesDock() {
    const panes = $('panes');
    const split = el('div', 'splitter dock-split');
    const drawer = el('div', 'wf-drawer dock-sheet');
    drawer.hidden = true;
    panes.append(split, drawer);
    state.wf = { root: drawer, drawer, detail: null, nodes: new Map(), cam: {}, piece: true, dock: true };
    pinnableDrawer(state.wf);
    drawer.addEventListener('mouseover', trackTip);
    drawer.addEventListener('mouseleave', hideTip);
    // La lista, entre un mínimo legible y dejar al fichero sitio para su código.
    const setWidth = px => {
        const width = Math.round(Math.max(260, Math.min(px, panes.clientWidth - 360)));
        panes.style.setProperty('--list-w', `${width}px`);
        return width;
    };
    split.addEventListener('pointerdown', e => {
        split.setPointerCapture(e.pointerId);
        split.classList.add('drag');
        const left = panes.getBoundingClientRect().left;
        const move = ev => setWidth(ev.clientX - left);
        const up = ev => {
            split.releasePointerCapture(ev.pointerId);
            split.classList.remove('drag');
            split.removeEventListener('pointermove', move);
            split.removeEventListener('pointerup', up);
            remember('dockListW', setWidth(ev.clientX - left));
        };
        split.addEventListener('pointermove', move);
        split.addEventListener('pointerup', up);
    });
    const kept = remembered('dockListW', null);
    if (typeof kept === 'number') panes.style.setProperty('--list-w', `${kept}px`);
}

/** Atiende lo que manda la app a una pieza: el tema, los del código, qué parte enseñar, y lo de git y la revisión de su
 * sesión. */
function receivePiece(m) {
    switch (m.type) {
        case 'host':
            setHost(m);
            break;
        case 'theme':
            applyTheme(m);
            state.design = m.design;
            break;
        case 'code':
            receiveCode(m);
            break;
        case 'finalFormat':
            state.finalFormat = Boolean(m.on);
            ffRefresh();
            break;
        case 'piece':
            if (PIECE === 'finder') finderSession(m);
            else pieceShow(m);
            break;
        case 'goTo':
            repoGoToReceive(m);
            break;
        case 'finderOpen':
            finderOpen(m.mode, m.word, true);
            break;
        case 'reveal':
            post({ type: 'revealed' });
            break;
        case 'changes':
            receiveChanges(m);
            break;
        case 'review':
            receiveReview(m);
            break;
        case 'round':
            if (state.changes.round !== (m.round || null)) setReviewRound(m.round || null);
            break;
        case 'changeDiff':
        case 'changeFile':
            receiveChangeText(m);
            break;
        case 'repoGrep':
            receiveRepoGrep(m);
            break;
    }
}

/** Enseña la ficha de un fichero cambiado de una sesión, a toda la ventana, y pide a la app lo que necesita: lo cambiado
 * y, si es toda la rama, su revisión, con la ronda que se ve aparte, si la hay. */
function pieceShow(m) {
    const v = state.wf;
    state.trace = { id: m.id, piece: true };
    state.changes.tab = m.tab === 'file' ? 'file' : 'diff';
    state.changes.round = m.scope === 'branch' && m.round ? m.round : null;
    state.changes.sinceAsked = null;
    v.detail = `file:${m.scope}:${m.path}`;
    post({ type: 'changes', scope: m.scope });
    if (m.scope === 'branch') post({ type: 'reviewLoad' });
    wfDetail(v, v.detail);
}

/** Arranca la página como pieza: sin la vista de Sesiones, con la ficha a toda la ventana y lo que necesita para
 * anotar en ella (las fichas de ayuda, los enlaces y el menú flotante de la revisión). */
function startPiece() {
    document.body.classList.add('piece-mode');
    const root = el('div', 'piece');
    const drawer = el('div', 'wf-drawer');
    drawer.hidden = true;
    root.append(drawer);
    // La ficha de ayuda vive en la vista, que en una pieza va escondida.
    document.body.append(root, $('tip'));
    state.wf = { root, drawer, detail: null, nodes: new Map(), cam: {}, piece: true };
    state.changes.reviewHidden = remembered('reviewHidden', false) === true;
    root.addEventListener('mouseover', trackTip);
    document.addEventListener('click', openLink, true);
    document.addEventListener('mousedown', () => reviewPopOutside());
    document.addEventListener('scroll', () => reviewPopPlace(), true);
    window.addEventListener('resize', () => reviewPopPlace());
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && state.changes.pop) {
            reviewPopClose();
            return;
        }
        if (repoSearchKey(e)) return;
        fileFindKey(e);
    });
    // Ctrl sobre un nombre del código lo subraya, y Ctrl+clic lleva sus usos al panel de Cambios.
    document.addEventListener('mousemove', repoUsesHover);
    document.addEventListener('keyup', e => {
        if (e.key === 'Control' || e.key === 'Meta') repoUsesHover({ ctrlKey: false });
    });
    document.addEventListener('click', repoUsesClick, true);
    if (host) host.addEventListener('message', e => receivePiece(e.data));
    window.agentPulseReceive = receivePiece;
    window.agentPulseHello = () => post({ type: 'ready' });
    post({ type: 'ready' });
}

// ARRANQUE

/** Engancha los controles, pinta el estado vacío y avisa a la app de que la página está lista. */
function start() {
    if (PIECE) document.body.classList.add('piece-part', `piece-${PIECE}`);
    if (DIAGRAM_PIECE) document.body.classList.add('piece-diagram');
    document.querySelectorAll('#periods .chip').forEach(chip => {
        chip.classList.toggle('on', chip.dataset.period === state.period);
        chip.addEventListener('click', () => {
            state.period = chip.dataset.period;
            document.querySelectorAll('#periods .chip').forEach(c => c.classList.toggle('on', c === chip));
            updateDayPicker();
            requestList();
        });
    });
    updateDayPicker();
    $('day').addEventListener('click', toggleCalendar);
    $('day-clear').addEventListener('click', () => pickDay(''));

    setActiveFolded(remembered('activeFolded', false));
    $('active-up').addEventListener('click', () => stepGroup(-1));
    $('active-down').addEventListener('click', () => stepGroup(1));
    $('active-list').addEventListener('scroll', updateGroupSteps);
    $('chat').addEventListener('scroll', onChatScroll);
    // La rueda o el ratón en la barra cortan el viaje al final: manda la persona.
    $('chat').addEventListener('wheel', () => { state.chatGliding = false; });
    $('chat').addEventListener('mousedown', () => { state.chatGliding = false; });
    // Anclado al final aunque cambie el tamaño del chat o termine de cargar una imagen.
    new ResizeObserver(pinChat).observe($('chat'));
    let chatWidth = 0;
    new ResizeObserver(() => {
        const w = $('chat').clientWidth;
        if (state.clampsStale || (w && w !== chatWidth)) remeasureClamps();
        chatWidth = w;
    }).observe($('chat'));
    $('chat').addEventListener('load', pinChat, true);
    $('to-bottom').addEventListener('click', scrollChatToBottom);
    $('session-secrets').addEventListener('click', () => goSecurity());
    $('session-artifacts').addEventListener('click', toggleArtifactMenu);
    $('session-tasks').addEventListener('click', e => {
        const target = e.target.closest('[data-act]');
        if (!target) return;
        const key = target.dataset.key;
        const type = { open: 'task', remove: 'taskUnlink', principal: 'taskPrincipal', link: 'taskLink', add: 'taskAdd' }[target.dataset.act];
        post(key ? { type, key } : { type });
    });
    $('active-list').addEventListener('mouseover', trackTip);
    $('active-list').addEventListener('mouseleave', hideTip);
    $('active-list').addEventListener('scroll', hideTip);
    $('active-list').addEventListener('mousedown', hideTip);
    $('diagram').addEventListener('mouseover', trackTip);
    $('diagram').addEventListener('mouseleave', hideTip);
    $('legend').addEventListener('mouseover', trackTip);
    $('legend').addEventListener('mouseleave', hideTip);
    $('diagram').addEventListener('scroll', schedulePaintSequence, { passive: true });
    for (const bar of [$('view-btn'), $('flow-dock')]) {
        bar.addEventListener('mouseover', trackTip);
        bar.addEventListener('mouseleave', hideTip);
    }
    $('diagram').addEventListener('scroll', hideTip);
    $('diagram').addEventListener('mousedown', hideTip);
    // Los enlaces del chat se abren en el navegador, nunca dentro de la página.
    document.addEventListener('click', openLink, true);
    new ResizeObserver(updateGroupSteps).observe($('active-list'));
    $('lightbox').addEventListener('click', closeLightbox);
    $('lb-prev').addEventListener('click', e => {
        e.stopPropagation();
        stepLightbox(-1);
    });
    $('lb-next').addEventListener('click', e => {
        e.stopPropagation();
        stepLightbox(1);
    });
    $('lightbox-img').addEventListener('click', e => e.stopPropagation());
    // El menú de colores se cierra al pulsar fuera de él o al desplazar el panel.
    document.addEventListener('mousedown', e => {
        if (!$('color-menu').hidden && !$('color-menu').contains(e.target)) closeColorMenu();
        // El botón del día queda fuera: su propio clic ya abre o cierra el calendario.
        if (!$('calendar').hidden && !$('calendar').contains(e.target) && !$('day').contains(e.target)) closeCalendar();
        if (!$('view-menu').hidden && !$('view-menu').contains(e.target) && !e.target.closest('#view-btn')) closeViewMenu();
        if (!$('artifact-menu').hidden && !$('artifact-menu').contains(e.target) && !e.target.closest('#session-artifacts')) closeArtifactMenu();
        if (state.codeMenu && !state.codeMenu.contains(e.target)) closeCodeThemeMenu();
    });
    $('active-list').addEventListener('scroll', closeColorMenu);
    $('active-fold').addEventListener('click', () => setActiveFolded(!document.querySelector('.app').classList.contains('active-folded')));
    renderActive();
    // Cuánto lleva cada sesión en su estado: se repinta cada medio minuto aunque no cambie nada más.
    setInterval(renderActive, 30000);

    let timer = null;
    $('search').addEventListener('input', e => {
        clearTimeout(timer);
        timer = setTimeout(() => {
            state.query = e.target.value;
            requestList();
        }, 250);
    });

    $('view-btn').addEventListener('click', toggleViewMenu);
    $('flow-bar').querySelectorAll('[data-scope]').forEach(c => c.addEventListener('click', () => setFlow({ flowScope: c.dataset.scope })));
    $('flow-earlier').addEventListener('click', () => setFlow({ showDone: true }));
    $('flow-follow').addEventListener('click', wfFollowAgain);
    $('flow-news').addEventListener('click', wfFollowAgain);
    $('flow-fit').addEventListener('click', () => {
        if (!state.wf) return;
        wfMovedByHand(state.wf);
        wfFit(state.wf);
        wfNews(state.wf, false);
    });
    $('flow-zoom-in').addEventListener('click', () => state.wf && wfZoomBy(state.wf, 1.2));
    $('flow-zoom-out').addEventListener('click', () => state.wf && wfZoomBy(state.wf, 1 / 1.2));
    // El porcentaje devuelve el zoom a la app: siguiendo, el que deja ver la fila entera; sin seguir, el tamaño real.
    $('flow-zoom-level').addEventListener('click', () => {
        const v = state.wf;
        if (!v) return;
        if (v.follow) {
            wfMovedByHand(v);
            v.autoZoom = true;
            wfFollow(v, $('diagram'), false);
        } else wfZoomBy(v, 1 / v.cam.z);
    });
    hint($('flow-follow'), '◎', 'Seguir', null, 'La cámara va al paso que se está haciendo. Se deja de seguir al mover el lienzo.');
    hint($('flow-fit'), '⛶', 'Ver todo', null, 'Todo el flujo en lo que se ve, y se queda encajado aunque cambie el hueco o lleguen pasos, hasta que muevas el lienzo. Si no cabe a un tamaño que se lea, enseña lo último. Para moverse: arrastrar el fondo, la rueda, y Ctrl y la rueda para acercar.');
    hint($('flow-zoom-in'), '+', 'Acercar', null, 'También con Ctrl y la rueda, hacia donde está el ratón.');
    hint($('flow-zoom-out'), '−', 'Alejar', null, 'También con Ctrl y la rueda, hacia donde está el ratón.');
    hint($('flow-zoom-level'), '⌕', 'Zoom', null, 'Púlsalo para volver al zoom de la app: siguiendo, el que deja ver la fila entera; sin seguir, el tamaño real.');
    hint($('flow-bar').querySelector('[data-scope="current"]'), '◧', 'Prompt en curso', null, 'Solo lo que se ha hecho en el prompt que se ve.');
    hint($('flow-bar').querySelector('[data-scope="all"]'), '▤', 'Toda la sesión', null, 'Los últimos prompts de la sesión, uno detrás de otro.');
    showView();

    let findTimer = null;
    $('find').addEventListener('input', e => {
        clearTimeout(findTimer);
        findTimer = setTimeout(() => {
            state.find.query = e.target.value;
            state.find.index = -1;
            refreshFind();
            renderChat(false);
            decorate();
        }, 200);
    });
    $('find').addEventListener('keydown', e => {
        if (e.key !== 'Enter') return;
        e.preventDefault();
        findStep(e.shiftKey ? -1 : 1);
    });
    $('find-next').addEventListener('click', () => findStep(1));
    $('find-prev').addEventListener('click', () => findStep(-1));

    $('p-first').addEventListener('click', () => { goTo(0); if (state.replay.playing) schedule(); });
    $('p-last').addEventListener('click', () => { goTo(selectedSteps().length - 1); pause(); });
    $('p-prev').addEventListener('click', () => stepReplay(-1));
    $('p-next').addEventListener('click', () => stepReplay(1));
    $('p-play').addEventListener('click', () => (state.replay.playing ? pause() : play()));
    $('p-exit').addEventListener('click', exitReplay);
    $('p-range').addEventListener('input', e => {
        goTo(Number(e.target.value));
        if (state.replay.playing) schedule();
    });
    document.querySelectorAll('#speeds .chip').forEach(chip => chip.addEventListener('click', () => {
        state.replay.speed = Number(chip.dataset.speed);
        document.querySelectorAll('#speeds .chip').forEach(c => c.classList.toggle('on', c === chip));
        if (state.replay.playing) schedule();
    }));

    // El menú flotante de la revisión: se cierra al pulsar fuera y sigue a sus líneas al desplazar o cambiar de tamaño.
    document.addEventListener('mousedown', () => reviewPopOutside());
    document.addEventListener('scroll', () => reviewPopPlace(), true);
    window.addEventListener('resize', () => reviewPopPlace());

    // Teclado de la repetición; no cuenta mientras se escribe en un buscador.
    // Ctrl sobre un nombre del código lo subraya, y Ctrl+clic abre sus usos; se escucha antes que los clics de la ficha.
    document.addEventListener('mousemove', repoUsesHover);
    document.addEventListener('keyup', e => {
        if (e.key === 'Control' || e.key === 'Meta') repoUsesHover({ ctrlKey: false });
    });
    document.addEventListener('click', repoUsesClick, true);
    document.addEventListener('keydown', e => {
        // Esc cierra antes el menú flotante de la revisión que la ficha en la que está.
        if (e.key === 'Escape' && state.changes.pop) {
            reviewPopClose();
            return;
        }
        if (repoSearchKey(e)) return;
        if (fileFindKey(e)) return;
        if (!$('lightbox').hidden) {
            if (e.key === 'Escape') closeLightbox();
            else if (e.key === 'ArrowRight') stepLightbox(1);
            else if (e.key === 'ArrowLeft') stepLightbox(-1);
            else return;
            e.preventDefault();
            return;
        }
        if (e.key === 'Escape' && (!$('view-menu').hidden || !$('artifact-menu').hidden)) {
            closeViewMenu();
            closeArtifactMenu();
            return;
        }
        if (e.key === 'Escape' && !$('color-menu').hidden) {
            closeColorMenu();
            return;
        }
        if (e.key === 'Escape' && !$('calendar').hidden) {
            closeCalendar();
            return;
        }
        if (!state.trace || e.target.tagName === 'INPUT') return;
        const keys = {
            ' ': () => (state.replay.playing ? pause() : play()),
            ArrowRight: () => stepReplay(1),
            ArrowLeft: () => stepReplay(-1),
            Home: () => goTo(0),
            End: () => goTo(selectedSteps().length - 1),
            Escape: () => {
                if (!$('lightbox').hidden) closeLightbox();
                else if (!$('color-menu').hidden) closeColorMenu();
                else if (state.wf && state.wf.root.isConnected && !state.wf.drawer.hidden) wfCloseDetail(state.wf);
                else if (state.listsOpen) setListsOpen(false);
                else exitReplay();
            },
        };
        if (!keys[e.key]) return;
        e.preventDefault();
        keys[e.key]();
    });

    $('toggle-lists').addEventListener('click', () => setListsOpen(!state.listsOpen));
    $('all-btn').addEventListener('click', () => selectInteraction('all', false));
    $('prev').addEventListener('click', () => stepInteraction(-1));
    $('next').addEventListener('click', () => stepInteraction(1));
    // Pulsar fuera de las listas las cierra, si ya hay una sesión que ver.
    document.addEventListener('mousedown', e => {
        if (state.listsOpen && state.trace && !$('lists').contains(e.target) && !$('toggle-lists').contains(e.target)) setListsOpen(false);
    });

    initSplitter();
    initChanges();
    initPieces();

    // El dibujo depende del hueco: al cambiar el tamaño, se vuelve a dibujar.
    let resize = null;
    new ResizeObserver(() => {
        clearTimeout(resize);
        resize = setTimeout(() => {
            if (!state.trace) return;
            const v = state.wf;
            wfAnchorView(v, v && !v.drawer.hidden ? v.drawer.dataset.key : null);
            renderDiagram(false);
        }, 120);
    }).observe($('diagram'));

    // Al entrar, las listas se quedan recogidas y nada toma el foco: se abren con ☰ o se elige una activa.
    setListsOpen(false);
    if (document.activeElement && document.activeElement !== document.body) document.activeElement.blur();
    updateNav();
    updatePlayer([]);
    renderTitle();
    $('session-head').append(el('div', 'faint', 'Elige una sesión de la lista.'));
    $('diagram').append(el('div', 'empty', 'Elige una sesión para ver cómo ha trabajado.'));
    $('chat').append(el('div', 'empty', 'Aquí se ve, paso a paso, lo que ha hecho la sesión.'));
    if (host) host.addEventListener('message', e => receive(e.data));
    // La ventana multiplataforma entrega sus mensajes llamando a estas funciones; la de WPF no las usa.
    window.agentPulseReceive = receive;
    window.agentPulseHello = () => post({ type: 'ready' });
    post({ type: 'ready' });
    // Una pieza enseña la sesión que le dice la app: no lleva lista.
    if (!PIECE) requestList();
}

if (PIECE === 'file') startPiece();
else if (PIECE === 'finder') startFinder();
else start();
