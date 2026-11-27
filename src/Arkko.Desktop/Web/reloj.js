// RELOJ: el reloj de una sesión, el mismo en Relojes y en el carril del corcho en directo.
//
// La esfera es la hora real: cada tramo cae en su minuto. El anillo de dentro es la sesión y los de fuera sus agentes,
// que comparten anillo si no coinciden en el tiempo. Los tramos los calcula la app («face»). Va encerrado en su propio
// ámbito para no chocar con los nombres de la página que lo usa; solo deja fuera ArkkoReloj.draw.

window.ArkkoReloj = (() => {
    const NS = 'http://www.w3.org/2000/svg';

    // El reloj se dibuja en un cuadrado de 480 con el centro en medio; los anillos van de RING_IN a RING_OUT.
    const CENTER = 240;
    const RING_IN = 92;
    const RING_OUT = 184;
    const RING_GAP = 6;

    // Dos letras de agente en el mismo anillo más cerca que esto, en minutos, se pisarían: se pone solo la primera.
    const TAG_GAP = 2.5;

    // Un tramo más corto que esto, en minutos, se dibuja con este largo para que se vea.
    const MIN_ARC = 0.35;

    /** Crea un elemento de SVG con sus atributos y lo cuelga de su padre. */
    function svg(tag, attrs, parent) {
        const node = document.createElementNS(NS, tag);
        for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
        if (parent) parent.append(node);
        return node;
    }

    /** El minuto de la hora local de un instante, con decimales: la posición en la esfera. */
    function minuteOf(ms) {
        const d = new Date(ms);
        return d.getMinutes() + d.getSeconds() / 60 + d.getMilliseconds() / 60000;
    }

    /** Un punto de la esfera a un radio y en un minuto; las 12 son el minuto 0. */
    function point(minute, r) {
        const a = (minute / 60) * Math.PI * 2 - Math.PI / 2;
        return [CENTER + r * Math.cos(a), CENTER + r * Math.sin(a)];
    }

    /** El arco de un tramo, de un instante a otro, como camino de SVG. */
    function arc(fromMs, toMs, r) {
        const span = Math.min(59.9, Math.max(MIN_ARC, (toMs - fromMs) / 60000));
        const a = minuteOf(fromMs);
        const [x1, y1] = point(a, r);
        const [x2, y2] = point(a + span, r);
        return `M${x1.toFixed(1)} ${y1.toFixed(1)}A${r} ${r} 0 ${span > 30 ? 1 : 0} 1 ${x2.toFixed(1)} ${y2.toFixed(1)}`;
    }

    /** Una raya del centro hacia fuera en el minuto de un instante, con su clase. */
    function radial(group, ms, r1, r2, cls) {
        const [x1, y1] = point(minuteOf(ms), r1);
        const [x2, y2] = point(minuteOf(ms), r2);
        svg('line', { x1, y1, x2, y2, class: cls }, group);
    }

    /** Las dos letras del centro del reloj: las primeras del nombre del repositorio. */
    function initials(title) {
        const t = (title || '').replace(/[^\p{L}\p{N}]/gu, '');
        return t ? t.charAt(0).toUpperCase() + t.charAt(1).toLowerCase() : '·';
    }

    /** Dibuja el reloj de una sesión: esfera, anillos con sus tramos, arranques de prompt, aguja o fin, y el centro. */
    function drawClock(s, now, size) {
        const asking = s.state === 'asking';
        const root = svg('svg', { viewBox: '0 0 480 480', class: 'ck-clock' + (asking ? ' ck-asking' : ''), 'aria-hidden': 'true' });
        svg('circle', { cx: CENTER, cy: CENTER, r: 214, class: 'ck-face' }, root);
        for (let m = 0; m < 60; m += 5) radial(root, new Date(now).setMinutes(m, 0, 0), 196, 208, 'ck-tick');
        const rings = s.face ? s.face.rings : [];
        const width = Math.min(40, (RING_OUT - RING_IN - RING_GAP * Math.max(0, rings.length - 1)) / Math.max(1, rings.length));
        rings.forEach((ring, i) => {
            const r = RING_IN + width / 2 + i * (width + RING_GAP);
            svg('circle', { cx: CENTER, cy: CENTER, r, class: 'ck-track', 'stroke-width': width }, root);
            for (const span of ring.spans) {
                svg('path', { d: arc(span.start, span.end, r), class: `ck-span ck-h-${span.hue}`, 'stroke-width': span.hue === 'wait' ? Math.max(5, width * 0.4) : width }, root);
            }
            if (size < 150) return;
            let previous = null;
            for (const tag of ring.tags) {
                if (previous !== null && (tag.at - previous) / 60000 < TAG_GAP) continue;
                previous = tag.at;
                const [x, y] = point(minuteOf(tag.at), r);
                const g = svg('g', { class: 'ck-tag' }, root);
                svg('circle', { cx: x, cy: y, r: 13 }, g);
                svg('text', { x, y: y + 6, 'text-anchor': 'middle' }, g).textContent = tag.letter;
            }
        });
        if (s.face) for (const p of s.face.prompts) radial(root, p, RING_IN - 4, 212, 'ck-prompt');
        const running = s.state === 'working' || s.state === 'asking' || s.state === 'agents' || s.state === 'command';
        if (running) radial(root, now, 0, 206, 'ck-hand');
        else if (s.face && s.face.last && s.face.last >= now - 3600000) radial(root, s.face.last, RING_IN - 6, RING_OUT + 6, 'ck-end');
        svg('circle', { cx: CENTER, cy: CENTER, r: RING_IN - 12, class: 'ck-disc' }, root);
        if (s.state === 'done' || s.state === 'review') svg('path', { d: `M${CENTER - 32} ${CENTER + 2} l22 22 l44 -50`, class: 'ck-check' }, root);
        else svg('text', { x: CENTER, y: CENTER + 23, 'text-anchor': 'middle', class: 'ck-mark' }, root).textContent = asking ? '?' : initials(s.title);
        return root;
    }

    return { draw: drawClock };
})();
