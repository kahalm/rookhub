import { Chess } from 'chess.js';
import { DrawShape } from 'chessground/draw';
import { BookPuzzleDto } from '../../puzzles/puzzle.service';
import { applyUci, tryLoadFen } from '../../puzzles/puzzle-move.util';
import { parseMoveShapes } from '../../puzzles/move-shapes.util';

/**
 * Eine druckbare/lernbare Karteikarte. Zwei Spielarten:
 *
 * KURS-Linie (Puzzle): VORN die AUSGANGSSTELLUNG des Trainings (Aufgabe; nur die dort schon
 * sichtbaren Kontext-Markierungen, nie Lösungs-Pfeile), HINTEN die LÖSUNG in Notation plus die
 * Abschlussbeschreibung (Kommentar des letzten Zugs, sonst Linien-Kommentar). Stellungs-/Info-
 * Linien: die Stellung selbst mit ihren Markierungen, hinten nur die Beschreibung.
 *
 * REPERTOIRE-Linie (umgekehrt): VORN die ENDSTELLUNG mit den Pfeilen des letzten Zugs
 * ([%cal]/[%csl] aus dem PGN-Kommentar), HINTEN die ganze Linie in Notation plus die
 * Abschlussbeschreibung — man erkennt das Bild und rekonstruiert den Weg dorthin.
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
    // die Karte; Markierungen aus der Einleitung (-1) bzw. dem letzten bekannten Ply.
    return {
      heading, chapter,
      frontFen: p.fen,
      orientation: fenTurn(p.fen),
      shapes: shapesByPly[-1] || shapesByPly[lastPly] || [],
      notation: '',
      closing,
    };
  }

  // Setup bis zum Trainingsstart einspielen → VORN steht die AUFGABEN-Stellung; danach die
  // Lösungszüge sammeln (HINTEN). startPly = letzter Setup-Halbzug; -1 = FEN ist schon die Aufgabe.
  const startPly = typeof p.startPly === 'number' ? p.startPly : 0;
  try {
    for (let i = 0; i <= Math.min(startPly, uciMoves.length - 1); i++) applyUci(chess, uciMoves[i]);
  } catch { return null; }
  const frontFen = chess.fen();
  const orientation: 'white' | 'black' = chess.turn() === 'w' ? 'white' : 'black';
  const solutionStartWhite = chess.turn() === 'w';
  const solutionStartNum = chess.moveNumber();

  const sans: string[] = [];
  for (let i = Math.max(startPly + 1, 0); i < uciMoves.length; i++) {
    try { sans.push(applyUci(chess, uciMoves[i]).san); }
    catch { return null; }   // kaputte Zugliste → keine Karte statt falscher Karte
  }

  return {
    heading, chapter,
    frontFen,
    orientation,
    // VORN nur, was der Löser dort ohnehin sieht (Kontext-Markierungen des Setups) —
    // Lösungs-Pfeile späterer Züge würden die Aufgabe verraten.
    shapes: shapesByPly[startPly] || shapesByPly[-1] || [],
    notation: formatNotation(sans, solutionStartWhite, solutionStartNum),
    closing,
  };
}

// ===== Repertoire-Linien (aus dem kombinierten PGN) ========================

/** Farb-Präfixe der Chessbase-Marker: G/R/B/Y → chessground-Brushes. */
const CAL_BRUSH: Record<string, string> = { G: 'green', R: 'red', B: 'blue', Y: 'yellow' };

/** Parst [%cal Gc8h3,Rd1d8] + [%csl Gd4,Re5] eines Kommentar-Blocks zu DrawShapes. */
export function parseCalCsl(comment: string): DrawShape[] {
  const shapes: DrawShape[] = [];
  for (const m of comment.matchAll(/\[%cal\s+([^\]]+)\]/g)) {
    for (const tok of m[1].split(',')) {
      const t = tok.trim();
      const b = CAL_BRUSH[t[0]?.toUpperCase() || ''];
      if (b && t.length >= 5) shapes.push({ orig: t.slice(1, 3) as never, dest: t.slice(3, 5) as never, brush: b });
    }
  }
  for (const m of comment.matchAll(/\[%csl\s+([^\]]+)\]/g)) {
    for (const tok of m[1].split(',')) {
      const t = tok.trim();
      const b = CAL_BRUSH[t[0]?.toUpperCase() || ''];
      if (b && t.length >= 3) shapes.push({ orig: t.slice(1, 3) as never, brush: b });
    }
  }
  return shapes;
}

/** Pfeile/Markierungen des LETZTEN Kommentar-Blocks mit Markern (≈ Endstellungs-Annotation). */
export function extractLastShapes(rawGame: string): DrawShape[] {
  let last: DrawShape[] = [];
  for (const m of rawGame.matchAll(/\{[^}]*\}/g)) {
    const shapes = parseCalCsl(m[0]);
    if (shapes.length) last = shapes;
  }
  return last;
}

/** Eine Repertoire-Karte samt Linien-Schlüssel (für ?lines=-Auswahl aus der Linienliste). */
export interface RepertoireFlashcard {
  lineKey: string;
  card: Flashcard;
}

/**
 * Baut aus dem kombinierten Repertoire-PGN je Spiel (= Linie) eine Karte — UMGEKEHRT zur
 * Kurs-Karte: vorn die Endstellung mit den Pfeilen, hinten Linie + Abschlussbeschreibung.
 * Braucht die bereits geparsten Spiele (pgn-viewer) UND die Roh-Abschnitte (für die Marker).
 */
export function buildRepertoireFlashcards(
  games: { headers: Record<string, string>; moves: { san: string }[]; fens: string[];
           comments: { [idx: number]: string } }[],
  rawGames: string[],
  lineKeyOf: (sans: string[]) => string,
): RepertoireFlashcard[] {
  const out: RepertoireFlashcard[] = [];
  for (let g = 0; g < games.length; g++) {
    const game = games[g];
    if (!game.moves.length || game.fens.length !== game.moves.length + 1) continue;
    const sans = game.moves.map(m => m.san);
    const startFen = game.fens[0];
    const startWhite = (startFen.split(' ')[1] || 'w') === 'w';
    const startNum = Number(startFen.split(' ')[5]) || 1;
    // Abschlussbeschreibung = letzter kommentierter Zug (Marker sind vom Parser schon entfernt).
    const commentIdxs = Object.keys(game.comments).map(Number).filter(n => n >= 0);
    const closing = commentIdxs.length ? game.comments[Math.max(...commentIdxs)] : '';
    // Ausrichtung = Seite, die den LETZTEN Zug macht (Repertoire-Linien enden mit dem eigenen Zug).
    const lastMoveWhite = startWhite === (sans.length % 2 === 1);
    out.push({
      lineKey: lineKeyOf(sans),
      card: {
        heading: game.headers['White']?.trim() || `#${g + 1}`,
        chapter: game.headers['Black']?.trim() || null,
        frontFen: game.fens[game.fens.length - 1],
        orientation: lastMoveWhite ? 'white' : 'black',
        shapes: extractLastShapes(rawGames[g] || ''),
        notation: formatNotation(sans, startWhite, startNum),
        closing,
      },
    });
  }
  return out;
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
