// DIRECTO: los widgets del corcho en directo.
//
// La app lee todas las sesiones abiertas una vez para todos los widgets y manda su foto («live»): estado, lo que espera
// cada una de la persona y desde cuándo, sus últimas acciones, contexto, modelo y su última hora. La página solo dibuja
// el widget que pide la dirección (?w=tetoca) y avisa cuando se pulsa algo. Cada widget elige su tamaño por el ancho.

const NS = 'http://www.w3.org/2000/svg';

// Los cortes de tamaño de los widgets en directo: por debajo del primero, S; hasta el segundo, M; por encima, L.
const SIZE_M = 400;
const SIZE_L = 720;

// Una espera más larga que esto, en minutos, sale en rojo.
const LONG_WAIT = 10;

// Cada cuánto se repinta solo, para que las esperas cuenten aunque no llegue nada nuevo.
const TICK_MS = 15000;

const host = window.chrome && window.chrome.webview;
const widget = new URLSearchParams(location.search).get('w') || 'tetoca';
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

/** Crea un elemento de SVG con sus atributos y lo cuelga de su padre. */
function svg(tag, attrs, parent) {
    const node = document.createElementNS(NS, tag);
    for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
    if (parent) parent.append(node);
    return node;
}

/** Pone a un elemento la ficha de su tooltip: un título y sus líneas. */
function tip(node, title, ...rows) {
    node.dataset.tip = [title, ...rows.filter(Boolean)].join('\n');
    return node;
}

/** El tamaño del widget por el ancho de la página. */
function sizeOf() {
    const w = document.documentElement.clientWidth;
    return w > SIZE_L ? 'l' : w >= SIZE_M ? 'm' : 's';
}

/** Cuánto hace de un instante, corto: «ahora», «4 min», «2 h». */
function ago(ms) {
    const min = Math.max(0, Math.floor((Date.now() - ms) / 60000));
    if (min < 1) return 'ahora';
    if (min < 60) return `${min} min`;
    const h = Math.floor(min / 60);
    return h < 24 ? `${h} h` : `${Math.floor(h / 24)} d`;
}

/** Cuánto lleva esperando, con los minutos sueltos pasada la hora: «6 min», «1 h 20». */
function waited(ms) {
    const min = Math.max(0, Math.floor((Date.now() - ms) / 60000));
    if (min < 1) return 'ahora';
    if (min < 60) return `${min} min`;
    const m = min % 60;
    return m ? `${Math.floor(min / 60)} h ${String(m).padStart(2, '0')}` : `${Math.floor(min / 60)} h`;
}

/** La hora local de un instante, «16:40». */
function clock(ms) {
    return new Date(ms).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' });
}

const STATES = {
    asking: { label: 'Espera tu respuesta', color: 'var(--st-waiting)' },
    working: { label: 'Trabaja', color: 'var(--st-working)' },
    agents: { label: 'Espera a sus agentes', color: 'var(--dt-agent)' },
    command: { label: 'Espera a un comando', color: 'var(--dt-command)' },
    review: { label: 'Terminada, sin revisar', color: 'var(--danger)' },
    done: { label: 'Terminada', color: 'var(--st-done)' },
    idle: { label: 'Inactiva', color: 'var(--st-idle)' },
};

/** El piloto de estado de una sesión, con su ficha. */
function glyph(state, size = 15) {
    const st = STATES[state] || STATES.idle;
    const root = svg('svg', { width: size, height: size, viewBox: '0 0 16 16', class: 'dt-glyph' + (state === 'working' ? ' dt-blink' : '') });
    const c = st.color;
    if (state === 'working') svg('circle', { cx: 8, cy: 8, r: 5, fill: c }, root);
    else if (state === 'asking') {
        svg('circle', { cx: 8, cy: 8, r: 7, fill: c }, root);
        svg('text', { x: 8, y: 12, 'text-anchor': 'middle', 'font-size': 11, 'font-weight': 800, fill: '#16181f' }, root).textContent = '?';
    } else if (state === 'agents') {
        svg('circle', { cx: 8, cy: 8, r: 6, fill: 'none', stroke: c, 'stroke-width': 2, 'stroke-dasharray': '3.2 2.2' }, root);
        svg('circle', { cx: 8, cy: 8, r: 2, fill: c }, root);
    } else if (state === 'command') {
        svg('rect', { x: 1, y: 2.5, width: 14, height: 11, rx: 3, fill: c, 'fill-opacity': 0.18, stroke: c, 'stroke-width': 1.5 }, root);
        svg('text', { x: 8, y: 10.6, 'text-anchor': 'middle', 'font-size': 6.5, 'font-weight': 800, fill: c, 'font-family': 'Consolas, monospace' }, root).textContent = '>_';
    } else if (state === 'idle') svg('circle', { cx: 8, cy: 8, r: 6, fill: 'none', stroke: c, 'stroke-width': 1.6 }, root);
    else {
        svg('circle', { cx: 8, cy: 8, r: 7, fill: c }, root);
        svg('path', { d: 'M4.5 8.3l2.4 2.4 4.6-5', fill: 'none', stroke: '#fff', 'stroke-width': 1.8, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }, root);
    }
    tip(root, st.label, 'Estado de la sesión');
    return root;
}

