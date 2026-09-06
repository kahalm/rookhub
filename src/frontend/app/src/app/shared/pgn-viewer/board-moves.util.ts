import { Chess } from 'chess.js';

/** Legale Ziele je Ausgangsfeld für die Seite am Zug — Chessground-`dests`-Format. */
export function legalDests(fen: string): { color: 'white' | 'black'; dests: Map<string, string[]> } | null {
  try {
    const chess = new Chess(fen);
    const dests = new Map<string, string[]>();
    for (const m of chess.moves({ verbose: true })) {
      const arr = dests.get(m.from);
      if (arr) arr.push(m.to); else dests.set(m.from, [m.to]);
    }
    return { color: chess.turn() === 'w' ? 'white' : 'black', dests };
  } catch {
    return null; // illegale/unvollständige FEN → Brett bleibt reine Anzeige
  }
}

/** Wendet einen Nutzer-Zug auf die FEN an (Umwandlung immer Dame). */
export function applyUserMove(fen: string, from: string, to: string): { san: string; fen: string } | null {
  try {
    const chess = new Chess(fen);
    const move = chess.move({ from, to, promotion: 'q' });
    return { san: move.san, fen: chess.fen() };
  } catch {
    return null;
  }
}

/** Stellung NACH einem UCI-Zug (`e2e4`, Umwandlung `e7e8q`); `null`, wenn der Zug dort nicht geht.
 *  Anders als `applyUserMove` respektiert das die Umwandlungsfigur, kommt also auch mit einer
 *  gespeicherten Partie-Zugliste zurecht (Unterverwandlung `e7e8n`). */
export function fenAfterUci(fen: string, uci: string): string | null {
  if (!uci || uci.length < 4) return null;
  try {
    const chess = new Chess(fen);
    chess.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci.length > 4 ? uci[4] : undefined });
    return chess.fen();
  } catch {
    return null;
  }
}

/**
 * Seite am Zug aus der FEN. Chessground fuehrt `turnColor` als EIGENEN Zustand neben den Figuren:
 * `baseUserMove` dreht ihn nach jedem Nutzerzug um, und `configure` setzt ihn NICHT aus einer
 * uebergebenen FEN zurueck. Wer ein Brett benutzt, das den Nutzerzug nicht selbst uebernimmt,
 * sondern die naechste Stellung zurueckbindet, muss `turnColor` deshalb mitgeben — sonst steht er
 * dauerhaft falsch und `isMovable` weist ab dem ZWEITEN Zug jeden Zug ab.
 */
export function turnColorOf(fen: string): 'white' | 'black' {
  return fen?.split(' ')[1] === 'b' ? 'black' : 'white';
}
