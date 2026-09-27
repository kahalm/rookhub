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

/** Mittelpunkt und Radius des Kreises in Feld-Koordinaten (0..100): oben rechts, ein Stück über den Rand wie bei
 *  chess.com — aber nur wenig, damit er am Brettrand (h-Linie, 8. Reihe) nicht abgeschnitten wird. */
const CX = 84;
const CY = 16;
const R = 18;

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
 * Feld): farbiger Kreis mit weißem Rand und leichtem Schatten, darin das Zeichen der Klasse — dieselbe Farbe wie
 * überall im Rückblick (`MOVE_CLASS_COLORS`).
 */
export function moveBadgeSvg(cls: MoveClass): string {
  const color = MOVE_CLASS_COLORS[cls];
  const glyph = GLYPHS[cls];
  const inner = glyph
    ? `<g transform="translate(${CX - 11.4} ${CY - 11.4}) scale(0.95)">${glyph}</g>`
    : `<text x="${CX}" y="${CY}" text-anchor="middle" dominant-baseline="central" fill="#fff" font-weight="700"`
      + ` font-family="Roboto, Arial, sans-serif" font-size="${MOVE_CLASS_SYMBOLS[cls].length > 1 ? 19 : 24}">`
      + `${MOVE_CLASS_SYMBOLS[cls]}</text>`;
  return `<circle cx="${CX + 1}" cy="${CY + 2.5}" r="${R}" fill="#000" fill-opacity="0.35"/>`
    + `<circle cx="${CX}" cy="${CY}" r="${R}" fill="${color}" stroke="#fff" stroke-width="2.5"/>`
    + inner;
}
