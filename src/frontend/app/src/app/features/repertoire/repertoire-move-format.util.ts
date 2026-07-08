import { Chess } from 'chess.js';

/**
 * Reine Zug-Nummerierung/Vollzug-Ableitung für den Repertoire-Trainer. Zieht die stabile Ausgangs-
 * Nummerierung (Seite am Zug + Vollzugzähler) aus dem Start-FEN — genau wie zuvor doppelt in
 * `movesInLine`/`currentMovePrettyLabel` der Trainer-Komponente. Kein Zustand, keine Mutation.
 */

export interface StartNumbering {
  /** Seite, die im Start-FEN am Zug ist. */
  side: 'w' | 'b';
  /** Vollzugzähler des Start-FEN (Feld 6), ≥ 1. */
  fullMove: number;
}

/** Ausgangs-Nummerierung aus dem Start-FEN. Unparsbare FEN → Defaults (Weiß, Zug 1) — identisch
 * zum bisherigen try/catch-Verhalten in der Komponente. */
export function startNumbering(startFen: string): StartNumbering {
  let side: 'w' | 'b' = 'w';
  let fullMove = 1;
  try {
    const start = new Chess(startFen);
    side = start.turn();
    const parts = startFen.split(/\s+/);
    const n = parseInt(parts[5] || '1', 10);
    if (Number.isFinite(n) && n >= 1) fullMove = n;
  } catch { /* Defaults reichen */ }
  return { side, fullMove };
}

/** Menschenlesbares Label eines Halbzugs, z. B. „3. exd5" (Weiß) oder „2… d6" (Schwarz).
 * `ply` = 0-basierter Halbzug-Index ab dem Start-FEN. */
export function prettyMoveLabel(startFen: string, san: string, ply: number): string {
  const { side, fullMove } = startNumbering(startFen);
  let curSide = side;
  let num = fullMove;
  for (let p = 0; p < ply; p++) {
    if (curSide === 'b') num++;
    curSide = curSide === 'w' ? 'b' : 'w';
  }
  return curSide === 'w' ? `${num}. ${san}` : `${num}… ${san}`;
}
