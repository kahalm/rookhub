/**
 * Seite am Zug aus dem zweiten FEN-Feld — die EINE Stelle im Frontend (Codereview N11-006, Gegenstück zu
 * `FenFields.WhiteToMove` im Server). Gleiche Regel wie dort: nur „b" (auch „B") heißt Schwarz; fehlt das Feld
 * oder ist es unlesbar, ist Weiß am Zug. Mehrfache und führende Leerzeichen zählen nicht. Bewusst ohne chess.js:
 * Buch-/Kurs-FENs sind nicht immer legal, die Seite am Zug soll trotzdem stimmen.
 */
export type FenSide = 'w' | 'b';

export function sideToMove(fen: string | null | undefined): FenSide {
  return (fen ?? '').trim().split(/\s+/)[1]?.toLowerCase() === 'b' ? 'b' : 'w';
}

export function whiteToMove(fen: string | null | undefined): boolean {
  return sideToMove(fen) === 'w';
}
