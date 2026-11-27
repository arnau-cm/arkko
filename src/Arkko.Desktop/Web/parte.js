// PARTE: el trabajo de una sesión contado por fases en castellano («Leyó el encargo: 3 ficheros»).
//
// Va aparte de sesiones.js y se apoya en el modelo del Diagrama (dgModel): sus paradas ya juntan las llamadas seguidas a
// una misma herramienta. Aquí se juntan además las seguidas de una misma clase (leer, buscar, cambiar, ejecutar) en un
// mismo actor, y cada racha se escribe en una frase, en pasado o, si es lo que se está haciendo, en presente. Las rachas,
// a su vez, van dentro de las fases del Diagrama visto de lejos (dgPhases: entender, cambiar, comprobar), que es la misma
// regla para el Parte, el zoom del Diagrama y la Pista. Lo usan la vista Ahora, para sus últimas líneas, y la vista Parte,
// entera (más abajo, «La vista Parte»).

// Cómo se dice lo que hace cada clase de herramienta: en pasado y en presente, para una o para varias llamadas.
const PT_SAY = {
    read: { one: ['Leyó', 'Lee'], many: (n, now) => (now ? `Lee ${n} ficheros` : `Leyó ${n} ficheros`) },
    look: { one: ['Buscó', 'Busca'], many: (n, now) => (now ? `Hace ${n} búsquedas` : `Hizo ${n} búsquedas`) },
    edit: { one: ['Cambió', 'Cambia'], many: (n, now) => (now ? `Cambia ${n} ficheros` : `Cambió ${n} ficheros`) },
    run: { one: ['Ejecutó una orden', 'Ejecuta una orden'], many: (n, now) => (now ? `Ejecuta ${n} órdenes` : `Ejecutó ${n} órdenes`) },
};

/** Las fases de lo que se ve, en el orden en que pasaron: una por cada racha de paradas seguidas de la misma clase en un
 * mismo actor, con su frase, su verbo corto, su detalle, su color, su icono y su hora. Las que están en marcha, en
 * presente; la del paso de ahora, además, marcada (`here`). Cada una dice qué parada abre su ficha (`pick`).
 * @param {object} model El de dgModel, con sus carriles (el tuyo, el de la sesión y uno por agente).
 * @param {Set<number>} live Los órdenes de los pasos en marcha: el de ahora y el último de cada actor que trabaja.
 * @param {number|null} here El orden del paso de ahora, si la sesión trabaja o se está repitiendo. */
function parteLines(model, live, here) {
    const lines = model.lanes.flatMap(lane => parteRuns(lane.stations)).map(r => parteLine(r, live)).filter(Boolean);
    for (const l of lines) parteMark(l, here);
    lines.sort((a, b) => a.order - b.order);
    return lines;
}

/** Las rachas de unas paradas de un mismo carril: las llamadas seguidas de una misma clase, juntas; lo demás, solo. */
function parteRuns(stations) {
    const runs = [];
    let open = null;
    for (const s of stations) {
        const kind = s.kind === 'tools' && !s.deliver && ['read', 'look', 'edit', 'run'].includes(s.family) ? s.family : null;
        if (kind && open && open.kind === kind && !s.check && !open.check) {
            open.members.push(s);
            continue;
        }
        open = kind ? { kind, check: s.check, members: [s] } : null;
        runs.push(open || { kind: null, members: [s] });
    }
    return runs;
}

/** Marca en una línea si es la del paso de ahora, cómo se dice si es una pregunta y qué parada abre su ficha: la del paso
 * de ahora, si es de esta racha; si no, la última. */
function parteMark(l, here) {
    l.here = here !== null && l.orders.includes(here);
    if (l.question !== undefined) parteAsk(l);
    const at = l.here ? l.stations.find(m => m.orders.includes(here)) : null;
    l.pick = (at || l.stations[l.stations.length - 1]).key;
}

