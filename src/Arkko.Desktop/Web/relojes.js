// RELOJES: un reloj por sesión abierta, todos con la misma hora.
//
// Cada reloj lo dibuja reloj.js, el mismo del carril del corcho en directo. La app calcula los tramos de la última hora
// de cada sesión y los manda ya hechos («clocks»), con su estado, lo que hace y el color de su repositorio; la página
// solo dibuja y avisa cuando se pulsa uno.
//
// Las sesiones van agrupadas por repositorio: cada repositorio es un plano con su franja de color, y cada sesión, una
// lámina con su reloj y un cajetín como el de un plano. Planos y láminas siguen el orden de apertura.

// Los tres tamaños, por el ancho que hay, con los mismos cortes que los widgets del corcho: en S, solo los relojes con
// su carpeta y su estado debajo; en M, la lámina con un cajetín corto (estado y ahora); en L, la lámina entera.
const SIZE_M = 480;
const SIZE_L = 900;

// Medidas de la cuadrícula para encajar los planos, por tamaño: lo que ocupa una lámina además de su reloj (a lo ancho,
// el cajetín más estrecho que se admite; a lo alto, el texto de debajo en S), y el reloj mayor y menor. El reloj va del
// mayor al menor hasta que todo cabe; si ni con el menor cabe, se queda ese y el resto se ve bajando.
const SIZES = {
    s: { extraW: 20, extraH: 58, min: 64, max: 150 },
    m: { extraW: 250, extraH: 40, min: 90, max: 170 },
    l: { extraW: 380, extraH: 44, min: 110, max: 220 },
};
const PLAN_HEAD = 38;
const PLAN_GAP = 14;
const SHEET_GAP = 10;
const CLOCK_STEP = 10;

const host = window.chrome && window.chrome.webview;
let last = null;

/** Manda un mensaje a la app: con invokeCSharpAction en la ventana multiplataforma, o con WebView2. */
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

/** Una fila del cajetín: su rótulo y su valor, con una clase para el valor si hace falta. */
function boxRow(label, value, cls) {
    const row = el('div', 'ck-row');
    row.append(el('span', 'ck-label', label), el('span', cls || null, value));
    return row;
}

/** Una casilla del pie del cajetín: su rótulo encima y su valor. */
function boxCell(label, value) {
    const cell = el('span');
    cell.append(el('b', null, label), document.createTextNode(value));
    return cell;
}

/** El cajetín de una sesión: su carpeta y su tarea en la cabecera, su estado y lo que hace; en el tamaño ancho, además,
 * el prompt en curso y, al pie, el modelo, el contexto y los agentes de la última hora. */
function titleBlock(s, size) {
    const box = el('div', 'ck-box');
    const head = el('div', 'ck-box-head');
    head.append(el('b', null, s.worktree || s.title));
    if (s.task) head.append(el('span', 'ck-key', s.task));
    box.append(head);
    box.append(boxRow('ESTADO', s.since ? `${s.status} · ${s.since}` : s.status, 'ck-state'));
    box.append(boxRow('AHORA', s.doing || '—'));
    if (size !== 'l') return box;
    box.append(boxRow('PROMPT', s.prompt ? `«${s.prompt}»${s.took ? ` · ${s.took}` : ''}` : '—'));
    const foot = el('div', 'ck-foot');
    foot.append(
        boxCell('MODELO', s.model || '—'),
        boxCell('CONTEXTO', s.context !== null && s.context !== undefined ? `${s.context} %` : '—'),
        boxCell('AGENTES', s.agents && s.agents.length ? s.agents.join(' ') : '—'));
    box.append(foot);
    return box;
}

/** Lo que va bajo el reloj en el tamaño estrecho: la carpeta y el estado con cuánto lleva. */
function caption(s) {
    const box = el('div', 'ck-caption');
    box.append(el('b', null, s.worktree || s.title), el('span', 'ck-state', s.since ? `${s.status} · ${s.since}` : s.status));
    return box;
}

/** La lámina de una sesión: su reloj y su cajetín, o solo su reloj con su carpeta debajo si es estrecha. Pulsarla abre
 * la sesión. */
function sheet(s, now, clockSize, size) {
    const node = el('button', `ck-sheet ${s.state}`);
    node.type = 'button';
    node.dataset.id = s.id;
    const clock = ArkkoReloj.draw(s, now, clockSize);
    const mark = clock.querySelector('.ck-mark');
    if (mark && s.color && s.state !== 'asking') mark.style.setProperty('fill', s.color);
    node.append(clock, size === 's' ? caption(s) : titleBlock(s, size));
    return node;
}

