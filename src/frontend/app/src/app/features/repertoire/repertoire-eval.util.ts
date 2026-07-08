/**
 * Reines Parsen eines Stockfish-Eval-Strings aus WEISS-Sicht für den Repertoire-Trainer-Vergleich
 * (Spielerzug vs. Repertoirezug). BEWUSST getrennt von `BasePuzzleSolver.whiteEvalToPlayerPawns`:
 * dort ist Matt = ±100 (eine Zahl), hier brauchen wir die Matt-SEITE separat (`mateFor`) und eine
 * große Centipawn-Skala (`#N` → 100000 − N), damit „Matt verpasst"/„Matt erlaubt" erkennbar bleibt.
 */

export interface WhiteEval {
  /** Bewertung in Centipawns aus Weiß-Sicht (Matt hoch skaliert: `#N`→100000−N, `#-N`→−100000+N). */
  cp: number;
  /** Welche Seite forciert mattsetzt (`'w'`/`'b'`) bzw. `null`, wenn kein Matt. */
  mateFor: 'w' | 'b' | null;
}

/** Eval-String aus Weiß-Sicht („1.5"/„#3"/„#-2") parsen. Leer/unparsbar → 0 cp, kein Matt. */
export function parseWhiteEval(s: string): WhiteEval {
  if (!s) return { cp: 0, mateFor: null };
  if (s.startsWith('#-')) return { cp: -100000 + parseInt(s.slice(2), 10), mateFor: 'b' };
  if (s.startsWith('#'))  return { cp:  100000 - parseInt(s.slice(1), 10), mateFor: 'w' };
  const v = parseFloat(s);
  return { cp: isNaN(v) ? 0 : Math.round(v * 100), mateFor: null };
}
