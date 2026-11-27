// OBRA: la vista «obra» de Sesiones, la sesión contada como una obra de videojuego tranquilo.
//
// Cada tarea del tasks.md del change es un solar: se marca con estacas, se construye con andamio mientras se trabaja y se
// remata con tejado y bandera al marcarla. Claude es el jefe de obra en la caseta, cada subagente es un operario y la
// persona es la dirección de obra. Nadie se mueve en cada acción: cada operario hace un trabajo continuo y solo camina al cambiar
// de tarea. El plano llega de la app («obraPlan»); lo demás sale de los pasos de la sesión. En directo, la obra va a la
// hora real; con la repetición, a la hora del paso que se ve.
//
// Todo va dentro de una función para no chocar con los nombres de sesiones.js; fuera solo quedan obRender, obLeave y
// obPlan. three.js se carga la primera vez que se abre la vista, y la escena solo se pinta mientras se ve.

(function () {
'use strict';

const THREE_SRC = 'vendor/three.min.js';
const PREFS = 'arkko.obra';
const plans = new Map();
let ob = null;
let loading = null;

const clamp = (x, a = 0, b = 1) => Math.max(a, Math.min(b, x));
const hexOf = c => '#' + c.toString(16).padStart(6, '0');
const escOf = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const ms = iso => iso ? Date.parse(iso) : NaN;
const hm = t => new Date(t).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' });
const dayOf = t => new Date(t).toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric' });
const durOf = d => { const m = Math.round(d / 60000); return m < 1 ? 'menos de 1 min' : m < 60 ? m + ' min' : Math.floor(m / 60) + ' h ' + (m % 60) + ' min'; };
const wrapA = d => { while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; return d; };
const toolOf = s => (s.title || '').split(' · ')[0];
const fileOf = s => { const i = (s.title || '').indexOf(' · '); return i < 0 ? '' : s.title.slice(i + 3).trim(); };

// Papel de cada subagente en la obra, por su tipo y su encargo.
const ROLES = {
    propose: [0xA78BFA, 'Propose'], apply: [0xA78BFA, 'Apply'], archive: [0xA78BFA, 'Archive'], review: [0xA78BFA, 'Revisión'],
    inspector: [0xF29BC4, 'Inspector'], commit: [0x3FB950, 'Camión'], analysis: [0xC4B5FD, 'Topógrafo'], docs: [0x8E9BF5, 'Documentación'], worker: [0x9A968F, 'Operario'],
};
function roleOf(a) {
    const s = `${a.type || ''} ${a.description || ''}`;
    if (/adversarial|secure-code|security|seguridad/i.test(s)) return 'inspector';
    if (/\bcommit\b|\bpush\b/i.test(s)) return 'commit';
    if (/propose/i.test(s)) return 'propose';
    if (/\bapply\b/i.test(s)) return 'apply';
    if (/archive/i.test(s)) return 'archive';
    if (/review/i.test(s)) return 'review';
    if (/analy|explore|investig|análisis/i.test(s)) return 'analysis';
    if (/docs|sync|spec/i.test(s)) return 'docs';
    return 'worker';
}
const BUILDS = new Set(['apply', 'review', 'worker']);
const WORD = { Read: 'lee planos', Grep: 'busca', Glob: 'busca', Edit: 'construye', MultiEdit: 'construye', Write: 'construye', NotebookEdit: 'construye', Bash: 'prueba', PowerShell: 'prueba' };

/** Recibe el plano de la obra de una sesión y, si es la que se ve, rehace los solares. */
function obPlan(m) {
    plans.set(m.id, m);
    if (ob && ob.id === m.id) { ob.planDirty = true; if (ob.active) buildPlan(); }
}

/** Deja de pintar la obra: se ha cambiado de vista. */
function obLeave() {
    if (ob) ob.active = false;
}

/** Pinta la obra de la sesión que se ve: la monta la primera vez y, después, solo pone al día lo que sabe. */
function obRender(steps, box) {
    const id = state.trace.id;
    if (!window.THREE) {
        if (!box.querySelector('.ob-wait')) { const w = document.createElement('div'); w.className = 'ob-wait'; w.textContent = 'Montando la obra…'; box.replaceChildren(w); }
        loading = loading || new Promise((ok, fail) => { const s = document.createElement('script'); s.src = THREE_SRC; s.onload = ok; s.onerror = fail; document.head.append(s); });
        loading.then(() => { if (state.mapLayout === 'obra' && state.trace && state.trace.id === id) renderDiagram(false); })
            .catch(() => { const w = box.querySelector('.ob-wait'); if (w) w.textContent = 'No se ha podido cargar la obra: falta three.js junto a la app.'; });
        return;
    }
    if (!ob || ob.id !== id || !box.contains(ob.root)) {
        dispose();
        try { ob = mount(box, id); }
        catch (e) { const w = document.createElement('div'); w.className = 'ob-wait'; w.textContent = 'No se ha podido montar la obra: este equipo no tiene WebGL.'; box.replaceChildren(w); return; }
    }
    ob.active = true;
    ob.moment = steps.length ? flowMoment(steps) : { now: null, replaying: false, working: Boolean(state.trace.working) };
    ob.trace = state.trace;
    ob.facts = facts(state.trace);
    if (ob.planDirty || ob.planFor !== id) buildPlan();
    if (!ob.running) { ob.running = true; ob.last = performance.now(); requestAnimationFrame(frame); }
}

function dispose() {
    if (!ob) return;
    ob.active = false;
    ob.disposed = true;
    try { ob.R.dispose(); } catch { /* ya no está */ }
    ob = null;
}

// HECHOS DE LA SESIÓN: lo que se calcula una vez por lectura, no en cada cuadro

/** Lo que se saca de los pasos al llegar una lectura: ratos de trabajo, ediciones y trabajo de cada agente, y preguntas. */
function facts(trace) {
    const steps = trace.steps || [];
    const byAgent = new Map();
    const asks = [], planAsks = [];
    for (const s of steps) {
        const id = s.agentId || 'main';
        if (!byAgent.has(id)) byAgent.set(id, []);
        byAgent.get(id).push(s);
        if (s.questions && s.questions.length) asks.push({ ts: ms(s.start), answered: s.end ? ms(s.end) : null, header: s.questions[0].header || s.questions[0].question || 'Pregunta', step: s });
        if (!s.agentId && toolOf(s) === 'ExitPlanMode') planAsks.push({ ts: ms(s.start), answered: s.end ? ms(s.end) : null, step: s });
    }
    const busy = new Map(), edits = new Map();
    for (const [id, list] of byAgent) {
        const iv = [];
        for (const s of list) {
            const a = ms(s.start), b = ms(s.end) || a;
            if (!a) continue;
            const last = iv[iv.length - 1];
            if (last && a - last[1] < 8 * 60000) last[1] = Math.max(last[1], b); else iv.push([a, b]);
        }
        busy.set(id, iv);
        edits.set(id, list.map(touchOf).filter(Boolean));
    }
    // Un agente sigue en obra mientras su llamada no ha vuelto; uno en segundo plano, mientras escribe.
    const callEnd = new Map();
    for (const s of steps) if (s.kind === 'agent' && s.childAgentId) callEnd.set(s.childAgentId, s.end ? ms(s.end) : null);
    const agents = (trace.agents || []).map(a => {
        const t0 = ms(a.startUtc), t1 = ms(a.endUtc) || t0;
        const call = callEnd.has(a.id) ? callEnd.get(a.id) : undefined;
        return { ...a, role: roleOf(a), t0, t1, until: a.background || call === undefined ? t1 : call ?? Infinity };
    });
    return { byAgent, busy, edits, asks, planAsks, agents };
}

/** El permiso que la sesión espera ahora, en directo: desde cuándo y la llamada que lo pide; null si no espera ninguno.
 *
 * Que pide permiso solo lo saben los hooks, así que en la repetición no se puede reconstruir. */
function permissionNow() {
    if (ob.moment.replaying || !ob.trace.live) return null;
    const s = (state.active || []).find(a => a.id === ob.id);
    if (!s || s.statusKey !== 'waiting' || s.status !== 'Permiso') return null;
    const open = (ob.trace.steps || []).filter(x => !x.agentId && x.kind === 'tool' && x.start && !x.end && !x.questions && toolOf(x) !== 'ExitPlanMode');
    const step = open[open.length - 1] || null;
    return { ts: ms(s.since) || (step ? ms(step.start) : Date.now()), step };
}

/** La tarea del panel de la sesión: la principal o, si no hay, la primera; null si no tiene ninguna. */
function panelTask() {
    const L = ob.trace.task && ob.trace.task.tasks || [];
    return L.find(k => k.principal) || L[0] || null;
}

// Nombres de fichero dentro del título de una orden: «python fix.py QuoteLines.vue» trabaja en QuoteLines.vue.
const FILE_IN = /[\w.-]+\.(?:vue|tsx?|jsx?|mjs|cs|cshtml|razor|css|scss|html|md|json|ya?ml|py|ps1|sql|xml|csproj|props|axaml)\b/gi;

/** El fichero en que trabaja un paso: el que edita o escribe, o el que nombra una orden; null si no toca ninguno. */
function touchOf(s) {
    if (s.kind !== 'tool') return null;
    const tool = toolOf(s), t = ms(s.start);
    if (/^(Edit|MultiEdit|Write|NotebookEdit)$/.test(tool)) return { t, file: fileOf(s), edit: true };
    if (/^(Bash|PowerShell)$/.test(tool)) {
        const files = (fileOf(s).match(FILE_IN) || []).filter(f => !/^(package|tsconfig|settings)\.json$/i.test(f));
        return files.length ? { t, file: files[files.length - 1], edit: false } : null;
    }
    return null;
}

/** Lo que más repite un agente en sus últimos minutos: «construye», «lee planos», «prueba»… */
function doing(id, t) {
    const list = ob.facts.byAgent.get(id) || [];
    const c = {};
    for (let i = list.length - 1; i >= 0; i--) {
        const s = list[i], at = ms(s.start);
        if (!at || at > t) continue;
        if (t - at > 6 * 60000) break;
        const w = s.kind === 'skill' ? 'consulta el manual' : WORD[toolOf(s)];
        if (w) c[w] = (c[w] || 0) + 1;
    }
    let best = '', n = 0;
    for (const k in c) if (c[k] > n) { n = c[k]; best = k; }
    return best;
}

/** El estado de la obra en un instante: tareas hechas y en obra, quién trabaja, qué te toca y en qué fase está. */
function stateAt(t) {
    const plan = plans.get(ob.id);
    const F = ob.facts;
    const tasks = (plan && plan.tasks || []).map((x, i) => {
        // Antes de apuntarse en el tasks.md, su solar no existe: así las rondas de triaje aparecen cuando se crearon.
        if (x.added && ms(x.added) > t) return { x, i, s: 'none', f: 0 };
        const at = x.marked ? ms(x.marked) : null;
        const done = x.done && (at === null || at <= t);
        return { x, i, s: done ? 'done' : 'pend', f: 0 };
    });
    const replaying = ob.moment.replaying;
    const live = F.agents.filter(a => a.t0 <= t && (!replaying && ob.trace.live && (a.busy || a.until === Infinity) ? true : t <= (a.until === Infinity ? a.t1 : a.until) + 15000));
    // En qué tarea está cada agente que construye: la pendiente que nombra el fichero que toca o, si no, la primera
    // sin hacer. Si ya no queda ninguna (retoques, arreglos fuera del plano), retoca el solar terminado de ese fichero
    // o, si el fichero no es de ninguna tarea, da los remates al último que se terminó.
    const working = new Map(), retouch = new Map();
    const has = (k, file) => (k.x.files || []).some(f => f.toLowerCase() === file.toLowerCase());
    for (const a of live.filter(x => BUILDS.has(x.role))) {
        const ed = (F.edits.get(a.id) || []).filter(e => e.t <= t && t - e.t < 15 * 60000);
        if (!ed.length) continue;
        const file = ed[ed.length - 1].file;
        const pend = tasks.filter(k => k.s === 'pend' && !k.x.human);
        const hit = pend.find(k => has(k, file)) || pend[0];
        if (hit) {
            const since = Math.max(...tasks.filter(k => k.s === 'done' && k.x.marked).map(k => ms(k.x.marked)).filter(v => v <= t), a.t0);
            const n = ed.filter(e => e.t >= since).length;
            hit.s = 'work'; hit.f = Math.max(hit.f, Math.min(0.92, 0.12 + 0.06 * n));
            working.set(a.id, { ...hit, file });
            if (!ob.started.has(hit.x.id)) ob.started.set(hit.x.id, t);
            continue;
        }
        const done = tasks.filter(k => k.s === 'done' && !k.x.human);
        const last = done.slice().sort((p, q) => (ms(q.x.marked) || 0) - (ms(p.x.marked) || 0))[0];
        const target = done.filter(k => has(k, file)).pop() || last;
        if (target) retouch.set(a.id, { x: target.x, file });
    }
    const ask = F.asks.find(q => q.ts <= t && (q.answered === null || t < q.answered));
    const planWait = F.planAsks.find(q => q.ts <= t && (q.answered === null || t < q.answered)) || null;
    const perm = ask || planWait ? null : permissionNow();
    const waitingYou = !replaying && ob.trace.live && !ob.trace.working && !ask && !planWait && !perm;
    const youPend = tasks.filter(k => k.x.human && k.s === 'pend');
    const hour = new Date(t).getHours();
    const night = hour >= 21 || hour < 7;
    const top = live.find(a => !a.parentAgentId);
    const roles = new Set(live.map(a => a.role));
    const w = tasks.find(k => k.s === 'work');
    const phase = ask ? 'Te toca decidir' : planWait ? 'Te toca aprobar el plan' : perm ? 'Te toca dar permiso' : roles.has('inspector') ? 'Inspección de lo construido' : roles.has('commit') ? 'Camión al almacén'
        : top && top.role === 'propose' ? (tasks.length ? 'Replanteo: marcando los solares' : 'Estudio del terreno') : top && top.role === 'archive' ? 'Entrega de la obra'
        : w ? (/triaje|ronda/i.test(w.x.section) ? 'Arreglos del ' + w.x.section.replace(/\s*\(.*\)$/, '') : 'Construyendo ' + secName(w.x.section))
        : retouch.size ? 'Remates: ' + [...retouch.values()][0].file
        : live.length ? 'En obra' : ob.trace.working ? 'El jefe de obra trabaja' : waitingYou ? 'Esperando tu mensaje' : night ? 'Obra parada: noche' : 'Obra parada';
    return { t, plan, tasks, live, working, retouch, ask, planWait, perm, waitingYou, youPend, night, phase, done: tasks.filter(k => k.s === 'done').length, total: tasks.filter(k => k.s !== 'none').length };
}
// El nombre del change sin la fecha que le pone el archivo («2026-11-12-devlab-183-…»).
const changeName = c => String(c || '').replace(/^\d{4}-\d{2}-\d{2}-/, '');
const secName = s =>String(s || '').replace(/^\d+\.\s*/, '').replace(/\s*\(.*\)$/, '').split(':')[0].trim();

// MONTAJE: el terreno, los edificios de servicio, la grúa y los controles

function mount(box, id) {
    const root = document.createElement('div');
    root.className = 'ob-root';
    root.innerHTML = `<div class="ob-tags"></div>
        <div class="ob-panel ob-hud"></div><div class="ob-panel ob-clock"></div><div class="ob-panel ob-parte"></div>
        <div class="ob-ficha" hidden></div>
        <div class="ob-pad">
            <div class="ob-seg" data-k="view"><button data-v="iso">Isométrica</button><button data-v="cine">Cine</button><button data-v="planta">Planta</button><button data-v="op">Junto al operario</button></div>
            <div class="ob-seg" data-k="theme"><button data-v="color">Color</button><button data-v="plano">Plano</button></div>
            <div class="ob-seg" data-k="style"><button data-v="robot">Robots</button><button data-v="figura">Figuras</button></div>
            <button data-act="rotl" title="Girar a la izquierda">⟲</button><button data-act="rotr" title="Girar a la derecha">⟳</button><button data-act="home">Centrar</button>
        </div>`;
    box.replaceChildren(root);
    const R = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    R.setPixelRatio(Math.min(2, devicePixelRatio || 1)); R.shadowMap.enabled = true; R.shadowMap.type = THREE.PCFSoftShadowMap;
    root.prepend(R.domElement);
    const S = new THREE.Scene();
    const o = {
        id, root, R, S, tagsBox: root.querySelector('.ob-tags'), tags: [], scen: [], lots: [], lotGroup: new THREE.Group(), actors: new Map(), slots: new Map(),
        started: new Map(), camO: new THREE.OrthographicCamera(-1, 1, 1, -1, 0.1, 400), camP: new THREE.PerspectiveCamera(38, 1, 0.1, 400),
        look: new THREE.Vector3(-1.5, 0, 1.8), yaw: Math.PI / 4, yawGoal: Math.PI / 4, pitch: 0.62, size: 13.5, dist: 38, view: 'iso', theme: 'color', style: 'robot',
        drag: null, sel: null, fichaT: 0, crane: { yaw: 0, reach: 10 }, planDirty: true, planFor: null, lastHud: '', lastClock: '', lastParte: '', stT: 0,
    };
    S.add(o.lotGroup);
    try { const p = JSON.parse(localStorage.getItem(PREFS) || '{}'); Object.assign(o, { view: p.view || o.view, theme: p.theme || o.theme, style: p.style || o.style }); } catch { /* sin almacén */ }
    ob = o;
    world(o);
    controls(o);
    setView(o.view, true); applyTheme(o.theme); markPrefs();
    return o;
}
const savePrefs = () => { try { localStorage.setItem(PREFS, JSON.stringify({ view: ob.view, theme: ob.theme, style: ob.style })); } catch { /* sin almacén */ } };
function markPrefs() { for (const seg of ob.root.querySelectorAll('.ob-seg')) for (const b of seg.children) b.setAttribute('aria-pressed', String(b.dataset.v === ob[seg.dataset.k])); }

// Piezas
const GC = {};
let REG = true;
const M = (c, o = {}) => new THREE.MeshStandardMaterial(Object.assign({ color: c, roughness: 0.78 }, o));
function rrect(w, d, r) { const s = new THREE.Shape(), x = -w / 2, y = -d / 2; r = Math.min(r, w / 2, d / 2); s.moveTo(x + r, y); s.lineTo(x + w - r, y); s.quadraticCurveTo(x + w, y, x + w, y + r); s.lineTo(x + w, y + d - r); s.quadraticCurveTo(x + w, y + d, x + w - r, y + d); s.lineTo(x + r, y + d); s.quadraticCurveTo(x, y + d, x, y + d - r); s.lineTo(x, y + r); s.quadraticCurveTo(x, y, x + r, y); return s; }
/** Caja con aristas redondeadas, centrada como una BoxGeometry. */
function rboxGeo(w, h, d, r = 0.08) {
    const k = [w, h, d, r].map(n => n.toFixed(3)).join(); if (GC[k]) return GC[k];
    const b = Math.min(r * 0.6, h / 4, w / 6, d / 6);
    const g = new THREE.ExtrudeGeometry(rrect(w - 2 * b, d - 2 * b, Math.max(0.005, r - b)), { depth: Math.max(0.001, h - 2 * b), bevelEnabled: true, bevelThickness: b, bevelSize: b, bevelSegments: 3, curveSegments: 6 });
    g.rotateX(-Math.PI / 2); g.translate(0, b - h / 2, 0); g.computeVertexNormals(); return GC[k] = g;
}
function gableGeo(w, rh, d) { const s = new THREE.Shape(); s.moveTo(-w / 2, 0); s.lineTo(w / 2, 0); s.lineTo(0, rh); s.lineTo(-w / 2, 0); const g = new THREE.ExtrudeGeometry(s, { depth: d, bevelEnabled: true, bevelThickness: 0.03, bevelSize: 0.03, bevelSegments: 2 }); g.translate(0, 0, -d / 2); return g; }
function archGeo(r, len) { const g = new THREE.CylinderGeometry(r, r, len, 20, 1, false, 0, Math.PI); g.rotateZ(Math.PI / 2); return g; }
/** Crea una pieza de la escena; si es decorado, queda apuntada para cambiar con el acabado. */
function mesh(geo, mat, x = 0, y = 0, z = 0, parent, keep = false) { const m = new THREE.Mesh(geo, mat); m.position.set(x, y, z); m.castShadow = true; m.receiveShadow = true; (parent || ob.S).add(m); if (REG) { m.userData.orig = mat; m.userData.keep = keep; ob.scen.push(m); } return m; }
const rbox = (w, h, d, mat, x, y, z, parent, r = 0.08, keep) => mesh(rboxGeo(w, h, d, r), mat, x, y, z, parent, keep);
function tag(cls, html, obj, dy = 0) { const e = document.createElement('div'); e.className = 'ob-tg ' + cls; e.innerHTML = html; ob.tagsBox.append(e); const t = { el: e, obj, dy, hidden: false }; ob.tags.push(t); return t; }
function at(x, y, z, parent) { const o = new THREE.Object3D(); o.position.set(x, y, z); (parent || ob.S).add(o); return o; }
function canvasTex(w, h, draw) { const c = document.createElement('canvas'); c.width = w; c.height = h; draw(c.getContext('2d'), w, h); const t = new THREE.CanvasTexture(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; return t; }
const stripes = (a, b, n, vertical) => canvasTex(128, 128, x => { x.fillStyle = a; x.fillRect(0, 0, 128, 128); x.fillStyle = b; const s = 128 / n; for (let i = 0; i < n; i++) vertical ? x.fillRect(i * s, 0, s * 0.42, 128) : x.fillRect(0, i * s, 128, s * 0.42); });
const noise = (base, dots, rx, ry) => { const t = canvasTex(256, 256, x => { x.fillStyle = base; x.fillRect(0, 0, 256, 256); for (let i = 0; i < 2600; i++) { x.fillStyle = dots[i % dots.length]; x.globalAlpha = 0.25 + Math.random() * 0.35; x.beginPath(); x.arc(Math.random() * 256, Math.random() * 256, Math.random() * 1.6 + 0.4, 0, 7); x.fill(); } }); t.repeat.set(rx, ry); return t; };
const sign = (txt, bg, fg) => canvasTex(512, 96, (x, w, h) => { x.fillStyle = bg; x.fillRect(0, 0, w, h); x.fillStyle = fg; x.font = '600 46px Consolas, monospace'; x.textAlign = 'center'; x.textBaseline = 'middle'; x.fillText(txt, w / 2, h / 2 + 2); });
const texM = (tex, rx, ry, o = {}) => { const t = tex.clone(); t.needsUpdate = true; t.repeat.set(rx, ry); return M(0xffffff, Object.assign({ map: t, roughness: 0.7 }, o)); };
function plane(w, h, mat, x, y, z, ry, parent) { const m = mesh(new THREE.PlaneGeometry(w, h), mat, x, y, z, parent, true); m.rotation.y = ry; m.castShadow = false; return m; }
function lines(pts, color, parent) { const b = new THREE.BufferGeometry(); b.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3)); const l = new THREE.LineSegments(b, new THREE.LineBasicMaterial({ color })); (parent || ob.S).add(l); return l; }

const FRONT = 11.2, AISLE = -18.2, GATE = 3;
const PL = { caseta: [-12.6, 9.4], planos: [-8, 9.3], almacen: [10.4, 11.0], archivo: [14.6, 9.6], promotor: [-8, 17.6], salida: [GATE, 21] };
const V3 = (p, y = 0) => new THREE.Vector3(p[0], y, p[1]);

/** Monta lo fijo de la obra: luces, terreno, caminos, valla, árboles, edificios de servicio y grúa. */
function world(o) {
    const S = o.S;
    o.hemi = new THREE.HemisphereLight(0xfff6e8, 0x2a2622, 0.7); S.add(o.hemi);
    o.sun = new THREE.DirectionalLight(0xfff1dc, 0.9); o.sun.position.set(12, 30, 24); o.sun.castShadow = true; o.sun.shadow.mapSize.set(2048, 2048);
    Object.assign(o.sun.shadow.camera, { left: -32, right: 32, top: 30, bottom: -30, near: 1, far: 110 }); o.sun.shadow.bias = -0.0005; S.add(o.sun);
    o.GLASS = M(0x223040, { roughness: 0.3, metalness: 0.2, emissive: 0xffc27a, emissiveIntensity: 0 });
    o.ground = mesh(new THREE.CircleGeometry(60, 64), M(0xffffff, { map: noise('#20271f', ['#2c3a29', '#18201a', '#33402d'], 12, 10), roughness: 1 }), 0, -0.02, -2); o.ground.rotation.x = -Math.PI / 2; o.ground.castShadow = false;
    const ROAD = M(0x3a3a3f, { roughness: 0.9 });
    const road = (x, z, w, d) => { const m = mesh(rboxGeo(w, 0.04, d, Math.min(w, d) / 2), ROAD, x, 0.05, z); m.castShadow = false; };
    road(0, FRONT, 40, 1.5); road(GATE, 15.6, 2.3, 7.5);
    for (let x = -19; x < 19; x += 1.6) { const m = mesh(new THREE.BoxGeometry(0.6, 0.01, 0.07), M(0xd9c27a, { roughness: 0.6 }), x, 0.08, FRONT); m.castShadow = false; }
    o.roadFor = road;
    // Árboles
    const LEAF = M(0x3f5a3a, { roughness: 0.9, flatShading: true }), TRUNK = M(0x5a4636);
    const tree = (x, z, s) => { mesh(new THREE.CylinderGeometry(0.08 * s, 0.12 * s, 0.9 * s, 7), TRUNK, x, 0.45 * s, z); mesh(new THREE.IcosahedronGeometry(0.62 * s, 0), LEAF, x, 1.25 * s, z); mesh(new THREE.IcosahedronGeometry(0.45 * s, 0), LEAF, x + 0.2 * s, 1.75 * s, z - 0.1 * s); };
    let seed = 7; const rnd = () => { seed = (seed * 9301 + 49297) % 233280; return seed / 233280; };
    for (let i = 0; i < 46; i++) { const a = rnd() * Math.PI * 2, r = 30 + rnd() * 14; const x = Math.cos(a) * r, z = Math.sin(a) * r * 0.75 - 2; if (Math.abs(x - GATE) < 3 && z > 12) continue; if (z > 12 && z < 19 && Math.abs(x + 8) < 4) continue; tree(x, z, 0.9 + rnd() * 0.8); }
    for (let i = 0; i < 10; i++) { const x = -20 + i * 4.2 + rnd(); if (Math.abs(x + 8) < 3.5 || Math.abs(x - GATE) < 2.5) continue; tree(x, 20 + rnd() * 3, 0.8 + rnd() * 0.6); }
    services(o);
    // Farolas
    o.lamps = [];
    for (const lx of [-15, -5, 5, 15]) { mesh(new THREE.CylinderGeometry(0.05, 0.07, 2.6, 8), M(0x55524c), lx, 1.3, FRONT + 1); const bulb = mesh(new THREE.SphereGeometry(0.16, 12, 8), new THREE.MeshStandardMaterial({ color: 0x333333, emissive: 0xffc27a, emissiveIntensity: 0 }), lx, 2.6, FRONT + 1, null, true); const pl = new THREE.PointLight(0xffc27a, 0, 9); pl.position.set(lx, 2.5, FRONT + 1); S.add(pl); o.lamps.push({ bulb, pl }); }
    o.quest = [tag('quest', '?', at(-13.6, 3.6, 8.0)), tag('quest', '?', at(-8, 2.6, 15.4))];
    o.sign = tag('sign', 'Obra', at(GATE - 3.2, 1.6, 12.9));
}

function services(o) {
    const S = o.S;
    const CORR_W = stripes('#e6e3dc', '#c9c4ba', 18, true), CORR_O = stripes('#E98A3F', '#c46a2a', 18, true), CORR_G = stripes('#46564a', '#38463c', 26, true), DOOR = stripes('#9a968f', '#77736c', 16, false), BARRIER = stripes('#e6e3dc', '#b83a3a', 8, true);
    const HAZARD = canvasTex(128, 16, x => { x.fillStyle = '#E5C07B'; x.fillRect(0, 0, 128, 16); x.fillStyle = '#1a1a1d'; for (let i = -16; i < 144; i += 16) { x.beginPath(); x.moveTo(i, 16); x.lineTo(i + 8, 16); x.lineTo(i + 16, 0); x.lineTo(i + 8, 0); x.fill(); } });
    const CURTAIN = canvasTex(128, 128, x => { x.fillStyle = '#1d2a3a'; x.fillRect(0, 0, 128, 128); const g = x.createLinearGradient(0, 0, 128, 128); g.addColorStop(0, 'rgba(170,205,240,.45)'); g.addColorStop(0.5, 'rgba(170,205,240,0)'); g.addColorStop(1, 'rgba(170,205,240,.25)'); x.fillStyle = g; x.fillRect(0, 0, 128, 128); x.strokeStyle = '#b8b3aa'; x.lineWidth = 4; for (let i = 0; i <= 128; i += 32) { x.beginPath(); x.moveTo(i, 0); x.lineTo(i, 128); x.stroke(); } x.beginPath(); x.moveTo(0, 64); x.lineTo(128, 64); x.stroke(); });
    const STEEL = M(0x55524c, { roughness: 0.5, metalness: 0.3 }), DARKM = M(0x2a2a2e), WHITEM = M(0xe6e3dc);
    const rail = (pts, parent) => lines(pts, 0xd9d5cc, parent);
    o.winMats = [];
    const pick = (g, place) => { g.userData.pick = { place, pos: g.position }; };
    const group = (x, z) => { const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = Math.PI; S.add(g); return g; };
    // Caseta
    { const g = group(-13.6, 8.0); pick(g, 'caseta');
        rbox(3.6, 1.3, 1.5, texM(CORR_W, 4, 1), 0, 0.68, 0, g, 0.05); rbox(3.6, 1.3, 1.5, texM(CORR_O, 4, 1), 0.3, 1.99, 0.05, g, 0.05, true);
        for (const wx of [-1.1, 0.1]) o.winMats.push(rbox(0.75, 0.5, 0.04, o.GLASS.clone(), wx, 0.8, -0.76, g, 0.04)); rbox(0.5, 1.0, 0.04, DARKM, 1.15, 0.55, -0.76, g, 0.03);
        for (const wx of [-0.8, 0.3, 1.4]) o.winMats.push(rbox(0.7, 0.45, 0.04, o.GLASS.clone(), wx, 2.1, -0.71, g, 0.04));
        rbox(3.9, 0.05, 0.55, STEEL, 0.3, 1.33, -1.0, g, 0.02);
        rail([-1.6, 1.36, -1.27, 2.2, 1.36, -1.27, -1.6, 1.85, -1.27, 2.2, 1.85, -1.27, ...[-1.6, -0.6, 0.4, 1.4, 2.2].flatMap(x => [x, 1.36, -1.27, x, 1.85, -1.27])], g);
        for (let i = 0; i < 6; i++) rbox(0.55, 0.05, 0.26, STEEL, 2.35, 0.22 * (i + 1), 0.55 - i * 0.26, g, 0.01);
        rbox(0.55, 0.32, 0.45, WHITEM, -1.1, 2.82, 0.2, g, 0.05);
        plane(1.7, 0.32, M(0xffffff, { map: sign('JEFE DE OBRA', '#1a1a1d', '#E98A3F') }), 0.3, 2.82, -0.72, Math.PI, g);
        tag('place', 'Caseta · jefe de obra', at(-13.6, 0, 9.25)); }
    // Mesa de planos
    { const g = group(-8, 8.2); pick(g, 'planos');
        rbox(1.9, 0.07, 1.2, DARKM, 0, 0.88, 0, g, 0.04); rbox(1.6, 0.02, 0.9, M(0xbcd0f0, { roughness: 0.5 }), 0, 0.93, 0, g, 0.01, true);
        for (const [lx, lz] of [[-0.8, -0.5], [0.8, -0.5], [-0.8, 0.5], [0.8, 0.5]]) mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.88, 8), STEEL, lx, 0.44, lz, g);
        for (const [px, pz] of [[-1.3, -0.9], [1.3, -0.9], [-1.3, 0.9], [1.3, 0.9]]) mesh(new THREE.CylinderGeometry(0.04, 0.04, 2.1, 8), STEEL, px, 1.05, pz, g);
        const roof = rbox(2.9, 0.07, 2.1, M(0x8E9BF5, { roughness: 0.6 }), 0, 2.1, 0, g, 0.04, true); roof.rotation.x = 0.1;
        tag('place', 'Mesa de planos', at(-8, 0, 9.4)); }
    // Almacén
    { const g = group(9.5, 7.9); pick(g, 'almacen');
        rbox(4.8, 2.3, 2.7, texM(CORR_G, 6, 1), 0, 1.15, 0, g, 0.06);
        mesh(archGeo(1.4, 5), M(0x2f6b3a, { roughness: 0.5, metalness: 0.2 }), 0, 2.3, 0, g); rbox(4.6, 0.05, 0.4, M(0xbcd0f0, { emissive: 0xbcd0f0, emissiveIntensity: 0.25, roughness: 0.2 }), 0, 3.69, 0, g, 0.02, true);
        rbox(2.0, 1.7, 0.06, texM(DOOR, 1, 1), -0.9, 1.35, -1.37, g, 0.02); rbox(0.55, 1.0, 0.05, DARKM, 1.5, 0.5, -1.37, g, 0.02);
        rbox(2.6, 0.5, 0.9, M(0x8a8478), -0.9, 0.25, -1.8, g, 0.04); plane(2.6, 0.14, M(0xffffff, { map: HAZARD }), -0.9, 0.43, -2.26, Math.PI, g);
        rbox(2.8, 0.06, 1.1, STEEL, -0.9, 2.25, -1.85, g, 0.02); for (const bx of [-2.2, 0.4]) mesh(new THREE.CylinderGeometry(0.03, 0.03, 2.25, 8), STEEL, bx, 1.12, -2.35, g);
        plane(2.0, 0.36, M(0xffffff, { map: sign('REGISTRO · COMMITS', '#1a1a1d', '#3FB950') }), 1.2, 1.85, -1.38, Math.PI, g);
        tag('place', 'Almacén · registro de commits', at(10.4, 0, 10.55)); }
    o.cargas = tag('sign', 'Cargas: 0', at(9.5, 4.3, 7.9));
    // Archivo
    { const g = group(14.6, 8.3); pick(g, 'archivo');
        rbox(2.5, 1.8, 1.6, M(0x3a4060), 0, 0.9, 0, g, 0.08); plane(2.3, 1.55, M(0xffffff, { map: CURTAIN, roughness: 0.25, metalness: 0.2, emissive: 0x1d2a3a, emissiveIntensity: 0.3 }), 0, 0.85, -0.81, Math.PI, g);
        rbox(3.0, 0.12, 2.1, M(0x8E9BF5, { roughness: 0.55 }), 0, 1.86, -0.15, g, 0.05, true);
        tag('place', 'Archivo', at(14.6, 0, 9.3)); }
    // Dirección de obra: la oficina de la persona
    { const g = group(-8, 15.4); pick(g, 'promotor');
        rbox(3.4, 0.14, 2.4, M(0x8a8478), 0, 0.07, 0, g, 0.08); rbox(2.9, 1.35, 1.9, M(0xe6dcc0), 0, 0.82, 0.05, g, 0.08);
        plane(2.7, 1.15, M(0xffffff, { map: CURTAIN, roughness: 0.25, metalness: 0.2, emissive: 0x1d2a3a, emissiveIntensity: 0.3 }), 0, 0.8, -0.91, Math.PI, g);
        rbox(3.9, 0.12, 2.8, M(0xE5C07B, { roughness: 0.55 }), 0, 1.56, -0.2, g, 0.05, true);
        tag('place', 'Dirección de obra', at(-8, 0, 17)); }
    // Entrada con barrera
    { const g = group(GATE + 2.1, 13.6); rbox(0.9, 1.25, 0.9, WHITEM, 0, 0.63, 0, g, 0.08); rbox(1.1, 0.08, 1.1, M(0x3FB950), 0, 1.3, 0, g, 0.04, true);
        o.barrier = new THREE.Group(); o.barrier.position.set(GATE + 1.6, 0.88, 12.9); o.barrier.rotation.y = Math.PI; S.add(o.barrier);
        mesh(new THREE.CylinderGeometry(0.035, 0.035, 2.9, 8).rotateZ(Math.PI / 2), texM(BARRIER, 1, 1), -1.45, 0, 0, o.barrier, true); }
    // Grúa
    o.craneG = new THREE.Group(); S.add(o.craneG);
    const CRANE = M(0xE5C07B, { roughness: 0.55 });
    rbox(1.8, 0.5, 1.8, M(0x55524c), 0, 0.25, 0, o.craneG, 0.1);
    { const lat = [], s = 0.28; for (const [x, z] of [[-s, -s], [s, -s], [s, s], [-s, s]]) lat.push(x, 0, z, x, 13, z); for (let y = 0; y < 13; y += 0.7) lat.push(-s, y, -s, s, y + 0.7, -s, s, y, s, -s, y + 0.7, s, -s, y, -s, s, y, -s, s, y, s, -s, y, s); lines(lat, 0xE5C07B, o.craneG); }
    for (const [x, z] of [[-0.28, -0.28], [0.28, -0.28], [0.28, 0.28], [-0.28, 0.28]]) mesh(new THREE.CylinderGeometry(0.04, 0.04, 13, 6), CRANE, x, 6.5, z, o.craneG, true);
    o.jib = new THREE.Group(); o.jib.position.y = 13; o.craneG.add(o.jib);
    for (const [y, z] of [[-0.2, -0.2], [-0.2, 0.2], [0.25, 0]]) mesh(new THREE.CylinderGeometry(0.05, 0.05, 24, 6).rotateZ(Math.PI / 2), CRANE, 7, y, z, o.jib, true);
    rbox(2.6, 0.9, 1, M(0x55524c), -4.3, -0.3, 0, o.jib, 0.12); rbox(1.1, 1, 1, M(0xe6e3dc), 0.1, -0.85, 0, o.jib, 0.18);
    o.trolley = rbox(0.5, 0.22, 0.5, M(0x55524c), 10, -0.35, 0, o.jib, 0.06);
    o.cable = new THREE.Line(new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3(0, -8, 0)]), new THREE.LineBasicMaterial({ color: 0x9A968F })); o.trolley.add(o.cable);
    o.hook = rbox(0.55, 0.35, 0.55, M(0xb5684a), 0, -8, 0, o.trolley, 0.06);
}