/** El repositorio de una sesión con su color y su carpeta o tarea. */
function repo(s) {
    const node = el('span', 'dt-repo');
    if (s.color) node.style.setProperty('--rc', s.color);
    node.append(el('i'), el('b', null, s.title || 'Sesión'));
    const where = s.worktree || s.task;
    if (where) node.append(el('em', null, where));
    tip(node, s.title || 'Sesión', s.worktree && `Carpeta ${s.worktree}`, s.taskTitle && `${s.task} · ${s.taskTitle}`);
    return node;
}

const FAMILY = { read: '--dg-read', search: '--dg-search', edit: '--dg-edit', run: '--dg-run', ask: '--st-waiting', say: '--dg-say', agent: '--dt-agent', user: '--c-user', other: '--dg-other' };

/** Una acción reciente: quién, de qué color, qué hizo, sobre qué y cuándo. */
function action(a) {
    const row = el('div', 'dt-act');
    const who = el('span', 'who' + (a.who === 'A' ? ' a' : a.who === 'B' ? ' b' : ''), a.who);
    tip(who, a.who === 'A' ? 'Tú' : a.who === 'B' ? 'Claude' : `Agente ${a.who}`);
    const fam = el('i', 'fam');
    fam.style.background = `var(${FAMILY[a.hue] || FAMILY.other})`;
    const at = el('span', 'ago', ago(a.at));
    tip(at, `A las ${clock(a.at)}`);
    row.append(who, fam, el('span', 'verb', a.verb), el('span', 'obj', a.target), at);
    return row;
}

// TE TOCA

const KINDS = {
    question: 'pregunta',
    permission: 'permiso',
    plan: 'plan',
    review: 'revisión',
};

/** Las sesiones que esperan algo de la persona, la que más lleva esperando primero. */
function pendings(sessions) {
    return sessions.filter(s => s.pending).sort((a, b) => a.pending.since - b.pending.since);
}

/** Cuánto lleva esperando, con su ficha y en rojo si es mucho. */
function waitTag(p) {
    const long = Date.now() - p.since >= LONG_WAIT * 60000;
    return tip(el('span', 'wait' + (long ? ' long' : ''), waited(p.since)), 'Lleva esperando', `Desde las ${clock(p.since)}`);
}

/** Lo que se puede hacer con lo pendiente: en una revisión, abrirla o darla por vista; si no, ver las opciones que
 * tiene en su terminal, que es donde se contesta. */
function answers(s) {
    const p = s.pending;
    const box = el('div', 'tt-opts');
    if (p.kind === 'review') {
        const open = el('button', 'tt-btn strong', 'Abrir');
        open.type = 'button';
        open.dataset.open = s.id;
        const seen = el('button', 'tt-btn', 'Marcar vista');
        seen.type = 'button';
        seen.dataset.seen = s.id;
        tip(seen, 'Marcar vista', 'La quita de aquí y de Inicio sin abrir la sesión');
        box.append(open, seen);
        return box;
    }
    p.options.forEach((o, i) => box.append(el('span', 'tt-opt' + (i === 0 ? ' first' : ''), o)));
    box.append(el('span', 'tt-hint', p.options.length ? '· contesta en su terminal' : 'Contesta en su terminal'));
    return box;
}

/** La cabecera de una ficha: estado, repositorio, qué es y cuánto lleva. */
function cardHead(s) {
    const head = el('header');
    head.append(glyph(s.state), repo(s), el('span', 'kind', KINDS[s.pending.kind] || s.pending.kind), waitTag(s.pending));
    return head;
}

/** Lo que pide, con su rótulo si es una pregunta y cuántas más hace. */
function askBlock(s) {
    const p = s.pending;
    const box = el('div', 'ask');
    if (p.header) box.append(el('span', 'head', p.header));
    box.append(el('p', null, p.text));
    if (p.more > 0) box.append(el('span', 'tt-hint', `y ${p.more} ${p.more === 1 ? 'pregunta más' : 'preguntas más'} en la misma llamada`));
    box.append(answers(s));
    return box;
}

