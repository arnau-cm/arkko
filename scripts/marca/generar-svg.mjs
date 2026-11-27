// Genera los SVG de las versiones del logo de Arkko (las del dossier) en la carpeta que se le pase:
//   node scripts/marca/generar-svg.mjs assets/arkko/svg
// Despues, scripts\marca\generar-iconos.ps1 saca de ellos los PNG y el ICO.
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const out = process.argv[2];
mkdirSync(out, { recursive: true });

const GREEN = '#3FB950';
const themes = {
  oscuro: { skirt: '#1B1B1E', edge: '#34343A', top: '#2C2C31', legend: '#D9D5CD', orbit: '#55524D' },
  claro:  { skirt: '#8C8880', edge: '#A6A29A', top: '#C8C4BC', legend: '#0B0B0C', orbit: '#7A766F' }
};

function stroke(x1, y1, x2, y2, ws, wm, we, bend = 0) {
  const dx = x2 - x1, dy = y2 - y1, len = Math.hypot(dx, dy);
  const nx = -dy / len, ny = dx / len;
  const mx = (x1 + x2) / 2 + nx * bend, my = (y1 + y2) / 2 + ny * bend;
  const side = s => {
    const A = [x1 + nx * ws / 2 * s, y1 + ny * ws / 2 * s];
    const M = [mx + nx * wm / 2 * s, my + ny * wm / 2 * s];
    const B = [x2 + nx * we / 2 * s, y2 + ny * we / 2 * s];
    return { A, B, C: [2 * M[0] - (A[0] + B[0]) / 2, 2 * M[1] - (A[1] + B[1]) / 2] };
  };
  const a = side(1), b = side(-1), f = n => n.toFixed(1);
  return `M${f(a.A[0])} ${f(a.A[1])} Q${f(a.C[0])} ${f(a.C[1])} ${f(a.B[0])} ${f(a.B[1])} L${f(b.B[0])} ${f(b.B[1])} Q${f(b.C[0])} ${f(b.C[1])} ${f(b.A[0])} ${f(b.A[1])} Z`;
}

const cuchilla = (ink, armInk = ink) => `
  <path d="${stroke(60, 30, 28, 98, 14, 10, 1)}" fill="${ink}"/>
  <path d="${stroke(60, 30, 60, 98, 14, 11, 2)}" fill="${ink}"/>
  <path d="${stroke(62, 68, 98, 98, 12, 8, 1)}" fill="${ink}"/>
  <path d="${stroke(62, 68, 98, 32, 12, 8, 1)}" fill="${armInk}"/>`;
const at = (cx, cy, s, body) => `<g transform="translate(${cx} ${cy}) scale(${s}) translate(-63 -64)">${body}</g>`;
const led = (x, y) => `<rect x="${x}" y="${y}" width="14" height="5" rx="2.5" fill="${GREEN}"/>`;

const defs = `<defs>
  <filter id="grain" x="0" y="0" width="100%" height="100%">
    <feTurbulence type="fractalNoise" baseFrequency="1.6" numOctaves="1" seed="3" result="fine"/>
    <feColorMatrix in="fine" values="0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 -2.6 1.2" result="raw"/>
    <feColorMatrix in="raw" values="0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 .12 0" result="pores"/>
    <feComposite in="pores" in2="SourceGraphic" operator="in" result="pores-in"/>
    <feMerge><feMergeNode in="SourceGraphic"/><feMergeNode in="pores-in"/></feMerge>
  </filter>
  <filter id="halo" x="-10%" y="-10%" width="120%" height="120%"><feMorphology operator="dilate" radius="3.5"/></filter>
</defs>`;

const keycap = t => `<rect x="7" y="8" width="114" height="114" rx="26" fill="${t.skirt}" stroke="${t.edge}" stroke-width="1.5"/>
<rect x="16" y="12" width="96" height="92" rx="19" fill="${t.top}" filter="url(#grain)"/>`;

const variants = {
  plana:   t => keycap(t) + at(60, 58, .6, cuchilla(t.legend)) + led(88, 22),
  leyenda: t => keycap(t) + at(46, 42, .4, cuchilla(t.legend)) + led(84, 86),
  brazo:   t => keycap(t) + at(62, 58, .62, cuchilla(t.legend, GREEN)),
  orbita:  t => keycap(t) + `<ellipse cx="64" cy="58" rx="40" ry="17" transform="rotate(-28 64 58)" fill="none" stroke="${t.orbit}" stroke-width="2.2"/>
<g filter="url(#halo)">${at(64, 58, .56, cuchilla(t.top))}</g>${at(64, 58, .56, cuchilla(t.legend))}
<circle cx="96" cy="37" r="6" fill="${GREEN}"/>`,
  // Para 16 a 24 px: la tecla con brazo verde, con menos borde y la letra mÃ¡s grande, para que se lea
  pequena: t => `<rect x="4" y="4" width="120" height="120" rx="24" fill="${t.skirt}"/>
<rect x="10" y="7" width="108" height="104" rx="19" fill="${t.top}"/>` + at(64, 59, .8, cuchilla(t.legend, GREEN)),
  // Para menos de 16 px: solo la letra, sin tecla, con el brazo verde
  letra:   t => cuchilla(t.legend, GREEN)
};

const svg = body => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128" width="128" height="128" role="img" aria-label="Arkko">
${defs}
${body}
</svg>
`;

for (const [v, draw] of Object.entries(variants))
  for (const [th, t] of Object.entries(themes)) {
    const name = `arkko-${v}-${th}.svg`;
    writeFileSync(join(out, name), svg(draw(t)));
    console.log(name);
  }
