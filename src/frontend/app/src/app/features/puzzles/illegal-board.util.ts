/**
 * „Dummer" Brett-Replayer für ILLEGALE Diagramm-Stellungen (Chessable-Muster-/Info-Seiten ohne König
 * o. Ä.). chess.js verwirft solche FENs, daher können die Demonstrationszüge dort nicht nachgespielt
 * werden. Diese Util schiebt Figuren rein per Koordinaten (from→to) über ein 8×8-Array — OHNE jede
 * Legalitätsprüfung — und liefert je Halbzug eine Anzeige-FEN für chessground. Nur für die reinen
 * Durchklick-Info-Linien gedacht (kein Lösen). Behandelt Schlag, Umwandlung, Rochade und En passant.
 *
 * Index-Konvention des internen Arrays: `rank*8 + file`, Reihe 0 = 1. Reihe (a1 = 0, h8 = 63).
 */

function sqIndex(sq: string): number {
  return (sq.charCodeAt(1) - 49) * 8 + (sq.charCodeAt(0) - 97); // '1'→0, 'a'→0
}

/** FEN-Brettteil (Feld 0) → 64er-Array ('' = leer). */
function parseBoard(fen: string): string[] {
  const board: string[] = new Array(64).fill('');
  const rows = fen.split(/\s+/)[0].split('/');
  for (let i = 0; i < rows.length && i < 8; i++) {
    const rank = 7 - i; // erste Zeile = 8. Reihe
    let file = 0;
    for (const c of rows[i]) {
      if (c >= '1' && c <= '9') file += c.charCodeAt(0) - 48;
      else { if (file < 8) board[rank * 8 + file] = c; file++; }
    }
  }
  return board;
}

/** 64er-Array → FEN-Brettteil (Reihe 8 zuerst). */
function boardToFenPlacement(board: string[]): string {
  const rows: string[] = [];
  for (let rank = 7; rank >= 0; rank--) {
    let row = '', empty = 0;
    for (let file = 0; file < 8; file++) {
      const p = board[rank * 8 + file];
      if (p === '') empty++;
      else { if (empty) { row += empty; empty = 0; } row += p; }
    }
    if (empty) row += empty;
    rows.push(row);
  }
  return rows.join('/');
}

/** Einen UCI-Zug auf das Array anwenden (Schlag/Umwandlung/Rochade/En passant), ohne Legalität. */
function applyOne(board: string[], uci: string): void {
  const from = sqIndex(uci.slice(0, 2));
  const to = sqIndex(uci.slice(2, 4));
  const promo = uci.length > 4 ? uci[4] : '';
  const piece = board[from];
  board[from] = '';
  const fromFile = from % 8, toFile = to % 8, fromRank = from / 8 | 0;
  const isPawn = piece === 'P' || piece === 'p';
  const isKing = piece === 'K' || piece === 'k';
  // En passant: Bauer schlägt diagonal auf ein leeres Feld → geschlagener Bauer steht „daneben".
  if (isPawn && fromFile !== toFile && board[to] === '') board[fromRank * 8 + toFile] = '';
  // Rochade: König zwei Felder → Turm mitziehen.
  if (isKing && Math.abs(toFile - fromFile) === 2) {
    const rank = fromRank;
    const kingside = toFile === 6;
    const rookFrom = rank * 8 + (kingside ? 7 : 0), rookTo = rank * 8 + (kingside ? 5 : 3);
    board[rookTo] = board[rookFrom]; board[rookFrom] = '';
  }
  board[to] = promo ? (piece === piece.toUpperCase() ? promo.toUpperCase() : promo.toLowerCase()) : piece;
}

export interface IllegalReplay {
  /** Anzeige-FEN (Brett + Farbe am Zug) für chessground. */
  fen: string;
  /** Zuletzt gespielter Zug [from, to] fürs Highlight (undefined bei Index 0). */
  lastMove?: [string, string];
  /** Farbe am Zug NACH `count` Halbzügen. */
  whiteToMove: boolean;
}

/**
 * Spielt die ersten `count` UCI-Halbzüge ab `fen` (rein per Koordinaten) und gibt die Anzeige-FEN,
 * den letzten Zug und die Farbe am Zug zurück. `count` wird auf [0, moves.length] geklemmt.
 */
export function replayIllegalFen(fen: string, uciMoves: string[], count: number): IllegalReplay {
  const board = parseBoard(fen);
  let white = fen.split(/\s+/)[1] !== 'b';
  let last: [string, string] | undefined;
  const n = Math.max(0, Math.min(count, uciMoves.length));
  for (let i = 0; i < n; i++) {
    const uci = uciMoves[i];
    if (uci.length < 4) continue;
    applyOne(board, uci);
    last = [uci.slice(0, 2), uci.slice(2, 4)];
    white = !white;
  }
  return { fen: `${boardToFenPlacement(board)} ${white ? 'w' : 'b'} - - 0 1`, lastMove: last, whiteToMove: white };
}