/** La frase de una pregunta, con la misma regla que el directo y el Diagrama (wfAsking): espera mientras su llamada no
 * tiene resultado, o si es el paso de ahora de la repetición, aunque esta ya sepa lo que se contestó. Contestada, con la
 * respuesta; rechazada, dicho. */
function parteAsk(l) {
    const node = l.stations[0].node;
    const q = l.question;
    l.asking = wfAsking(node, l.here);
    const words = text => clip(oneLine(plain(text || '')), 110);
    l.text = l.asking ? 'Te pregunta' : 'Te preguntó';
    if (!q) l.small = '';
    else if (l.asking) l.small = words(q.question);
    else if (q.answer) l.small = `${words(q.question)} → ${words(q.answer)}`;
    else if (node.last && node.last.isError) l.small = `${words(q.question)} → no la contestaste`;
    else l.small = words(q.question);
}

/** La línea del Parte de una racha de paradas: lo que hizo, en una frase, y lo que tocó o dijo, debajo; si es un mensaje,
 * con su texto entero en `md`, para pintarlo con su formato. */
function parteLine(run, live) {
    const s = run.members[0];
    const orders = run.members.flatMap(m => m.orders);
    const isNow = orders.some(o => live.has(o));
    const lane = s.lane === 'you' || s.kind === 'prompt' ? 'you' : s.lane;
    const base = { key: s.key, order: s.order, orders, stations: run.members, lane, start: s.start, end: run.members[run.members.length - 1].end, now: isNow };
    const step = s.node && s.node.step;
    const words = text => clip(oneLine(plain(text || '')), 110);
    switch (s.kind) {
        case 'prompt':
            if (s.shell) return { ...base, icon: 'prompt', color: actorColor('shell'), verb: 'Tu comando', text: 'Lanzaste un comando', small: clip(oneLine(step.text || '').replace(/^!\s*/, ''), 110), code: true };
            return { ...base, icon: 'prompt', color: actorColor('user'), verb: 'Tu prompt', text: `Tu prompt ${step.interaction}`, small: words(step.text), md: step.text };
        case 'think':
            return { ...base, icon: 'think', color: 'var(--dg-think)', ring: true, verb: 'Piensa', text: isNow ? 'Piensa' : 'Pensó', small: words(step && step.text), md: step && step.text };
        case 'pending':
            return { ...base, icon: 'think', color: 'var(--dg-think)', ring: true, verb: 'Piensa', text: 'Piensa', small: 'sin escribir aún' };
        case 'say':
            if (s.final) return { ...base, icon: 'reply', color: actorColor(lane), final: true, verb: 'Respuesta final', text: 'Respondió: respuesta final', small: words(step.text), md: step.text };
            return { ...base, icon: 'reply', color: 'var(--dg-say)', ring: true, verb: 'Escribe', text: isNow ? 'Escribe' : 'Dijo', small: words(step.text), md: step.text };
        case 'ask': {
            // Su frase depende de si sigue esperando, que se sabe al marcar el paso de ahora (parteAsk).
            const q = s.node.last && s.node.last.questions && s.node.last.questions[0];
            return { ...base, icon: 'question', color: 'var(--st-waiting)', verb: 'Te pregunta', question: q || null };
        }
        case 'launch':
            // Un agente cuyo eje no se ve (no consta, o lanzado en segundo plano y sin pasos aún) cuenta igual: por sus
            // llamadas, no por sus ejes.
            return { ...base, icon: 'agent', color: actorColor(lane), verb: 'Lanza agentes', text: `${isNow ? 'Lanza' : 'Lanzó'} ${agentsWord(Math.max(s.children.length, s.orders.length, 1))}`, small: s.children.map(c => actorName(c)).join(' · ') };
        case 'start':
            return null;
        case 'end':
            return { ...base, icon: 'done', color: actorColor(lane), ring: true, verb: 'Vuelve', text: 'Volvió con el resultado', small: s.agent ? `${duration(between(s.agent.startUtc, s.agent.endUtc))} en total` : '' };
        case 'wait':
            if (s.wait.kind === 'command')
                return { ...base, icon: 'terminal', color: 'var(--st-command)', verb: 'Espera a un comando', text: `Espera a ${commandsWord(s.wait.commands.length)}`, small: s.wait.commands.map(c => c.description).join(' · ') };
            return { ...base, icon: 'agent', color: 'var(--st-agents)', ring: true, verb: 'Espera a sus agentes', text: `Espera a ${agentsWord(s.wait.running.length)}`, small: s.wait.running.map(a => a.description).join(' · ') };
        default:
            return { ...base, ...parteTools(run, isNow) };
    }
}

