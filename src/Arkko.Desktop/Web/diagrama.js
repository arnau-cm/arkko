// DIAGRAMA: la vista «diagram» de Sesiones, el trabajo de la sesión como un metro dibujado como un plano.
//
// Va aparte de sesiones.js y se apoya en lo suyo: laneNodes junta las llamadas seguidas a la misma herramienta, y
// agentOf, callOf y flowDone dicen de cada agente quién lo lanzó y si ha terminado. Aquí está el modelo (qué paradas
// hay, cómo se agrupan en fases y dónde va cada una) y su dibujo.

// Familias de herramientas: lo que hace cada una, para su color, su cápsula y la fase en la que se agrupa de lejos.
const DG_FAMILIES = {
    read: ['Read', 'NotebookRead'],
    look: ['Grep', 'Glob', 'ToolSearch', 'WebSearch', 'WebFetch', 'LS'],
    edit: ['Edit', 'Write', 'MultiEdit', 'NotebookEdit'],
    run: ['Bash', 'PowerShell', 'BashOutput', 'KillShell', 'KillBash', 'TaskStop', 'Monitor'],
    ask: ['AskUserQuestion', 'ExitPlanMode'],
};

// Las fases en que se juntan las paradas de lejos, con el nombre que se les da.
const DG_PHASES = { talk: 'Conversación', understand: 'Entender', change: 'Cambiar', check: 'Comprobar' };

// Comandos que comprueban (pruebas y compilaciones) y comandos que entregan (commit y push): los primeros son su propia
// fase y los segundos un hito que no se junta con nada.
const DG_CHECK = /\b(test|tests|build|pytest|vitest|jest|lint|check-encoding)\b/i;
const DG_DELIVER = /\bgit\s+(commit|push)\b/i;

// Distancias entre paradas, en píxeles, según la orientación: la mínima en un mismo carril (lo que ocupa un rótulo), y
// cuánto añade y hasta dónde el tiempo que pasó entre una parada y la siguiente.
const DG_GAPS = { h: { min: 150, k: 26, max: 210 }, v: { min: 74, k: 14, max: 120 } };

/** La familia de una herramienta: leer, buscar, cambiar, ejecutar, preguntar, una skill u otra.
 *
 * Las de MCP se miran por lo que hacen: el grafo de código y las búsquedas buscan; el resto, otra. */
function dgFamily(name, skill) {
    if (skill) return 'skill';
    for (const [family, names] of Object.entries(DG_FAMILIES)) if (names.includes(name)) return family;
    if (/^mcp__.*(codegraph|search|explore|grep)/i.test(name)) return 'look';
    return 'other';
}

/** El instante de un texto ISO en milisegundos; null si no hay. */
function dgTime(iso) {
    const t = iso ? new Date(iso).getTime() : NaN;
    return Number.isFinite(t) ? t : null;
}

/** Lo que tocó una llamada, sacado de su título: el fichero de una lectura o un cambio («Read · proposal.md» da
 * «proposal.md») y el principio de la orden de un comando; null si no dice nada. */
function dgTarget(step, family) {
    const parts = (step && step.title || '').split(' · ');
    if (parts.length < 2) return null;
    const what = parts.slice(1).join(' · ').trim();
    if (!what) return null;
    if (family === 'run') return what.length > 48 ? what.slice(0, 47) + '…' : what;
    return what.split(/[\\/]/).pop();
}

/** Las paradas de un carril, una por nodo de laneNodes: cada prompt, respuesta, pensamiento, pregunta, lanzamiento de
 * agentes, el principio y el final de un agente, y cada racha de llamadas seguidas a la misma herramienta. */
function dgStations(lane, nodes, byOrder) {
    const out = [];
    for (const n of nodes) {
        const first = n.step || byOrder.get(n.orders[0]);
        const base = { key: n.key, lane, node: n, order: n.order, orders: n.orders || [] };
        const span = (a, b) => ({ start: dgTime(a), end: dgTime(b) ?? dgTime(a) });
        switch (n.kind) {
            case 'prompt':
                out.push({ ...base, kind: 'prompt', family: 'you', shell: Boolean(n.step.shell), ...span(n.step.start, n.step.end) });
                break;
            case 'reply':
                out.push({ ...base, kind: 'say', family: lane === 'main' ? 'claude' : 'agent', final: Boolean(n.final), unread: Boolean(n.unread), ...span(n.step.start, n.step.end) });
                break;
            case 'think':
                out.push({ ...base, kind: 'think', family: 'think', ...span(n.step.start, n.step.end) });
                break;
            case 'fork':
                out.push({ ...base, kind: 'launch', family: 'agent', children: n.children, ...span(first && first.start, first && first.end) });
                break;
            case 'agent':
                out.push({ ...base, kind: 'start', family: 'agent', agent: n.agent, ...span(n.agent && n.agent.startUtc, n.agent && n.agent.startUtc) });
                break;
            case 'end':
                out.push({ ...base, kind: 'end', family: 'done', agent: n.agent, ...span(n.agent && n.agent.endUtc, n.agent && n.agent.endUtc) });
                break;
            case 'pending':
                out.push({ ...base, kind: 'pending', family: 'think', start: null, end: null });
                break;
            case 'agentswait':
                out.push({ ...base, kind: 'wait', family: 'agent', wait: n.wait, start: dgTime(n.wait && n.wait.since), end: Date.now() });
                break;
            default: {
                if (n.kind !== 'tools') break;
                const family = dgFamily(n.tool, n.skill);
                const targets = [...new Set(n.orders.map(o => dgTarget(byOrder.get(o), family)).filter(Boolean))];
                const errors = n.orders.filter(o => byOrder.get(o) && byOrder.get(o).isError).length;
                const commands = n.orders.map(o => byOrder.get(o)).filter(Boolean).map(s => s.title || '');
                const deliver = family === 'run' && commands.some(c => DG_DELIVER.test(c));
                const check = family === 'run' && !deliver && commands.some(c => DG_CHECK.test(c));
                out.push({ ...base, kind: family === 'ask' ? 'ask' : 'tools', family, tool: n.tool, count: n.orders.length, targets, errors, deliver, check, ...span(n.start, n.end) });
            }
        }
    }
    return out;
}

/** La fase de una parada al mirarla de lejos: «entender» (leer, buscar y los comandos que miran), «cambiar» o
 * «comprobar»; «talk» para los mensajes y «any» para lo que se junta con la fase en curso sin cambiarla; null si va
 * sola (el prompt, una pregunta, un lanzamiento, un commit o el principio y el final de un agente). */
function dgPhaseOf(s) {
    if (s.kind === 'say' && s.final) return null;
    if (s.kind === 'say' || s.kind === 'think') return 'talk';
    if (s.kind !== 'tools' || s.deliver) return null;
    if (s.family === 'edit') return 'change';
    if (s.check) return 'check';
    if (s.family === 'read' || s.family === 'look' || s.family === 'run') return 'understand';
    return 'any';
}

/** Cuántas llamadas de cada clase lleva una fase, dicho en corto: «5 lecturas, 3 búsquedas». */
function dgPhaseCount(members) {
    const n = { read: 0, look: 0, edit: 0, run: 0, other: 0, skill: 0, say: 0 };
    for (const m of members) {
        if (m.kind === 'tools') n[m.family] = (n[m.family] || 0) + m.count;
        else n.say++;
    }
    const words = [[n.read, 'lectura', 'lecturas'], [n.look, 'búsqueda', 'búsquedas'], [n.edit, 'edición', 'ediciones'], [n.run, 'comando', 'comandos'],
        [n.other + n.skill, 'llamada', 'llamadas'], [n.say, 'mensaje', 'mensajes']];
    return words.filter(([c]) => c > 0).map(([c, one, many]) => `${c} ${c === 1 ? one : many}`).join(', ');
}

/** Las paradas de lejos: las seguidas de una misma fase se juntan en una, con su recuento y los ficheros que tocaron.
 *
 * Un mensaje o una herramienta sin fase propia se suman a la fase en curso sin partirla: entre lectura y lectura, el
 * agente suele decir algo o hacer una llamada suelta, y partir ahí dejaba casi tantas fases como paradas. Una fase que
 * empieza por un mensaje y sigue con trabajo pasa a ser de ese trabajo. */
function dgPhases(stations) {
    const out = [];
    const join = (into, s) => {
        into.members.push(s);
        into.orders.push(...s.orders);
        into.end = Math.max(into.end ?? 0, s.end ?? 0) || into.end;
        for (const t of s.targets || []) if (!into.targets.includes(t)) into.targets.push(t);
    };
    for (const s of stations) {
        let phase = dgPhaseOf(s);
        const last = out[out.length - 1];
        const open = last && last.kind === 'phase' && last.lane === s.lane ? last : null;
        if ((phase === 'talk' || phase === 'any') && open && open.phase !== 'talk') phase = open.phase;
        if (phase === 'any') phase = 'understand';
        if (phase && open && open.phase === phase) {
            join(open, s);
            continue;
        }
        if (phase && phase !== 'talk' && open && open.phase === 'talk') {
            open.phase = phase;
            open.family = phase === 'understand' ? 'read' : phase === 'change' ? 'edit' : 'run';
            join(open, s);
            continue;
        }
        if (!phase) {
            out.push(s);
            continue;
        }
        out.push({ key: s.key, kind: 'phase', phase, lane: s.lane, family: phase === 'understand' ? 'read' : phase === 'change' ? 'edit' : phase === 'check' ? 'run' : (s.lane === 'main' ? 'claude' : 'agent'),
            order: s.order, orders: [...s.orders], members: [s], targets: [...(s.targets || [])], start: s.start, end: s.end });
    }
    for (const p of out) if (p.kind === 'phase') {
        p.title = DG_PHASES[p.phase];
        p.sub = dgPhaseCount(p.members);
    }
    return out;
}

/** Marca la respuesta final de cada prompt: la última de Claude, sin nada detrás hasta el prompt siguiente; en el prompt
 * en curso, solo si la sesión ha terminado de verdad, y no si espera a la persona. La última, además, como sin revisar
 * si la sesión lo está, salvo en la repetición, como en el directo. */
