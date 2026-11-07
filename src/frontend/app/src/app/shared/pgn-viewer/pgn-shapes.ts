import type { DrawShape } from 'chessground/draw';
import type { Key } from 'chessground/types';

/** Farb-Präfixe der Chessbase-Marker: G/R/B/Y → chessground-Brushes. */
const CAL_BRUSH: Record<string, string> = { G: 'green', R: 'red', B: 'blue', Y: 'yellow' };

/**
 * Parst [%cal Gc8h3,Rd1d8] + [%csl Gd4,Re5] eines Kommentar-Blocks zu DrawShapes — der EINE Leser dieser Marker im
 * Client (Karteikarten, Repertoire-Trainer über {@link ParsedGame.shapes}).
 */
export function parseCalCsl(comment: string): DrawShape[] {
  const shapes: DrawShape[] = [];
  for (const m of comment.matchAll(/\[%cal\s+([^\]]+)\]/g)) {
    for (const tok of m[1].split(',')) {
      const t = tok.trim();
      const b = CAL_BRUSH[t[0]?.toUpperCase() || ''];
      if (b && t.length >= 5) shapes.push({ orig: t.slice(1, 3) as Key, dest: t.slice(3, 5) as Key, brush: b });
    }
  }
  for (const m of comment.matchAll(/\[%csl\s+([^\]]+)\]/g)) {
    for (const tok of m[1].split(',')) {
      const t = tok.trim();
      const b = CAL_BRUSH[t[0]?.toUpperCase() || ''];
      if (b && t.length >= 3) shapes.push({ orig: t.slice(1, 3) as Key, brush: b });
    }
  }
  return shapes;
}
