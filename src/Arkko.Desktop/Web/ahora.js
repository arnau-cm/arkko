// AHORA: la vista «now» de Sesiones, la sección de la sesión en el instante que se ve.
//
// Arriba, la ficha grande: lo que pasa en una frase, con el reloj de lo que lleva, quién y qué hace, la orden en curso y
// lo último que terminó (la salida de una orden no se guarda hasta que acaba). Si Claude te pregunta, la ficha es la
// pregunta; si espera a sus agentes, la espera; y si ha terminado, la respuesta final. Debajo, lado a lado, las últimas
// líneas del Parte (parte.js) y una ficha por actor que trabaja, cada lista con su propio scroll: la vista ocupa lo que se
// ve y no se mueve entera. Con la repetición, todo es el corte de ese momento.
//
// Va sobre el mismo lienzo que el Flujo y el Diagrama (wfStage), sin su plano: así lleva su misma ficha, con sus mismos
// controles. Lo que se pulsa (la ficha grande, una línea del Parte o un actor) abre la ficha de su parada.

// Cuántas líneas del Parte se enseñan, y cuántas líneas de lo último que terminó.
const AH_LOG_LINES = 40;
const AH_DETAIL_LINES = 8;

/** Pinta la vista Ahora de los pasos de lo elegido en el cajón del diagrama. Monta el lienzo la primera vez y, después,
 * rehace entera la página con cada novedad: es corta, y así no hay nada que poner al día a mano. La ficha abierta se
 * queda, y se pone al día como en el directo. */
function ahRender(steps, box) {
    const v = ahStage(box, `ah|${state.trace.id}`);
    const page = v.page;
    const keep = page.scrollTop;
    // Cada lista conserva su scroll; el Parte, si se dejó al final, sigue al final, que es por donde entra lo nuevo. Se
    // mira al moverlo la persona y no al pintar: abrir la ficha estrecha la página y lo que era el final deja de serlo.
    const oldLog = page.querySelector('.ah-log-scroll');
    const oldCrew = page.querySelector('.ah-crew-scroll');
    const atEnd = v.ahStick !== false;
    const moment = steps.length ? flowMoment(steps) : { now: null, replaying: false, working: Boolean(state.trace.working) };
    const upTo = moment.now ? moment.now.order : -1;
    const seen = steps.filter(s => s.order <= upTo);
    const model = dgLayout(dgModel(seen, moment, false, null), 'h');
    const here = (moment.working || moment.replaying) && moment.now ? moment.now.order : null;
    const alive = ahAlive(model, moment);
    const live = new Set(here !== null ? [here] : []);
    for (const lane of alive) {
        const last = lane.stations[lane.stations.length - 1];
        if (last) for (const o of last.orders) live.add(o);
    }
    const lines = parteLines(model, live, here);
    ahNodeData(v, model);
    const view = el('div', 'ah-view');
    view.append(ahHero(model, lines, seen, moment), ahLog(model, lines, moment), ahActors(model, lines, alive, moment));
    ahPicks(v, view);
    page.replaceChildren(view);
    page.scrollTop = keep;
    const log = view.querySelector('.ah-log-scroll');
    log.scrollTop = atEnd ? log.scrollHeight : oldLog.scrollTop;
    log.addEventListener('scroll', () => {
        if (log.isConnected) v.ahStick = log.scrollHeight - log.scrollTop - log.clientHeight < 24;
    }, { passive: true });
    if (oldCrew) view.querySelector('.ah-crew-scroll').scrollTop = oldCrew.scrollTop;
    ahSettle(v);
    ahTick();
}

/** El lienzo de una vista sin plano (Ahora y el Parte): el del directo, con su ficha, y encima una página con su scroll,
 * que ocupa lo que deja libre la ficha. Se monta la primera vez y se reutiliza mientras sea la misma vista de la misma
 * sesión.
 *
 * Lo que se pulsa abre la ficha de su parada; lo que se despliega (`data-toggle`) se lo pide a la vista (`onToggle`); lo
 * demás llega al lienzo, que cierra la ficha como siempre. */