function dgFinals(nodes, moment) {
    const current = state.active.find(a => a.id === state.trace.id);
    const over = !moment.working && !moment.replaying && !(current && current.statusKey === 'waiting');
    nodes.forEach((n, i) => {
        if (n.kind !== 'reply') return;
        let closed = false;
        let clean = true;
        for (const m of nodes.slice(i + 1)) {
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
    });
    const last = [...nodes].reverse().find(n => n.final);
    if (last && current && needOf(current) === 'review' && !moment.replaying) last.unread = true;
}

/** Los carriles de lo que se ve: el de la sesión, el tuyo (sus prompts) y uno por agente, cada uno con sus paradas.
 *
 * El tuyo se saca del de la sesión: los prompts son tuyos, y en el plano van en su propio eje. Con el lienzo `v`, la
 * sesión que piensa sin escribir lleva al final su parada de pensar, como la bombilla del directo. */
function dgModel(shown, moment, far, v) {
    const byOrder = new Map(state.trace.steps.map(s => [s.order, s]));
    const byLane = new Map([['main', []]]);
    for (const s of shown) {
        const lane = s.agentId || 'main';
        if (!byLane.has(lane)) byLane.set(lane, []);
        byLane.get(lane).push(s);
        if (s.kind === 'agent' && s.childAgentId && !byLane.has(s.childAgentId)) byLane.set(s.childAgentId, []);
    }
    const lanes = [];
    let thinking = null;
    for (const [id, laneSteps] of byLane) {
        if (id !== 'main' && !agentOf(id)) continue;
        let nodes = laneNodes(id, laneSteps, moment);
        if (id === 'main') {
            dgFinals(nodes, moment);
            thinking = v ? wfThinking(v, shown, moment) : null;
            if (thinking) nodes = [...nodes, thinking];
        }
        let stations = dgStations(id, nodes, byOrder);
        if (far) stations = dgPhases(stations);
        lanes.push({ id, agent: id === 'main' ? null : agentOf(id), parent: id === 'main' ? null : (agentOf(id).parentAgentId && byLane.has(agentOf(id).parentAgentId) ? agentOf(id).parentAgentId : 'main'), stations });
    }
    const main = lanes.find(l => l.id === 'main');
    const yours = { id: 'you', agent: null, parent: null, stations: main.stations.filter(s => s.kind === 'prompt') };
    main.stations = main.stations.filter(s => s.kind !== 'prompt');
    // El turno acabó con agentes trabajando: la línea de la sesión acaba en el nudo de la espera.
    const wait = typeof agentsWait === 'function' ? agentsWait() : null;
    if (wait && !thinking) {
        const last = main.stations[main.stations.length - 1];
        main.stations.push({ key: 'dg-wait', kind: 'wait', family: 'agent', lane: 'main', order: (last ? last.order : 0) + 0.5, orders: [], start: dgTime(wait.since), end: Date.now(), wait });
    }
    return { lanes: [yours, ...lanes], byOrder, wait };
}

/** Coloca las paradas a lo largo del eje del tiempo y reparte los carriles en filas.
 *
 * Cada parada va tras la anterior de su carril con hueco para su rótulo, y no antes de la última parada de cualquier
 * carril más lo que creció el tiempo entre las dos, con tope: así dos cosas que pasan a la vez en carriles distintos
 * quedan una encima de otra, y una espera larga se nota sin alargar el dibujo sin fin. Tus prompts y la sesión tienen
 * fila propia; los agentes reutilizan la primera fila que ya ha quedado libre. */
function dgLayout(model, orient) {
    const g = DG_GAPS[orient];
    // El principio de un agente va siempre delante de sus llamadas, aunque alguna lleve un orden anterior al de la suya.
    for (const lane of model.lanes) {
        const start = lane.stations.find(s => s.kind === 'start');
        const rest = lane.stations.filter(s => s.kind !== 'start');
        if (start && rest.length) start.order = Math.min(start.order, Math.min(...rest.map(s => s.order)) - 0.001);
    }
    const all = model.lanes.flatMap(l => l.stations.map(s => ({ s, lane: l }))).sort((a, b) => a.s.order - b.s.order);
    const laneLast = new Map();
    let prev = null;
    let at = 0;
    // En horizontal, los rótulos van centrados en su parada, encima las que pesan y debajo las herramientas: dos de un
    // mismo lado de un mismo carril se separan lo que miden, para que no se toquen.
    const sides = new Map();
    for (const { s, lane } of all) {
        const dt = prev && s.start !== null && prev.start !== null ? Math.max(0, (s.start - prev.start) / 1000) : 0;
        const timeGap = prev ? Math.min(g.max, g.k * Math.log2(1 + dt / 4)) : 0;
        const own = laneLast.has(lane.id) ? laneLast.get(lane.id) + g.min : 0;
        // Un agente empieza un poco después de la parada que lo lanza, para que se vea el desvío.
        const launched = s.kind === 'start' ? dgLaunchAt(model, lane) + g.min * 0.6 : 0;
        at = Math.max(own, prev ? prev.at + timeGap : 0, launched);
        if (orient === 'h') {
            const side = `${lane.id}|${dgMajor(s) ? 'up' : 'down'}`;
            const w = dgLabelWidth(s);
            const last = sides.get(side);
            if (last) at = Math.max(at, last.at + (last.w + w) / 2 + DG_LABEL_GAP);
            sides.set(side, { at, w });
        }
        s.at = at;
        laneLast.set(lane.id, at);
        prev = s;
    }
    // Filas: la tuya, la de la sesión y, para los agentes, la primera libre desde que empiezan.
    for (const lane of model.lanes) {
        const ats = lane.stations.map(s => s.at);
        lane.first = ats.length ? Math.min(...ats) : 0;
        lane.last = ats.length ? Math.max(...ats) : 0;
        if (lane.id === 'you') lane.row = 0;
        else if (lane.id === 'main') lane.row = 1;
    }
    const agents = model.lanes.filter(l => l.id !== 'you' && l.id !== 'main').sort((a, b) => a.first - b.first);
    const free = [];
    for (const lane of agents) {
        let row = free.findIndex(end => end + g.min < lane.first);
        if (row < 0) row = free.push(-Infinity) - 1;
        free[row] = lane.last;
        lane.row = row + 2;
    }
    model.rows = 2 + free.length;
    model.length = Math.max(0, ...model.lanes.map(l => l.last));
    return model;
}

// Lo que queda como poco entre dos rótulos de un mismo lado de un carril, en horizontal.
const DG_LABEL_GAP = 14;

/** Lo que mide a lo ancho el rótulo de una parada en horizontal, a ojo: el más largo de sus dos renglones, hasta el ancho
 * máximo que le da la hoja de estilos. */
function dgLabelWidth(s) {
    const [title, sub] = dgWords(s);
    const max = s.final ? 160 : dgMajor(s) ? 138 : 132;
    return Math.min(max, Math.max(String(title || '').length * 7.4, String(sub || '').length * 6.2) + 10);
}

/** Dónde va la parada que lanzó un agente: la de su carril padre que tiene su llamada; 0 si no se encuentra. */
function dgLaunchAt(model, lane) {
    const call = lane.agent ? callOf(lane.id) : null;
    const parent = model.lanes.find(l => l.id === lane.parent);
    const fallback = Number.isFinite(lane.first) ? Math.max(0, lane.first - DG_GAPS.h.min) : 0;
    if (!call || !parent) return fallback;
    const s = parent.stations.find(x => x.orders.includes(call.order))
        || parent.stations.filter(x => x.order <= call.order).pop();
    return s && Number.isFinite(s.at) ? s.at : fallback;
}

/** Resumen del modelo para revisarlo desde fuera: carriles, filas y paradas con su tipo y su sitio. */
function dgDump(orient = 'h', far = false) {
    const steps = selectedSteps();
    const moment = flowMoment(steps);
    const { steps: shown } = flowSteps(steps, moment);
    const model = dgLayout(dgModel(shown, moment, far), orient);
    return {
        rows: model.rows,
        length: Math.round(model.length),
        lanes: model.lanes.map(l => ({
            id: l.id, row: l.row, parent: l.parent, first: Math.round(l.first), last: Math.round(l.last),
            stations: l.stations.map(s => [s.kind, s.family, s.tool || s.title || '', s.count || s.members?.length || '', Math.round(s.at), s.sub || (s.targets || []).slice(0, 3).join(',')]),
        })),
    };
}

// EL DIBUJO
//
// El plano va en el lienzo del directo (wfStage): la misma cámara, la misma barra de abajo con «Seguir», el zoom y
// «Ver todo», y la misma ficha al pulsar un paso. Lo de aquí es lo propio del plano: las paradas y las líneas, que se
// mueven a su sitio nuevo en lugar de rehacerse, la regla de los ejes, la cuadrícula del papel y el cajetín.

// Medidas del plano en el mundo del lienzo, según la orientación: dónde empieza el eje del tiempo (tras la regla de los
// ejes), dónde va la primera fila, la distancia entre filas, lo que queda tras la última parada y tras la última fila.
const DG_DRAW = {
    h: { start: 130, rowFirst: 100, rowGap: 180, end: 260, cross: 110 },
    v: { start: 110, rowFirst: 180, rowGap: 220, end: 220, cross: 200 },
};

// Hasta dónde acerca el zoom automático: las piezas ya tienen su tamaño de lectura a escala 1, y lo que sobre se reparte
// entre las filas, hasta algo más de la mitad de su distancia.
const DG_ZOOM_MAX = 1;

// Lo que cuelgan de la línea de la sesión sus herramientas, para que en ella se lean los mensajes; las de los agentes
// van en su línea.
const DG_DROP = 40;

const DG_ROW_STRETCH = 1.6;

// La distancia hasta una fila fina, la de los agentes que ya terminaron: lo justo para su línea y su etiqueta. Tras una
// fila normal, más (`after`): por debajo de ella van colgadas sus herramientas con sus rótulos.
const DG_THIN = { h: 46, v: 56 };
const DG_THIN_AFTER = { h: 135, v: 210 };

// El tiempo entre paradas: lo que tarda menos que el primero no se escribe; desde el segundo, en ámbar; desde el
// tercero es una pausa (entre prompts, casi siempre) y se escribe en minutos.
const DG_GAP_MIN = 2000;
const DG_GAP_LONG = 30000;
const DG_GAP_PAUSE = 300000;

// Iconos de cada parada, de los del flujo de sesiones.js.
const DG_ICONS = { prompt: 'prompt', say: 'reply', think: 'think', pending: 'think', ask: 'question', launch: 'agent', start: 'agent', end: 'done', read: 'read', look: 'search', edit: 'edit', run: 'terminal', skill: 'skill', other: 'tool', deliver: 'send', understand: 'read', change: 'edit', check: 'terminal', talk: 'reply' };

/** Si el plano va en horizontal o en vertical: en horizontal si lo que se ve del lienzo es tan ancho como alto o más. */
function dgOrient(v) {
    const free = wfFree(v);
    return free.w >= free.h ? 'h' : 'v';
}

/** Pinta el Diagrama en el lienzo del directo: lo monta la primera vez y, después, lo pone al día. */
function dgLive(steps, box) {
    const key = `dg|${state.trace.id}|${state.flowScope}`;
    let v = state.wf;
    if (!v || v.key !== key || !box.contains(v.root)) {
        box.replaceChildren();
        v = state.wf = wfStage(box, key);
        v.dg = { where: new Map(), paths: new Map(), labels: new Map() };
        v.root.classList.add('dg-root');
        v.layer = el('div', 'dg-layer');
        v.dgLines = svg('svg', { class: 'dg-lines', width: 1, height: 1 });
        v.layer.append(v.dgLines);
        v.world.append(v.layer);
        v.dg.here = el('div', 'dg-here');
        v.dg.here.hidden = true;
        v.layer.append(v.dg.here);
        v.rail = el('div', 'dg-rail');
        v.root.insertBefore(v.rail, v.drawer);
        v.stamp = el('div', 'dg-stamp');
        v.root.insertBefore(v.stamp, v.drawer);
        // La rama va en el sello, como una casilla más: abre la misma lista que la rama del directo.
        v.stampGit = el('button', 'dg-stamp-git');
        v.stampGit.addEventListener('click', e => {
            e.stopPropagation();
            $('flow-git-head').click();
        });
        dgRulerMount(v);
    }
    dgWire();
    $('flow-dock').classList.add('dg');
    const slot = $('dg-ruler-slot');
    if (v.ruler.parentNode !== slot) slot.replaceChildren(v.ruler);
    const moment = flowMoment(steps);
    const { steps: shown, earlier } = flowSteps(steps, moment);
    const prompt = moment.working && moment.now ? shown.find(s => s.interaction === moment.now.interaction) : null;
    const asking = Boolean(state.trace.live) && shown.some(s => !s.agentId && s.title === 'AskUserQuestion' && !s.end);
    const far = Boolean(state.dgFar);
    // Al moverse por los pasos (la regla, las flechas, ▶), el plano vuelve a seguir el paso en que se está.
    if (v.dg.replayAt !== undefined && v.dg.replayAt !== state.replay.index) v.follow = true;
    v.dg.replayAt = state.replay.index;
    const orient = dgOrient(v);
    const model = dgFold(dgLayout(dgModel(shown, moment, far, v), orient), moment);
    wfBar(v, moment, earlier, asking, prompt && prompt.start, model.wait);
    $('dg-scale').querySelectorAll('[data-far]').forEach(b => b.classList.toggle('on', (b.dataset.far === '1') === far));
    const turned = dgPaint(v, model, orient, moment, dgRowGap(v, orient, model));
    dgStamp(v, model, far);
    dgRuler(v, steps, moment);
    // Mientras el marcador recorre lo nuevo, la cámara va con él.
    if (v.dg.train && v.follow) dgRail(v, false);
    else if (v.follow || turned) dgFollow(v, v.first);
    else if (v.fit) wfFit(v);
    else dgRail(v, false);
    v.first = false;
}

/** La distancia entre filas que llena lo que se ve: la de serie, si con el zoom automático ya no sobra sitio; si sobra,
 * más, hasta su tope. Las filas finas tienen la suya y no se estiran. */
function dgRowGap(v, orient, model) {
    const d = DG_DRAW[orient];
    const thins = (model.thin || []).filter(Boolean).length;
    const steps = model.rows - 1 - thins;
    if (steps < 1) return d.rowGap;
    const thin = dgCrossings({ ...d, rowGap: 0 }, model, orient)[model.rows - 1] - d.rowFirst;
    const sight = dgSight(v, orient);
    const cross = orient === 'h' ? sight.h : sight.w;
    const breadth = d.rowFirst + thin + steps * d.rowGap + d.cross;
    const z = Math.max(WF_READABLE, Math.min(DG_ZOOM_MAX, (cross - 16) / breadth));
    const fill = ((cross - 16) / z - d.rowFirst - d.cross - thin) / steps;
    return Math.round(Math.max(d.rowGap, Math.min(d.rowGap * DG_ROW_STRETCH, fill)));
}

/** Recoge los carriles de los agentes que ya han terminado en el momento que se ve, salvo los que se han abierto a mano;
 * una fila es fina (`model.thin`) si todo lo que pasa por ella está recogido. */
function dgFold(model, moment) {
    const open = state.dgOpen || (state.dgOpen = new Set());
    for (const lane of model.lanes) lane.folded = Boolean(lane.agent) && flowDone(lane.agent, moment) && !open.has(lane.id);
    model.thin = Array.from({ length: model.rows }, (_, row) => {
        const lanes = model.lanes.filter(l => l.row === row);
        return row > 1 && lanes.length > 0 && lanes.every(l => l.folded);
    });
    return model;
}

/** Dónde va cada fila a lo ancho: la primera en su sitio y cada una tras la anterior, a la distancia de las filas o, si
 * es fina, a la suya. */
function dgCrossings(d, model, orient) {
    const out = [d.rowFirst];
    const thin = row => Boolean(model.thin && model.thin[row]);
    for (let row = 1; row < model.rows; row++) out.push(out[row - 1] + (!thin(row) ? d.rowGap : thin(row - 1) ? DG_THIN[orient] : DG_THIN_AFTER[orient]));
    return out;
}

/** Abre los agentes recogidos de una fila o, si ya estaban abiertos, los vuelve a recoger. */
function dgToggleRow(v, row) {
    const lanes = v.dg.model ? v.dg.model.lanes.filter(l => l.row === row && l.agent) : [];
    if (!lanes.length) return;
    const open = state.dgOpen || (state.dgOpen = new Set());
    const unfold = lanes.some(l => l.folded);
    for (const l of lanes) {
        if (unfold) open.add(l.id);
        else open.delete(l.id);
    }
    renderDiagram(false);
}

/** Pone el plano al día: cada parada y cada línea van a su sitio nuevo con una transición, lo nuevo entra y lo que ya no
 * está se va. Al pasar de cerca a lejos, cada parada se va a la fase en que se junta, y al volver sale de ella. Devuelve
 * si ha cambiado la orientación. */
function dgPaint(v, model, orient, moment, rowGap) {
    const g = v.dg;
    dgTrainEnd(v);
    const d = { ...DG_DRAW[orient], rowGap };
    const H = orient === 'h';
    const turned = Boolean(g.orient) && g.orient !== orient;
    const fresh = !g.model;
    const crossings = dgCrossings(d, model, orient);
    const across = row => crossings[row];
    const xy = (at, row) => (H ? [d.start + at, across(row)] : [across(row), d.start + at]);
    const hangs = (s, lane) => lane.id === 'main' && !dgMajor(s);
    const spot = (s, lane) => {
        const p = xy(s.at, lane.row);
        if (!hangs(s, lane)) return p;
        return H ? [p[0], p[1] + DG_DROP] : [p[0] + DG_DROP, p[1]];
    };
    Object.assign(g, { orient, H, d, model, crossings, length: d.start + model.length + d.end, breadth: crossings[model.rows - 1] + d.cross });
    v.layer.className = `dg-layer dg-${orient}`;
    v.bounds = H ? { minX: 0, minY: 0, maxX: g.length, maxY: g.breadth } : { minX: 0, minY: 0, maxX: g.breadth, maxY: g.length };
    // Un plano que no comparte nada con el de antes (otro prompt) aparece entero, fundiéndose.
    const all = model.lanes.flatMap(l => l.stations);
    const anew = !fresh && !all.some(s => v.nodes.has(s.key));
    if (fresh || anew) v.layer.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 350, easing: 'ease-out' });

    // Lo que llega nuevo, si son pocas paradas, lo trae el marcador de «estás aquí» recorriendo la línea: el tramo se
    // dibuja tras él y la parada aparece cuando llega. Un salto que trae muchas de golpe las enseña todas a la vez, y lo
    // que sale de una fase, al acercar o alejar, no espera: ya estaba.
    let train = [];
    if (!fresh && !anew && !turned && !DG_STILL.matches) {
        const arrivals = all.filter(s => !v.nodes.has(s.key) && !s.orders.some(o => g.where.has(o))).sort((a, b) => a.order - b.order);
        if (arrivals.length <= DG_TRAIN_MAX) train = arrivals;
    }
    const waiting = new Set(train.map(s => s.key));
    Object.assign(g, { waiting, geo: new Map(), into: new Map(), leadOf: new Map(), pos: new Map() });
    const seen = new Set();
    const track = (key, to, geo) => {
        g.geo.set(key, geo);
        if (to) g.into.set(to, [...(g.into.get(to) || []), key]);
        return waiting.has(to);
    };
    const line = (key, a, b, cls, color, to) => dgPath(v, seen, key, `M${a[0]} ${a[1]} L${b[0]} ${b[1]}`, cls, color, fresh, track(key, to, { a, b }));
    const curve = (key, a, b, cls, color, to) => {
        const mid = H ? (a[1] + b[1]) / 2 : (a[0] + b[0]) / 2;
        const c1 = H ? [a[0], mid] : [mid, a[1]];
        const c2 = H ? [b[0], mid] : [mid, b[1]];
        dgPath(v, seen, key, `M${a[0]} ${a[1]} C${c1[0]} ${c1[1]} ${c2[0]} ${c2[1]} ${b[0]} ${b[1]}`, cls, color, fresh, track(key, to, { a, b, c1, c2 }));
    };
    // Ejes: uno por fila, de punta a punta.
    for (let row = 0; row < model.rows; row++) line(`axis:${row}`, xy(-d.start + 70, row), xy(model.length + d.end - 40, row), 'dg-axis' + (model.thin[row] ? ' folded' : ''));
    // La línea de cada carril, tramo a tramo, y los desvíos de los agentes desde la parada que los lanzó y de vuelta.
    const lead = new Map();
    for (const lane of model.lanes) {
        if (!lane.stations.length || lane.id === 'you') continue;
        const color = actorColor(lane.id);
        const folded = lane.folded ? ' folded' : '';
        lane.stations.forEach((s, i) => {
            if (hangs(s, lane)) line(`stem:${s.key}`, xy(s.at, lane.row), spot(s, lane), 'dg-stem', color, s.key);
            const prev = lane.stations[i - 1];
            if (!prev) return;
            const key = `seg:${prev.key}>${s.key}`;
            line(key, xy(prev.at, lane.row), xy(s.at, lane.row), 'dg-line' + folded, color, s.key);
            g.leadOf.set(s.key, key);
            for (const o of s.orders) lead.set(o, key);
        });
        const parent = lane.id === 'main' ? null : model.lanes.find(l => l.id === lane.parent);
        if (!parent) continue;
        const start = lane.stations[0];
        curve(`branch:${lane.id}`, xy(dgLaunchAt(model, lane), parent.row), xy(lane.first, lane.row), 'dg-branch' + folded, color, start.key);
        g.leadOf.set(start.key, `branch:${lane.id}`);
        for (const o of start.orders) lead.set(o, `branch:${lane.id}`);
        const end = lane.stations.find(s => s.kind === 'end');
        if (end) curve(`back:${lane.id}`, xy(end.at, lane.row), xy(end.at + d.start * 0.25, parent.row), 'dg-branch back' + folded, color, end.key);
    }
    // Tus prompts bajan a la línea de la sesión.
    const you = model.lanes.find(l => l.id === 'you');
    for (const s of you.stations) {
        line(`drop:${s.key}`, xy(s.at, 0), xy(s.at, 1), 'dg-line', actorColor('user'), s.key);
        for (const o of s.orders) lead.set(o, `drop:${s.key}`);
    }
    // La espera a sus agentes sigue a rayas hasta lo último; sin espera, mientras trabajan sus agentes, la línea de la
    // sesión sigue, fina, porque la sesión sigue ahí.
    const main = model.lanes.find(l => l.id === 'main');
    const wait = main.stations.find(s => s.kind === 'wait');
    if (wait) line('wait', xy(wait.at, 1), xy(model.length, 1), 'dg-wait-line');
    else if (main.stations.length && main.last < model.length) line('idle', xy(main.last, 1), xy(model.length, 1), 'dg-idle', actorColor('main'));
    // El tiempo de una parada a la siguiente de su carril, en una píldora sobre el tramo, si cabe entre las dos.
    const labels = new Set();
    for (const lane of model.lanes) {
        if (lane.id === 'you' || lane.folded) continue;
        lane.stations.forEach((s, i) => {
            const next = lane.stations[i + 1];
            if (!next || s.start === null || next.start === null) return;
            const ms = next.start - s.start;
            if (ms < DG_GAP_MIN) return;
            const pause = ms >= DG_GAP_PAUSE;
            const text = pause && ms < 3600000 ? `${Math.round(ms / 60000)} min` : duration(ms);
            const half = x => (hangs(x, lane) ? 4 : dgHalf(x));
            const room = next.at - s.at - half(s) - half(next) - 10;
            if (room < (H ? text.length * 6.6 + 18 : 22)) return;
            const cls = 'dg-gap' + (pause ? ' pause' : ms >= DG_GAP_LONG ? ' long' : '');
            dgLabel(v, labels, `gap:${next.key}`, xy((s.at + next.at) / 2, lane.row), text, cls, fresh, waiting.has(next.key));
        });
    }
    for (const [k, p] of g.paths) if (!seen.has(k)) {
        p.classList.add('leave');
        setTimeout(() => p.remove(), 300);
        g.paths.delete(k);
    }
    for (const [k, t] of g.labels) if (!labels.has(k)) {
        t.classList.add('leave');
        setTimeout(() => t.remove(), 300);
        g.labels.delete(k);
    }

    // Las paradas. La de ahora es la del último paso mientras la sesión trabaja, o la del momento de la repetición; es
    // la que se sigue, y sin ella, la más avanzada.
    const now = (moment.working || moment.replaying) && moment.now ? moment.now.order : null;
    const where = new Map();
    const keep = new Set();
    v.nodeData = new Map();
    let active = null;
    g.rows = new Map();
    for (const lane of model.lanes) for (const s of lane.stations) {
        const at = spot(s, lane);
        dgStation(v, s, lane, at, now, moment, fresh);
        if (!g.rows.has(lane.row)) g.rows.set(lane.row, []);
        g.rows.get(lane.row).push({ s, lane, along: H ? at[0] : at[1] });
        keep.add(s.key);
        g.pos.set(s.key, at);
        for (const o of s.orders) where.set(o, at);
        if (now !== null && s.orders.includes(now)) active = { s, lane };
    }
    for (const list of g.rows.values()) list.sort((a, b) => a.along - b.along);
    if (!active) for (const lane of model.lanes) for (const s of lane.stations) if (!active || s.at > active.s.at) active = { s, lane };
    g.activeAt = active ? d.start + active.s.at : 0;
    g.activeRow = active ? active.lane.row : 1;
    g.activeCross = across(g.activeRow);
    g.placed = model.lanes.flatMap(l => l.stations.map(s => ({ along: d.start + s.at, orders: s.orders })));
    // La corriente: mientras la sesión trabaja, o en la repetición, unos puntos corren por el tramo que lleva a la parada
    // de ahora; si llega con el marcador, cuando este ha llegado.
    const flowing = active && now !== null ? lead.get(now) : null;
    const current = flowing && g.paths.get(flowing);
    if (!g.current) {
        g.current = svg('path', { class: 'dg-current' });
        v.dgLines.append(g.current);
    }
    g.currentOn = Boolean(current);
    g.current.classList.toggle('on', g.currentOn && !train.length);
    if (current) {
        g.current.style.d = current.style.d;
        g.current.style.stroke = current.style.stroke;
        v.dgLines.append(g.current);
    }
    // El anillo de «estás aquí» va a la parada en que se está. Si llegan paradas nuevas, las recorre él (dgTrain); si no,
    // se desliza hasta ella.
    const here = g.here;
    here.hidden = !active;
    if (active) {
        const target = spot(active.s, active.lane);
        // En una pregunta, el anillo no late: ya llama la pregunta.
        here.className = 'dg-here' + (dgMajor(active.s) ? ' major' : '') + (active.s.final ? ' final' : dgSoft(active.s) ? ' soft' : '') + (moment.working ? ' live' : moment.replaying ? ' replay' : '')
            + (active.s.kind === 'ask' ? ' quiet' : '');
        paint(here, active.lane.id === 'you' ? 'user' : active.lane.id);
        g.hereFinal = target;
        if (train.length && g.hereAt) here.classList.add('jump');
        else {
            if (fresh || turned) {
                here.classList.add('jump');
                requestAnimationFrame(() => here.classList.remove('jump'));
            }
            here.style.transform = `translate(${target[0]}px, ${target[1]}px)`;
            g.hereAt = target;
        }
    }
    // Lo que se va, a la parada que se queda con sus pasos, si la hay: así se ve en qué fase se junta.
    for (const [k, node] of v.nodes) if (!keep.has(k)) {
        const to = (node.dataset.orders || '').split(',').filter(Boolean).map(o => where.get(Number(o))).find(Boolean);
        if (to && !turned) node.style.transform = `translate(${to[0]}px, ${to[1]}px)`;
        node.classList.add(to ? 'merge' : 'leave');
        setTimeout(() => node.remove(), to ? 550 : 260);
        v.nodes.delete(k);
    }
    g.where = where;
    if (train.length && active && g.hereAt) dgTrain(v, train);
    else for (const s of train) dgArrive(v, s.key, false);
    return turned;
}