/** Una ficha de lo pendiente; en L, con lo que hizo antes al lado. */
function card(s, wide) {
    const node = el('article', `tt-card ${s.pending.kind}` + (wide ? ' wide' : ''));
    node.dataset.open = s.id;
    if (s.color) node.style.setProperty('--rc', s.color);
    node.append(cardHead(s), askBlock(s));
    if (wide) {
        const before = el('div', 'before');
        before.append(el('span', 'dt-cap', 'Antes de esto'));
        const acts = (s.actions || []).filter(a => s.pending.kind === 'review' || a.at < s.pending.since - 500).slice(0, 3);
        if (acts.length) before.append(...acts.map(action));
        else before.append(el('span', 'tt-hint', 'Sin acciones anteriores leídas'));
        node.append(before);
    }
    return node;
}

/** «Te toca»: preguntas, permisos, planes y revisiones de todas las sesiones, la que más espera primero. */
function drawTeToca(page, sessions, size) {
    const list = pendings(sessions);
    const n = list.length;
    if (size === 's') {
        const big = el('div', 'tt-big' + (n ? '' : ' none'));
        big.append(el('b', null, String(n)), el('span', null, n === 1 ? 'te toca' : n ? 'te tocan' : 'nada pendiente'));
        page.append(big);
        if (!n) return;
        const first = list[0];
        const mini = el('button', 'tt-mini');
        mini.type = 'button';
        mini.dataset.open = first.id;
        mini.style.setProperty('--tc', first.pending.kind === 'review' ? 'var(--danger)' : 'var(--st-waiting)');
        if (first.color) mini.style.setProperty('--rc', first.color);
        mini.append(repo(first), el('p', null, first.pending.text), el('small', null, `${KINDS[first.pending.kind]} · espera ${waited(first.pending.since)}`));
        page.append(mini);
        if (n > 1) page.append(el('span', 'dt-more', `y ${n - 1} más`));
        return;
    }
    if (!n) {
        const empty = el('div', 'dt-empty');
        empty.append(el('b', null, 'No te toca nada'), el('span', null, 'Cuando una sesión te pregunte, pida permiso, tenga un plan o termine sin revisar, aparece aquí.'));
        page.append(empty);
        return;
    }
    const cap = el('div', 'dt-cap');
    cap.append(el('span', null, `${n} ${n === 1 ? 'pendiente' : 'pendientes'} · la que más espera, primero`), el('span', null, clock(Date.now())));
    const cards = el('div', 'tt-list');
    cards.append(...list.map(s => card(s, size === 'l')));
    page.append(cap, cards);
}

// PULSO Y AHORA, DE TODAS

// El contexto pasa a ámbar y a rojo a partir de estos porcentajes, como en Avisos.
const CONTEXT_HIGH = 70;
const CONTEXT_FULL = 90;

/** Si la sesión espera algo de la persona: se tiñe en las listas. */
function asks(s) {
    return Boolean(s.pending);
}

/** Lo que hace la sesión, en una línea: si espera algo, lo que pide, como en «Te toca»; si no, lo que dice la app. */
function doingOf(s) {
    if (s.pending && s.pending.kind !== 'review') return s.pending.text;
    return s.doing || (STATES[s.state] || STATES.idle).label;
}

/** Una fila o bloque que abre su sesión al pulsarlo, teñido si espera algo: en rojo una revisión, en ámbar lo demás. */
function opener(tag, cls, s) {
    const tint = !asks(s) ? '' : s.pending.kind === 'review' ? ' ask revw' : ' ask';
    const node = el(tag, cls + tint);
    node.dataset.open = s.id;
    if (s.color) node.style.setProperty('--rc', s.color);
    return node;
}

/** Cuánto lleva en su estado, con su ficha. */
function sinceTag(s, cls) {
    const st = STATES[s.state] || STATES.idle;
    return tip(el('span', cls, ago(s.since)), st.label, `Desde las ${clock(s.since)}`);
}

/** El medidor del contexto de una sesión, con su porcentaje. */
function contextMeter(s) {
    const box = el('span', 'pu-ctx');
    if (s.context === undefined || s.context === null) {
        box.append(el('span', 'pu-faint', '—'));
        return box;
    }
    const bar = el('span', 'dt-meter');
    const fill = el('i');
    fill.style.width = `${Math.min(100, s.context)}%`;
    fill.style.background = s.context >= CONTEXT_FULL ? 'var(--danger)' : s.context >= CONTEXT_HIGH ? 'var(--st-waiting)' : 'var(--fg-muted)';
    bar.append(fill);
    box.append(bar, el('span', 'pu-pct', `${s.context} %`));
    tip(box, 'Contexto usado', `${s.context} % de la ventana`, s.context >= CONTEXT_HIGH ? 'Conviene resumir o abrir otra sesión' : null);
    return box;
}