// SOLARES: uno por tarea del plano, en filas por sección

const SEC_COLS = [0xd9d5cc, 0xcfd8de, 0xe2d6c2, 0xa9c4dc, 0xd8cfc4, 0xe0d8cc];
const HGT = { tower: 2.8, gable: 2.0, dome: 1.5, round: 2.5, pavilion: 1.2, cabin: 1.0 };
function kindOf(sec, i, small, human) {
    if (human) return 'pavilion';
    if (small) return 'cabin';
    if (/front|web|vista|interfaz|ui\b/i.test(sec)) return 'round';
    if (/https|segur|local|red\b/i.test(sec)) return 'dome';
    if (/endpoint|refuerzo|hardening|config/i.test(sec)) return 'gable';
    if (/back|api|base|servidor|datos/i.test(sec)) return 'tower';
    return ['tower', 'gable', 'round', 'dome'][i % 4];
}
const scaffoldGeo = (w, h) => { const g = [], s = w / 2 + 0.14; for (const [x, z] of [[-s, -s], [s, -s], [s, s], [-s, s]]) g.push(x, 0, z, x, h, z); for (let y = 0.5; y <= h; y += 0.5) g.push(-s, y, -s, s, y, -s, s, y, -s, s, y, s, s, y, s, -s, y, s, -s, y, s, -s, y, -s); const b = new THREE.BufferGeometry(); b.setAttribute('position', new THREE.Float32BufferAttribute(g, 3)); return b; };