/** Media anchura de la chapa de una parada, para medir el hueco que queda entre dos. */
function dgHalf(s) {
    if (!dgMajor(s) || dgSoft(s)) return 18;
    return s.final ? 24 : s.kind === 'end' ? 18 : 23;
}

/** Si una parada es un mensaje que no pesa (pensar o responder sin ser la respuesta final): va del tamaño de una
 * herramienta, con un aro fino. */
function dgSoft(s) {
    return (s.kind === 'think' || s.kind === 'pending' || s.kind === 'say') && !s.final;
}

/** Una línea del plano por su clave: la crea si no estaba y le pone su trazo, hacia el que se desliza si ya estaba. La que
 * lleva a una parada que va a traer el marcador (`waiting`) nace sin dibujar, y la dibuja él; otra que aparece, se
 * funde. */
function dgPath(v, seen, key, d, cls, color, fresh, waiting) {
    let p = v.dg.paths.get(key);
    if (!p) {
        p = svg('path', { class: cls + (fresh || waiting ? '' : ' enter') });
        // La longitud a 1 deja dibujar el trazo de punta a punta; al llegar se quita, para que las rayas vuelvan a su medida.
        if (waiting) {
            p.setAttribute('pathLength', '1');
            p.style.strokeDasharray = '1 1';
            p.style.strokeDashoffset = '1';
        }
        v.dgLines.insertBefore(p, v.dg.current || null);
        v.dg.paths.set(key, p);
        if (!fresh && !waiting) setTimeout(() => p.classList.remove('enter'), 500);
    }
    p.style.d = `path("${d}")`;
    p.classList.toggle('folded', /\bfolded\b/.test(cls));
    if (color) {
        p.style.stroke = color;
        p.style.color = color;
    }
    seen.add(key);
}