/** La actividad de la última hora: una barra por trozo, alta según lo que trabajó. */
function spark(s, width) {
    const buckets = s.activity || [];
    const n = buckets.length || 30;
    const root = svg('svg', { width, height: 17, viewBox: `0 0 ${width} 17`, class: 'dt-glyph' });
    const bw = width / n;
    for (let i = 0; i < n; i++) {
        const v = buckets[i] || 0;
        const h = v ? Math.max(2, Math.round(v / 100 * 15)) : 1;
        svg('rect', { x: (i * bw + 0.5).toFixed(1), y: 16 - h, width: Math.max(1, bw - 1.5).toFixed(1), height: h, fill: v ? 'var(--st-working)' : 'var(--st-idle)', opacity: v ? 1 : 0.45 }, root);
    }
    tip(root, 'Última hora', `Cada barra, ${Math.round(60 / n)} minutos`, 'Alta si trabajó todo ese rato');
    return root;
}

/** El resumen de estados: cuántas te tocan, trabajan, esperan a sus agentes o a un comando y están quietas. */
function summary(sessions) {
    const count = st => sessions.filter(s => s.state === st).length;
    const box = el('div', 'pu-sum');
    const parts = [
        [sessions.filter(asks).length, 'te tocan', 'var(--st-waiting)'],
        [count('working'), 'trabajan', null],
        [count('agents'), 'con agentes', null],
        [count('command'), 'esperan un comando', null],
        [count('idle') + count('done'), 'quietas', null],
    ];
    for (const [n, label, color] of parts) {
        const item = el('span');
        if (color && n) item.style.color = color;
        item.append(el('b', null, String(n)), document.createTextNode(` ${label}`));
        box.append(item);
    }
    return box;
}

/** El mensaje de cuando no hay ninguna sesión abierta. */
function noSessions(page) {
    const empty = el('div', 'dt-empty');
    empty.append(el('b', null, 'No hay sesiones abiertas'), el('span', null, 'Cuando abras una con Claude Code, aparece aquí.'));
    page.append(empty);
}

/** «Pulso»: una fila de estado por sesión. En S, un cuadro por sesión; en M, estado y desde cuándo; en L, además, su
 * última hora, el contexto y el modelo. */
function drawPulso(page, sessions, size) {
    if (!sessions.length) return noSessions(page);
    if (size === 's') {
        const grid = el('div', 'pu-squares');
        grid.append(...sessions.map(s => {
            const sq = opener('button', 'pu-sq', s);
            sq.type = 'button';
            sq.append(glyph(s.state), el('em', null, s.worktree || s.task || s.title || ''));
            tip(sq, `${s.title}${s.worktree ? ' · ' + s.worktree : ''}`, (STATES[s.state] || STATES.idle).label, `Desde hace ${ago(s.since)}`, doingOf(s));
            return sq;
        }));
        page.append(grid, summary(sessions));
        return;
    }
    const rows = el('div', 'pu-rows');
    rows.append(...sessions.map(s => {
        const st = STATES[s.state] || STATES.idle;
        const row = opener('div', `pu-row ${size}`, s);
        const label = el('span', 'pu-state', st.label);
        label.style.color = st.color;
        row.append(glyph(s.state), repo(s), label);
        if (size === 'l') {
            const model = el('span', 'pu-model', s.model || '');
            if (s.model) tip(model, s.model, s.effort && `Esfuerzo ${s.effort}`);
            row.append(spark(s, 180), contextMeter(s), model);
        }
        row.append(sinceTag(s, 'pu-time'));
        return row;
    }));
    page.append(summary(sessions), rows);
}

/** Quién hizo lo último de una sesión: la letra de su última acción. */
function lastActor(s) {
    return s.actions && s.actions.length ? s.actions[0] : null;
}

/** «Ahora, de todas»: lo que hace cada sesión en este momento. En S y M, una línea por sesión; en L, un bloque con sus
 * últimas acciones. */
function drawTodas(page, sessions, size) {
    if (!sessions.length) return noSessions(page);
    if (size !== 'l') {
        const list = el('div', 'na-rows');
        list.append(...sessions.map(s => {
            const row = opener('div', `na-row ${size}`, s);
            const doing = el('span', 'na-doing', doingOf(s));
            if (size === 's') {
                const name = el('b', 'na-name', s.worktree || s.title || '');
                tip(name, s.title || 'Sesión', s.worktree && `Carpeta ${s.worktree}`);
                row.append(glyph(s.state), name, doing);
            } else {
                const a = lastActor(s);
                const who = a ? action(a).querySelector('.who') : el('span');
                const when = a ? tip(el('span', 'na-ago', ago(a.at)), 'Su última acción', `A las ${clock(a.at)}`) : el('span', 'na-ago');
                row.append(glyph(s.state), who, repo(s), doing, when);
            }
            return row;
        }));
        page.append(list);
        return;
    }
    const grid = el('div', 'na-grid');
    grid.append(...sessions.map(s => {
        const st = STATES[s.state] || STATES.idle;
        const block = opener('article', 'na-block', s);
        const head = el('header');
        const since = tip(el('span', 'na-since', `${st.label} · ${ago(s.since)}`), st.label, `Desde las ${clock(s.since)}`);
        head.append(glyph(s.state), repo(s), since);
        block.append(head);
        block.append(el('p', 'na-now', doingOf(s)));
        const acts = (s.actions || []).slice(0, 3);
        if (acts.length) block.append(...acts.map(action));
        else block.append(el('span', 'tt-hint', 'Sin acciones leídas todavía'));
        return block;
    }));
    page.append(grid);
}

