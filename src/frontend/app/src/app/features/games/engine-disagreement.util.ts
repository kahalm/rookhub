import { GameReview, MoveClass } from './game-review.util';

/**
 * Wo zwei Analysen derselben Partie (Stockfish und Lc0) einen Zug WIRKLICH verschieden sehen (0.683.0, gewünscht
 * 2026-10-06: „Züge markieren, wo sie unterschiedlicher Meinung sind, einfach anspringbar").
 *
 * Verglichen wird die GRUNDklasse (`ReviewedMove.base`) — Etiketten wie Miss/Great/Buch hängen an Nebenbedingungen,
 * nicht an der Meinung der Engine. Bewusst streng: an sechs Prod-Partien (488 Züge) war die Klasse bei einem Drittel
 * irgendwie verschieden, fast immer an einer Bandgrenze (3,9 gegen 6,2 Punkte Verlust = „gut" gegen „Ungenauigkeit").
 * Uneinig heißt deshalb nur:
 * - die eine sieht einen Fehler oder groben Fehler, die andere höchstens „gut", ODER
 * - nur eine sieht einen groben Fehler, und die andere liegt mindestens zwei Stufen darunter.
 * Mit dieser Regel blieben 5 Züge, alle echte Meinungsverschiedenheiten (u. a. ein Matt, das Lc0 nicht sah).
 */
const RANK: Partial<Record<MoveClass, number>> = {
  best: 0, excellent: 1, good: 2, inaccuracy: 3, mistake: 4, blunder: 5,
};

export interface EngineDisagreement {
  /** Halbzug-Index wie `currentMoveIndex`. */
  ply: number;
  primary: MoveClass;
  alt: MoveClass;
}

/** Sehen die beiden Grundklassen den Zug so verschieden, dass es eine Markierung wert ist? */
export function classesDisagree(a: MoveClass, b: MoveClass): boolean {
  const ra = RANK[a];
  const rb = RANK[b];
  if (ra === undefined || rb === undefined) return false;
  if ((ra >= 4 && rb <= 2) || (rb >= 4 && ra <= 2)) return true;
  return (ra === 5) !== (rb === 5) && Math.abs(ra - rb) >= 2;
}

/** Die uneinigen Züge in Partie-Reihenfolge; Züge, die eine der beiden nicht bewerten kann, fallen weg. */
export function engineDisagreements(primary: GameReview, alt: GameReview): EngineDisagreement[] {
  const out: EngineDisagreement[] = [];
  const n = Math.min(primary.moves.length, alt.moves.length);
  for (let i = 0; i < n; i++) {
    const a = primary.moves[i];
    const b = alt.moves[i];
    if (a && b && classesDisagree(a.base, b.base)) out.push({ ply: i, primary: a.base, alt: b.base });
  }
  return out;
}

/** Nächster (`dir` = 1) bzw. voriger (−1) uneiniger Zug ab `current`, mit Umlauf; `null` ohne uneinige Züge. */
export function stepDisagreement(list: readonly EngineDisagreement[], current: number, dir: 1 | -1): number | null {
  if (!list.length) return null;
  if (dir === 1) return (list.find(d => d.ply > current) ?? list[0]).ply;
  const before = list.filter(d => d.ply < current);
  return (before.length ? before[before.length - 1] : list[list.length - 1]).ply;
}

/** „12." bzw. „12..." aus der Stellung VOR dem Zug — die Partie muss nicht bei Zug 1 beginnen. */
export function moveNumberLabel(fenBefore: string | undefined, ply: number): string {
  const parts = fenBefore?.split(' ') ?? [];
  const full = Number(parts[5]);
  const black = parts[1] === 'b';
  if (Number.isFinite(full) && full > 0 && parts[1]) return black ? `${full}...` : `${full}.`;
  const n = Math.floor(ply / 2) + 1;
  return ply % 2 ? `${n}...` : `${n}.`;
}