/** Un texto del plano por su clave, centrado en su sitio, al que se desliza si ya estaba. El de una parada que va a
 * traer el marcador espera a que llegue. */
function dgLabel(v, seen, key, [x, y], text, cls, fresh, waiting) {
    let t = v.dg.labels.get(key);
    if (!t) {
        t = el('span', cls + (waiting ? ' await' : fresh ? '' : ' enter'));
        v.layer.append(t);
        v.dg.labels.set(key, t);
        if (!fresh && !waiting) setTimeout(() => t.classList.remove('enter'), 500);
    }
    t.className = cls + (t.classList.contains('await') ? ' await' : '') + (t.classList.contains('enter') ? ' enter' : '');
    t.textContent = text;
    t.style.transform = `translate(${x}px, ${y}px) translate(-50%, -50%)`;
    seen.add(key);
}

// EL MARCADOR QUE RECORRE LA LÍNEA
//
// Cuando llegan paradas nuevas, el anillo de «estás aquí» va de la parada en que estaba a cada una de ellas por la línea
// que las une: el tramo se dibuja tras él, la parada aparece al llegar y, si el plano sigue lo que pasa, la cámara va con
// él. Un solo movimiento, para que se vea de dónde viene lo nuevo.

// Cuántas paradas nuevas recorre el marcador de una vez; más, aparecen todas juntas.
const DG_TRAIN_MAX = 12;
// Sin animaciones si el sistema lo pide.
const DG_STILL = matchMedia('(prefers-reduced-motion: reduce)');