/** La frase de una racha de llamadas: «Leyó 3 ficheros» con cuáles debajo, «Ejecutó una orden» con la orden, «Hizo commit
 * y push», «Usó la skill X». Si lo de debajo son ficheros u órdenes, lo marca con `code`. */
function parteTools(run, isNow) {
    const s = run.members[0];
    const count = run.members.reduce((n, m) => n + (m.count || 1), 0);
    const targets = [...new Set(run.members.flatMap(m => m.targets || []))];
    const errors = run.members.reduce((n, m) => n + (m.errors || 0), 0);
    const list = targets.length > 3 ? `${targets.slice(0, 3).join(' · ')} y ${targets.length - 3} más` : targets.join(' · ');
    // Lo que está en marcha aún no ha fallado, aunque la repetición ya sepa cómo acabó.
    const failed = errors && !isNow ? ` · ${errors === 1 ? '1 con error' : `${errors} con error`}` : '';
    const hue = dgHue(s);
    const look = { icon: s.tool ? (s.family === 'skill' ? 'skill' : toolIcon(s.tool)) : DG_ICONS[s.family] || 'tool', color: `var(--dg-${hue || 'other'})` };
    if (s.deliver) return { ...look, icon: 'send', color: actorColor(s.lane), verb: 'Commit y push', text: isNow ? 'Hace commit y push' : 'Hizo commit y push', small: list + failed, code: Boolean(list) };
    if (s.check) return { ...look, verb: 'Pruebas', text: isNow ? 'Ejecuta las pruebas' : 'Ejecutó las pruebas', small: list + failed, code: Boolean(list) };
    const say = PT_SAY[run.kind];
    if (say) {
        const files = run.kind === 'read' || run.kind === 'edit';
        const n = files ? Math.max(1, targets.length) : count;
        let text;
        if (n > 1) text = say.many(n, isNow);
        else if (run.kind === 'run') text = say.one[isNow ? 1 : 0];
        else text = `${say.one[isNow ? 1 : 0]} ${targets[0] || (run.kind === 'look' ? 'en el código' : 'un fichero')}`;
        const small = n > 1 || run.kind === 'run' ? list : '';
        return { ...look, verb: { read: 'Lee', look: 'Busca', edit: 'Cambia', run: 'Ejecuta' }[run.kind], text, small: small + failed, code: Boolean(small) };
    }
    if (s.family === 'skill') return { ...look, verb: 'Usa una skill', text: `${isNow ? 'Usa' : 'Usó'} la skill ${s.tool}`, small: failed.slice(3) };
    const mcp = mcpServer(s.tool);
    if (mcp) return { ...look, verb: 'Consulta', text: `${isNow ? 'Consulta' : 'Consultó'} ${mcp.server}`, small: `${mcp.tool}${count > 1 ? ` · ${count} veces` : ''}${failed}` };
    const [verb, object] = dgVerb(s);
    return { ...look, verb, text: `${verb}${object ? ` ${object}` : ''}`, small: (count > 1 ? `${count} veces` : '') + failed };
}

