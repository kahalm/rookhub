import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { BookPuzzleDto } from './puzzle.service';
import { parseMoveShapes } from './move-shapes.util';
import { applyUci, tryLoadFen } from './puzzle-move.util';
import { replayIllegalFen } from './illegal-board.util';
import { lineStepAt } from './line-step.util';
import { latestCommentUpTo } from './book-comment.util';

/** Brettstand einer Durchsicht-Stelle — der Buch-Löser übernimmt ihn 1:1 (Brett, Kommentar, Pfeile). */
export interface ReviewFrame {
  index: number;
  chess: Chess;
  fen: string;
  lastMove: [Key, Key] | undefined;
  turnColor: 'white' | 'black';
  isCheck: boolean;
  moveComment: string | null;
  shapes: DrawShape[];
}

type ReviewPuzzle = Pick<BookPuzzleDto, 'fen' | 'moves' | 'moveComments'>;

const splitMoves = (moves: string | null | undefined): string[] => (moves ?? '').split(' ').filter(m => m);

/**
 * Durchsicht einer Buch-Linie ohne Angular (Zerlegung Z18): „Ganze Partie“ (alle Halbzüge ab der FEN) und
 * Lösungs-Durchsicht (ab dem Trainingsstart), dazu die Brett-Annotationen je Halbzug. Rechnet nur Stellungen;
 * den Zustand (reviewMode/solutionReview) hält die Komponente.
 */
export class BookReviewController {
  /** Ply → Shapes des aktuellen Puzzles (aus `puzzle.moveShapes` geparst; -1 = Einleitung). */
  private shapesByPly: Record<number, DrawShape[]> = {};

  /** Annotationen eines neu aufgesetzten Puzzles übernehmen. */
  setShapes(raw: BookPuzzleDto['moveShapes']): void { this.shapesByPly = parseMoveShapes(raw); }

  /** Board-Annotationen (Pfeile/Feld-Markierungen) zum zuletzt gespielten Halbzug (gleiche Ply-Konvention). */
  shapesForPlyPlayed(plyPlayed: number): DrawShape[] { return this.shapesByPly[plyPlayed] ?? []; }

  /** Anzahl Schritte der Durchsicht: Lösung ab Trainingsstart bzw. die ganze Partie. */
  total(puzzle: ReviewPuzzle | null, solutionReview: boolean, startPly: number): number {
    if (!puzzle) return 0;
    const all = splitMoves(puzzle.moves);
    return solutionReview ? all.length - Math.max(0, startPly) : all.length;
  }

  /** Lösungs-Durchsicht: index = Anzahl Lösungszüge ab Trainingsstart; absoluter Halbzug = start + index. */
  solutionFrame(puzzle: ReviewPuzzle, startPly: number, index: number): ReviewFrame {
    const allMoves = splitMoves(puzzle.moves);
    const start = Math.max(0, startPly);
    const solutionMoves = allMoves.slice(start);
    index = Math.max(0, Math.min(index, solutionMoves.length));
    const chess = tryLoadFen(puzzle.fen);
    if (!chess) return this.staticFrame(puzzle, start + index);   // illegale Diagramm-FEN → per Koordinaten
    // Vorspiel still aufs Brett
    for (let i = 0; i < start; i++) applyUci(chess, allMoves[i]);
    // Lösungszüge bis index
    let last: [Key, Key] | undefined;
    for (let i = 0; i < index; i++) {
      applyUci(chess, solutionMoves[i]);
      last = [solutionMoves[i].substring(0, 2) as Key, solutionMoves[i].substring(2, 4) as Key];
    }
    return {
      index, chess, fen: chess.fen(), lastMove: last,
      turnColor: chess.turn() === 'w' ? 'white' : 'black',
      isCheck: chess.isCheck(),
      // Fällt der aktuelle Zug ohne Kommentar aus, den zuletzt vorher gesehenen behalten (statt zurück
      // auf die Einleitung zu fallen — das wirkte für den User wie ein „Sprung nach hinten").
      moveComment: latestCommentUpTo(puzzle.moveComments, start, start + index - 1),
      shapes: this.shapesForPlyPlayed(start + index - 1),
    };
  }

  /** „Ganze Partie“: index = Anzahl gespielter Halbzüge ab FEN; letzter Zug = moves[index-1]. Dieselbe Regel wie
   *  die Kurs-Durchsicht (`lineStepAt`): illegale Diagramm-FEN → per Koordinaten, chess.js bekommt dann einen nie
   *  angezeigten Platzhalter. */
  fullGameFrame(puzzle: ReviewPuzzle, index: number): ReviewFrame {
    const step = lineStepAt(puzzle.fen, splitMoves(puzzle.moves), index);
    return {
      index: step.index, chess: step.chess ?? new Chess(), fen: step.fen, lastMove: step.lastMove,
      turnColor: step.turnColor, isCheck: step.isCheck,
      // Bei Zügen ohne Kommentar den zuletzt gesehenen behalten (statt bis zur Einleitung zurückzufallen).
      moveComment: latestCommentUpTo(puzzle.moveComments, 0, step.index - 1),
      shapes: this.shapesForPlyPlayed(step.index - 1),
    };
  }

  /** Illegale (Chessable-Diagramm-)FEN ohne chess.js: die Stellung nach `index` Halbzügen rein per Koordinaten
   *  (kein Legalitätscheck) — chess.js bekommt einen sicheren, nie angezeigten Platzhalter. */
  staticFrame(puzzle: ReviewPuzzle, index: number): ReviewFrame {
    const moves = splitMoves(puzzle.moves);
    const idx = Math.max(0, Math.min(index, moves.length));
    const replay = replayIllegalFen(puzzle.fen, moves, idx);
    return {
      index: idx, chess: new Chess(), fen: replay.fen,
      lastMove: replay.lastMove as [Key, Key] | undefined,
      turnColor: replay.whiteToMove ? 'white' : 'black',
      isCheck: false,
      moveComment: latestCommentUpTo(puzzle.moveComments, 0, idx - 1),
      shapes: this.shapesForPlyPlayed(idx - 1),
    };
  }
}