/** Recorre las paradas nuevas una detrás de otra, cada una en su tramo, más deprisa cuantas más son. */
function dgTrain(v, train) {
    const g = v.dg;
    const id = (g.trainId || 0) + 1;
    g.trainId = id;
    g.train = { id, final: { at: g.activeAt, row: g.activeRow, cross: g.activeCross } };
    const each = train.length === 1 ? 680 : Math.max(150, Math.min(520, 1500 / train.length));
    let k = 0;
    const leg = () => {
        if (g.trainId !== id) return;
        if (k >= train.length) {
            dgTrainEnd(v);
            return;
        }
        const s = train[k];
        const to = g.pos.get(s.key);
        if (!to) {
            k++;
            leg();
            return;
        }
        const lead = g.leadOf.get(s.key);
        const route = dgRoute(g.hereAt || to, lead ? g.geo.get(lead) : null, to);
        const segs = (g.into.get(s.key) || []).map(key => g.paths.get(key)).filter(p => p && p.hasAttribute('pathLength'));
        const t0 = performance.now();
        const frame = now => {
            if (g.trainId !== id) return;
            const t = Math.min(1, (now - t0) / each);
            const e = train.length === 1 ? (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2) : t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2;
            const [x, y] = route(e);
            g.here.style.transform = `translate(${x}px, ${y}px)`;
            g.hereAt = [x, y];
            for (const p of segs) p.style.strokeDashoffset = String(1 - e);
            if (v.follow) {
                g.activeAt = g.H ? x : y;
                g.activeCross = g.H ? y : x;
                dgFollow(v, true);
            }
            if (t < 1) requestAnimationFrame(frame);
            else {
                dgArrive(v, s.key, true);
                k++;
                leg();
            }
        };
        requestAnimationFrame(frame);
    };
    leg();
}

/** Deja una parada que traía el marcador como si ya hubiera llegado: a la vista (`pop`, apareciendo), con sus tramos
 * dibujados y su tiempo. */
function dgArrive(v, key, pop) {
    const g = v.dg;
    g.waiting.delete(key);
    const node = v.nodes.get(key);
    if (node && node.classList.contains('await')) {
        node.classList.remove('await');
        if (pop) {
            node.classList.add('arrive');
            setTimeout(() => node.classList.remove('arrive'), 450);
        }
    }
    for (const k of g.into.get(key) || []) {
        const p = g.paths.get(k);
        if (!p || !p.hasAttribute('pathLength')) continue;
        p.removeAttribute('pathLength');
        p.style.strokeDasharray = '';
        p.style.strokeDashoffset = '';
    }
    const t = g.labels.get(`gap:${key}`);
    if (t) t.classList.remove('await');
}

/** Acaba el recorrido en curso, si lo hay: lo que faltaba aparece, el marcador queda en la parada de ahora y la cámara
 * vuelve a seguirla a ella. */
function dgTrainEnd(v) {
    const g = v.dg;
    if (!g || !g.train) return;
    const { final } = g.train;
    g.train = null;
    g.trainId = (g.trainId || 0) + 1;
    for (const key of [...(g.waiting || [])]) dgArrive(v, key, false);
    g.activeAt = final.at;
    g.activeRow = final.row;
    g.activeCross = final.cross;
    if (g.hereFinal) {
        g.here.style.transform = `translate(${g.hereFinal[0]}px, ${g.hereFinal[1]}px)`;
        g.hereAt = g.hereFinal;
    }
    requestAnimationFrame(() => g.here.classList.remove('jump'));
    if (g.current) g.current.classList.toggle('on', Boolean(g.currentOn));
}

/** El camino del marcador hasta una parada: desde donde está hasta el principio de su tramo, si no estaba allí, y luego
 * por el tramo (recto o en curva); sin tramo, recto hasta ella. Devuelve el punto a cada fracción del camino. */
function dgRoute(from, geo, to) {
    const lerp = (a, b) => u => [a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u];
    const bezier = ({ a, c1, c2, b }) => u => {
        const w = 1 - u;
        return [0, 1].map(i => w * w * w * a[i] + 3 * w * w * u * c1[i] + 3 * w * u * u * c2[i] + u * u * u * b[i]);
    };
    const parts = [];
    const start = geo ? geo.a : from;
    if (Math.hypot(start[0] - from[0], start[1] - from[1]) > 4) parts.push(lerp(from, start));
    if (geo) parts.push(geo.c1 ? bezier(geo) : lerp(geo.a, geo.b));
    const end = geo ? geo.b : start;
    if (Math.hypot(to[0] - end[0], to[1] - end[1]) > 4) parts.push(lerp(end, to));
    if (!parts.length) return () => to;
    const lengths = parts.map(f => {
        let len = 0;
        let p = f(0);
        for (let i = 1; i <= 12; i++) {
            const q = f(i / 12);
            len += Math.hypot(q[0] - p[0], q[1] - p[1]);
            p = q;
        }
        return Math.max(1, len);
    });
    const total = lengths.reduce((a, b) => a + b, 0);
    return e => {
        let dist = e * total;
        for (let i = 0; i < parts.length; i++) {
            if (dist <= lengths[i] || i === parts.length - 1) return parts[i](Math.min(1, dist / lengths[i]));
            dist -= lengths[i];
        }
        return to;
    };
}

// Paradas que pesan en el plano: los mensajes y los hitos van en grande, con su rótulo encima; las herramientas, en
// pequeño en la línea, con su icono y el rótulo debajo.
const DG_MAJOR = new Set(['prompt', 'say', 'think', 'pending', 'ask', 'launch', 'start', 'end', 'wait']);

// Lo que se hace con cada herramienta, dicho como lo diría una persona; las que no están, por su familia.
const DG_VERBS = { Write: 'Escribe', WebSearch: 'Busca en la web', WebFetch: 'Abre', ToolSearch: 'Busca herramientas', TodoWrite: 'Apunta tareas', BashOutput: 'Mira la salida', KillShell: 'Para', KillBash: 'Para', TaskStop: 'Para', Monitor: 'Vigila', NotebookEdit: 'Edita', SubagentHandback: 'Devuelve el trabajo' };
const DG_FAMILY_VERBS = { read: 'Lee', look: 'Busca', edit: 'Edita', run: 'Ejecuta', skill: 'Usa' };

/** Si una parada es de las que pesan: un mensaje, un hito o lo que se entrega (commit y push). */
function dgMajor(s) {
    return DG_MAJOR.has(s.kind) || Boolean(s.deliver);
}

/** El verbo y el objeto de una racha de llamadas: «Lee» y «7 ficheros», «Edita» y «proposal.md». */
function dgVerb(s) {
    const mcp = mcpServer(s.tool);
    const verb = DG_VERBS[s.tool] || DG_FAMILY_VERBS[s.family] || (mcp ? 'Consulta' : s.tool);
    const targets = s.targets || [];
    const units = { read: 'ficheros', edit: 'ficheros', run: 'órdenes', look: 'búsquedas' }[s.family];
    let object = '';
    if (s.family === 'skill') object = s.tool;
    else if (mcp) object = `${mcp.server} · ${mcp.tool}`;
    else if (targets.length > 1 && units) object = `${s.family === 'look' || s.family === 'run' ? s.count : targets.length} ${units}`;
    else if (targets.length) object = targets[0];
    return [verb, object];
}

/** El título y el subtítulo de una parada, en pocas palabras. */
function dgWords(s) {
    switch (s.kind) {
        case 'prompt': return s.shell
            ? ['Comando', clip(oneLine(s.node.step.text || '').replace(/^!\s*/, ''), 36)]
            : [`Prompt ${s.node.step.interaction}`, clip(oneLine(plain(s.node.step.text || '')), 36)];
        case 'say': return [s.final ? 'Respuesta final' : 'Responde', clip(oneLine(plain(s.node.step.text || '')), 36)];
        case 'think': return ['Piensa', clip(oneLine(plain(s.node.step.text || '')), 36)];
        case 'pending': return ['Pensando', 'sin escribir aún'];
        case 'ask': return ['Te pregunta', clip(oneLine(plain((s.node.last && s.node.last.questions && s.node.last.questions[0] && s.node.last.questions[0].question) || '')), 36)];
        case 'launch': return [s.children.length === 1 ? 'Lanza un agente' : `Lanza ${s.children.length} agentes`, clip(s.children.map(c => actorName(c)).join(', '), 36)];
        case 'start': return [clip(actorName(s.lane), 34), [s.agent && s.agent.type, s.agent && modelLabel(s.agent.model, s.agent.effort)].filter(Boolean).join(' · ')];
        case 'end': return ['Vuelve', s.agent ? duration(between(s.agent.startUtc, s.agent.endUtc)) : ''];
        case 'wait': return ['Esperando a sus agentes', `el turno acabó a las ${hour(s.wait.since)}`];
        case 'phase': return [s.title, s.sub];
        default: {
            if (s.deliver) return ['Entrega', clip((s.targets || [])[0] || '', 36)];
            const [verb, object] = dgVerb(s);
            return [verb, clip(object, 24)];
        }
    }
}