// LA VISTA PARTE: el trabajo de lo elegido, prompt a prompt, en fases (las del Diagrama visto de lejos) con sus rachas
// dentro; cada racha se despliega en sus pasos, y lo de cada agente va en una lámina bajo su lanzamiento. Va sobre el
// lienzo, como Ahora (ahStage), con la misma ficha. «Cada paso» es la Secuencia de siempre (renderDiagram).

// La franja de arriba del Parte, con el conmutador: lo que mide y dónde va él dentro de ella. La página empieza debajo
// (wfPlaceOverlays), para que el conmutador no flote sobre lo que se lee.
const PT_BAND = 48;
const PT_BAND_TOP = 9;

// Lo que se ha abierto o recogido a mano, por su clave («prompt:8», «agent:<id>», «steps:<parada>»). Lo que no está aquí
// va como de serie: abiertos el último prompt y el del paso de ahora, y los agentes que trabajan; los pasos, recogidos.
const PT_OPEN = new Map();

/** Si algo del Parte está abierto: lo que se eligió a mano o, si no, lo de serie. */
function ptOpen(key, byDefault) {
    return PT_OPEN.has(key) ? PT_OPEN.get(key) : byDefault;
}

/** Enseña el conmutador del Parte («Resumen» o «Cada paso») solo en el Parte, con lo elegido marcado; lo engancha la
 * primera vez. */
function ptModeBar() {
    const bar = document.getElementById('parte-mode');
    if (!bar) return;
    if (!bar.dataset.wired) {
        bar.dataset.wired = '1';
        bar.addEventListener('click', e => {
            const b = e.target.closest('[data-mode]');
            if (!b || b.dataset.mode === state.parteMode) return;
            state.parteMode = b.dataset.mode;
            remember('parteMode', state.parteMode);
            renderDiagram(true);
        });
    }
    bar.hidden = !(state.tab === 'map' && state.mapLayout === 'parte' && state.trace);
    const mode = state.parteMode === 'steps' ? 'steps' : 'summary';
    for (const b of bar.querySelectorAll('[data-mode]')) b.classList.toggle('on', b.dataset.mode === mode);
}

/** Pinta el Parte en resumen. Se rehace entero con cada novedad; lo abierto y lo recogido se recuerdan, y la página se
 * queda donde se leía, salvo al cambiar el paso de ahora, que se lleva a la vista. */
function ptRender(steps, box) {
    const v = ahStage(box, `pt|${state.trace.id}`);
    v.ptShown = new Map();
    v.onToggle = key => {
        PT_OPEN.set(key, !v.ptShown.get(key));
        v.ptFocus = key;
        renderDiagram(false);
    };
    const page = v.page;
    const first = !page.firstChild;
    const keep = page.scrollTop;
    const moment = steps.length ? flowMoment(steps) : { now: null, replaying: false, working: Boolean(state.trace.working) };
    const upTo = moment.now ? moment.now.order : -1;
    const seen = steps.filter(s => s.order <= upTo);
    const model = dgLayout(dgModel(seen, moment, false, null), 'h');
    const here = (moment.working || moment.replaying) && moment.now ? moment.now.order : null;
    const live = new Set(here !== null ? [here] : []);
    for (const lane of ahAlive(model, moment)) {
        const last = lane.stations[lane.stations.length - 1];
        if (last) for (const o of last.orders) live.add(o);
    }
    ahNodeData(v, model);
    const ctx = { model, live, here, moment, shown: v.ptShown, hereIt: here !== null ? (stepAt(here) || {}).interaction : null };
    const groups = ptGroups(model);
    const view = el('div', 'ah-view pt-view');
    groups.forEach((g, i) => view.append(ptPrompt(ctx, g, i === groups.length - 1)));
    if (!groups.length) view.append(el('p', 'pt-empty', 'Aún no hay trabajo que contar.'));
    ahPicks(v, view);
    page.replaceChildren(view);
    const now = view.querySelector('.ah-item.here');
    if (first) page.scrollTop = now ? Math.max(0, now.offsetTop - page.clientHeight / 2) : page.scrollHeight;
    else if (now && v.ptHere !== here) now.scrollIntoView({ block: 'center' });
    else page.scrollTop = keep;
    v.ptHere = here;
    // Lo que se acaba de abrir o recoger, a la vista: al recoger un prompt largo leyendo por su mitad, su cabecera
    // quedaría arriba, fuera, y se perdería el sitio.
    if (v.ptFocus) {
        const head = view.querySelector(`[data-toggle="${CSS.escape(v.ptFocus)}"]`);
        v.ptFocus = null;
        if (head && head.getBoundingClientRect().top < page.getBoundingClientRect().top) head.scrollIntoView({ block: 'start' });
    }
    ahSettle(v);
}