/** Un edificio por tipo: el cuerpo crece al construirse y el remate aparece al terminar. */
function building(kind, bw, H, col) {
    const g = new THREE.Group(), body = new THREE.Group(), top = new THREE.Group(); g.add(body, top); top.position.y = H;
    const mat = M(col, { roughness: 0.82 }), bands = [], ROOF = M(0x8a8478);
    if (kind === 'round') {
        mesh(new THREE.CylinderGeometry(bw / 2, bw / 2, H, 28), mat, 0, H / 2, 0, body);
        for (let y = 0.55; y < H - 0.3; y += 0.7) bands.push(mesh(new THREE.CylinderGeometry(bw / 2 + 0.01, bw / 2 + 0.01, 0.1, 28), ob.GLASS.clone(), 0, y, 0, body));
        mesh(new THREE.CylinderGeometry(bw / 2 + 0.08, bw / 2 + 0.08, 0.1, 28), ROOF, 0, 0.05, 0, top);
    } else {
        const bh = kind === 'dome' || kind === 'pavilion' || kind === 'cabin' ? H * 0.75 : H;
        rbox(bw, bh, bw, mat, 0, bh / 2, 0, body, 0.12);
        for (let y = 0.5; y < bh - 0.3; y += 0.72) bands.push(mesh(rboxGeo(bw + 0.02, 0.1, bw + 0.02, 0.1), ob.GLASS.clone(), 0, y, 0, body));
        if (kind === 'tower') { rbox(bw + 0.1, 0.14, bw + 0.1, ROOF, 0, 0.07, 0, top, 0.08); rbox(bw * 0.4, 0.3, bw * 0.3, M(0x6a6a70), bw * 0.15, 0.3, 0, top, 0.06); }
        if (kind === 'gable') mesh(gableGeo(bw + 0.2, bw * 0.38, bw + 0.2), M(0x9a5b3c, { roughness: 0.7 }), 0, 0, 0, top);
        if (kind === 'dome') { top.position.y = bh; mesh(new THREE.SphereGeometry(bw * 0.42, 24, 12, 0, Math.PI * 2, 0, Math.PI / 2), M(0xb9c9d8, { roughness: 0.4 }), 0, 0, 0, top); }
        if (kind === 'pavilion') { top.position.y = bh; mesh(gableGeo(bw + 0.5, bw * 0.3, bw + 0.3), M(0xE5C07B, { roughness: 0.6 }), 0, 0, 0, top, true); }
        if (kind === 'cabin') { top.position.y = bh; mesh(archGeo(bw * 0.52, bw + 0.15), M(0x7a8a9a, { roughness: 0.5 }), 0, 0, 0, top); }
    }
    mesh(new THREE.CylinderGeometry(0.025, 0.025, 0.8, 6), M(0xd9d5cc), bw / 2 - 0.05, 0.4, bw / 2 - 0.05, top);
    const flag = mesh(new THREE.PlaneGeometry(0.36, 0.22), M(0x3FB950, { emissive: 0x3FB950, emissiveIntensity: 0.4, side: THREE.DoubleSide }), bw / 2 + 0.13, 0.68, bw / 2 - 0.05, top, true);
    return { g, body, top, bands, flag };
}

