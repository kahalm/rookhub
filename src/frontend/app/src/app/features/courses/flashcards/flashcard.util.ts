import { Chess } from 'chess.js';
import { DrawShape } from 'chessground/draw';
import { BookPuzzleDto } from '../../puzzles/puzzle.service';
import { applyUci, tryLoadFen } from '../../puzzles/puzzle-move.util';
import { parseMoveShapes } from '../../puzzles/move-shapes.util';

/**
 * Eine druckbare Karteikarte zu einer Kurs-Linie:
 * VORN die ENDSTELLUNG der Linie (bei Stellungs-/Info-Linien die Stellung selbst) mit den
 * Pfeilen/Markierungen, die Chessable an den letzten Zug hängt; HINTEN die ganze Linie in
 * Notation plus die Abschlussbeschreibung (Kommentar des letzten Zugs, sonst Linien-Kommentar).
 */
export interface Flashcard {
  /** Kopfzeile zur Identifikation (Titel bzw. Linien-Nummer). */
  heading: string;
  chapter: string | null;
  /** FEN der Endstellung (bzw. der Stellung selbst, wenn keine Züge da sind). */
  frontFen: string;
  /** Brett-Ausrichtung = Seite des Trainierenden. */
  orientation: 'white' | 'black';
  /** Pfeile/Feld-Markierungen der Endstellung. */
  shapes: DrawShape[];
  /** Linie in SAN-Notation mit Zugnummern ('' bei Stellungs-Linien). */
  notation: string;
  /** Abschlussbeschreibung (letzter Zug-Kommentar > Linien-Kommentar), '' wenn keiner. */
  closing: string;
}

/** Baut die Karte zu einer Linie; null, wenn nichts Druckbares da ist (weder Stellung noch Züge). */
export function buildFlashcard(p: BookPuzzleDto): Flashcard | null {
  if (!p?.fen) return null;
  const uciMoves = (p.moves || '').split(' ').filter(m => m.length >= 4);
  const shapesByPly = parseMoveShapes(p.moveShapes);
  const heading = (p.title && p.title.trim()) || `#${p.round}`;
  const chapter = p.chapter?.trim() || null;

  // Kommentare: Schlüssel = Halbzug NACH dem Zug; -1 = Einleitung. Abschluss = letzter Zug.
  const mc = p.moveComments || {};
  const lastPly = uciMoves.length - 1;
  const closing = (uciMoves.length > 0 ? mc[String(lastPly)] : undefined)
    || p.comment || mc['-1'] || '';

  const chess = tryLoadFen(p.fen);
  if (!chess || uciMoves.length === 0) {
    // Stellungs-/Info-Linie (auch bewusst illegale Muster-Diagramme): die Stellung selbst ist
    // die Karte; Pfeile aus der Einleitung (-1) bzw. dem letzten bekannten Ply.
    return {
      heading, chapter,
      frontFen: p.fen,
      orientation: fenTurn(p.fen),
      shapes: shapesByPly[-1] || shapesByPly[lastPly] || [],
      notation: '',
      closing,
    };
  }

  // Linie durchspielen: SANs sammeln, Endstellung bestimmen, Trainierenden-Seite ermitteln.
  const startTurn = chess.turn();
  const startNum = chess.moveNumber();
  const sans: string[] = [];
  const startPly = typeof p.startPly === 'number' ? p.startPly : 0;
  let orientation: 'white' | 'black' = startTurn === 'w' ? 'white' : 'black';
  for (let i = 0; i < uciMoves.length; i++) {
    // Der Trainierende spielt ab moves[startPly+1] → seine Seite ist die, die dort am Zug ist.
    if (i === startPly + 1) orientation = chess.turn() === 'w' ? 'white' : 'black';
    let san: string;
    try { san = applyUci(chess, uciMoves[i]).san; }
    catch { return null; }   // kaputte Zugliste → keine Karte statt falscher Karte
    sans.push(san);
  }
  if (startPly + 1 >= uciMoves.length && uciMoves.length > 0 && startPly >= 0) {
    // Trainingsstart läge hinter dem Linienende (defensiv) — Seite des letzten Zugs nehmen.
    orientation = sans.length % 2 === (startTurn === 'w' ? 1 : 0) ? 'white' : 'black';
  }
  if (startPly < 0) orientation = startTurn === 'w' ? 'white' : 'black';

  return {
    heading, chapter,
    frontFen: chess.fen(),
    orientation,
    shapes: shapesByPly[lastPly] || [],
    notation: formatNotation(sans, startTurn === 'w', startNum),
    closing,
  };
}

/** SAN-Liste mit Zugnummern („12.Lb5 a6 13.…" bzw. „12…a6 13.…"). */
export function formatNotation(sans: string[], startWhite: boolean, startNum: number): string {
  const parts: string[] = [];
  let num = startNum;
  let white = startWhite;
  for (let i = 0; i < sans.length; i++) {
    if (white) parts.push(`${num}.${sans[i]}`);
    else if (i === 0) parts.push(`${num}…${sans[i]}`);
    else parts.push(sans[i]);
    if (!white) num++;
    white = !white;
  }
  return parts.join(' ');
}

function fenTurn(fen: string): 'white' | 'black' {
  return (fen.split(' ')[1] || 'w') === 'b' ? 'black' : 'white';
}