/** El subtítulo de una parada en su rótulo: el de un mensaje (tu prompt, lo que piensa o responde), con su Markdown en
 * línea como en el directo; el de lo demás, tal cual. */
function dgSub(s, sub) {
    const text = !s.shell && ['prompt', 'say', 'think'].includes(s.kind) && s.node && s.node.step && s.node.step.text;
    return text ? previewMd(plain(text), 36) : sub;
}

/** Lo que enseña la ficha de una parada: su nodo del flujo, con su sitio en el plano. Una fase es una racha de llamadas
 * con su nombre, y la espera a los agentes no tiene ficha. */
function dgNodeData(s, x, y) {
    if (s.kind === 'phase') return { kind: 'tools', tool: `${s.title} · ${s.sub}`, key: s.key, lane: s.lane, orders: s.orders, x, y };
    if (s.kind === 'wait' || s.kind === 'pending') return { kind: s.kind === 'wait' ? 'agentswait' : 'pending', key: s.key, lane: 'main', orders: [], x, y };
    return Object.assign(s.node, { key: s.key, x, y });
}

// El color de lo que hace cada parada: las herramientas, por lo que hacen; pensar y responder, el suyo. La línea, las
// llegadas de tus prompts, la respuesta final y los agentes siguen del color de quien actúa.
const DG_HUES = { read: 'read', search: 'search', web: 'search', edit: 'edit', write: 'edit', terminal: 'run' };
const DG_FAMILY_HUES = { read: 'read', look: 'search', edit: 'edit', run: 'run', understand: 'read', change: 'edit', check: 'run' };

/** El tono de una parada, o null si va del color de su actor. */
function dgHue(s) {
    if (s.kind === 'think' || s.kind === 'pending') return 'think';
    if (s.kind === 'say' && !s.final) return 'say';
    if (dgMajor(s)) return null;
    if (s.kind !== 'phase' && s.tool) return DG_HUES[toolKind(s.tool)] || 'other';
    return DG_FAMILY_HUES[s.family] || 'other';
}

/** La chapa de una parada, como la estación de un plano de metro. Las que pesan, redondas con un aro grueso y su icono
 * (las de llegada y la respuesta final, llenas). Las herramientas, un punto lleno de su color con su icono en blanco y,
 * si son varias llamadas seguidas, cuántas en una chapita. */
function dgChip(s) {
    const chip = el('span', 'dg-chip');
    if (dgMajor(s)) {
        if (s.kind === 'wait') chip.append(el('span', 'orbit'));
        else chip.append(flowIcon(DG_ICONS[s.deliver ? 'deliver' : s.kind] || 'tool', 'dg-icon'));
        if (s.kind === 'pending') {
            const dots = el('span', 'wf-think-dots');
            dots.append(el('i'), el('i'), el('i'));
            chip.append(dots);
        }
        return chip;
    }
    const kind = s.kind === 'phase' || !s.tool ? DG_ICONS[s.family] || 'tool' : s.family === 'skill' ? 'skill' : toolIcon(s.tool);
    chip.append(flowIcon(kind, 'dg-icon'));
    const n = s.kind === 'phase' ? s.members.length : s.count || 1;
    if (n > 1) chip.append(el('b', 'dg-badge', n > 99 ? '99+' : String(n)));
    return chip;
}

/** Una parada en su sitio, con su chapa y su rótulo, del color de su actor o de lo que hace; la de ahora, en rojo, con
 * «AHORA» si es en directo, y como en el directo: un aro que gira si es una herramienta, ondas si es una pregunta que
 * espera. Al pulsarla se abre su ficha.
 *
 * Una que entra sale de donde estaba lo que lleva (la fase que se abre, o la parada que se junta en ella); si la trae el
 * marcador, espera oculta a que llegue; si no, aparece. */
function dgStation(v, s, lane, [x, y], now, moment, fresh) {
    const live = moment.working;
    const isNow = now !== null && s.orders.includes(now);
    const [title, sub] = dgWords(s);
    const major = dgMajor(s);
    const asking = s.kind === 'ask' && Boolean(s.node) && wfAsking(s.node, isNow);
    const sig = [s.kind, s.family, s.count, s.members && s.members.length, title, sub, isNow, live, asking, s.final, s.unread, s.deliver, s.errors, lane.id, lane.folded].join('|');
    let node = v.nodes.get(s.key);
    if (!node) {
        node = el('div', 'dg-st');
        const from = !fresh && s.orders.map(o => v.dg.where.get(o)).find(Boolean);
        if (from) node.style.transform = `translate(${from[0]}px, ${from[1]}px)`;
        else if (v.dg.waiting.has(s.key)) node.classList.add('await');
        else if (!fresh) {
            node.classList.add('enter');
            setTimeout(() => node.classList.remove('enter'), 600);
        }
        v.layer.append(node);
        if (from) node.getBoundingClientRect();
        node.addEventListener('click', e => {
            e.stopPropagation();
            if (v.dragged) return;
            v.detail = node.dataset.key;
            state.focus = null;
            decorate();
            wfDetail(v, v.detail);
        });
        v.nodes.set(s.key, node);
    }
    node.style.transform = `translate(${x}px, ${y}px)`;
    node.dataset.key = s.key;
    node.dataset.orders = s.orders.join(',');
    v.nodeData.set(s.key, dgNodeData(s, x, y));
    if (node.dataset.sig !== sig) {
        node.dataset.sig = sig;
        const calls = s.kind === 'phase' ? s.members.length : s.count || 1;
        const more = node.dataset.n !== undefined && calls > Number(node.dataset.n);
        node.dataset.n = String(calls);
        const keepClasses = ['enter', 'await', 'arrive'].filter(c => node.classList.contains(c));
        node.className = `dg-st k-${s.kind} f-${s.family} ${major ? 'major' : 'minor'}` + (s.deliver ? ' deliver' : '') + (s.final ? ' final' : '') + (s.unread ? ' unread' : '')
            + (s.errors ? ' has-error' : '') + (lane.folded ? ' folded' : '') + (isNow ? ' now' : '') + (isNow && live && !major ? ' busy' : '') + (asking ? ' asking' : '')
            + keepClasses.map(c => ` ${c}`).join('');
        paint(node, lane.id === 'you' ? (s.shell ? 'shell' : 'user') : lane.id);
        const hue = dgHue(s);
        if (hue) node.style.setProperty('--actor', `var(--dg-${hue})`);
        const label = el('span', 'dg-lbl');
        label.append(el('b', null, isNow && live ? `AHORA · ${title}` : title));
        if (sub) {
            const line = el('span');
            line.append(dgSub(s, sub));
            label.append(line);
        }
        if (s.unread) label.append(el('i', 'dg-unread', 'Sin revisar'));
        const chip = dgChip(s);
        node.replaceChildren(chip, label);
        // Una llamada más a la misma herramienta: la chapita de cuántas salta.
        const badge = more && chip.querySelector('.dg-badge');
        if (badge) badge.classList.add('bump');
        const order = s.orders[0];
        if (order !== undefined && s.kind !== 'phase') {
            node.dataset.tip = `q:${order}`;
            node.dataset.tipActor = lane.id === 'you' ? 'user' : lane.id;
        } else {
            delete node.dataset.tip;
            delete node.dataset.tipActor;
        }
    }
    node.classList.toggle('picked', v.detail === s.key);
    return node;
}

/** Lo que se ve del lienzo para el plano: lo de wfSight, que ya descuenta la franja de abajo con la regla de pasos. */
function dgSight(v) {
    return wfSight(v);
}

/** Lleva el plano a una parada sin seguir lo que pasa: la deja a un tercio de lo que se ve. */
function dgShowStation(v, key) {
    const g = v.dg;
    const at = g.pos && g.pos.get(key);
    if (!at) return;
    wfMovedByHand(v);
    v.follow = false;
    $('flow-follow').classList.remove('on');
    const sight = dgSight(v);
    if (g.H) v.cam.x = sight.x + sight.w * 0.35 - at[0] * v.cam.z;
    else v.cam.y = sight.y + sight.h * 0.35 - at[1] * v.cam.z;
    wfCamera(v, true);
}

/** Encuadra el plano siguiendo lo que pasa: de ancho (o de alto, en vertical), las filas llenan lo que se ve, acercando
 * hasta un tope si son pocas; a lo largo, si cabe entero, desde el principio, y si no, con la parada de ahora hacia el
 * final de lo que se ve, que es por donde entra lo nuevo. */
function dgFollow(v, jump) {
    const g = v.dg;
    if (!g || !g.model) return;
    const sight = dgSight(v, g.orient);
    const along = g.H ? sight.w : sight.h;
    const cross = g.H ? sight.h : sight.w;
    if (v.autoZoom) v.cam.z = Math.max(WF_READABLE, Math.min(DG_ZOOM_MAX, (cross - 16) / g.breadth));
    const z = v.cam.z;
    let c = (cross - g.breadth * z) / 2;
    if (g.breadth * z > cross) {
        c = cross / 2 - g.activeCross * z;
        c = Math.min(0, Math.max(cross - g.breadth * z, c));
    }
    let a = 0;
    if (g.length * z > along) a = Math.max(along - g.length * z, Math.min(0, along * 0.72 - g.activeAt * z));
    v.cam.x = sight.x + (g.H ? a : c);
    v.cam.y = sight.y + (g.H ? c : a);
    wfCamera(v, !jump);
}

