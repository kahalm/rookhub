import { Chess } from 'chess.js';

/**
 * Der Lichess-Broker kodiert Rochaden im ausgelieferten PV als **König-schlägt-Turm**
 * (`m.to_uci(CastlingMode::Chess960)` in lila-engine `emit.rs`) — also `e1h1` statt `e1g1`.
 * chess.js kennt in der Standardvariante nur die König-zwei-Felder-Form und wirft bei `e1h1`;
 * ohne Umschreiben bräche jede Variante an der ersten Rochade ab (und der Zugpfeil zeigte
 * auf den eigenen Turm).
 *
 * Blindes Ersetzen der vier Formen wäre falsch: `e1h1` ist ein völlig legaler TURM-Zug, wenn
 * auf e1 ein Turm steht (König anderswo). Deshalb wird die Linie mitgespielt und nur dort
 * umgeschrieben, wo das ziehende Feld wirklich einen König trägt.
 */
const CASTLE_FORMS: Record<string, string> = {
  e1h1: 'e1g1',
  e1a1: 'e1c1',
  e8h8: 'e8g8',
  e8a8: 'e8c8',
};

/** Schreibt König-schlägt-Turm-Rochaden einer UCI-Linie in die Standardform um. */
export function normalizeCastlingUci(fen: string, moves: string[]): string[] {
  if (!moves.length || !moves.some(m => CASTLE_FORMS[m?.slice(0, 4)])) return moves;

  let board: Chess;
  try { board = new Chess(fen); } catch { return moves; }

  const out: string[] = [];
  for (let i = 0; i < moves.length; i++) {
    const uci = moves[i];
    let normalized = uci;
    const target = CASTLE_FORMS[uci?.slice(0, 4)];
    if (target) {
      let piece: { type: string } | null | undefined;
      try { piece = board.get(uci.slice(0, 2) as never) as { type: string } | null; } catch { piece = null; }
      if (piece?.type === 'k') normalized = target;
    }
    out.push(normalized);

    // Weiterspielen, damit auch spätere Rochaden der Variante erkannt werden. Bricht der Nachbau
    // ab (illegaler/unbekannter Zug), bleibt der Rest unverändert — lieber die Rohform liefern
    // als die Linie zu verfälschen; der Anzeige-Nachbau bricht dort ohnehin ab.
    let played: unknown = null;
    try {
      played = board.move({
        from: normalized.slice(0, 2),
        to: normalized.slice(2, 4),
        promotion: normalized.length > 4 ? normalized[4] : undefined,
      } as never);
    } catch { played = null; }
    if (!played) return out.concat(moves.slice(i + 1));
  }
  return out;
}