// AVISOS

// Por debajo de este alto, Avisos deja fuera la frase de la previsión para que quepan los límites y el contexto.
const AVISOS_SHORT = 230;

/** El nombre corto de un límite: «5 h», «Semana» o el que traiga. */
function limitName(l, short) {
    if (l.key === 'five_hour') return '5 h';
    if (l.key === 'seven_day') return short ? 'Sem.' : 'Semana';
    return l.label;
}

/** El nombre largo de un límite, para su ficha. */
function limitTitle(l) {
    return l.key === 'five_hour' ? 'Límite de 5 horas' : l.key === 'seven_day' ? 'Límite semanal' : `Límite de ${l.label}`;
}

/** Cuándo cae un instante, como se dice: «18:20» hoy, «mañana 09:00» o «lun 09:00»; en corto, sin la hora fuera de hoy. */
function dayTime(ms, short) {
    const d = new Date(ms);
    const today = new Date();
    const days = Math.round((new Date(d.getFullYear(), d.getMonth(), d.getDate()) - new Date(today.getFullYear(), today.getMonth(), today.getDate())) / 86400000);
    const day = d.toLocaleDateString('es-ES', { weekday: 'short' }).replace('.', '');
    if (days === 0) return clock(ms);
    if (short) return day;
    return days === 1 ? `mañana ${clock(ms)}` : `${day} ${clock(ms)}`;
}

/** Cuándo se renueva un límite, para seguir a «se renueva»: «a las 18:20» hoy, «mañana 09:00» o «lun 09:00». */
function renews(ms) {
    return new Date(ms).toDateString() === new Date().toDateString() ? `a las ${clock(ms)}` : dayTime(ms);
}

/** La previsión de un límite en una frase: cuándo llega al tope o cuánto marcará al renovarse. */
function forecast(l) {
    if (l.full) return `Al ritmo actual, tope a las ${dayTime(l.full)}`;
    if (l.atReset !== undefined && l.atReset !== null) return `Al ritmo actual, ~${l.atReset} % al renovarse`;
    return 'Aún sin datos para la previsión';
}

/** Una fila de límite: su nombre, la barra de lo gastado y el porcentaje con cuándo se renueva. */
function limitRow(l, short) {
    const row = el('div', 'av-row' + (short ? ' s' : ''));
    const bar = el('span', 'av-bar');
    const fill = el('i');
    fill.style.width = `${l.pct}%`;
    fill.style.background = l.color || 'var(--st-working)';
    bar.append(fill);
    const when = el('span', 'av-when');
    const pct = el('b', null, `${l.pct} %`);
    pct.style.color = l.pressing ? 'var(--danger)' : (l.color || 'var(--fg)');
    when.append(pct);
    if (l.resets) when.append(document.createTextNode(short ? ` · ${dayTime(l.resets, true)}` : ` · se renueva ${renews(l.resets)}`));
    row.append(el('b', null, limitName(l, short)), bar, when);
    tip(row, limitTitle(l), `${l.pct} % gastado`, l.resets && `Se renueva ${renews(l.resets)}`, forecast(l),
        l.perHour ? `Sube ~${String(l.perHour).replace('.', ',')} puntos por hora` : null, l.pressing ? 'Aprieta: pasa del umbral de aviso' : null);
    return row;
}

/** La frase de la previsión: en ámbar si algún límite llega al tope antes de renovarse; si no, la del de 5 horas. */
function callout(limits) {
    const full = limits.filter(l => l.full).sort((a, b) => a.full - b.full)[0];
    if (full) {
        const box = el('div', 'av-call');
        box.append(document.createTextNode(`Al ritmo actual llegas al tope ${full.key === 'five_hour' ? 'de las 5 h' : 'semanal'} a las `), el('b', null, dayTime(full.full)));
        if (full.resets) box.append(document.createTextNode(`; se renueva ${renews(full.resets)}.`));
        return box;
    }
    const five = limits.find(l => l.key === 'five_hour' && l.atReset !== undefined && l.atReset !== null);
    return five ? el('div', 'av-call calm', `Al ritmo actual no llegas al tope: ~${five.atReset} % al renovarse.`) : null;
}