function ahStage(box, key) {
    let v = state.wf;
    if (v && v.key === key && box.contains(v.root)) return v;
    box.replaceChildren();
    v = state.wf = wfStage(box, key);
    v.ahora = true;
    v.root.classList.add('ah-root');
    v.page = el('div', 'ah-page');
    v.root.insertBefore(v.page, v.drawer);
    v.page.addEventListener('click', e => {
        const toggle = e.target.closest('[data-toggle]');
        if (toggle && v.onToggle) {
            e.stopPropagation();
            v.onToggle(toggle.dataset.toggle);
            return;
        }
        const pick = e.target.closest('[data-nkey]');
        if (!pick) return;
        e.stopPropagation();
        v.detail = pick.dataset.nkey;
        state.focus = null;
        wfDetail(v, v.detail);
        ahMarkPicked(v);
        decorate();
    });
    return v;
}

/** Las fichas de las paradas de un modelo, como las del Diagrama. */
function ahNodeData(v, model) {
    v.nodeData = new Map();
    for (const lane of model.lanes) for (const s of lane.stations) v.nodeData.set(s.key, dgNodeData(s, 0, 0));
}

/** Deja que se pulse lo que abre una ficha: lo que no tiene (la espera a los agentes, el pensar sin escribir) se queda
 * quieto. Las líneas quedan apuntadas para marcar la que tiene su ficha abierta. */
function ahPicks(v, view) {
    v.nodes = new Map();
    for (const node of view.querySelectorAll('[data-nkey]')) {
        const n = v.nodeData.get(node.dataset.nkey);
        if (!n || n.kind === 'agentswait' || n.kind === 'pending') {
            delete node.dataset.nkey;
            continue;
        }
        node.classList.add('ah-pick');
        if (node.classList.contains('ah-item')) v.nodes.set(node.dataset.nkey, node);
    }
}

/** Remata un pintado: la ficha abierta se pone al día con lo nuevo, como en el directo, o se cierra si su parada ya no
 * está; se marca lo que la tiene abierta, y la página se ajusta a lo que deja libre la ficha. */
function ahSettle(v) {
    if (v.detail && !v.drawer.hidden && !v.drawer.dataset.key?.startsWith('file:')) {
        if (v.nodeData.has(v.detail)) wfDetail(v, v.detail);
        else wfCloseDetail(v);
    }
    ahMarkPicked(v);
    wfPlaceOverlays(v);
}

/** Marca lo que tiene abierta su ficha: la línea del Parte, el actor o la ficha grande de esa parada. */
function ahMarkPicked(v) {
    const open = !v.drawer.hidden && v.detail;
    for (const node of v.page.querySelectorAll('.ah-pick')) node.classList.toggle('picked', Boolean(open) && node.dataset.nkey === v.detail);
}

/** Los carriles que trabajan en el momento que se ve: la sesión, si trabaja o espera a sus agentes, y cada agente que
 * aún no ha terminado. */
function ahAlive(model, moment) {
    if (!moment.working && !moment.replaying && !model.wait) return [];
    return model.lanes.filter(l => l.id !== 'you' && l.stations.length
        && (l.id === 'main' ? moment.working || moment.replaying || Boolean(model.wait) : !flowDone(l.agent, moment)));
}

/** La letra del eje de un carril, la misma que en el Diagrama: A tú, B la sesión, y desde C sus agentes. */
function ahLetter(model, laneId) {
    const lane = model.lanes.find(l => l.id === laneId);
    return String.fromCharCode(65 + (lane ? lane.row : 1));
}

/** Quién es un carril, dicho como sujeto de una frase: «Claude» o «El agente «Revisa la rama»». */
function ahSubject(laneId) {
    if (laneId === 'you') return 'Tú';
    return laneId === 'main' ? 'Claude' : `El agente «${clip(actorName(laneId), 40)}»`;
}

/** Una frase con su sujeto delante: «Claude ejecuta una orden». */
function ahSentence(line) {
    if (line.lane === 'you') return line.text;
    return `${ahSubject(line.lane)} ${line.text.charAt(0).toLowerCase()}${line.text.slice(1)}`;
}

/** La chapa de una fase o de un actor: redonda, llena del color de lo que hace o, si es un mensaje o una vuelta, con
 * un tinte suave de su color y el icono encima. */