/** Pone donde está la cámara lo que va pegado a la pantalla: la etiqueta de cada eje en el borde, encima de su línea, y,
 * si su fila no tiene nada a la vista, la copia de su último paso; la cuadrícula del papel, a su escala; el sello, en la
 * esquina de lo libre; y en la regla de pasos, el tramo que se ve. Si la cámara se desliza, todo se desliza con ella. */
function dgRail(v, smooth) {
    const g = v.dg;
    if (!g || !g.model) return;
    const z = v.cam.z;
    v.rail.className = `dg-rail dg-${g.orient}` + (smooth ? ' glide' : '');
    v.root.classList.toggle('dg-glide', Boolean(smooth));
    v.ruler.classList.toggle('glide', Boolean(smooth));
    const rows = g.model.rows;
    const free = wfFree(v);
    // El sello, en la esquina de lo libre. En un hueco estrecho o en vertical taparía rótulos, y lo que dice ya está en la
    // barra de arriba: se queda solo la casilla de la rama.
    const compact = free.w < 720 || !g.H;
    const git = !$('flow-git').hidden;
    v.stampGit.hidden = !git;
    v.stamp.classList.toggle('compact', compact);
    v.stamp.hidden = compact && !git;
    v.stamp.style.right = `${Math.round(v.root.clientWidth - (free.x + free.w) + 14)}px`;
    $('flow-git').classList.add('in-stamp');
    dgGitPlace(v);
    // En vertical, las etiquetas de los ejes van arriba: debajo del sello, si lo hay.
    const lift = !g.H && !v.stamp.hidden ? v.stamp.offsetHeight + 8 : 0;
    while (v.rail.children.length < rows * 2) {
        const pill = el('span', 'dg-axis-pill');
        pill.addEventListener('click', () => dgToggleRow(v, Number(pill.dataset.row)));
        v.rail.append(pill, dgGhost(v));
    }
    while (v.rail.children.length > rows * 2) v.rail.lastElementChild.remove();
    const kids = [...v.rail.children];
    // A lo largo, en pantalla: dónde va la copia del último paso y dónde acaba lo que se ve.
    const camAlong = g.H ? v.cam.x : v.cam.y;
    const edge = (g.H ? free.x : free.y + lift) + (g.H ? DG_GHOST.h : DG_GHOST.v);
    const end = g.H ? free.x + free.w : free.y + free.h;
    const screen = p => camAlong + p.along * z;
    const ghosted = new Set();
    for (let row = 0; row < rows; row++) {
        const pill = kids[row * 2];
        const ghost = kids[row * 2 + 1];
        const letter = String.fromCharCode(65 + row);
        const lanes = g.model.lanes.filter(l => l.row === row && l.id !== 'you');
        const latest = lanes[lanes.length - 1];
        const thin = Boolean(g.model.thin[row]);
        const done = lanes.filter(l => l.folded || (state.dgOpen && state.dgOpen.has(l.id)));
        const who = row === 0 ? 'Tú' : row === 1 ? 'Claude' : latest ? clip(actorName(latest.id), 24) + (thin ? ' · terminó' : '') : 'Libre';
        const sig = `${letter}|${who}|${lanes.length}|${thin}|${done.length}`;
        if (pill.dataset.sig !== sig) {
            pill.dataset.sig = sig;
            pill.dataset.row = String(row);
            pill.dataset.w = String(Math.min(170, 34 + who.length * 6.4));
            pill.replaceChildren(el('i', null, letter), el('span', null, who));
            const agents = `Los agentes que han pasado por este eje: ${lanes.map(l => actorName(l.id)).join(' · ') || 'ninguno aún'}.`;
            const toggle = thin ? ' Ya terminaron: su eje va fino. Pulsa para abrirlo.' : done.length ? ' Pulsa para recoger los que ya terminaron.' : '';
            hint(pill, letter, `Eje ${letter} · ${who}`, null, row === 0 ? 'Tus prompts.' : row === 1 ? 'La sesión.' : agents + toggle);
            paint(pill, row === 0 ? 'user' : row === 1 ? 'main' : latest ? latest.id : 'main');
        }
        pill.classList.toggle('thin', thin);
        pill.classList.toggle('can-fold', row > 1 && (thin || done.length > 0));
        const at = g.crossings[row] * z + (g.H ? v.cam.y : v.cam.x);
        // La de una fila fina, algo más pegada a su línea: encima solo hay sitio hasta la fila de antes.
        pill.style.transform = g.H ? `translate(${free.x + 10}px, ${at - (thin ? DG_PILL_RISE_THIN : DG_PILL_RISE)}px)` : `translate(${at}px, ${free.y + 12 + lift}px)`;
        // La copia del último paso: la fila no tiene nada a la vista y sí algo antes.
        const list = g.rows.get(row) || [];
        let before = null;
        let seen = false;
        for (const p of list) {
            const x = screen(p);
            if (x < edge) before = p;
            else if (x <= end) {
                seen = true;
                break;
            }
        }
        const show = Boolean(before) && !seen && !thin;
        ghost.hidden = !show;
        if (show) {
            dgGhostFill(ghost, before);
            ghost.style.transform = g.H ? `translate(${edge}px, ${at}px)` : `translate(${at}px, ${edge}px)`;
            ghosted.add(before.s.key);
        }
        // La etiqueta se queda en la letra si va a tocar una parada que pasa por debajo, o su rótulo.
        const w = Number(pill.dataset.w);
        const near = (p, from, to) => {
            const x = screen(p);
            return x + DG_PILL_MARGIN > from && x - DG_PILL_MARGIN < to;
        };
        let tight;
        // En vertical, una columna fina no tiene sitio para el nombre.
        if (!g.H && thin) tight = true;
        else if (g.H) tight = list.some(p => dgMajor(p.s) && !p.lane.folded && !ghosted.has(p.s.key) && near(p, free.x + 10, free.x + 10 + w));
        else {
            const top = free.y + 12 + lift;
            tight = list.some(p => !ghosted.has(p.s.key) && near(p, top, top + 22));
        }
        pill.classList.toggle('tight', tight);
    }
    // La parada de verdad que se queda en el borde no se ve dos veces.
    for (const key of g.ghosted || []) if (!ghosted.has(key)) v.nodes.get(key)?.classList.remove('ghosted');
    for (const key of ghosted) v.nodes.get(key)?.classList.add('ghosted');
    g.ghosted = ghosted;
    const grid = 24 * z;
    v.root.style.backgroundSize = `${grid * 5}px ${grid * 5}px, ${grid * 5}px ${grid * 5}px, ${grid}px ${grid}px, ${grid}px ${grid}px`;
    v.root.style.backgroundPosition = `${v.cam.x}px ${v.cam.y}px`;
    v.root.classList.toggle('dg-vert', !g.H);
    // La franja de abajo va a lo ancho de lo libre: con la ficha a la izquierda, empieza tras ella.
    $('flow-dock').style.left = `${Math.round(free.x + 14)}px`;
    dgRulerPlace(v);
}

// Cuánto sube la etiqueta de un eje sobre su línea, en horizontal; cuánto se aparta, a lo largo, de una parada que pasa
// (la mitad del rótulo de una estación), y dónde va, desde el borde, la copia del último paso de una fila.
const DG_PILL_RISE = 30;
const DG_PILL_RISE_THIN = 15;
const DG_PILL_MARGIN = 70;
const DG_GHOST = { h: 40, v: 64 };

/** La copia del último paso de una fila, en el borde: la chapa, tenue, y su nombre con la hora. Al pulsarla, el plano va
 * allí. */
function dgGhost(v) {
    const ghost = el('div', 'dg-st dg-ghost');
    ghost.hidden = true;
    ghost.addEventListener('click', e => {
        e.stopPropagation();
        dgShowStation(v, ghost.dataset.key);
    });
    for (const type of ['pointerdown', 'mousedown', 'wheel']) ghost.addEventListener(type, e => e.stopPropagation());
    return ghost;
}

/** Pone en la copia del borde la parada que toca, si ha cambiado. */
function dgGhostFill(ghost, { s, lane }) {
    const sig = `${s.key}|${s.count || ''}|${s.final}`;
    if (ghost.dataset.sig === sig) return;
    ghost.dataset.sig = sig;
    ghost.dataset.key = s.key;
    const [title] = dgWords(s);
    ghost.className = `dg-st dg-ghost k-${s.kind} ${dgMajor(s) ? 'major' : 'minor'}` + (s.final ? ' final' : '') + (s.deliver ? ' deliver' : '');
    paint(ghost, lane.id === 'you' ? (s.shell ? 'shell' : 'user') : lane.id);
    const hue = dgHue(s);
    if (hue) ghost.style.setProperty('--actor', `var(--dg-${hue})`);
    const label = el('span', 'dg-lbl');
    label.append(el('b', null, `‹ ${clip(title, 22)}`), el('span', null, hour(s.start)));
    ghost.replaceChildren(dgChip(s), label);
    // La misma ficha que la parada de verdad, con su mensaje; una fase o una espera, que no tienen un paso, su nombre.
    const order = s.orders[0];
    if (order !== undefined && s.kind !== 'phase') {
        ghost.dataset.tip = `q:${order}`;
        ghost.dataset.tipActor = lane.id === 'you' ? 'user' : lane.id;
    } else {
        delete ghost.dataset.tipActor;
        hint(ghost, '‹', title, null, dgWords(s)[1] || '');
    }
}

/** Engancha una vez los botones de la escala, que van en la barra de abajo junto a los del directo. */
function dgWire() {
    const scale = $('dg-scale');
    if (scale.dataset.wired) return;
    scale.dataset.wired = '1';
    scale.addEventListener('click', e => {
        const b = e.target.closest('[data-far]');
        if (!b) return;
        state.dgFar = b.dataset.far === '1';
        renderDiagram(false);
    });
    hint(scale.children[0], '◉', 'De cerca', null, 'Una parada por paso: cada respuesta, cada pensamiento y cada racha de llamadas a la misma herramienta.');
    hint(scale.children[1], '◎', 'De lejos', null, 'Los pasos seguidos de una misma clase, juntos en una fase: entender, cambiar o comprobar.');
}