/** Lo que se ve, prompt a prompt: cada uno con su parada de prompt (si se ve) y las paradas de la sesión que le siguen. */
function ptGroups(model) {
    const main = model.lanes.find(l => l.id === 'main');
    const you = model.lanes.find(l => l.id === 'you');
    const groups = new Map();
    const take = it => {
        if (!groups.has(it)) groups.set(it, { it, prompt: null, stations: [] });
        return groups.get(it);
    };
    for (const p of you ? you.stations : []) if (p.node && p.node.step) take(p.node.step.interaction).prompt = p;
    let it = null;
    for (const s of main ? main.stations : []) {
        const step = s.orders.length ? stepAt(s.orders[0]) : null;
        if (step) it = step.interaction;
        if (it !== null && it !== undefined) take(it).stations.push(s);
    }
    const at = g => (g.prompt ? g.prompt.order : g.stations.length ? g.stations[0].order : 0);
    return [...groups.values()].sort((a, b) => at(a) - at(b));
}

/** Un prompt del Parte: su cabecera (el prompt, cuándo y cuánto duró, y en corto lo que hubo) y, abierto, sus fases. */
function ptPrompt(ctx, g, last) {
    const key = `prompt:${g.it}`;
    const open = ptOpen(key, last || g.it === ctx.hereIt);
    ctx.shown.set(key, open);
    const sheet = el('section', 'ah-sheet pt-prompt' + (open ? ' open' : ''));
    const head = el('div', 'pt-prompt-head');
    head.dataset.toggle = key;
    // El título es lo que escribiste; el número del prompt, al lado y más claro.
    const step = g.prompt && g.prompt.node.step;
    const shell = Boolean(g.prompt && g.prompt.shell);
    const title = el('div', 'pt-prompt-title');
    const text = el('b', 'pt-prompt-text' + (shell ? ' shell' : ''));
    if (!step) text.textContent = 'Sin prompt a la vista';
    else if (shell) text.textContent = clip(oneLine(step.text || '').replace(/^!\s*/, ''), 160);
    else text.append(previewMd(plain(step.text || ''), 160));
    title.append(text, el('span', 'pt-prompt-num', `#${g.it}`));
    const start = g.prompt ? g.prompt.start : g.stations.length ? g.stations[0].start : null;
    const end = Math.max(0, ...g.stations.map(s => s.end || s.start || 0));
    const meta = el('span', 'ah-when');
    meta.textContent = start ? `${clock(new Date(start).toISOString())}${end > start ? ` · ${duration(end - start)}` : ''}` : '';
    head.append(ahChip(shell ? 'terminal' : 'prompt', actorColor(shell ? 'shell' : 'user'), false, true), title, meta, el('span', 'pt-caret', open ? '▾' : '▸'));
    sheet.append(head);
    if (!open) {
        const phases = dgPhases(g.stations).filter(p => p.kind === 'phase').length;
        const agents = g.stations.filter(s => s.kind === 'launch').reduce((n, s) => n + s.children.length, 0);
        const bits = [phases ? `${phases} ${phases === 1 ? 'fase' : 'fases'}` : '', agents ? agentsWord(agents) : ''].filter(Boolean);
        if (bits.length) title.append(el('small', 'pt-prompt-sum', bits.join(' · ')));
        return sheet;
    }
    const body = el('div', 'pt-body');
    body.append(ptBlocks(ctx, g.stations, false));
    sheet.append(body);
    return sheet;
}