/** Las sesiones con el contexto alto, de la más llena a la menos. */
function hotContext(sessions) {
    return sessions.filter(s => s.context >= CONTEXT_HIGH).sort((a, b) => b.context - a.context);
}

/** La lista de sesiones con el contexto alto: repositorio, medidor y porcentaje; cada fila abre su sesión. */
function contextList(sessions) {
    const hot = hotContext(sessions);
    const box = el('div', 'av-ctx');
    if (!hot.length) {
        box.append(el('span', 'tt-hint', 'Ninguna sesión pasa del 70 % de contexto'));
        return box;
    }
    box.append(...hot.map(s => {
        const row = el('div', 'av-crow');
        row.dataset.open = s.id;
        if (s.color) row.style.setProperty('--rc', s.color);
        const meter = contextMeter(s).querySelector('.dt-meter');
        const pct = el('span', 'pct', `${s.context} %`);
        pct.style.color = s.context >= CONTEXT_FULL ? 'var(--danger)' : 'var(--st-waiting)';
        row.append(repo(s), meter, pct);
        tip(row, 'Contexto usado', `${s.context} % de la ventana`, 'Conviene resumir o abrir otra sesión');
        return row;
    }));
    return box;
}

/** La gráfica del límite de 5 horas: lo gastado desde que empezó su ventana, lo que vendría al ritmo actual y, si se
 * llega al tope antes de renovarse, el tramo sin cupo. */
function limitChart(l, w) {
    const H = 132, L = 34, R = w - 10, T = 12, B = 22;
    const from = l.start, to = l.resets, now = Date.now();
    const X = t => L + Math.max(0, Math.min(1, (t - from) / (to - from))) * (R - L);
    const Y = p => H - B - p / 100 * (H - B - T);
    const root = svg('svg', { width: w, height: H, viewBox: `0 0 ${w} ${H}`, class: 'av-chart dt-glyph' });
    for (const v of [0, 50, 100]) {
        const line = { x1: L, x2: R, y1: Y(v), y2: Y(v), stroke: 'var(--dt-ink)', 'stroke-opacity': 0.5 };
        if (v === 100) line['stroke-dasharray'] = '4 3';
        svg('line', line, root);
        svg('text', { x: L - 6, y: Y(v) + 3, 'text-anchor': 'end' }, root).textContent = `${v} %`;
    }
    for (const [t, anchor] of [[from, 'start'], [(from + to) / 2, 'middle'], [to, 'end']])
        svg('text', { x: X(t), y: H - 6, 'text-anchor': anchor }, root).textContent = clock(t);
    if (l.full) {
        svg('rect', { x: X(l.full), y: Y(100), width: Math.max(0, R - X(l.full)), height: Y(0) - Y(100), fill: 'var(--danger)', opacity: 0.16 }, root);
        svg('text', { x: Math.min(X(l.full) + 4, R - 44), y: Y(100) + 13, style: 'fill:var(--danger)' }, root).textContent = 'sin cupo';
    }
    const pts = (l.trail || []).map(([t, p]) => `${X(t).toFixed(1)} ${Y(p).toFixed(1)}`);
    if (pts.length) {
        const path = 'M' + pts.join('L');
        svg('path', { d: `${path}L${X(now).toFixed(1)} ${Y(0)}L${X(from).toFixed(1)} ${Y(0)}Z`, fill: 'var(--st-working)', opacity: 0.16 }, root);
        svg('path', { d: path, fill: 'none', stroke: 'var(--st-working)', 'stroke-width': 2, 'stroke-linejoin': 'round' }, root);
    }
    const end = l.full ? [l.full, 100] : l.atReset !== undefined && l.atReset !== null ? [to, l.atReset] : null;
    if (end) svg('path', { d: `M${X(now)} ${Y(l.pct)}L${X(end[0])} ${Y(end[1])}`, stroke: 'var(--st-waiting)', 'stroke-width': 2, 'stroke-dasharray': '5 4', fill: 'none' }, root);
    svg('circle', { cx: X(now), cy: Y(l.pct), r: 4, fill: 'var(--danger)' }, root);
    tip(root, 'Consumo del límite de 5 horas', 'Línea: lo gastado hasta ahora', end ? 'Discontinua: al ritmo actual' : null, l.full ? 'Rojo: sin cupo hasta renovarse' : null);
    return root;
}

/** «Avisos»: los límites con su previsión y las sesiones con el contexto alto. En S, las barras y cuántas sesiones van
 * llenas; en M, además, la previsión y la lista; en L, también la gráfica del límite de 5 horas. */
