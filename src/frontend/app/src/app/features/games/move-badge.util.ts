import { MOVE_CLASS_COLORS, MoveClass } from './game-review.util';

/**
 * Zeichen je Klasse — dieselben Symbole wie in der Schachnotation, wo es sie gibt. „!" gehört seit den
 * Sonderklassen dem Great (so auch bei chess.com); Excellent trägt deshalb den Daumen wie dort.
 * Die EINE Tabelle für den Rückblick (Abzeichen, Zähler) — das Brett zeichnet dieselben Klassen als Symbol.
 */
export const MOVE_CLASS_SYMBOLS: Readonly<Record<MoveClass, string>> = {
  brilliant: '!!', great: '!', best: '★', excellent: '👍', good: '✓', book: '📖', inaccuracy: '?!', mistake: '?', miss: '✗',
  blunder: '??',
};

/** Ein Symbol auf dem Brett: die Klasse des aktuellen Zugs am Zielfeld (seit 0.557.0, wie chess.com beim Durchsehen). */
export interface BoardBadge {
  /** Zielfeld des Zugs („d5"; bei der Rochade das Feld des Königs). */
  square: string;
  /** Inhalt für Chessgrounds `customSvg` — Koordinaten 0..100 über dem Feld. */
  svg: string;
}

/** Mittelpunkt und Radius des Kreises in Feld-Koordinaten (0..100). Seit 0.559.0 Variante „D" aus dem
 *  Vergleich vom 2026-09-27 (gewählt vom Nutzer): groß (⌀ knapp die Hälfte des Felds) und nahe der Ecke, mit weißem
 *  Ring — auch am Handy lesbar. Er ragt ein Stück über das Feld hinaus; die Figur in der Mitte bleibt frei. */
const CX = 90;
const CY = 10;
const R = 24;
/** Glyphen und Schrift sind für R = 18 gezeichnet und wachsen mit. */
const SCALE = R / 18;

/** CSS-Klasse fürs kurze Aufpoppen nach der Ankunft der Figur — Keyframes in `styles.scss` (das SVG setzt
 *  Chessground per innerHTML ein, außerhalb jeder Komponenten-Kapselung). */
export const BADGE_POP_CLASS = 'rh-badge-pop';

/**
 * Glyphen, die als TEXT nicht taugen: Daumen und Buch sind Emoji (farbig, je System anders), Stern, Haken und Kreuz
 * sind je nach Schrift mal Emoji, mal zu dünn. Deshalb gezeichnet, in einem 24er-Raster um (12, 12).
 */
const GLYPHS: Partial<Record<MoveClass, string>> = {
  best: `<path d="${starPath()}" fill="#fff"/>`,
  excellent: '<path d="M3.5 10.5h3.5v9h-3.5z M8.5 10.5 12 4c1.6 0 2.4 1.1 2.1 2.6L13.5 9.5H19c1.3 0 2.1 1.1 1.8 2.3l-1.3 6.2'
    + 'c-.2 1-1 1.5-2 1.5H8.5z" fill="#fff"/>',
  good: '<path d="M5.5 12.5l4.2 4.2L18.5 7.5" fill="none" stroke="#fff" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round"/>',
  miss: '<path d="M7 7l10 10M17 7 7 17" fill="none" stroke="#fff" stroke-width="3.2" stroke-linecap="round"/>',
  book: '<path d="M3 6.5c3-1.4 6-1.4 8.4 0v12.2c-2.4-1.4-5.4-1.4-8.4 0z M12.6 6.5c2.4-1.4 5.4-1.4 8.4 0v12.2c-3-1.4-6-1.4-8.4 0z" fill="#fff"/>',
};

/** Zahl für ein SVG-Attribut, ohne Gleitkomma-Schwanz. */
function fmt(n: number): string {
  return String(Math.round(n * 100) / 100);
}

/** Fünfzackiger Stern um (12, 12) — außen 9,5, innen 4. */
function starPath(): string {
  const pts: string[] = [];
  for (let i = 0; i < 10; i++) {
    const r = i % 2 === 0 ? 9.5 : 4;
    const a = -Math.PI / 2 + (i * Math.PI) / 5;
    pts.push(`${(12 + r * Math.cos(a)).toFixed(2)} ${(12.6 + r * Math.sin(a)).toFixed(2)}`);
  }
  return `M${pts.join('L')}Z`;
}

/**
 * Das Symbol einer Klasse als SVG-Schnipsel für Chessground (`DrawShape.customSvg.html`, Raster 0..100 über dem
 * Feld): farbiger Kreis mit weißem Rand und weichem Schatten, darin das Zeichen der Klasse — dieselbe Farbe wie
 * überall im Rückblick (`MOVE_CLASS_COLORS`). Beim Einsetzen poppt er kurz auf (`BADGE_POP_CLASS`).
 */
export function moveBadgeSvg(cls: MoveClass): string {
  const color = MOVE_CLASS_COLORS[cls];
  const glyph = GLYPHS[cls];
  const g = 11.4 * SCALE;
  const inner = glyph
    ? `<g transform="translate(${fmt(CX - g)} ${fmt(CY - g)}) scale(${fmt(0.95 * SCALE)})">${glyph}</g>`
    : `<text x="${CX}" y="${CY}" text-anchor="middle" dominant-baseline="central" fill="#fff" font-weight="700"`
      + ` font-family="Roboto, Arial, sans-serif" font-size="${fmt((MOVE_CLASS_SYMBOLS[cls].length > 1 ? 19 : 24) * SCALE)}">`
      + `${MOVE_CLASS_SYMBOLS[cls]}</text>`;
  // Die Hülle trägt das Aufpoppen; der Drehpunkt ist die Kreismitte (SVG-Benutzereinheiten = px im Feld-Raster).
  return `<g class="${BADGE_POP_CLASS}" style="transform-origin:${CX}px ${CY}px">`
    + `<circle cx="${CX + 0.5}" cy="${CY + 2}" r="${R + 1}" fill="#000" fill-opacity="0.22"/>`
    + `<circle cx="${CX}" cy="${CY}" r="${R}" fill="${color}" stroke="#fff" stroke-width="3"/>`
    + inner + '</g>';
}