/** Pinta el Parte paso a paso: la Secuencia de siempre en la página del lienzo, con cada fila abriendo la ficha de su
 * parada. Se queda donde se leía, o al final si estaba al final, como la Secuencia. */
function ptStepsRender(steps, box, scrollToStart) {
    const v = ahStage(box, `pts|${state.trace.id}`);
    v.onToggle = null;
    const page = v.page;
    const first = !page.firstChild;
    const keep = page.scrollTop;
    const follow = page.scrollHeight - page.scrollTop - page.clientHeight < 24;
    const moment = flowMoment(steps);
    const model = dgLayout(dgModel(steps, moment, false, null), 'h');
    ahNodeData(v, model);
    const nkeys = new Map();
    for (const lane of model.lanes) for (const s of lane.stations) {
        if (s.kind === 'wait' || s.kind === 'pending') continue;
        for (const o of s.orders) nkeys.set(o, s.key);
    }
    page.replaceChildren(sequence(steps));
    state.seqView.scroller = page;
    state.seqView.nkeys = nkeys;
    if (!page.dataset.seq) {
        page.dataset.seq = '1';
        page.addEventListener('scroll', schedulePaintSequence, { passive: true });
    }
    if (first || scrollToStart === true) page.scrollTop = state.interaction === 'all' ? page.scrollHeight : 0;
    else page.scrollTop = follow ? page.scrollHeight : keep;
    paintSequence();
    v.nodes = new Map();
    ahSettle(v);
}

/** Las fases de unas paradas de un mismo carril, con sus rachas dentro; lo que va solo (una pregunta, un lanzamiento, la
 * respuesta final), como una línea suelta, y bajo cada lanzamiento, la lámina de cada agente. */
function ptBlocks(ctx, stations, inAgent) {
    const list = el('div', 'ah-log pt-log');
    for (const b of dgPhases(stations)) {
        if (b.kind === 'phase') {
            const lines = parteRuns(b.members).map(r => parteLine(r, ctx.live)).filter(Boolean);
            if (!lines.length) continue;
            for (const l of lines) parteMark(l, ctx.here);
            list.append(ptPhase(b, lines.some(l => l.here)));
            for (const l of lines) list.append(ptLine(ctx, l, inAgent));
            continue;
        }
        const l = parteLine({ kind: null, members: [b] }, ctx.live);
        if (!l) continue;
        parteMark(l, ctx.here);
        list.append(ptLine(ctx, l, inAgent));
        if (b.kind === 'launch') {
            for (const id of b.children) {
                const lane = ctx.model.lanes.find(x => x.id === id);
                if (lane) list.append(ptAgent(ctx, lane));
            }
        }
    }
    return list;
}

/** La cabecera de una fase: su chapa grande del color de lo que hace, su nombre, lo que duró y cuándo empezó. */
function ptPhase(p, on) {
    const hue = { understand: 'read', change: 'edit', check: 'run', talk: 'say' }[p.phase] || 'other';
    const icon = { understand: 'read', change: 'edit', check: 'terminal', talk: 'reply' }[p.phase] || 'tool';
    const head = el('div', 'pt-phase' + (on ? ' on' : ''));
    const words = el('div', 'pt-phase-words');
    words.append(el('span', 'pt-phase-name', DG_PHASES[p.phase] || 'Fase'));
    const took = p.end && p.start && p.end > p.start ? duration(p.end - p.start) : '';
    if (took) words.append(el('span', 'pt-phase-took', took));
    head.append(ahChip(icon, `var(--dg-${hue})`, p.phase === 'talk'), words, el('span', 'ah-when', p.start ? clock(new Date(p.start).toISOString()) : ''));
    return head;
}