function drawAvisos(page, sessions, size, limits) {
    if (!limits.length) {
        const empty = el('div', 'dt-empty');
        empty.append(el('b', null, 'Sin datos de los límites todavía'), el('span', null, 'Llegan con la primera respuesta de cualquier sesión.'));
        page.append(empty);
        return;
    }
    const main = limits.filter(l => l.key === 'five_hour' || l.key === 'seven_day');
    const shown = main.length ? main : limits;
    if (size === 's') {
        const hot = hotContext(sessions).length;
        page.append(...shown.map(l => limitRow(l, true)));
        const chip = el('span', 'av-chip' + (hot ? ' hot' : ''), hot ? `${hot} con contexto alto` : 'contexto en orden');
        page.append(chip);
        return;
    }
    const left = size === 'l' ? el('div', 'av-col') : page;
    left.append(...shown.map(l => limitRow(l, false)));
    const call = innerHeight >= AVISOS_SHORT ? callout(limits) : null;
    if (call) left.append(call);
    left.append(el('div', 'dt-cap', 'Contexto alto'), contextList(sessions));
    if (size !== 'l') return;
    const cols = el('div', 'av-cols');
    const right = el('div', 'av-col');
    const five = limits.find(l => l.key === 'five_hour' && l.start && l.resets);
    if (five) {
        right.append(el('div', 'dt-cap', `Límite de 5 horas, desde las ${clock(five.start)}`));
        right.append(limitChart(five, Math.max(240, Math.floor((document.documentElement.clientWidth - 24 - 20) / 2))));
    } else right.append(el('span', 'tt-hint', 'Sin la ventana del límite de 5 horas para dibujarla'));
    cols.append(left, right);
    page.append(cols);
}

// CARRIL DE SESIÓN

// Los cortes de tamaño del carril, más estrecho que los demás: por debajo del primero, S; hasta el segundo, M.
const LANE_M = 280;
const LANE_L = 400;

/** El tamaño del carril por el ancho de la página. */
function laneSize() {
    const w = document.documentElement.clientWidth;
    return w > LANE_L ? 'l' : w >= LANE_M ? 'm' : 's';
}

/** Desde cuándo está en su estado, en una frase: «desde hace 6 min» o «desde ahora». */
function sinceText(ms) {
    const a = ago(ms);
    return a === 'ahora' ? 'desde ahora' : `desde hace ${a}`;
}

/** Lo que espera la sesión, en su ficha como en «Te toca»: qué es, cuánto lleva, lo que pide y cómo contestar. */
function laneAsk(s) {
    const node = el('article', `tt-card ${s.pending.kind}`);
    node.dataset.open = s.id;
    if (s.color) node.style.setProperty('--rc', s.color);
    const head = el('header');
    head.append(el('span', 'kind', KINDS[s.pending.kind] || s.pending.kind), waitTag(s.pending));
    node.append(head, askBlock(s));
    return node;
}

/** «Carril de sesión»: una sesión entera en una columna, la que está en su posición por orden de apertura. La franja de
 * su repositorio, su reloj (no en S), su estado y lo que hace, lo que espera si espera algo, sus agentes, sus últimas
 * acciones y, al pie, el contexto y el modelo. */
function drawCarril(page, sessions, size) {
    const slot = options.slot || 1;
    const s = sessions[slot - 1];
    if (!s) {
        const empty = el('div', 'dt-empty');
        empty.append(el('b', null, `No hay una ${slot}.ª sesión abierta`),
            el('span', null, 'Este carril sigue a la sesión que está en esa posición, por orden de apertura. Se cambia en el menú del recuadro, en «Seguir a».'));
        page.append(empty);
        return;
    }
    const lane = el('div', 'cr');
    const band = el('button', 'cr-band');
    band.type = 'button';
    band.dataset.open = s.id;
    band.style.background = s.band || s.color || 'var(--fg-faint)';
    band.append(el('b', null, s.title || 'Sesión'));
    if (s.worktree || s.task) band.append(el('span', null, `· ${s.worktree || s.task}`));
    tip(band, s.title || 'Sesión', s.worktree && `Carpeta ${s.worktree}`, s.taskTitle && `${s.task} · ${s.taskTitle}`, 'Pulsa para abrir la sesión');

    const st = STATES[s.state] || STATES.idle;
    const state = el('div', 'cr-state');
    state.dataset.open = s.id;
    if (size !== 's') {
        const px = size === 'l' ? 120 : 96;
        const dial = el('div', 'cr-clock');
        dial.style.width = dial.style.height = `${px}px`;
        dial.append(ArkkoReloj.draw(s, last.now || Date.now(), px));
        tip(dial, 'Su última hora', 'Cada tramo cae en su minuto', 'Dentro, la sesión; fuera, sus agentes');
        state.append(dial);
    }
    const text = el('div', 'cr-tx');
    const label = el('span', 'cr-label');
    label.style.color = st.color;
    label.append(glyph(s.state), document.createTextNode(st.label));
    text.append(label, tip(el('span', 'cr-since', sinceText(s.since)), st.label, `Desde las ${clock(s.since)}`));
    // Si espera algo, lo que pide ya va en su ficha, debajo.
    if (!s.pending) text.append(el('span', 'cr-doing', doingOf(s)));
    state.append(text);
    lane.append(band, state);

    if (s.pending) lane.append(laneAsk(s));
    if (s.agents && s.agents.length && (s.state === 'agents' || s.state === 'working')) {
        const agents = el('div', 'cr-agents');
        agents.append(el('span', 'dt-cap', 'Agentes'), ...s.agents.map(a => tip(el('span', 'who', a), `Agente ${a}`)));
        lane.append(agents);
    }
    // Lo que espera una sesión cuyo turno acabó con un comando en marcha: cada uno, con su «>_».
    if (s.state === 'command' && s.commands && s.commands.length) {
        const commands = el('div', 'cr-agents cr-commands');
        commands.append(el('span', 'dt-cap', 'Espera a'), ...s.commands.map(c => tip(el('span', 'cmd', c), 'Comando en marcha', c)));
        lane.append(commands);
    }
    const feed = el('div', 'cr-feed');
    feed.append(el('span', 'dt-cap', 'Directo'));
    const acts = (s.actions || []).slice(0, size === 's' ? 3 : size === 'l' ? 6 : 5);
    if (acts.length) feed.append(...acts.map(action));
    else feed.append(el('span', 'tt-hint', 'Sin acciones leídas todavía'));
    lane.append(feed);

    const foot = el('div', 'cr-foot');
    const model = el('span', 'pu-model', s.model || '');
    if (s.model) tip(model, s.model, s.effort && `Esfuerzo ${s.effort}`);
    foot.append(el('span', null, 'Contexto'), contextMeter(s), model);
    lane.append(foot);
    page.append(lane);
}