/** Junta las sesiones por repositorio, en el orden en que se abrió la primera de cada uno. */
function plans(sessions) {
    const byRepo = new Map();
    for (const s of sessions) {
        if (!byRepo.has(s.title)) byRepo.set(s.title, { title: s.title, color: s.color, band: s.band, sessions: [] });
        byRepo.get(s.title).sessions.push(s);
    }
    return [...byRepo.values()];
}

/** Cuánto mide de alto la cuadrícula con unas columnas y un tamaño de reloj: coloca los planos fila a fila, cada uno
 * con tantas columnas como sesiones tenga (o todas), como los coloca la rejilla. */
function heightFor(groups, cols, clock, extraH) {
    const sheetH = clock + extraH;
    let total = 0, used = cols, rowH = 0, rows = 0;
    for (const g of groups) {
        const span = Math.min(g.sessions.length, cols);
        const h = PLAN_HEAD + Math.ceil(g.sessions.length / cols) * (sheetH + SHEET_GAP) - SHEET_GAP;
        if (used + span > cols) {
            total += rowH;
            rows++;
            used = 0;
            rowH = 0;
        }
        used += span;
        rowH = Math.max(rowH, h);
    }
    return total + rowH + PLAN_GAP * Math.max(0, rows - 1);
}

/** Elige el tamaño por el ancho, y columnas y reloj para que quepan todos los planos en el sitio que hay, con el reloj
 * más grande posible; si ni con el menor caben, deja ese y el resto se ve bajando. */
function fit(grid, groups) {
    const w = grid.clientWidth;
    const h = grid.clientHeight;
    const size = w >= SIZE_L ? 'l' : w >= SIZE_M ? 'm' : 's';
    const m = SIZES[size];
    const colsFor = clock => Math.max(1, Math.floor((w + PLAN_GAP) / (clock + m.extraW + PLAN_GAP)));
    let choice = null;
    for (let clock = m.max; clock >= m.min; clock -= CLOCK_STEP) {
        const cols = colsFor(clock);
        if (heightFor(groups, cols, clock, m.extraH) <= h) {
            choice = { cols, clock, fits: true };
            break;
        }
    }
    if (!choice) choice = { cols: colsFor(m.min), clock: m.min, fits: false };
    grid.style.setProperty('--cols', String(choice.cols));
    grid.style.setProperty('--clock', `${choice.clock}px`);
    grid.classList.toggle('fits', choice.fits);
    document.body.dataset.size = size;
    return { ...choice, size };
}

/** Pinta los planos de cada repositorio con sus láminas, con lo último que mandó la app. */
function render() {
    if (!last) return;
    const grid = document.getElementById('grid');
    const sessions = last.sessions || [];
    const groups = plans(sessions);
    const time = new Date(last.now).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' });
    document.getElementById('head').textContent = sessions.length
        ? `La última hora de cada sesión · ${sessions.length} ${sessions.length === 1 ? 'sesión' : 'sesiones'} en ${groups.length} ${groups.length === 1 ? 'repositorio' : 'repositorios'} · son las ${time}`
        : '';
    document.getElementById('empty').hidden = sessions.length > 0;
    const { cols, clock, size } = fit(grid, groups);
    grid.replaceChildren(...groups.map(g => {
        const span = Math.min(g.sessions.length, cols);
        const plan = el('section', 'ck-plan');
        plan.style.setProperty('grid-column', `span ${span}`);
        if (g.color) plan.style.setProperty('--repo', g.color);
        if (g.band) plan.style.setProperty('--band', g.band);
        const head = el('header', 'ck-plan-head', g.title);
        head.append(el('small', null, `${g.sessions.length} ${g.sessions.length === 1 ? 'sesión' : 'sesiones'}`));
        const sheets = el('div', 'ck-sheets');
        sheets.style.setProperty('--span', String(span));
        sheets.append(...g.sessions.map(s => sheet(s, last.now, clock, size)));
        plan.append(head, sheets);
        return plan;
    }));
}

/** Aplica el tema de la app: sus colores como variables, si es claro y su diseño. */
function applyTheme(m) {
    for (const [name, value] of Object.entries(m.vars || {})) document.documentElement.style.setProperty(name, value);
    document.documentElement.dataset.theme = m.light ? 'light' : 'dark';
    document.documentElement.dataset.design = m.design || '';
}

/** Atiende lo que manda la app: el tema o los relojes. */
function receive(m) {
    if (m.type === 'theme') applyTheme(m);
    else if (m.type === 'clocks') {
        last = m;
        render();
    }
}

document.getElementById('grid').addEventListener('click', e => {
    const card = e.target.closest('.ck-sheet');
    if (card) post({ type: 'open', id: card.dataset.id });
});
new ResizeObserver(() => render()).observe(document.getElementById('grid'));
window.agentPulseReceive = receive;
window.agentPulseHello = () => post({ type: 'ready' });
post({ type: 'ready' });