/** Rehace los solares con el plano que llegó: filas de secciones, de atrás adelante, y la valla y la grúa a su medida. */
function buildPlan() {
    const o = ob, plan = plans.get(o.id);
    o.planDirty = false; o.planFor = o.id;
    const shape = plan && plan.tasks ? plan.tasks.map(k => k.section + '|' + k.id).join(';') : '';
    if (shape === o.planShape) return;
    o.planShape = shape;
    for (const l of o.lots) { l.label.el.remove(); l.label.dead = true; if (l.secTag) { l.secTag.el.remove(); l.secTag.dead = true; } }
    o.tags = o.tags.filter(t => !t.dead);
    o.scen = o.scen.filter(m => !isIn(m, o.lotGroup));
    o.S.remove(o.lotGroup); o.lotGroup = new THREE.Group(); o.S.add(o.lotGroup);
    o.lots = []; o.rows = [];
    if (o.board) { o.board.el.remove(); o.tags = o.tags.filter(t => t !== o.board); o.board = null; }
    o.sign.el.textContent = plan && plan.change ? 'Obra · ' + changeName(plan.change) : 'Obra';
    if (!plan || !plan.tasks || !plan.tasks.length) {
        o.board = tag('board', '<b>Aquí aún no hay obra</b>Esta sesión no sigue un change de openspec. Por ahora, la Obra solo construye el ciclo SDD: las demás sesiones tendrán su forma más adelante.', at(-1, 1.5, 0));
        fence(o, -8, 4);
        return;
    }
    // Secciones en filas que no pasan de 33 unidades
    const secs = []; for (const k of plan.tasks) { let s = secs.find(x => x.name === k.section); if (!s) secs.push(s = { name: k.section, tasks: [] }); s.tasks.push(k); }
    const rows = []; let row = null, w = 0;
    secs.forEach((s, i) => {
        s.small = /triaje|ronda|revisi|review|comprobaci/i.test(s.name) || s.tasks.length > 9;
        s.human = s.tasks.every(k => k.human);
        s.pitch = s.small ? 2.0 : 2.9; s.lot = s.small ? 1.6 : 2.4; s.i = i;
        const width = s.tasks.length * s.pitch + s.pitch * 0.35;
        if (!row || w + width > 33.5 || row.small !== s.small) { row = { secs: [], small: s.small }; rows.push(row); w = 0; }
        row.secs.push(s); w += width;
    });
    let z = 2.6;
    for (let r = rows.length - 1; r >= 0; r--) { rows[r].z = z; z -= rows[r].small ? 3.6 : 4.0; }
    const back = rows.length ? rows[0].z - (rows[0].small ? 0.8 : 1.2) : -8;
    o.rows = rows;
    for (const rw of rows) {
        let x = -16.4;
        const lot0 = rw.secs[0].lot; rw.lane = rw.z + lot0 / 2 + 0.75; rw.lot = lot0;
        const m = mesh(rboxGeo(36, 0.04, 0.95, 0.47), M(0x3a3a3f, { roughness: 0.9 }), -0.6, 0.05, rw.lane, o.lotGroup); m.castShadow = false;
        for (const s of rw.secs) {
            const col = s.human ? 0xeadfbf : SEC_COLS[s.i % SEC_COLS.length];
            const kind = kindOf(s.name, s.i, s.small, s.human), H = s.small ? 0.9 : HGT[kind];
            const secTag = tag('sec', escOf(secName(s.name) || s.name), at(x - s.lot / 2 - 0.2, 0, rw.z - s.lot / 2 - 0.35, o.lotGroup));
            secTag.el.style.setProperty('--sc', hexOf(s.human ? 0xE5C07B : s.small ? 0xF29BC4 : col));
            for (const k of s.tasks) {
                const g = new THREE.Group(); g.position.set(x, 0, rw.z); o.lotGroup.add(g);
                const pad = rbox(s.lot, 0.1, s.lot, M(0x4a453d, { roughness: 1 }), 0, 0.06, 0, g, 0.18); pad.castShadow = false;
                const stakes = new THREE.Group(); g.add(stakes); const e = s.lot / 2 - 0.12;
                for (const [sx, sz] of [[-e, -e], [e, -e], [e, e], [-e, e]]) mesh(new THREE.CylinderGeometry(0.035, 0.035, 0.45, 6), M(0xc8a46e), sx, 0.22, sz, stakes);
                lines([-e, 0.34, -e, e, 0.34, -e, e, 0.34, -e, e, 0.34, e, e, 0.34, e, -e, 0.34, e, -e, 0.34, e, -e, 0.34, -e], k.human ? 0xE5C07B : 0xE98A3F, stakes);
                const bw = s.lot * 0.7, B = building(kind, bw, H, col); B.g.position.y = 0.1; g.add(B.g); B.body.scale.y = 0.001; B.top.visible = false;
                const works = new THREE.Group(); g.add(works); works.visible = false;
                const CONE = M(0xE98A3F, { roughness: 0.5 });
                mesh(new THREE.ConeGeometry(0.09, 0.28, 12), CONE, -e - 0.1, 0.14, e + 0.15, works, true); mesh(new THREE.ConeGeometry(0.09, 0.28, 12), CONE, e + 0.1, 0.14, e + 0.15, works, true);
                const scaff = new THREE.LineSegments(scaffoldGeo(bw, H + 0.4), new THREE.LineBasicMaterial({ color: 0xE98A3F })); scaff.position.y = 0.1; g.add(scaff); scaff.visible = false;
                const label = tag('lot', escOf(k.id), at(x, 0, rw.z + s.lot / 2, o.lotGroup));
                const lot = { k, g, stakes, B, works, scaff, H, x, z: rw.z, lot: s.lot, label, secTag, lane: rw.lane, grow: 0, pop: 1, st: '' };
                g.userData.pick = { lot };
                o.lots.push(lot);
                x += s.pitch;
            }
            x += s.pitch * 0.35;
        }
    }
    // Pasillo lateral que une las filas con la carretera de servicio
    const len = FRONT - back; const ai = mesh(rboxGeo(1.15, 0.04, len, 0.5), M(0x3a3a3f, { roughness: 0.9 }), AISLE, 0.05, back + len / 2, o.lotGroup); ai.castShadow = false;
    fence(o, back - 2.0, rows.length);
}
const isIn = (m, g) => { for (let p = m.parent; p; p = p.parent) if (p === g) return true; return false; };

/** La valla, el terreno de la obra y la grúa, a la medida de las filas. */
function fence(o, backZ, nRows) {
    if (o.fenceG) { o.S.remove(o.fenceG); o.scen = o.scen.filter(m => !isIn(m, o.fenceG)); }
    o.fenceG = new THREE.Group(); o.S.add(o.fenceG);
    const depth = 12.8 - backZ, cz = (12.8 + backZ) / 2;
    const site = mesh(rboxGeo(40, 0.06, depth, 1.2), M(0xffffff, { map: noise('#3a332b', ['#4a4136', '#2c2620', '#55493b'], 8, Math.max(3, depth / 5)), roughness: 1 }), 0, 0.01, cz, o.fenceG); site.castShadow = false;
    const post = M(0x8a8478), panel = new THREE.MeshStandardMaterial({ color: 0xd9c27a, transparent: true, opacity: 0.35, roughness: 0.6, side: THREE.DoubleSide });
    const run = (x0, z0, x1, z1) => { const n = Math.max(1, Math.round(Math.hypot(x1 - x0, z1 - z0) / 2.2)); for (let i = 0; i <= n; i++) { const x = x0 + (x1 - x0) * i / n, z = z0 + (z1 - z0) * i / n; mesh(new THREE.CylinderGeometry(0.05, 0.05, 1.1, 8), post, x, 0.55, z, o.fenceG); if (i < n) { REG = false; const p = mesh(new THREE.PlaneGeometry(Math.hypot(x1 - x0, z1 - z0) / n - 0.1, 0.9), panel, (x + x0 + (x1 - x0) * (i + 1) / n) / 2, 0.55, (z + z0 + (z1 - z0) * (i + 1) / n) / 2, o.fenceG); p.rotation.y = -Math.atan2(z1 - z0, x1 - x0); p.castShadow = false; REG = true; } } };
    run(-19.6, backZ, 19.6, backZ); run(-19.6, backZ, -19.6, 12.8); run(19.6, backZ, 19.6, 12.8); run(-19.6, 12.8, GATE - 1.5, 12.8); run(GATE + 1.5, 12.8, 19.6, 12.8);
    o.craneG.position.set(0, 0, backZ - 1.2);
    o.siteBack = backZ;
    if (o.theme !== 'color') applyTheme(o.theme);
}