const WIDGETS = { tetoca: drawTeToca, pulso: drawPulso, todas: drawTodas, avisos: drawAvisos, carril: drawCarril };

// Lo elegido en el widget, que manda la app («options»): en el carril, la posición de la sesión que sigue.
let options = {};

/** Pinta el widget con lo último que mandó la app, conservando el desplazamiento. */
function render() {
    if (!last) return;
    const page = document.getElementById('page');
    const top = page.scrollTop;
    const size = widget === 'carril' ? laneSize() : sizeOf();
    document.body.dataset.size = size;
    page.replaceChildren();
    (WIDGETS[widget] || drawTeToca)(page, last.sessions || [], size, last.limits || []);
    page.scrollTop = top;
    hideTip();
}

/** Aplica el tema de la app: sus colores como variables, si es claro y su diseño. */
function applyTheme(m) {
    for (const [name, value] of Object.entries(m.vars || {})) document.documentElement.style.setProperty(name, value);
    document.documentElement.dataset.theme = m.light ? 'light' : 'dark';
    document.documentElement.dataset.design = m.design || '';
}

/** Atiende lo que manda la app: el tema, lo elegido en el widget o la foto de las sesiones. */
function receive(m) {
    if (m.type === 'theme') applyTheme(m);
    else if (m.type === 'options') {
        options = m.options || {};
        render();
    }
    else if (m.type === 'live') {
        last = m;
        render();
    }
}

// TOOLTIP CON DISEÑO

const tipBox = document.getElementById('tip');

/** Enseña la ficha de un elemento junto al ratón. */
function showTip(node, x, y) {
    const [title, ...rows] = node.dataset.tip.split('\n');
    tipBox.replaceChildren(el('b', null, title), ...rows.map(r => el('span', null, r)));
    tipBox.hidden = false;
    const w = tipBox.offsetWidth, h = tipBox.offsetHeight;
    tipBox.style.left = `${Math.max(4, Math.min(x + 12, innerWidth - w - 4))}px`;
    tipBox.style.top = `${y + 16 + h > innerHeight ? Math.max(4, y - h - 8) : y + 16}px`;
}

/** Esconde la ficha. */
function hideTip() {
    tipBox.hidden = true;
}

document.addEventListener('mousemove', e => {
    const node = e.target.closest && e.target.closest('[data-tip]');
    if (node) showTip(node, e.clientX, e.clientY);
    else hideTip();
});
document.addEventListener('mouseleave', hideTip);

document.addEventListener('click', e => {
    const seen = e.target.closest('[data-seen]');
    if (seen) {
        post({ type: 'seen', id: seen.dataset.seen });
        return;
    }
    const open = e.target.closest('[data-open]');
    if (open) post({ type: 'open', id: open.dataset.open });
});

new ResizeObserver(() => render()).observe(document.documentElement);
setInterval(render, TICK_MS);
window.agentPulseReceive = receive;
window.agentPulseHello = () => post({ type: 'ready' });
post({ type: 'ready' });