function ahChip(icon, color, soft, small) {
    const chip = el('span', 'ah-chip' + (soft ? ' ah-soft' : '') + (small ? ' small' : ''));
    chip.style.setProperty('--c', color);
    chip.append(flowIcon(icon, 'ah-icon'));
    return chip;
}

/** La letra de un eje en su círculo, del color de su actor. */
function ahAxis(model, laneId) {
    const letter = el('span', 'ah-letter', ahLetter(model, laneId));
    paint(letter, laneId === 'you' ? 'user' : laneId);
    return letter;
}

/** Lo que dura algo, en segundos como poco: un reloj que dice «850 ms» parece roto. */
function ahDuration(ms) {
    return ms < 1000 ? '0 s' : duration(ms);
}

/** Un reloj que cuenta solo (ahTick) desde un instante, o fijo si no está en marcha. */
function ahClock(since, until, running) {
    const b = el('b', 'ah-clock-value');
    if (running) {
        b.classList.add('ah-tick');
        b.dataset.since = String(since);
        b.append(el('span', 'ah-live-dot'), el('span', 'ah-tick-text', ahDuration(Math.max(0, Date.now() - since))));
    } else b.textContent = ahDuration(Math.max(0, (until ?? since) - since));
    return b;
}

/** Pone al día los relojes que cuentan solos de la vista Ahora. */
function ahTick() {
    const box = document.getElementById('diagram');
    if (!box) return;
    for (const b of box.querySelectorAll('.ah-tick')) {
        const text = b.querySelector('.ah-tick-text');
        if (text) text.textContent = ahDuration(Math.max(0, Date.now() - Number(b.dataset.since)));
    }
}
setInterval(ahTick, 1000);

/** La lámina de la ficha grande: el titular con el reloj a su derecha, la franja de quién y qué hace, y lo que siga. */
function ahSheet(o) {
    const sheet = el('section', 'ah-sheet ah-hero' + (o.ask ? ' ask' : ''));
    if (o.pick) sheet.dataset.nkey = o.pick;
    const body = el('div', 'ah-body');
    const top = el('div', 'ah-title');
    const h = el('h2', null, o.title);
    top.append(ahChip(o.icon, o.color, o.ring), h);
    if (o.clock) {
        const clock = el('div', 'ah-bigclock' + (o.running ? ' live' : ''));
        clock.append(o.clock, el('small', null, o.clockNote || ''));
        top.append(clock);
    }
    body.append(top);
    if (o.who) {
        const strip = el('div', 'ah-cells');
        const who = el('div', 'ah-cell');
        const name = el('b');
        name.append(o.who.letter, o.who.name);
        who.append(el('small', null, 'Quién'), name);
        const what = el('div', 'ah-cell');
        what.append(el('small', null, 'Qué hace'), el('b', null, o.doing));
        strip.append(who, what);
        body.append(strip);
    }
    for (const part of o.rest || []) if (part) body.append(part);
    sheet.append(body);
    return sheet;
}

/** La orden, el fichero o la búsqueda en curso, entera, como en la terminal. */
function ahCommand(line) {
    if (!line || !line.orders.length || !/^(read|search|edit|write|terminal|web|plug|tool|toolbox)$/.test(line.icon)) return null;
    const step = stepAt(line.orders[line.orders.length - 1]);
    const what = step && (step.title || '').split(' · ').slice(1).join(' · ').trim();
    return what ? el('pre', 'ah-cmd', what) : null;
}

/** Lo último que terminó antes de un momento: la última herramienta con resultado, o la última respuesta o
 * razonamiento con texto, sin contar lo que sigue en marcha. */
function ahLastDone(seen, skip) {
    let best = null;
    for (const s of seen) {
        if (skip && skip.has(s.order)) continue;
        const tool = (s.kind === 'tool' || s.kind === 'skill' || s.kind === 'agent') && s.end && s.result;
        const said = (s.kind === 'reply' || s.kind === 'thinking') && s.text;
        if (!tool && !said) continue;
        const at = new Date(s.end || s.start).getTime();
        if (!best || at >= best.at) best = { step: s, at };
    }
    return best;
}

/** El texto de una respuesta sin las marcas de Markdown que se verían tal cual en el recuadro: negritas, código y
 * almohadillas de los títulos. Las listas se quedan, que se leen bien. */