// PERSONAS Y ROBOTS

function makeChar(color, style, kind) {
    REG = false;
    const root = new THREE.Group(), body = new THREE.Group(); root.add(body);
    const skin = M(0xe6c9ad, { roughness: 0.7 }), cloth = M(color, { roughness: 0.6 }), dark = M(0x2a2a2e, { roughness: 0.8 });
    const hatCol = kind === 'boss' || kind === 'you' ? 0xffffff : 0xE5C07B;
    const head = new THREE.Group(); head.position.y = 1.02; body.add(head);
    if (style === 'robot') {
        rbox(0.44, 0.5, 0.3, cloth, 0, 0.62, 0, body, 0.1);
        rbox(0.34, 0.28, 0.3, M(0xd9d5cc), 0, 0, 0, head, 0.08); rbox(0.26, 0.07, 0.02, new THREE.MeshStandardMaterial({ color, emissive: color, emissiveIntensity: 0.9 }), 0, 0.02, 0.155, head, 0.02);
    } else {
        mesh(new THREE.CylinderGeometry(0.15, 0.19, 0.44, 18), cloth, 0, 0.62, 0, body); mesh(new THREE.SphereGeometry(0.15, 18, 10, 0, Math.PI * 2, 0, Math.PI / 2), cloth, 0, 0.84, 0, body);
        mesh(new THREE.SphereGeometry(0.135, 20, 14), skin, 0, 0, 0, head);
        for (const ex of [-0.045, 0.045]) mesh(new THREE.SphereGeometry(0.014, 6, 4), dark, ex, 0.01, 0.125, head);
    }
    if (kind !== 'you') { mesh(new THREE.CylinderGeometry(0.165, 0.195, 0.2, 18), M(0xE98A3F, { roughness: 0.5 }), 0, 0.66, 0, body); mesh(new THREE.CylinderGeometry(0.168, 0.188, 0.035, 18), M(0xdcdcdc, { emissive: 0xffffff, emissiveIntensity: 0.25 }), 0, 0.66, 0, body); }
    const hy = style === 'robot' ? 0.15 : 0.06;
    mesh(new THREE.SphereGeometry(0.15, 18, 10, 0, Math.PI * 2, 0, Math.PI / 2), M(hatCol, { roughness: 0.35 }), 0, hy, 0, head);
    mesh(new THREE.CylinderGeometry(0.19, 0.19, 0.02, 20), M(hatCol, { roughness: 0.35 }), 0, hy, 0.03, head);
    const limb = (r, h, x, y, mat, foot) => { const p = new THREE.Group(); p.position.set(x, y, 0); mesh(new THREE.CylinderGeometry(r, r * 0.9, h, 10), mat, 0, -h / 2, 0, p); mesh(new THREE.SphereGeometry(r * 1.1, 10, 8), foot ? dark : (style === 'robot' ? M(0xd9d5cc) : skin), 0, -h, foot ? 0.04 : 0, p); body.add(p); return p; };
    const armL = limb(0.045, 0.34, -0.22, 0.82, cloth), armR = limb(0.045, 0.34, 0.22, 0.82, cloth);
    const legL = limb(0.06, 0.36, -0.09, 0.4, dark, true), legR = limb(0.06, 0.36, 0.09, 0.4, dark, true);
    const hR = new THREE.Object3D(); hR.position.y = -0.38; armR.add(hR); const hL = new THREE.Object3D(); hL.position.y = -0.38; armL.add(hL);
    const P = {};
    P.hammer = new THREE.Group(); mesh(new THREE.CylinderGeometry(0.015, 0.015, 0.26, 6), M(0x8a6a44), 0, -0.1, 0, P.hammer); rbox(0.13, 0.055, 0.055, M(0x77736c), 0, -0.22, 0, P.hammer, 0.015); hR.add(P.hammer);
    P.plans = rbox(0.42, 0.01, 0.3, M(0xbcd0f0), 0, 0.82, 0.3, body, 0.004); P.plans.rotation.x = 0.9;
    P.clip = new THREE.Group(); rbox(0.2, 0.26, 0.02, M(0x8a6a44), 0, 0, 0, P.clip, 0.01); P.clip.position.set(0, -0.06, 0.08); hL.add(P.clip);
    P.laptop = new THREE.Group(); rbox(0.36, 0.02, 0.24, M(0x2a2a2e), 0, 0, 0, P.laptop, 0.01); const scr = rbox(0.36, 0.22, 0.015, new THREE.MeshStandardMaterial({ color: 0x0c120e, emissive: 0x3FB950, emissiveIntensity: 0.6 }), 0, 0.11, -0.12, P.laptop, 0.01); scr.rotation.x = -0.2; P.laptop.position.set(0, 0.6, 0.32); body.add(P.laptop);
    P.watch = mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.015, 10), M(0xE6E3DC, { emissive: 0xE6E3DC, emissiveIntensity: 0.3 }), 0, -0.3, 0.05, hL);
    for (const k in P) P[k].visible = false;
    REG = true;
    return { root, body, head, armL, armR, legL, legR, P };
}
function makeTripod() { REG = false; const g = new THREE.Group(); for (const a of [0, 2.1, 4.2]) { const l = mesh(new THREE.CylinderGeometry(0.015, 0.02, 0.92, 6), M(0xc8a46e), Math.cos(a) * 0.14, 0.42, Math.sin(a) * 0.14, g); l.rotation.set(Math.sin(a) * 0.3, 0, -Math.cos(a) * 0.3); } rbox(0.16, 0.12, 0.22, M(0xE5C07B), 0, 0.92, 0, g, 0.04); REG = true; return g; }
function makeTruck() { REG = false; const g = new THREE.Group(); rbox(0.95, 0.75, 0.86, M(0x3FB950, { roughness: 0.45 }), 0.78, 0.6, 0, g, 0.18); rbox(0.04, 0.32, 0.66, new THREE.MeshStandardMaterial({ color: 0x223040, roughness: 0.2 }), 1.25, 0.75, 0, g, 0.02); rbox(1.6, 0.95, 0.95, M(0xe6e3dc), -0.45, 0.7, 0, g, 0.12); for (const [x, z] of [[0.8, 0.45], [0.8, -0.45], [-0.8, 0.45], [-0.8, -0.45]]) { const w = mesh(new THREE.CylinderGeometry(0.19, 0.19, 0.13, 16), M(0x1a1a1d), x, 0.19, z, g); w.rotation.x = Math.PI / 2; } REG = true; return g; }

/** El actor de un agente, de Claude, de la persona o de un camión, creado la primera vez que hace falta. */
function actor(id, info) {
    if (ob.actors.has(id)) return ob.actors.get(id);
    const kind = info.kind;
    const a = { id, kind, color: info.color, name: info.name, role: info.role, pos: new THREE.Vector3(), path: [], goal: '', sc: 0, face: 0, on: false };
    a.c = kind === 'truck' ? { root: makeTruck() } : makeChar(info.color, id === 'tu' ? 'figura' : ob.style, kind);
    a.scale = kind === 'truck' ? 1.15 : 1.7;
    a.c.root.scale.setScalar(0.001); ob.S.add(a.c.root); a.c.root.userData.pick = { actor: a };
    if (info.role === 'analysis') { a.tripod = makeTripod(); ob.S.add(a.tripod); }
    a.label = tag('who', `<b style="color:${hexOf(info.color)}">${escOf(info.name)}</b><span></span>`, kind === 'truck' ? a.c.root : a.c.head, kind === 'truck' ? 1.6 : 0.42);
    ob.actors.set(id, a);
    return a;
}
function restyle(style) {
    ob.style = style;
    for (const a of ob.actors.values()) {
        if (a.kind === 'truck' || a.id === 'tu') continue;
        ob.S.remove(a.c.root); a.c = makeChar(a.color, style, a.kind); ob.S.add(a.c.root); a.c.root.userData.pick = { actor: a }; a.c.root.position.copy(a.pos); a.label.obj = a.c.head;
    }
}
const say = (a, t) => { const s = a.label.el.querySelector('span'); if (s.textContent !== t) s.textContent = t; };
/** El pasillo por el que sale un punto: el de su fila si está en un solar o delante de él, la carretera de servicio si está
 * fuera de las filas, o el pasillo más cercano. */
function laneOf(p) {
    for (const rw of ob.rows || []) if (p.z >= rw.z - rw.lot / 2 - 0.5 && p.z <= rw.lane + 0.55) return rw.lane;
    if (p.z > 5.5 || !(ob.rows || []).length) return FRONT;
    return ob.rows.reduce((best, rw) => Math.abs(rw.lane - p.z) < Math.abs(best - p.z) ? rw.lane : best, FRONT);
}
function route(a, b) { const la = laneOf(a) ?? FRONT, lb = laneOf(b) ?? FRONT, V = (x, z) => new THREE.Vector3(x, 0, z); if (a.distanceTo(b) < 0.2) return [b.clone()]; if (la === lb) return [V(a.x, la), V(b.x, lb), b.clone()]; return [V(a.x, la), V(AISLE, la), V(AISLE, lb), V(b.x, lb), b.clone()]; }
function slot(id) { if (!ob.slots.has(id)) { const k = ob.slots.size; ob.slots.set(id, [Math.cos(k * 2.4) * 0.55, Math.sin(k * 2.4) * 0.35 + 0.15]); } return ob.slots.get(id); }
const atLot = (l, id) => new THREE.Vector3(l.x + slot(id)[0] * l.lot / 2, 0, l.z + l.lot / 2 + 0.38);

function pose(c, act, time, walking) {
    if (!c.body) return;
    const { body, head, armL, armR, legL, legR, P } = c;
    for (const k in P) P[k].visible = false;
    armL.rotation.set(0, 0, 0.1); armR.rotation.set(0, 0, -0.1); legL.rotation.set(0, 0, 0); legR.rotation.set(0, 0, 0); head.rotation.set(0, 0, 0); body.rotation.set(0, 0, 0); body.position.y = 0;
    if (walking) { const s = Math.sin(time * 7); legL.rotation.x = s * 0.5; legR.rotation.x = -s * 0.5; armL.rotation.x = -s * 0.4; armR.rotation.x = s * 0.4; body.position.y = Math.abs(Math.cos(time * 7)) * 0.03; return; }
    body.position.y = Math.sin(time * 1.6) * 0.006;
    if (act === 'construye') { P.hammer.visible = true; armR.rotation.x = -1.6 + Math.max(0, Math.sin(time * 3.2)) * 1.0; body.rotation.x = 0.15; head.rotation.x = 0.3; }
    else if (act === 'lee planos' || act === 'planos' || act === 'consulta el manual') { P.plans.visible = true; armL.rotation.x = -1.1; armR.rotation.x = -1.1; armL.rotation.z = 0.35; armR.rotation.z = -0.35; head.rotation.x = 0.35; head.rotation.y = Math.sin(time * 0.7) * 0.2; }
    else if (act === 'busca') { armR.rotation.x = -1.5; armR.rotation.z = -0.3; head.rotation.y = Math.sin(time * 0.9) * 0.6; }
    else if (act === 'prueba' || act === 'portátil') { P.laptop.visible = true; armL.rotation.x = -1.0 + Math.sin(time * 9) * 0.05; armR.rotation.x = -1.0 + Math.cos(time * 9) * 0.05; head.rotation.x = 0.3; }
    else if (act === 'inspecciona') { P.clip.visible = true; armL.rotation.x = -1.1; armR.rotation.x = -0.6 + Math.sin(time * 2) * 0.15; head.rotation.x = 0.25; head.rotation.y = Math.sin(time * 0.6) * 0.4; }
    else if (act === 'mide') { body.rotation.x = 0.2; armL.rotation.x = -1.3; armR.rotation.x = -1.3; }
    else if (act === 'pregunta') { armR.rotation.x = -2.8 + Math.sin(time * 2.4) * 0.12; head.rotation.y = 0.4; }
    else if (act === 'piensa') { armR.rotation.x = -2.1; armR.rotation.z = 0.6; head.rotation.z = Math.sin(time * 1.1) * 0.1; }
    else if (act === 'duerme') { head.rotation.x = 0.6; body.rotation.x = 0.15; }
    else if (act === 'espera') { P.watch.visible = true; armL.rotation.x = -1.5 - Math.max(0, Math.sin(time * 0.6)) * 0.3; head.rotation.x = 0.4; }
}

// ACABADO Y VISTAS