/** El sello del plano, arriba a la derecha: el proyecto y el prompt que se ve, y la escala con su barra. */
function dgStamp(v, model, far) {
    const t = state.trace;
    const prompts = model.lanes.find(l => l.id === 'you').stations.filter(s => !s.shell).map(s => s.node.step.interaction);
    const total = t.interactions.length;
    const plan = prompts.length === 1 ? `Prompt ${prompts[0]} de ${total}` : prompts.length ? `Prompts ${prompts[0]} a ${prompts[prompts.length - 1]} de ${total}` : `${total} prompts`;
    const sig = [placeOf(t), plan, far].join('|');
    if (v.stamp.dataset.sig === sig) return;
    v.stamp.dataset.sig = sig;
    const what = el('div', 'dg-stamp-what');
    what.append(el('b', null, placeOf(t) || t.id.slice(0, 8)), el('span', null, plan));
    v.stamp.replaceChildren(what, v.stampGit);
    dgStampGit(v);
}

/** Pinta la casilla de la rama del sello: el icono en su disco y, como la casilla de al lado, el nombre en fuerte y debajo
 * lo que pasa en palabras: los commits sin subir en azul, lo pendiente con sus líneas en verde y rojo y la palabra en
 * amarillo, o que está al día. Lleva la misma ficha que la rama del directo, de la que en el Diagrama solo se ve su lista,
 * debajo del sello. */
function dgStampGit(v = state.wf) {
    if (!v || !v.stampGit) return;
    const d = state.changes.sets.pending;
    const files = d && !d.error ? d.files : [];
    const total = changeTotals(files);
    const branch = (d && d.branch) || (state.trace && state.trace.branch) || '';
    const disc = el('span', 'dg-git-disc');
    disc.append(flowIcon('branch', 'flow-icon'));
    const name = el('span', 'dg-git-name');
    name.append(el('b', null, branch || 'Sin rama'), el('span', 'dg-git-caret', state.changes.listOpen ? '▴' : '▾'));
    const parts = [];
    if (d && d.ahead) parts.push([el('span', 'dg-git-up', `↑${d.ahead} sin subir`)]);
    else if (d && !d.error && d.branch && !d.upstream) parts.push([el('span', 'dg-git-pending', 'sin publicar')]);
    if (files.length) parts.push([changeNums(total.added, total.deleted), el('span', 'dg-git-pending', 'pendiente')]);
    if (!parts.length && d && !d.error) parts.push([el('span', 'dg-git-ok', '✓ al día')]);
    if (d && d.error) parts.push([el('span', null, 'sin datos de git')]);
    const status = el('small', 'dg-git-state');
    parts.forEach((p, i) => status.append(...(i ? [' · '] : []), ...p));
    const words = el('span', 'dg-git-words');
    words.append(name);
    if (parts.length) words.append(status);
    v.stampGit.replaceChildren(disc, words);
    const head = $('flow-git-head');
    if (head.dataset.tip) v.stampGit.dataset.tip = head.dataset.tip;
}

/** Lleva la lista de lo pendiente de la rama justo debajo del sello, alineada con él. */
function dgGitPlace(v) {
    const git = $('flow-git');
    if (!v.stamp || v.stamp.hidden || !git.parentElement) return;
    const s = v.stamp.getBoundingClientRect();
    const p = git.parentElement.getBoundingClientRect();
    if (!s.height) return;
    git.style.top = `${Math.round(s.bottom - p.top + 6)}px`;
    git.style.right = `${Math.round(p.right - s.right)}px`;
}

// LA REGLA DE PASOS: todos los pasos de lo elegido en una regla, en medio de la franja de abajo, cada uno con una marca
// del color de quien lo hizo, la parte que enseña el plano recuadrada y el paso en que se está. Pulsarla o arrastrarla
// lleva la repetición a ese paso, y ← → pasan de uno en uno, como siempre.

// Columnas en que se reparten las marcas: con miles de pasos, cada columna lleva la del actor que más pasos tiene en ella.
const DG_RULER_COLUMNS = 600;

/** Monta la regla la primera vez: sus marcas, el recuadro de lo que se ve, el paso en que se está y su rótulo. */
function dgRulerMount(v) {
    const ruler = el('div', 'dg-ruler');
    const marks = svg('svg', { class: 'dg-ruler-marks', preserveAspectRatio: 'none' });
    const brush = el('div', 'dg-ruler-brush');
    const cursor = el('div', 'dg-ruler-cursor');
    cursor.append(el('span', 'dg-ruler-label'));
    ruler.append(marks, brush, cursor);
    // Se pulsa o se arrastra: la repetición va al paso que queda debajo, y el plano lo sigue.
    let drag = false;
    let queued = null;
    const seek = e => {
        const r = ruler.getBoundingClientRect();
        const frac = (e.clientX - r.left) / r.width;
        const steps = selectedSteps();
        const index = Math.round(Math.max(0, Math.min(1, frac)) * (steps.length - 1));
        if (queued !== null) {
            queued = index;
            return;
        }
        queued = index;
        requestAnimationFrame(() => {
            const to = queued;
            queued = null;
            v.follow = true;
            if (to >= steps.length - 1 && state.replay.index !== null) exitReplay();
            else if (to < steps.length - 1) goTo(to);
        });
    };
    ruler.addEventListener('pointerdown', e => {
        if (e.button !== 0) return;
        e.stopPropagation();
        e.preventDefault();
        drag = true;
        ruler.setPointerCapture(e.pointerId);
        seek(e);
    });
    ruler.addEventListener('pointermove', e => drag && seek(e));
    ruler.addEventListener('pointerup', () => { drag = false; });
    ruler.addEventListener('pointercancel', () => { drag = false; });
    for (const type of ['mousedown', 'click', 'wheel']) ruler.addEventListener(type, e => e.stopPropagation());
    hint(ruler, '⇥', 'Regla de pasos', null, 'Cada marca es un paso, del color de quien lo hizo; lo recuadrado es lo que se ve. Pulsa o arrastra para ir a un paso, y ← → para pasar de uno en uno.');
    $('dg-ruler-slot').replaceChildren(ruler);
    v.ruler = ruler;
}

/** Pone al día la regla: las marcas, si han cambiado los pasos, y el paso en que se está. */
function dgRuler(v, steps, moment) {
    const g = v.dg;
    const ruler = v.ruler;
    const n = steps.length;
    g.stepIndex = new Map(steps.map((s, i) => [s.order, i]));
    g.stepCount = n;
    const sig = `${n}|${state.trace.id}`;
    if (ruler.dataset.sig !== sig) {
        ruler.dataset.sig = sig;
        const marks = ruler.querySelector('.dg-ruler-marks');
        const cols = Math.min(DG_RULER_COLUMNS, Math.max(1, n));
        const best = Array.from({ length: cols }, () => new Map());
        const prompts = new Set();
        steps.forEach((s, i) => {
            const col = n === 1 ? 0 : Math.round((i / (n - 1)) * (cols - 1));
            const who = s.agentId || (s.kind === 'prompt' ? 'user' : 'main');
            best[col].set(who, (best[col].get(who) || 0) + 1);
            if (s.kind === 'prompt' && !s.agentId) prompts.add(col);
        });
        const paths = new Map();
        best.forEach((count, col) => {
            if (!count.size) return;
            const who = [...count.entries()].sort((a, b) => b[1] - a[1])[0][0];
            const at = cols === 1 ? 500 : (col / (cols - 1)) * 1000;
            const long = prompts.has(col) ? 0 : 4;
            const d = `M${at} ${long} V 12`;
            paths.set(who, (paths.get(who) || '') + d);
        });
        marks.setAttribute('viewBox', '0 0 1000 12');
        marks.replaceChildren(...[...paths].map(([who, d]) => {
            const p = svg('path', { d, class: 'dg-ruler-mark' });
            p.style.stroke = actorColor(who);
            return p;
        }));
    }
    const at = moment.replaying ? state.replay.index : n - 1;
    const step = steps[at];
    g.stepAt = n > 1 ? at / (n - 1) : 1;
    ruler.querySelector('.dg-ruler-label').textContent = step ? `paso ${at + 1} de ${n} · ${clock(step.start)}` : '';
    ruler.classList.toggle('replay', Boolean(moment.replaying));
    ruler.classList.toggle('live', Boolean(moment.working));
}

/** Pone en la regla el recuadro de lo que enseña la cámara y la marca del paso en que se está. */
function dgRulerPlace(v) {
    const g = v.dg;
    const ruler = v.ruler;
    if (!ruler || !g.model) return;
    // Lo que se ve, de la primera a la última parada que entran en pantalla, en pasos.
    const z = v.cam.z;
    const sight = dgSight(v, g.orient);
    const lo = g.H ? (sight.x - v.cam.x) / z : (sight.y - v.cam.y) / z;
    const hi = g.H ? (sight.x + sight.w - v.cam.x) / z : (sight.y + sight.h - v.cam.y) / z;
    let first = Infinity;
    let last = -Infinity;
    for (const p of g.placed || []) {
        if (p.along < lo || p.along > hi) continue;
        for (const o of p.orders) {
            const i = g.stepIndex && g.stepIndex.get(o);
            if (i === undefined) continue;
            first = Math.min(first, i);
            last = Math.max(last, i);
        }
    }
    const n = Math.max(1, (g.stepCount || 1) - 1);
    const brush = ruler.querySelector('.dg-ruler-brush');
    brush.hidden = first === Infinity;
    if (!brush.hidden) {
        const from = (first / n) * 100;
        const size = Math.max(0.8, ((last - first) / n) * 100);
        Object.assign(brush.style, { left: `${from}%`, width: `${size}%` });
    }
    ruler.querySelector('.dg-ruler-cursor').style.left = `${(g.stepAt ?? 1) * 100}%`;
}

/** Deja la barra de abajo como la del directo al pasar a otra vista: sin la escala ni la regla, y en su esquina. */
function dgLeave() {
    const dock = document.getElementById('flow-dock');
    if (!dock) return;
    dock.classList.remove('dg');
    dock.style.left = '';
}
