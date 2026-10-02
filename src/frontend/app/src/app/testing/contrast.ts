/**
 * Kontrast-Messung fuer Specs (WCAG 2.x), an BERECHNETEN Farben statt an Quelltexten (Codereview 2026-09-29,
 * UX-055/UX-008/UX-015/UX-060/F7-015). Die globalen Styles laufen im Test mit (angular.json → test.styles), also
 * sehen die Specs dieselben Theme-Tokens wie der Browser: hell ohne, dunkel mit `dark-theme` am `<html>`.
 *
 * `effectiveBackground` laeuft die Vorfahren hoch und mischt halbdurchsichtige Flaechen, bis eine deckt. Findet
 * sie keine, rechnet sie wie axe gegen Weiss — genau die Messfalle, die UX-055 beschreibt (die Seite hatte keinen
 * eigenen Hintergrund, die dunkle Leinwand kam nur aus `color-scheme`).
 */
export type Rgba = [number, number, number, number];

const WHITE: Rgba = [255, 255, 255, 1];

/** `rgb()`/`rgba()` und `color(srgb r g b / a)` (so meldet Chrome `color-mix()`-Ergebnisse). */
export function parseColor(css: string): Rgba {
  const rgb = /^rgba?\(([\d.]+),\s*([\d.]+),\s*([\d.]+)(?:,\s*([\d.]+))?\)$/.exec(css);
  if (rgb) return [Number(rgb[1]), Number(rgb[2]), Number(rgb[3]), rgb[4] === undefined ? 1 : Number(rgb[4])];
  const srgb = /^color\(srgb\s+([\d.e-]+)\s+([\d.e-]+)\s+([\d.e-]+)(?:\s*\/\s*([\d.]+))?\)$/.exec(css);
  if (srgb) {
    return [Number(srgb[1]) * 255, Number(srgb[2]) * 255, Number(srgb[3]) * 255,
      srgb[4] === undefined ? 1 : Number(srgb[4])];
  }
  throw new Error(`keine Farbe: ${css}`);
}

/** `top` (mit Deckkraft) ueber `base`. */
export function over(top: Rgba, base: Rgba): Rgba {
  const a = top[3];
  return [top[0] * a + base[0] * (1 - a), top[1] * a + base[1] * (1 - a), top[2] * a + base[2] * (1 - a),
    a + base[3] * (1 - a)];
}

function luminance([r, g, b]: Rgba): number {
  const lin = (c: number) => {
    const s = c / 255;
    return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
}

/** Kontrast von `fg` (halbdurchsichtig: ueber `bg` gemischt) auf dem deckenden `bg`. */
export function contrast(fg: Rgba, bg: Rgba): number {
  const top = fg[3] < 1 ? over(fg, bg) : fg;
  const [hi, lo] = [luminance(top), luminance(bg)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/** Loest einen Farbausdruck (auch `var(...)`) am Dokument auf. */
export function resolveColor(expr: string): Rgba {
  const probe = document.createElement('span');
  probe.style.color = expr;
  document.body.appendChild(probe);
  try { return parseColor(getComputedStyle(probe).color); } finally { probe.remove(); }
}

/** Die Flaeche, auf der `el` steht: Hintergruende der Vorfahren (inkl. `el`) gemischt, bis einer deckt. */
export function effectiveBackground(el: Element): Rgba {
  const layers: Rgba[] = [];
  for (let node: Element | null = el; node; node = node.parentElement) {
    const bg = parseColor(getComputedStyle(node).backgroundColor);
    if (bg[3] === 0) continue;
    layers.push(bg);
    if (bg[3] >= 1) break;
  }
  return layers.reduceRight<Rgba>((base, layer) => over(layer, base), WHITE);
}

/** Kontrast der Textfarbe von `el` auf seiner Flaeche (inkl. `opacity` von `el`). */
export function textContrast(el: Element): number {
  const fg = parseColor(getComputedStyle(el).color);
  const opacity = Number(getComputedStyle(el).opacity);
  return contrast([fg[0], fg[1], fg[2], fg[3] * opacity], effectiveBackground(el));
}

/** Dunkelmodus an/aus wie der ThemeService (Klasse am `<html>`); gibt den vorigen Zustand zurueck. */
export function setDarkTheme(dark: boolean): boolean {
  const html = document.documentElement;
  const was = html.classList.contains('dark-theme');
  html.classList.toggle('dark-theme', dark);
  return was;
}

let probeId = 0;

/**
 * Haengt `html` unter einen Wirt, auf den die gekapselten Stile der Komponente wirken (aus ihrer kompilierten
 * Definition, `%COMP%` → eigene Kennung) — so wie Angular sie rendert, ohne den Zustand nachzubauen, in dem das
 * Element erscheint (Puzzle geladen, Engine rechnet …). `surface` legt eine Flaeche darunter (z. B.
 * `var(--mat-sys-surface-container-low)` fuer eine Karte), ohne sie steht der Wirt auf der Seite. Gibt das Aufraeumen
 * zurueck.
 *
 * `%NS%`: Seit Angular 22 setzt der Compiler vor jede Custom Property der Komponentenstile diesen Platzhalter
 * (`var(--%NS%rh-accent)`); der Renderer ersetzt ihn durch `CSS_VAR_NAMESPACE` — ohne `provideCssVarNamespacing`
 * (so die App) ist das ''. Bliebe er stehen, waere jede `var()`-Deklaration ungueltig und fiele weg, gemessen
 * wuerde dann die Browser-Vorgabe (`buttontext`, geerbte Textfarbe) — ein Kontrast-Spec waere nur zufaellig gruen
 * (Nacharbeit UX-008). Darum bricht der Helfer ab, wenn nach dem Ersetzen noch ein `%NAME%` im Stiltext steht.
 */
export function mountWithComponentStyles(cmp: unknown, html: string, surface?: string): () => void {
  const scope = `rh-contrast-${probeId++}`;
  const style = document.createElement('style');
  const css = ((cmp as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n')
    .replace(/%COMP%/g, scope).replace(/%NS%/g, '');
  const leftover = /%[A-Z_]+%/.exec(css);
  if (leftover) throw new Error(`mountWithComponentStyles: Platzhalter ${leftover[0]} im Stiltext nicht aufgeloest`);
  style.textContent = css;
  document.head.appendChild(style);
  const ground = document.createElement('div');
  if (surface) ground.style.backgroundColor = surface;
  const host = document.createElement('div');
  host.setAttribute(`_nghost-${scope}`, '');
  host.innerHTML = html;
  host.querySelectorAll('*').forEach(e => e.setAttribute(`_ngcontent-${scope}`, ''));
  ground.appendChild(host);
  document.body.appendChild(ground);
  return () => { ground.remove(); style.remove(); };
}
