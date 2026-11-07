import { plyLabel } from './training-lines';

/**
 * Partien des Gegners je Stellung (0.892.0, Wunsch 07.11.: „rechts eine meldung ab wann ich von gespielten partien abgewichen
 * bin"): `positions` von `training-lines?positions=true` bzw. `…/deviations` — Schlüssel = die ersten drei FEN-Felder (wie
 * `RepertoireReach.Key` am Server), `n` erreicht, `y` jüngstes Jahr, `m` SAN → Partien seiner Züge (nur wo ER am Zug war).
 */
export interface OpponentPosition { n: number; y: number | null; m: Record<string, number> }
export type OpponentPositions = Record<string, OpponentPosition>;

/** Schlüssel einer Stellung wie am Server (Stellung, Seite am Zug, Rochaderechte). */
export function positionKey(fen: string): string {
  return fen.trim().split(/\s+/).slice(0, 3).join(' ');
}

/** Was die Anzeige zur aktuellen Stellung sagt. `in`: in seinen Partien erreicht (mit seinen Zügen, falls er am Zug ist);
 *  `left`: ab Halbzug `leftPly` (Index in den Zügen) kommt keine seiner Partien mehr — bleibt bis zum Linienende stehen;
 *  `never`: schon die Ausgangsstellung kennt er nicht. */
export type OpponentNote =
  | { kind: 'in'; reached: number; lastYear: number | null; moves: { san: string; n: number }[] }
  | { kind: 'left'; leftPly: number; move: string }
  | { kind: 'never' };

/**
 * Anzeige für die Stellung nach `ply` Halbzügen einer Linie (`fens[0]` = Ausgangsstellung, `fens[i]` = nach Halbzug i): ist eine
 * Stellung auf dem Weg dorthin NICHT in seinen Partien, gilt der erste solche Halbzug („Ab 7.Le3 keine Partien mehr"). Seine Züge
 * höchstens `maxMoves`, häufigste zuerst. `lang` für die Zugangabe (Figurenbuchstaben).
 */
export function opponentNote(fens: readonly string[], sans: readonly string[], ply: number, positions: OpponentPositions | null | undefined,
                             lang = 'de', maxMoves = 3): OpponentNote | null {
  if (!positions || !fens.length) return null;
  const upto = Math.max(0, Math.min(ply, fens.length - 1));
  for (let i = 0; i <= upto; i++) {
    if (positions[positionKey(fens[i])]) continue;
    if (i === 0) return { kind: 'never' };
    return { kind: 'left', leftPly: i - 1, move: plyLabel([...sans], fens[0], i - 1, lang) };
  }
  const st = positions[positionKey(fens[upto])];
  const moves = Object.entries(st.m ?? {}).map(([san, n]) => ({ san, n })).sort((a, b) => b.n - a.n).slice(0, maxMoves);
  return { kind: 'in', reached: st.n, lastYear: st.y ?? null, moves };
}

/** „6…Sf6 9×, 6…e6 3×" — seine Züge in der Stellung nach `ply` Halbzügen. */
export function opponentMovesText(moves: { san: string; n: number }[], fens: readonly string[], sans: readonly string[], ply: number,
                                  lang = 'de'): string {
  return moves.map(m => {
    const line = [...sans.slice(0, ply), m.san];
    return `${plyLabel(line, fens[0], ply, lang)} ${m.n}×`;
  }).join(', ');
}
