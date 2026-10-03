import { MAIA_BAD_MOVE_PAWNS } from './maia-model';

/**
 * „Schlechte Züge melden" im Sparring gegen Maia: die reinen Regeln, ohne Angular.
 *
 * Die stille Engine des Analysebretts rechnet die Stellung VOR dem eigenen Zug, solange der Nutzer überlegt, und die
 * Stellung DANACH, während Maia antwortet. Verglichen wird bei GLEICHER Tiefe (`matchingBefore`): eine flache Vorher-
 * gegen eine tiefe Nachher-Bewertung meldete sonst Schwankungen der Suche als Fehler. Alles aus WEISS-Sicht wie die
 * Engine-Zeilen; erst `evalDrop` dreht auf die Sicht des Ziehenden.
 */

/** Eine Bewertung der Engine bei einer Tiefe — WEISS-Sicht (`score` cp bzw. Züge bis Matt, wie `AnalysisLine`). */
export interface EvalPoint { depth: number; score: number; scoreType: 'cp' | 'mate'; evalText: string; }

/** Was die Maia-Karte von einer Warnung braucht (der Knoten der Stellung davor bleibt beim Analysebrett). */
export interface SparringWarning { san: string; beforeText: string; afterText: string; }

/** Das Urteil über einen eigenen Zug: Verlust in Bauern (positiv = schlechter) und die verglichenen Werte. */
export interface BadMoveVerdict { drop: number; before: EvalPoint; after: EvalPoint; }

/** Bewertung in Centibauern — Matt in n als ±(1000 − |n|) Bauern, damit „Matt verpasst" immer weit über der Schwelle
 *  liegt. Ganzzahlig, damit Vergleich und Schwelle nicht an Gleitkomma-Resten kippen (0,4 + 0,3 ≠ 0,7). */
function centiOf(p: Pick<EvalPoint, 'score' | 'scoreType'>): number {
  if (p.scoreType === 'mate') return Math.sign(p.score) * (1000 - Math.abs(p.score)) * 100;
  return Math.round(p.score);
}

/** Bewertung in Bauern aus Weiß-Sicht; Matt in n = ±(1000 − |n|) — genug, damit „Matt verpasst" immer eine Warnung ist. */
export function pawnsOf(p: Pick<EvalPoint, 'score' | 'scoreType'>): number {
  return centiOf(p) / 100;
}

/** Die Bewertung VOR dem Zug, die zur Tiefe des Nachher-Werts passt: die größte Tiefe ≤ `afterDepth`, sonst die kleinste
 *  vorhandene. Leere Spur → `null`. */
export function matchingBefore(before: readonly EvalPoint[], afterDepth: number): EvalPoint | null {
  let atOrBelow: EvalPoint | null = null;
  let smallest: EvalPoint | null = null;
  for (const p of before) {
    if (p.depth <= afterDepth && (!atOrBelow || p.depth > atOrBelow.depth)) atOrBelow = p;
    if (!smallest || p.depth < smallest.depth) smallest = p;
  }
  return atOrBelow ?? smallest;
}

/** Verlust aus Sicht des Ziehenden in Bauern (positiv = schlechter geworden). */
export function evalDrop(before: EvalPoint, after: EvalPoint, mover: 'white' | 'black'): number {
  const whiteLoss = centiOf(before) - centiOf(after);
  return (mover === 'white' ? whiteLoss : -whiteLoss) / 100;
}

/** `null` = kein Grund zur Warnung (auch ohne Vorher-Wert). */
export function badMoveVerdict(before: readonly EvalPoint[], after: EvalPoint, mover: 'white' | 'black',
                               threshold = MAIA_BAD_MOVE_PAWNS): BadMoveVerdict | null {
  const b = matchingBefore(before, after.depth);
  if (!b) return null;
  const drop = evalDrop(b, after, mover);
  // In Centibauern verglichen: 0,2 als Gleitkommazahl darf an der Grenze nicht kippen.
  return Math.round(drop * 100) >= Math.round(threshold * 100) ? { drop, before: b, after } : null;
}