/** Una línea del Parte, como las de Ahora; si es una racha de llamadas, con un botón que despliega sus pasos. */
function ptLine(ctx, l, inAgent) {
    const item = ahItem(ctx.model, l, { inAgent });
    const calls = l.stations[0].kind === 'tools' ? l.orders : [];
    if (!calls.length) return item;
    const key = `steps:${l.key}`;
    const open = ptOpen(key, false);
    ctx.shown.set(key, open);
    const button = el('button', 'pt-steps-btn', `${open ? '▾' : '▸'} ${calls.length === 1 ? '1 paso' : `${calls.length} pasos`}`);
    button.type = 'button';
    button.dataset.toggle = key;
    item.querySelector('.ah-item-head').append(button);
    if (open) item.querySelector('.ah-item-words').append(ptSteps(l, calls));
    return item;
}

/** Los pasos de una racha, uno por fila: la hora, la herramienta, sobre qué y lo que tardó. Cada uno abre su ficha. */
function ptSteps(l, calls) {
    const list = el('div', 'pt-steps');
    for (const o of calls) {
        const step = stepAt(o);
        if (!step) continue;
        const [tool, ...rest] = (step.title || '').split(' · ');
        const row = el('div', 'pt-step' + (step.isError ? ' error' : ''));
        const station = l.stations.find(m => m.orders.includes(o));
        if (station) row.dataset.nkey = station.key;
        row.dataset.orders = String(o);
        const took = step.end ? between(step.start, step.end) : null;
        row.append(el('span', 'pt-step-when', clock(step.start)), el('span', 'pt-step-tool', tool), el('span', 'pt-step-what', clip(oneLine(rest.join(' · ')), 140)),
            el('span', 'pt-step-took', took !== null ? duration(took) : '…'));
        list.append(row);
    }
    return list;
}

/** La lámina de un agente bajo su lanzamiento: quién es, si trabaja o cuándo terminó, y en corto lo que hizo; abierta,
 * sus fases, con las láminas de sus propios agentes dentro. */
function ptAgent(ctx, lane) {
    const key = `agent:${lane.id}`;
    const done = Boolean(lane.agent) && flowDone(lane.agent, ctx.moment);
    const open = ptOpen(key, !done);
    ctx.shown.set(key, open);
    const card = paint(el('div', 'pt-agent' + (open ? ' open' : '') + (done ? ' done' : '')), lane.id);
    const head = el('div', 'pt-agent-head');
    head.dataset.toggle = key;
    const who = el('span', 'ah-who');
    who.append(ahAxis(ctx.model, lane.id), clip(actorName(lane.id), 60));
    const edits = new Set(lane.stations.filter(s => s.kind === 'tools' && s.family === 'edit').flatMap(s => s.targets || []));
    const phases = dgPhases(lane.stations).filter(p => p.kind === 'phase').length;
    const bits = [done ? 'terminó' : 'trabaja', phases ? `${phases} ${phases === 1 ? 'fase' : 'fases'}` : '',
        edits.size ? `cambió ${edits.size === 1 ? '1 fichero' : `${edits.size} ficheros`}` : ''].filter(Boolean);
    // Lo que lleva uno que trabaja, hasta el momento que se ve: en la repetición, el del paso, no el de ahora.
    const until = done && lane.agent.endUtc ? lane.agent.endUtc : ctx.moment.replaying && ctx.moment.now ? ctx.moment.now.start : new Date().toISOString();
    const took = lane.agent && lane.agent.startUtc ? between(lane.agent.startUtc, until) : null;
    head.append(who, el('span', 'pt-agent-sum', bits.join(' · ')), el('span', 'ah-when', took !== null ? duration(took) : ''), el('span', 'pt-caret', open ? '▾' : '▸'));
    card.append(head);
    if (open) {
        const body = el('div', 'pt-agent-body');
        body.append(ptBlocks(ctx, lane.stations, true));
        card.append(body);
    }
    return card;
}