const VIEWS = { iso: { ortho: true, yaw: Math.PI / 4, pitch: 0.62, size: 13.5 }, planta: { ortho: true, yaw: 0, pitch: 1.52, size: 15.5 }, cine: { ortho: false, yaw: 0.4, pitch: 0.36, dist: 38 }, op: { ortho: false, yaw: Math.PI / 4, pitch: 0.42, dist: 8.5 } };
function setView(v, quiet) {
    const o = ob; o.view = VIEWS[v] ? v : 'iso'; const V = VIEWS[o.view];
    // El encuadre se abre más en un hueco estrecho, para que quepa el ancho de la obra.
    const asp = o.root.clientWidth / Math.max(1, o.root.clientHeight) || 1.6, wide = clamp(1.5 / asp, 1, 2.4);
    o.yawGoal = V.yaw; if (o.view === 'cine') o.yaw = V.yaw; o.size = V.size ? V.size * wide : o.size; o.dist = V.dist ? V.dist * (o.view === 'cine' ? wide : 1) : o.dist;
    if (o.view !== 'op') o.look.set(-1.5, 0, o.view === 'planta' ? 0.5 : 1.8);
    o.root.className = o.root.className.replace(/\bv-\w+/g, '').trim() + ' v-' + o.view;
    if (!quiet) { savePrefs(); markPrefs(); }
}
function applyTheme(th) {
    const o = ob; o.theme = th === 'plano' ? 'plano' : 'color';
    o.root.classList.toggle('t-plano', o.theme === 'plano');
    o.BLUE = o.BLUE || new THREE.MeshStandardMaterial({ color: 0x15304f, roughness: 0.9, emissive: 0x0b2240, emissiveIntensity: 0.6 });
    o.EDGE = o.EDGE || new THREE.LineBasicMaterial({ color: 0x9cc4f0, transparent: true, opacity: 0.55 });
    for (const m of o.scen) {
        if (!m.userData.orig) continue;
        m.material = o.theme === 'color' ? m.userData.orig : o.BLUE;
        if (o.theme === 'plano' && !m.userData.edges && m !== o.ground && m.geometry.attributes.position.count < 4000) { m.userData.edges = new THREE.LineSegments(new THREE.EdgesGeometry(m.geometry, 35), o.EDGE); m.add(m.userData.edges); }
        if (m.userData.edges) m.userData.edges.visible = o.theme === 'plano';
    }
}

// CONTROLES Y FICHAS

function controls(o) {
    const root = o.root, ray = new THREE.Raycaster(), mouse = new THREE.Vector2();
    o.pickAt = e => {
        const r = o.R.domElement.getBoundingClientRect(); mouse.set(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1);
        ray.setFromCamera(mouse, o.cam || o.camO);
        for (const h of ray.intersectObjects(o.S.children, true)) { let x = h.object, vis = true; while (x && !x.userData.pick) { if (x.visible === false) vis = false; x = x.parent; } if (x && vis && x.visible !== false) return x.userData.pick; }
        return null;
    };
    for (const p of root.querySelectorAll('.ob-pad, .ob-ficha')) p.addEventListener('pointerdown', e => e.stopPropagation());
    root.addEventListener('pointerdown', e => { o.down = [e.clientX, e.clientY]; o.drag = { x: e.clientX, y: e.clientY, look: o.look.clone(), yaw: o.yawGoal }; root.setPointerCapture(e.pointerId); root.classList.add('grabbing'); });
    root.addEventListener('pointermove', e => {
        if (!o.drag) { const now = performance.now(); if (now - (o.hoverT || 0) > 90) { o.hoverT = now; root.style.cursor = o.pickAt(e) ? 'pointer' : ''; } return; }
        if (VIEWS[o.view].ortho) { const k = o.size * 2 / root.clientHeight; const dx = (e.clientX - o.drag.x) * k, dy = (e.clientY - o.drag.y) * k / Math.max(0.3, Math.sin(VIEWS[o.view].pitch)); const c = Math.cos(o.yaw), s = Math.sin(o.yaw); o.look.set(o.drag.look.x - dx * c - dy * s, 0, o.drag.look.z + dx * s - dy * c); }
        else { o.yawGoal = o.drag.yaw - (e.clientX - o.drag.x) * 0.006; o.yaw = o.yawGoal; }
    });
    const end = e => {
        root.classList.remove('grabbing'); o.drag = null;
        if (e && o.down && Math.hypot(e.clientX - o.down[0], e.clientY - o.down[1]) < 5) { o.sel = o.pickAt(e); o.fichaT = 0; if (!o.sel) { root.querySelector('.ob-ficha').hidden = true; if (o.ring) o.ring.visible = false; } }
        o.down = null;
    };
    root.addEventListener('pointerup', end); root.addEventListener('pointercancel', () => end(null));
    root.addEventListener('wheel', e => { e.preventDefault(); if (VIEWS[o.view].ortho) o.size = clamp(o.size * (1 + e.deltaY * 0.001), 4, 30); else o.dist = clamp(o.dist * (1 + e.deltaY * 0.001), 4, 70); }, { passive: false });
    root.querySelector('.ob-pad').addEventListener('click', e => {
        const b = e.target.closest('button'); if (!b) return;
        const seg = b.parentElement.dataset.k;
        if (seg === 'view') { o.follow = null; setView(b.dataset.v); }
        else if (seg === 'theme') { applyTheme(b.dataset.v); savePrefs(); markPrefs(); }
        else if (seg === 'style') { restyle(b.dataset.v); savePrefs(); markPrefs(); }
        else if (b.dataset.act === 'rotl') o.yawGoal -= Math.PI / 2;
        else if (b.dataset.act === 'rotr') o.yawGoal += Math.PI / 2;
        else if (b.dataset.act === 'home') setView(o.view, true);
    });
    const ficha = root.querySelector('.ob-ficha');
    ficha.addEventListener('click', e => {
        const b = e.target.closest('button'); if (!b) return;
        if (b.dataset.act === 'close') { o.sel = null; ficha.hidden = true; if (o.ring) o.ring.visible = false; }
        if (b.dataset.act === 'follow' && o.sel && o.sel.actor) { o.follow = o.sel.actor; setView('op'); }
        if (b.dataset.go) window.setView(b.dataset.go);
        if (b.dataset.act === 'chat') openChat(Number(b.dataset.order));
        if (b.dataset.act === 'tasks') { const p = plans.get(o.id); if (p && p.tasksPath) repoOpenAt(p.tasksPath, Number(b.dataset.line) || undefined); }
        if (b.dataset.act === 'branch' || b.dataset.act === 'repo') openChanges(b.dataset.act);
        if (b.dataset.act === 'board' && b.dataset.key) post({ type: 'task', key: b.dataset.key });
    });
    o.ring = new THREE.Mesh(new THREE.RingGeometry(0.85, 1, 48), new THREE.MeshBasicMaterial({ color: 0xE6E3DC, transparent: true, opacity: 0.85, side: THREE.DoubleSide, depthWrite: false }));
    o.ring.rotation.x = -Math.PI / 2; o.ring.visible = false; o.S.add(o.ring);
    new IntersectionObserver(es => { o.visible = es[0].isIntersecting; }).observe(root);
    o.visible = true;
}

const rowsH = rs => `<dl>${rs.filter(Boolean).map(([k, v]) => `<dt>${k}</dt><dd>${v}</dd>`).join('')}</dl>`;
const headH = (t, s, c) => `<div class="fh"><i style="background:${hexOf(c)}"></i><div><b>${escOf(t)}</b><span>${escOf(s)}</span></div><button type="button" data-act="close" aria-label="Cerrar">×</button></div>`;
const goB = (label, view) => `<button type="button" data-go="${view}">${escOf(label)}</button>`;
const actB = (label, act, data = '') => `<button type="button" data-act="${act}"${data}>${escOf(label)}</button>`;
const chatB = s => s ? actB('Ver en el chat', 'chat', ` data-order="${s.order}"`) : '';
const tasksB = line => plans.get(ob.id) && plans.get(ob.id).tasksPath ? actB('Abrir en el tasks.md', 'tasks', ` data-line="${line || ''}"`) : '';
const boardB = task => task ? actB('Ver en el Tablero', 'board', ` data-key="${escOf(task.key)}"`) : '';
const taskRow = task => task ? ['Panel', escOf(task.line || task.key)] : null;

/** El último paso de la sesión que empezó antes de un instante, para llevar el chat hasta él. */
function stepNear(ts) {
    let best = null;
    for (const s of ob.trace.steps || []) { const a = ms(s.start); if (a && a <= ts) best = s; }
    return best;
}

/** Abre el chat de la sesión en un paso; en una pieza del corcho, que no tiene chat, lo abre al lado. */
function openChat(order) {
    if (DIAGRAM_PIECE) { post({ type: 'pieceOpen', piece: 'chat', title: `Chat · ${sessionName(state.trace)}`, where: 'beside' }); return; }
    setChatOpen(true, true);
    if (!Number.isFinite(order)) return;
    if (state.replay.index === null) { focusStep(order, 'diagram'); return; }
    // Repitiendo, señalar el paso movería la repetición: solo se lleva el chat hasta él.
    const node = $('chat').querySelector(`[data-order="${order}"]`);
    if (node) node.scrollIntoView({ block: 'center' });
}

/** Abre los cambios de toda la rama («branch») o el explorador del repositorio («repo»); en una pieza del corcho, al lado. */
function openChanges(kind) {
    if (DIAGRAM_PIECE) { post({ type: 'pieceOpen', piece: 'changes', title: `Cambios · ${sessionName(state.trace)}`, where: 'beside' }); return; }
    setChangesOpen(true, true);
    setChangesView(kind === 'repo' ? 'repo' : 'changes');
    if (kind === 'branch') setChangesScope('branch');
}

