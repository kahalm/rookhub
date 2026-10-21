import { EvalGraphMark } from '../../../shared/pgn-viewer/eval-graph.component';
import { GameEvals, MOVE_CLASS_COLORS, MoveClass, reviewGame } from '../game-review.util';

/** Diese Zug-Klassen bekommen einen Punkt auf der Kurve — beim Korrigieren zählen nur die Stellen, an denen etwas kippt. */
const MARKED: ReadonlySet<MoveClass> = new Set<MoveClass>(['mistake', 'miss', 'blunder']);

export interface CorrectionCurve {
  /** Höhe je Stellung (0..100) wie `EvalGraphComponent.series`, Länge = Züge der Korrektur + 1; `null` = keine Bewertung. */
  curve: (number | null)[];
  marks: EvalGraphMark[];
  /** So viele Halbzüge stimmen noch mit der gespeicherten (analysierten) Partie überein. */
  same: number;
}

/**
 * Die Bewertungskurve beim Korrigieren einer Vereinspartie (Wunsch 2026-10-12: „zeig beim Korrigieren auch den Grafen an
 * für eine einfachere Korrektur"): ein Zickzack — ein Zug verschenkt viel, der nächste gibt es gleich zurück — zeigt fast
 * immer einen falsch gelesenen Zug. Die Analyse gehört zur GESPEICHERTEN Partie: gezeichnet wird nur, solange die Züge der
 * Korrektur mit ihr übereinstimmen; ab der ersten Änderung bleibt die Kurve leer (eine Bewertung dorthin zu malen, hieße,
 * eine Stellung zu bewerten, die niemand gerechnet hat). `null` = keine Analyse.
 *
 * `fens` wie in `SheetEditSession.fens()`: `fens[0]` = Start, `fens[i+1]` = nach Halbzug i.
 */
export function correctionCurve(evals: GameEvals | null | undefined, saved: readonly string[],
  plies: readonly { san: string; uci: string }[], fens: readonly string[]): CorrectionCurve | null {
  if (!evals || !evals.plies.length) return null;
  let same = 0;
  while (same < plies.length && same < saved.length && plies[same].san === saved[same]) same++;
  const whole = same === saved.length && same === plies.length;
  const n = Math.min(same, fens.length - 1);
  const review = reviewGame(whole ? evals : { ...evals, final: null }, fens.slice(0, n + 1), plies.slice(0, n).map(p => p.uci));
  const curve: (number | null)[] = Array.from({ length: plies.length + 1 }, (_, j) => j <= n ? review.curve[j] ?? null : null);
  const marks = review.moves
    .filter((m): m is NonNullable<typeof m> => !!m && MARKED.has(m.cls))
    .map(m => ({ ply: m.ply, kind: m.cls, color: MOVE_CLASS_COLORS[m.cls] }));
  return curve.some(v => v != null) ? { curve, marks, same } : null;
}