function ahUnmark(text) {
    return String(text || '').replace(/\*\*|__|`/g, '').replace(/^#{1,6}\s+/gm, '');
}

/** Lo que hizo un paso, en pasado y en minúscula, para el rótulo de lo último que terminó: «leyó Models.cs». */
function ahPast(step) {
    if (step.kind === 'reply') return 'respondió';
    if (step.kind === 'thinking') return 'pensó';
    if (step.kind === 'agent') return 'volvió un agente';
    const tool = (step.title || '').split(' · ')[0];
    const family = dgFamily(tool, step.kind === 'skill' ? step.title : null);
    const target = dgTarget(step, family);
    const verb = { read: 'leyó', look: 'buscó', edit: 'cambió', run: 'ejecutó' }[family];
    if (verb) return target ? `${verb} ${target}` : verb;
    return step.kind === 'skill' ? `usó la skill ${step.title}` : `usó ${tool}`;
}

/** El recuadro de lo último que terminó: su rótulo y sus últimas líneas de salida o, si es una respuesta, sus primeras
 * líneas con su formato, como en el chat. */
function ahDetail(last, moment, label) {
    if (!last) return null;
    const s = last.step;
    const said = s.kind === 'reply' || s.kind === 'thinking';
    const ref = moment.replaying && moment.now ? new Date(moment.now.start).getTime() : Date.now();
    const box = el('div', 'ah-detail' + (s.isError ? ' error' : ''));
    box.append(el('small', null, label || `Lo último que terminó: ${ahPast(s)}, hace ${duration(Math.max(0, ref - last.at))}`));
    if (said) box.append(markdown(firstLines(plain(s.text), AH_DETAIL_LINES * 2, 1500)));
    else box.append(el('pre', null, String(s.result || '').replace(/\s+$/, '').split(/\r?\n/).slice(-AH_DETAIL_LINES).join('\n')));
    return box;
}

/** La ficha grande según el momento: la pregunta, el trabajo en marcha, la espera a los agentes o lo que terminó. */
function ahHero(model, lines, seen, moment) {
    if (!seen.length) return ahSheet({ icon: 'prompt', color: actorColor('user'), title: 'Aún no ha empezado: la sesión espera tu primer prompt' });
    // La pregunta que espera respuesta (parteAsk): la del paso de ahora en la repetición, o en directo la que aún no tiene
    // resultado. Una contestada o rechazada ya no espera, aunque la sesión siga abierta.
    const asking = lines.find(l => l.asking && l.question);
    if (asking) return ahAsk(model, asking, moment);
    const ref = moment.replaying && moment.now ? new Date(moment.now.start).getTime() : null;
    const running = !moment.replaying;
    if (model.wait && !moment.replaying) {
        const since = dgTime(model.wait.since);
        // Espera a sus agentes (violeta, en órbita) o a un comando en marcha (cian, con la consola).
        const command = model.wait.kind === 'command';
        return ahSheet({
            icon: command ? 'terminal' : 'agent', color: command ? 'var(--st-command)' : 'var(--st-agents)', ring: !command,
            title: command ? `Claude espera a ${commandsWord(model.wait.commands.length)}` : `Claude espera a sus ${agentsWord(model.wait.running.length)}`,
            clock: ahClock(since, null, true), running: true, clockNote: `desde las ${clock(model.wait.since)}`,
            who: { letter: ahAxis(model, 'main'), name: 'Claude' }, doing: command ? waitWhat(model.wait) : 'Espera a sus agentes',
            rest: [ahDetail(ahLastDone(seen), moment)],
        });
    }
    const active = (moment.working || moment.replaying) ? lines.find(l => l.here) : null;
    if (active) {
        const skip = new Set(active.end ? [] : active.orders);
        return ahSheet({
            icon: active.icon, color: active.color, ring: active.ring, title: ahSentence(active),
            clock: active.start ? ahClock(active.start, ref, running) : null, running, clockNote: active.start ? `desde las ${clock(new Date(active.start).toISOString())}` : '',
            who: { letter: ahAxis(model, active.lane), name: active.lane === 'main' ? 'Claude' : active.lane === 'you' ? 'Tú' : actorName(active.lane) },
            doing: active.verb || active.text, pick: active.pick,
            rest: [ahCommand(active), ahDetail(ahLastDone(seen, skip), moment)],
        });
    }
    // Terminada: la respuesta final, con lo que duró el prompt.
    const final = [...lines].reverse().find(l => l.final) || lines[lines.length - 1];
    const prompt = [...lines].reverse().find(l => l.lane === 'you' && l.order <= final.order);
    const step = stepAt(final.orders[0]);
    const first = oneLine(ahUnmark(plain((step && step.text) || ''))).split(/(?<=[.!?])\s/)[0];
    const from = prompt ? prompt.start : final.start;
    const to = final.end || final.start;
    return ahSheet({
        icon: final.icon, color: final.color, ring: final.ring, title: final.final ? `Terminó: ${clip(first, 120)}` : ahSentence(final),
        clock: ahClock(from, to, false), clockNote: `de ${clock(new Date(from).toISOString())} a ${clock(new Date(to).toISOString())}`,
        who: { letter: ahAxis(model, final.lane === 'you' ? 'you' : final.lane), name: final.lane === 'main' ? 'Claude' : ahSubject(final.lane) },
        doing: final.final ? 'Respuesta final' : final.verb || final.text, pick: final.pick,
        rest: [step && step.text ? ahDetail({ step, at: to }, moment, `La respuesta final, a las ${clock(new Date(to).toISOString())}`) : null],
    });
}

/** La pregunta que espera respuesta, en la ficha grande: en grande, con sus opciones numeradas y dónde se contesta. */
function ahAsk(model, line, moment) {
    const q = line.question;
    const opts = el('div', 'ah-opts');
    (q.options || []).forEach((o, i) => {
        const opt = el('div', 'ah-opt');
        const words = el('div');
        words.append(el('b', null, o.label));
        if (o.description) words.append(el('span', null, o.description));
        opt.append(el('i', null, String(i + 1)), words);
        opts.append(opt);
    });
    const ref = moment.replaying && moment.now ? new Date(moment.now.start).getTime() : null;
    return ahSheet({
        ask: true, icon: 'question', color: 'var(--st-waiting)', title: q.question,
        clock: ahClock(line.start, ref, !moment.replaying), running: !moment.replaying, clockNote: 'esperando tu respuesta',
        who: { letter: ahAxis(model, line.lane), name: line.lane === 'main' ? 'Claude' : actorName(line.lane) }, doing: 'Te pregunta', pick: line.pick,
        rest: [q.options && q.options.length ? opts : null, el('p', 'ah-where', 'Se contesta en su terminal: la sesión está parada hasta entonces.')],
    });
}

/** Una ficha por actor que trabaja: quién, cuánto lleva en lo que hace, qué hace y desde cuándo. Sin nadie, una lámina
 * que lo dice, en el mismo sitio, para que la columna no aparezca de golpe. */
function ahActors(model, lines, alive, moment) {
    const section = el('section', 'ah-section ah-crew');
    const head = el('div', 'ah-section-head');
    const title = el('h3', null, 'Quién trabaja');
    const grid = el('div', 'ah-actors ah-crew-scroll');
    section.append(head, grid);
    if (!alive.length) {
        const agents = model.lanes.filter(l => l.agent && l.stations.length).length;
        head.append(title, el('span', null, 'nadie ahora'));
        const none = el('div', 'ah-sheet ah-actor ah-none');
        const body = el('div', 'ah-body');
        body.append(el('p', 'ah-doing', 'Nadie trabaja ahora'), el('p', 'ah-since', agents ? `Trabajaron Claude y ${agentsWord(agents)}.` : 'Trabajó Claude, sin agentes.'));
        none.append(body);
        grid.append(none);
        return section;
    }
    head.append(title, el('span', null, alive.length === 1 ? '1 actor' : `${alive.length} actores`));
    const ref = moment.replaying && moment.now ? new Date(moment.now.start).getTime() : null;
    for (const lane of alive) {
        const own = lines.filter(l => l.lane === lane.id);
        let line = own[own.length - 1];
        if (!line) continue;
        // Quien tiene agentes suyos trabajando y no está en el paso de ahora, los espera.
        const children = alive.filter(l => l.agent && l.parent === lane.id);
        const waiting = !line.here && (children.length > 0 || (lane.id === 'main' && Boolean(model.wait)));
        const card = el('div', 'ah-sheet ah-actor');
        const cells = el('div', 'ah-cells ah-actor-head');
        const who = el('div', 'ah-cell');
        const name = el('b');
        name.append(ahAxis(model, lane.id), lane.id === 'main' ? 'Claude' : clip(actorName(lane.id), 40));
        who.append(el('small', null, 'Quién'), name);
        const time = el('div', 'ah-cell ah-cell-time');
        time.append(el('small', null, 'Cuánto lleva'), ahClock(line.start, ref, !moment.replaying));
        cells.append(who, time);
        const doing = el('p', 'ah-doing');
        if (waiting) {
            const who = children.length === 1 ? `a «${clip(actorName(children[0].id), 48)}»` : children.length ? `a sus ${agentsWord(children.length)}` : `a sus ${agentsWord(model.wait.running.length)}`;
            doing.append(ahChip('agent', 'var(--st-agents)', true, true), `Espera ${who}`);
        }
        else doing.append(ahChip(line.icon, line.color, line.ring, true), line.text);
        const since = el('p', 'ah-since', `desde las ${clock(new Date(line.start).toISOString())}`);
        const body = el('div', 'ah-body');
        body.append(doing, since);
        card.append(cells, body);
        card.dataset.orders = line.orders.join(',');
        card.dataset.nkey = line.pick;
        grid.append(card);
    }
    return section;
}

/** Las últimas líneas del Parte, como un hilo de metro: cada fase con su chapa, su frase en fuerte, quién la hizo si es
 * un agente y su hora, y debajo su detalle en un recuadro (el mensaje con su formato; los ficheros y las órdenes, en
 * mono). Lo de los agentes, sangrado según lo hondo que esté, y lo de ahora, recuadrado. */
function ahLog(model, lines, moment) {
    const section = el('section', 'ah-section ah-parte');
    const head = el('div', 'ah-section-head');
    head.append(el('h3', null, 'Parte'), el('span', null, lines.length > AH_LOG_LINES ? `las últimas ${AH_LOG_LINES} fases de ${lines.length}` : `${lines.length === 1 ? '1 fase' : `${lines.length} fases`}`));
    const list = el('div', 'ah-log ah-log-scroll');
    const shown = lines.slice(-AH_LOG_LINES);
    const ended = !moment.working && !moment.replaying;
    shown.forEach((l, i) => list.append(ahItem(model, l, { end: ended && l.final && i === shown.length - 1 })));
    section.append(head, list);
    return section;
}

/** Una línea del Parte: su chapa, su frase en fuerte, quién la hizo si es un agente (salvo dentro de su lámina, `inAgent`)
 * y su hora; debajo, su detalle en un recuadro. Lo de un agente, sangrado según lo hondo que esté. */
function ahItem(model, l, { end = false, inAgent = false } = {}) {
    const a = l.lane !== 'you' && l.lane !== 'main' ? agentOf(l.lane) : null;
    const item = el('div', 'ah-item' + (l.here ? ' here' : '') + (end ? ' end' : ''));
    if (a && !inAgent) item.style.setProperty('--depth', String(Math.max(1, a.depth || 1)));
    item.dataset.orders = l.orders.join(',');
    item.dataset.nkey = l.pick;
    const words = el('div', 'ah-item-words');
    const line = el('div', 'ah-item-head');
    line.append(el('span', 'ah-item-text', l.text));
    if (a && !inAgent) {
        const who = el('span', 'ah-who');
        who.append(ahAxis(model, l.lane), clip(actorName(l.lane), 40));
        line.append(who);
    }
    words.append(line);
    if (l.md) {
        const box = el('div', 'ah-item-box');
        box.append(previewMd(plain(l.md), 260));
        words.append(box);
    } else if (l.small && l.code) words.append(el('div', 'ah-item-box mono', l.small));
    else if (l.small) line.append(el('span', 'ah-item-note', l.small));
    item.append(ahChip(l.icon, l.color, l.ring), words, el('span', 'ah-when', l.start ? clock(new Date(l.start).toISOString()) : ''));
    return item;
}