/** Lo que la sesión espera de ti en el instante que se ve: la pregunta, el plan o el permiso, con sus opciones. */
function needH(st) {
    const opts = (L, note = 'Se contesta en la terminal de la sesión.') => `<ul>${L.map(x => `<li>${x}</li>`).join('')}</ul><p class="fn">${note}</p>`;
    if (st.ask) {
        const q = st.ask.step.questions[0];
        return `<h4>Te pregunta</h4><p class="ft">${escOf(q.question)}</p>` + opts((q.options || []).map(op => `<b>${escOf(op.label)}</b>${op.description ? ' · ' + escOf(op.description) : ''}`));
    }
    if (st.planWait) {
        const s = st.planWait.step;
        if (s.hasDetail && !state.details.has(s.order)) askCall(s);
        const input = callInput(s);
        const heads = input && input.plan ? input.plan.split('\n').filter(l => /^#{1,3}\s/.test(l)).slice(0, 6).map(l => l.replace(/^#+\s*/, '')) : [];
        // Claude Code guarda el plan en ~/.claude/plans y la llamada puede llegar sin él: entonces solo se lee en la terminal.
        const body = heads.length ? `<ul>${heads.map(h => `<li>${escOf(h)}</li>`).join('')}</ul>`
            : s.hasDetail && !state.details.has(s.order) ? '<p class="fn">Leyendo el plan…</p>' : '';
        return '<h4>Te pide aprobar el plan</h4>' + body + opts(['<b>Sí</b> · y acepta las ediciones solo', '<b>Sí</b> · y apruebas cada edición', '<b>No</b> · sigue planificando'],
            heads.length ? undefined : 'El plan se lee y se contesta en la terminal de la sesión.');
    }
    if (st.perm) {
        return `<h4>Te pide permiso</h4><p class="ft">${escOf(st.perm.step ? st.perm.step.title : 'Para usar una herramienta')}</p>` + opts(['<b>Sí</b>', '<b>Sí</b> · y no vuelve a preguntar', '<b>No</b> · y le dices qué hacer']);
    }
    return '';
}

/** Lo que te toca, en corto, para la fila de las fichas. */
function needText(st) {
    const t = st.t;
    if (st.ask) return `<em>${escOf(st.ask.header)} · hace ${durOf(t - st.ask.ts)}</em>`;
    if (st.planWait) return `<em>aprobar el plan · hace ${durOf(t - st.planWait.ts)}</em>`;
    if (st.perm) return `<em>dar permiso · hace ${durOf(t - st.perm.ts)}</em>`;
    return st.waitingYou ? '<em>escribir tu siguiente mensaje</em>' : 'nada ahora';
}

function asksH(t) {
    const L = ob.facts.asks.filter(q => q.ts <= t);
    return L.length ? `<ul>${L.map(q => `<li><b>${escOf(q.header)}</b> · ${hm(q.ts)} · ${q.answered === null || t < q.answered ? '<em>esperando tu respuesta</em>' : 'contestaste en ' + durOf(q.answered - q.ts)}</li>`).join('')}</ul>` : '<p class="fn">Aún no te ha preguntado nada.</p>';
}
/** La ficha de lo que se ha pulsado, con el estado del instante que se ve. */
function fichaHTML(p, st) {
    const t = st.t;
    if (p.lot) {
        const l = p.lot, k = l.k, x = st.tasks.find(y => y.x.id === k.id) || { s: 'pend', f: 0 };
        const est = x.s === 'pend' ? (k.human ? 'Te toca a ti' : 'Apuntada, sin empezar') : x.s === 'work' ? `En obra · ${Math.round(x.f * 100)} %` : 'Terminada';
        const who = [...st.working.entries()].filter(([, w]) => w.x.id === k.id).map(([id]) => actorName(id)).join(', ');
        return headH('Solar ' + k.id, secName(k.section) || k.section, k.human ? 0xE5C07B : 0xE98A3F) + `<p class="ft">${escOf(k.text || k.title)}</p>`
            + rowsH([['Estado', est], who ? ['La hace', escOf(who)] : k.human ? ['La hace', 'Tú'] : null, k.marked ? ['Marcada', hm(ms(k.marked))] : null, ob.started.has(k.id) ? ['Empezó', hm(ob.started.get(k.id))] : null, (k.files || []).length ? ['Ficheros', escOf(k.files.join(', '))] : null])
            + `<div class="fb">${goB('Ver en el Parte', 'parte')}${goB('Ver en el Diagrama', 'diagram')}${tasksB(k.line)}`
            + `${chatB(k.marked ? stepNear(ms(k.marked)) : ob.started.has(k.id) ? stepNear(ob.started.get(k.id)) : null)}${x.s !== 'pend' ? actB('Ver los cambios', 'branch') : ''}</div>`;
    }
    if (p.actor && p.actor.kind === 'worker') {
        const a = p.actor, info = ob.facts.agents.find(x => x.id === a.id) || {};
        const w = st.working.get(a.id) || st.retouch.get(a.id);
        return headH(a.name, info.description || info.type || '', a.color) + rowsH([['Ahora', escOf(a.label.el.querySelector('span').textContent || 'sin tarea')], w ? [st.working.has(a.id) ? 'Tarea' : 'Retoca', escOf(w.x.id + ' · ' + w.x.title)] : null, w && w.file ? ['Fichero', escOf(w.file)] : null, ['Entró', info.t0 ? hm(info.t0) : '—'], ['Salió', info.t1 && t >= info.t1 + 20000 ? hm(info.t1) : 'sigue en obra'], info.model ? ['Modelo', escOf(info.model)] : null])
            + `<div class="fb"><button type="button" data-act="follow">Seguirle de cerca</button>${goB('Ver en el Parte', 'parte')}`
            + `${chatB((ob.trace.steps || []).find(s => s.kind === 'agent' && s.childAgentId === a.id))}</div>`;
    }
    if (p.actor && p.actor.kind === 'truck') return headH('Camión', 'Un commit', 0x3FB950) + rowsH([['Carga', p.actor.hash ? `<code>${escOf(p.actor.hash)}</code>` : 'sin hash'], ['Hora', hm(p.actor.at)]]);
    if ((p.actor && p.actor.id === 'main') || p.place === 'caseta') {
        const crew = st.live.filter(a => !a.parentAgentId);
        const need = st.ask || st.planWait || st.perm;
        const now = st.ask ? '<em>te ha preguntado y espera</em>' : st.planWait ? '<em>te ha presentado el plan y espera</em>' : st.perm ? '<em>te ha pedido permiso y espera</em>'
            : crew.length ? 'dirige a ' + escOf(crew.map(a => ROLES[a.role][1]).join(', ')) : st.waitingYou ? 'ha terminado y espera tu mensaje' : 'revisa los planos';
        return headH('Claude', 'Jefe de obra · orquestador', 0xE98A3F) + rowsH([['Ahora', now], ['Fase', escOf(st.phase)], taskRow(panelTask())])
            + needH(st) + '<h4>Sus preguntas</h4>' + asksH(t)
            + `<div class="fb">${chatB(need && need.step ? need.step : stepNear(t))}${boardB(panelTask())}</div>`;
    }
    if ((p.actor && p.actor.id === 'tu') || p.place === 'promotor') {
        const yours = st.tasks.filter(k => k.x.human), done = yours.filter(k => k.s === 'done').length;
        return headH('Tú', 'Dirección de obra: decides, apruebas y revisas', 0xE5C07B) + rowsH([['Te toca', needText(st)], yours.length ? ['Tus pruebas', `${done} de ${yours.length} hechas`] : null, taskRow(panelTask())])
            + needH(st) + '<h4>Tus decisiones</h4>' + asksH(t) + `<div class="fb">${boardB(panelTask())}</div>`;
    }
    if (p.place === 'almacen') {
        const L = (st.plan && st.plan.commits || []).filter(c => ms(c.at) <= t);
        return headH('Almacén', 'Registro de commits', 0x3FB950) + (L.length ? `<ul>${L.slice(-8).reverse().map(c => `<li><code>${escOf(c.hash || 'commit')}</code> · ${hm(ms(c.at))}</li>`).join('')}</ul>` : '<p class="fn">Aún no ha llegado ninguna carga.</p>')
            + rowsH([['Cargas', String(L.length)]]) + `<div class="fb">${actB('Ver los cambios de la rama', 'branch')}</div>`;
    }
    if (p.place === 'archivo') {
        return headH('Archivo', 'Documentación del change', 0x8E9BF5) + rowsH([['Change', escOf(st.plan && st.plan.change || '—')], ['Estado', st.plan && st.plan.archived ? 'Archivado' : 'Abierto'], taskRow(panelTask())])
            + `<div class="fb">${tasksB()}${actB('Explorar el repositorio', 'repo')}${boardB(panelTask())}</div>`;
    }
    if (p.place === 'planos') {
        const secs = {}; for (const k of st.tasks) secs[k.x.section] = (secs[k.x.section] || 0) + 1;
        return headH('Mesa de planos', 'El tasks.md del change', 0x8E9BF5) + (Object.keys(secs).length ? `<ul>${Object.entries(secs).map(([k, v]) => `<li><b>${escOf(secName(k) || k)}</b> · ${v} ${v === 1 ? 'solar' : 'solares'}</li>`).join('')}</ul>` : '<p class="fn">Aún no hay plano.</p>')
            + `<div class="fb">${tasksB()}</div>`;
    }
    return '';
}
const actorName = id => (ob.actors.get(id) || {}).name || id;

// CUADRO A CUADRO

function frame(now) {
    const o = ob;
    if (!o || o.disposed) return;
    if (!o.active || !o.root.isConnected) { o.running = false; return; }
    requestAnimationFrame(frame);
    const dt = Math.min(0.05, (now - o.last) / 1000); o.last = now; const time = now / 1000;
    if (!o.visible || document.hidden) return;
    const mo = o.moment;
    // En directo, la hora real; en la repetición o en una sesión ya cerrada, la del paso que se ve.
    // Un paso que te pregunta o te presenta el plan se enseña desde que empieza: su final es tu respuesta.
    const waits = mo.now && (mo.now.questions && mo.now.questions.length || toolOf(mo.now) === 'ExitPlanMode');
    const t = mo.now && (mo.replaying || !o.trace.live) ? (waits ? ms(mo.now.start) : ms(mo.now.end)) || ms(mo.now.start) || Date.now() : Date.now();
    const jump = o.lastT === undefined || Math.abs(t - o.lastT) > 30 * 60000; o.lastT = t;
    o.stT -= dt; if (o.stT <= 0 || !o.st || jump) { o.st = stateAt(t); o.stT = 0.25; }
    const st = o.st;

    // Solares
    let active = null;
    const secOn = new Set();
    const retouched = new Set([...st.retouch.values()].map(r => r.x.id));
    for (const l of o.lots) {
        const x = st.tasks.find(y => y.x.id === l.k.id); const s = x ? x.s : 'pend';
        l.st = s;
        l.g.visible = s !== 'none'; l.label.hidden = s === 'none';
        if (s === 'none') continue;
        secOn.add(l.secTag);
        // Un solar terminado que se retoca vuelve a tener andamio y conos, sin perder el tejado.
        const busy = s === 'work' || retouched.has(l.k.id);
        l.stakes.visible = s !== 'done'; l.works.visible = busy; l.scaff.visible = busy;
        if (busy && s === 'done' && !active) active = l;
        const want = s === 'done' ? 1 : s === 'work' ? Math.max(0.08, x.f) : 0;
        if (jump) l.grow = want; else l.grow += (want - l.grow) * Math.min(1, dt * 1.5);
        l.B.body.scale.y = Math.max(0.001, l.grow);
        const roofOn = s === 'done' && l.grow > 0.97; if (roofOn && !l.B.top.visible) l.roofPop = jump ? 1 : 0; l.B.top.visible = roofOn;
        if (roofOn) { l.roofPop = Math.min(1, (l.roofPop || 0) + dt * 3); l.B.top.scale.setScalar(0.6 + 0.4 * (1 - Math.pow(1 - l.roofPop, 3))); }
        l.B.flag.rotation.y = Math.sin(time * 1.5 + l.x) * 0.35;
        for (const b of l.B.bands) if (b.userData.orig) b.userData.orig.emissiveIntensity = st.night && s === 'done' ? 0.9 : 0;
        if (s === 'work' && (!active || active.st === 'done')) active = l;
    }
    for (const l of o.lots) l.secTag.hidden = !secOn.has(l.secTag);

    // Grúa
    if (active) { const cp = o.craneG.position, dx = active.x - cp.x, dz = active.z - cp.z; o.crane.yaw += wrapA(Math.atan2(-dz, dx) - o.crane.yaw) * Math.min(1, dt * (jump ? 60 : 0.6)); o.crane.reach += (Math.hypot(dx, dz) - o.crane.reach) * Math.min(1, dt * 0.8); }
    o.jib.rotation.y = o.crane.yaw; o.trolley.position.x = clamp(o.crane.reach, 3, 18);
    { const drop = 13 - (active ? active.H + 1.6 : 6) + Math.sin(time * 0.8) * 0.4; o.hook.position.y = -drop; o.cable.geometry.setFromPoints([new THREE.Vector3(), new THREE.Vector3(0, -drop, 0)]); }

    // Día y noche
    const n = st.night ? 1 : 0;
    // De noche baja la luz, pero la obra se tiene que seguir leyendo: muchas sesiones se miran de noche.
    o.hemi.intensity += ((n ? 0.42 : 0.7) - o.hemi.intensity) * Math.min(1, dt * 1.5); o.sun.intensity += ((n ? 0.3 : 0.9) - o.sun.intensity) * Math.min(1, dt * 1.5);
    for (const L of o.lamps) { L.pl.intensity += ((n ? 1.6 : 0) - L.pl.intensity) * Math.min(1, dt * 1.5); L.bulb.userData.orig.emissiveIntensity = n; }
    for (const w of o.winMats) w.userData.orig.emissiveIntensity = n ? 1 : 0.1;
    o.root.classList.toggle('night', !!st.night);
    const commits = (st.plan && st.plan.commits || []).filter(c => ms(c.at) <= t);
    o.cargas.el.textContent = 'Cargas: ' + commits.length;

    // Quién está en obra
    const want = new Map();
    want.set('main', { kind: 'boss', color: 0xE98A3F, name: 'Claude · jefe de obra', role: 'boss' });
    want.set('tu', { kind: 'you', color: 0xE5C07B, name: 'Tú · dirección de obra', role: 'you' });
    for (const a of st.live) if (a.role !== 'commit') want.set(a.id, { kind: 'worker', color: ROLES[a.role][0], name: ROLES[a.role][1], role: a.role, info: a });
    (st.plan && st.plan.commits || []).forEach((c, i) => { const ct = ms(c.at); if (t >= ct - 20000 && t <= ct + 40000) want.set('truck' + i, { kind: 'truck', color: 0x3FB950, name: 'Camión · ' + (c.hash || 'commit'), role: 'commit', commit: c }); });
    let star = null;
    for (const [id, info] of want) { const a = actor(id, info); if (info.commit) { a.hash = info.commit.hash; a.at = ms(info.commit.at); } }
    for (const a of o.actors.values()) {
        const info = want.get(a.id);
        let target = null, act = 'idle', txt = '';
        if (a.id === 'main') {
            a.on = true; target = V3(PL.caseta);
            const crew = st.live.filter(x => !x.parentAgentId);
            act = st.ask ? 'pregunta' : crew.length ? 'espera' : 'planos';
            txt = st.ask ? 'te pregunta: ' + st.ask.header : crew.length ? 'dirige la obra' : o.trace.working && !mo.replaying ? 'trabaja en la caseta' : st.waitingYou ? 'espera tu mensaje' : '';
        } else if (a.id === 'tu') {
            a.on = true;
            if (st.ask) { target = V3(PL.promotor); act = 'piensa'; txt = 'decidiendo · ' + durOf(t - st.ask.ts); }
            else if (st.waitingYou) { target = V3(PL.promotor); act = 'piensa'; txt = 'te toca escribir'; }
            else if (st.youPend.length && !st.night && !st.live.length && !o.trace.working) { const k = st.youPend[Math.floor(t / 20000) % st.youPend.length]; const l = o.lots.find(y => y.k.id === k.x.id); if (l) { target = atLot(l, 'tu'); act = 'portátil'; txt = 'prueba ' + k.x.id; } }
            // Solo duerme en la repetición de una noche sin nadie trabajando: en directo, quien mira la obra no duerme.
            const asleep = mo.replaying && st.night && !st.live.length && !o.trace.working;
            if (!target) { target = V3(PL.promotor); act = asleep ? 'duerme' : 'idle'; txt = asleep ? 'zzz' : ''; }
        } else if (a.kind === 'truck') {
            if (info) { if (!a.on) { a.on = true; a.pos.set(GATE, 0, 20); a.path = []; a.goal = ''; a.sc = 0.05; } target = V3(PL.almacen); txt = 'lleva la carga'; }
            else if (a.on) { target = V3(PL.salida); txt = 'sale de la obra'; if (a.pos.distanceTo(target) < 0.5 || jump) a.on = false; }
        } else {
            const x = info && info.info;
            if (x && !a.on) { a.on = true; a.pos.copy(V3(PL.caseta)); a.path = []; a.goal = ''; a.sc = 0.05; }
            if (!x && a.on) { target = V3(PL.caseta); txt = 'sale de la obra'; if (a.pos.distanceTo(target) < 0.5 || jump) a.on = false; }
            if (x) {
                const d = doing(a.id, t);
                if (x.role === 'analysis') { const l = o.lots[0]; target = l ? new THREE.Vector3(l.x + slot(a.id)[0] * 3, 0, l.z - l.lot / 2 - 0.9) : V3(PL.planos).add(new THREE.Vector3(1.5, 0, 0)); act = 'mide'; txt = 'mide el terreno'; }
                else if (x.role === 'inspector') { const done = o.lots.filter(y => y.st === 'done' && !y.k.human); const l = done.length ? done[(Math.floor(t / 90000) + a.id.charCodeAt(1)) % done.length] : null; target = l ? atLot(l, a.id) : V3(PL.caseta); act = 'inspecciona'; txt = 'inspecciona ' + (l ? l.k.id : 'la obra'); }
                else if (x.role === 'docs') { target = V3(PL.archivo); act = 'planos'; txt = 'pasa los planos a limpio'; }
                else if (x.role === 'propose') { target = V3(PL.planos); act = 'planos'; txt = st.total ? 'dibuja el plano de la obra' : 'estudia el encargo'; }
                else if (x.role === 'archive') { target = V3(PL.archivo).add(new THREE.Vector3(-1.6, 0, 0)); act = 'planos'; txt = 'entrega la obra'; }
                else {
                    const w = st.working.get(a.id) || st.retouch.get(a.id), fix = !st.working.has(a.id);
                    if (w) {
                        const l = o.lots.find(y => y.k.id === w.x.id); target = l ? atLot(l, a.id) : V3(PL.caseta);
                        act = d === 'lee planos' ? 'lee planos' : d === 'prueba' ? 'prueba' : 'construye';
                        const verb = act === 'prueba' ? 'prueba' : act === 'lee planos' ? 'estudia' : fix ? 'retoca' : 'construye';
                        txt = `${verb} ${w.file || w.x.id}${fix ? '' : ' · ' + w.x.id}`; star = a;
                    }
                    else { target = V3(PL.caseta).add(new THREE.Vector3(2.8, 0, 0.3)); const kids = st.live.some(y => y.parentAgentId === a.id); act = kids ? 'espera' : d || 'idle'; txt = kids ? 'espera a su cuadrilla' : d || 'en la caseta'; }
                }
            }
        }
        say(a, txt);
        if (target && a.on) {
            const key = target.x.toFixed(1) + ',' + target.z.toFixed(1);
            if (key !== a.goal) { a.goal = key; a.path = jump || a.sc < 0.2 ? [target.clone()] : a.kind === 'truck' ? [new THREE.Vector3(a.pos.x, 0, FRONT), new THREE.Vector3(target.x, 0, FRONT), target.clone()] : route(a.pos, target); if (jump) a.pos.copy(target); }
            const p = a.path[0];
            if (p) { const d = a.pos.distanceTo(p), sp = (a.kind === 'truck' ? 5 : 2.6) * dt; if (d > 0.02) { const dir = p.clone().sub(a.pos).normalize(); a.pos.addScaledVector(dir, Math.min(d, sp)); a.face += wrapA(Math.atan2(dir.x, dir.z) - (a.kind === 'truck' ? Math.PI / 2 : 0) - a.face) * Math.min(1, dt * 8); } else if (a.path.length > 1) a.path.shift(); }
        }
        const walking = a.path.length > 0 && a.pos.distanceTo(a.path[a.path.length - 1]) > 0.05;
        if (!walking && a.kind !== 'truck') a.face += wrapA(0 - a.face) * Math.min(1, dt * 3);
        a.sc += ((a.on ? 1 : 0) - a.sc) * Math.min(1, dt * 4);
        a.c.root.visible = a.sc > 0.03; a.c.root.position.copy(a.pos); a.c.root.rotation.y = a.face; a.c.root.scale.setScalar(a.scale * Math.max(0.001, a.sc));
        if (a.tripod) { a.tripod.visible = a.c.root.visible && !walking && act === 'mide'; a.tripod.position.copy(a.pos).add(new THREE.Vector3(0, 0, 0.55)); }
        a.label.hidden = !a.c.root.visible;
        pose(a.c, act, time + a.id.length * 0.7, walking);
    }
    const truckOn = [...o.actors.values()].some(a => a.kind === 'truck' && a.on);
    o.barrier.rotation.z += ((truckOn ? -1.35 : 0) - o.barrier.rotation.z) * Math.min(1, dt * 3);
    for (const q of o.quest) q.hidden = !(st.ask || st.planWait || st.perm);

    // Cartel, reloj y parte
    const pct = st.total ? Math.round(st.done / st.total * 100) : 0;
    const you = st.ask ? `? Te toca: ${escOf(st.ask.header)} · hace ${durOf(t - st.ask.ts)}`
        : st.planWait ? `? Te toca: aprobar el plan · hace ${durOf(t - st.planWait.ts)}`
        : st.perm ? `? Te toca: dar permiso${st.perm.step ? ' a ' + escOf(toolOf(st.perm.step)) : ''} · hace ${durOf(t - st.perm.ts)}`
        : st.waitingYou ? 'Te toca: Claude espera tu mensaje' : '';
    const task = panelTask();
    const hud = `<div class="t"><span>${escOf(st.plan && st.plan.change ? changeName(st.plan.change) : 'Obra')}</span><span>${st.total ? pct + ' %' : ''}</span></div><div class="ph">${escOf(st.phase)}</div>`
        + (st.total ? `<div class="bar"><i style="width:${pct}%"></i></div><div class="row"><span>Edificios terminados</span><span>${st.done} de ${st.total}</span></div>` : '')
        + `<div class="row"><span>Operarios en obra</span><span>${st.live.length}</span></div>`
        + (task ? `<div class="row"><span>Tarea del panel</span><span>${escOf(task.key)}</span></div>` : '')
        + `<div class="you"${you ? '' : ' hidden'}>${you}</div>`;
    if (hud !== o.lastHud) { o.root.querySelector('.ob-hud').innerHTML = hud; o.lastHud = hud; }
    const ck = `<i>${st.night ? '☾' : '☀'}</i>${dayOf(t)} · ${hm(t)}${mo.replaying ? ' · repetición' : ''}`;
    if (ck !== o.lastClock) { o.root.querySelector('.ob-clock').innerHTML = ck; o.lastClock = ck; }
    const pt = '<span class="t">Parte de obra</span>' + notes(st).slice(-4).map(x => `<div><b>${hm(x.ts)}</b><i style="background:${hexOf(x.c)}"></i><span>${escOf(x.txt)}</span></div>`).join('');
    if (pt !== o.lastParte) { o.root.querySelector('.ob-parte').innerHTML = pt; o.lastParte = pt; }

    // Ficha abierta y su aro
    if (o.sel) {
        o.fichaT -= dt;
        if (o.fichaT <= 0) { o.fichaT = 0.4; const f = o.root.querySelector('.ob-ficha'); const html = fichaHTML(o.sel, st); if (html && f.dataset.h !== html) { f.innerHTML = html; f.dataset.h = html; } f.hidden = !html; }
        let pos = null, r = 1;
        if (o.sel.lot) { pos = new THREE.Vector3(o.sel.lot.x, 0, o.sel.lot.z); r = o.sel.lot.lot * 0.75; } else if (o.sel.actor) { pos = o.sel.actor.pos; r = 0.55; } else if (o.sel.pos) { pos = o.sel.pos; r = 2.2; }
        if (pos) { o.ring.visible = true; o.ring.position.set(pos.x, 0.12, pos.z); o.ring.scale.setScalar(r); o.ring.material.opacity = 0.6 + Math.sin(time * 3.3) * 0.25; }
    }

    // Cámara
    const V = VIEWS[o.view], w = o.root.clientWidth, h = o.root.clientHeight, pr = o.R.getPixelRatio(), asp = w / Math.max(1, h);
    if (w !== o.lastW) { o.lastW = w; o.root.classList.toggle('narrow', w < 720); o.root.classList.toggle('tiny', w < 460); if (!o.framed && w > 0) { o.framed = true; setView(o.view, true); } }
    if (o.R.domElement.width !== Math.round(w * pr) || o.R.domElement.height !== Math.round(h * pr)) o.R.setSize(w, h, false);
    if (o.view === 'cine' && !o.drag) { o.yawGoal += dt * 0.045; o.yaw = o.yawGoal; } else o.yaw += (o.yawGoal - o.yaw) * Math.min(1, dt * 4);
    o.pitch += (V.pitch - o.pitch) * Math.min(1, dt * 4);
    if (o.view === 'op') { const f = (o.follow && o.follow.on ? o.follow : null) || star || o.actors.get('main'); o.look.lerp(f.pos.clone().add(new THREE.Vector3(0, 1, 0)), Math.min(1, dt * 2)); }
    else if (o.view === 'cine') o.look.lerp(new THREE.Vector3(-1, 0, (o.siteBack + 12.8) / 2), Math.min(1, dt));
    let cam;
    if (V.ortho) { cam = o.camO; cam.left = -o.size * asp; cam.right = o.size * asp; cam.top = o.size; cam.bottom = -o.size; cam.position.set(o.look.x + Math.sin(o.yaw) * Math.cos(o.pitch) * 90, o.look.y + Math.sin(o.pitch) * 90, o.look.z + Math.cos(o.yaw) * Math.cos(o.pitch) * 90); }
    else { cam = o.camP; cam.aspect = asp; cam.position.set(o.look.x + Math.sin(o.yaw) * Math.cos(o.pitch) * o.dist, o.look.y + Math.sin(o.pitch) * o.dist, o.look.z + Math.cos(o.yaw) * Math.cos(o.pitch) * o.dist); }
    cam.updateProjectionMatrix(); cam.lookAt(o.look); o.cam = cam;
    o.R.render(o.S, cam);
    const v = new THREE.Vector3();
    for (const tg of o.tags) { if (!tg.obj) continue; tg.obj.getWorldPosition(v); v.y += tg.dy; v.project(cam); tg.el.style.left = ((v.x * 0.5 + 0.5) * w) + 'px'; tg.el.style.top = ((-v.y * 0.5 + 0.5) * h) + 'px'; tg.el.style.opacity = (tg.hidden || v.z > 1) ? 0 : 1; }
}

/** El parte de obra: solo lo importante, en orden y hasta el instante que se ve. */
function notes(st) {
    const t = st.t, L = [];
    for (const k of st.tasks) { if (k.s === 'done' && k.x.marked) L.push({ ts: ms(k.x.marked), c: 0x3FB950, txt: `Terminado ${k.x.id}: ${k.x.title}` }); }
    for (const [id, at] of ob.started) { const k = st.tasks.find(y => y.x.id === id); if (k && at <= t) L.push({ ts: at, c: 0xE98A3F, txt: `Empieza la obra de ${id} · ${secName(k.x.section)}` }); }
    for (const a of ob.facts.agents) { if (a.role === 'commit') continue; if (a.t0 <= t) L.push({ ts: a.t0, c: ROLES[a.role][0], txt: `Entra en obra: ${ROLES[a.role][1]}` }); if (a.until !== Infinity && a.until + 15000 <= t) L.push({ ts: a.t1, c: ROLES[a.role][0], txt: `Sale de la obra: ${ROLES[a.role][1]}` }); }
    for (const q of ob.facts.asks) { if (q.ts <= t) L.push({ ts: q.ts, c: 0xE5C07B, txt: `El jefe de obra te pregunta: ${q.header}` }); if (q.answered !== null && q.answered <= t) L.push({ ts: q.answered, c: 0xE5C07B, txt: `Contestas tras ${durOf(q.answered - q.ts)}` }); }
    for (const c of (st.plan && st.plan.commits || [])) { const ct = ms(c.at); if (ct <= t) L.push({ ts: ct, c: 0x3FB950, txt: `Carga registrada en el almacén: ${c.hash || 'commit'}` }); }
    return L.filter(x => x.ts).sort((a, b) => a.ts - b.ts);
}

window.obRender = obRender;
window.obLeave = obLeave;
window.obPlan = obPlan;
})();
