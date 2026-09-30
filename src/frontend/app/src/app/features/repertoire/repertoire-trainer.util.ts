import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { calcDests } from '../puzzles/puzzle-move.util';
import { ParsedGame } from '../../shared/pgn-viewer/pgn-parser';
import { TrainColor } from './repertoire-color.util';

/**
 * Reine Helfer des Repertoire-Trainers (ohne Angular, ohne Timer). Erster Schnitt der Zerlegung von
 * `RepertoireTrainerComponent` (Codereview 2026-09-29, F3-002): was hier liegt, ist ohne Komponente
 * testbar und war dort mehrfach ausgeschrieben.
 */

/** Kapitel einer Linie = ihr Black-Header (getrimmt), wie `?chapter=` es meint. */
export function lineChapter(line: ParsedGame): string {
  return (line.headers['Black'] || '').trim();
}

/** Linien eines Kapitels (`?chapter=`); ohne Filter alle — dieselbe Liste, nicht kopiert. */
export function linesInChapter(lines: ParsedGame[], chapter: string | null): ParsedGame[] {
  return chapter ? lines.filter(l => lineChapter(l) === chapter.trim()) : lines;
}

/**
 * Wie viele Züge der Linie spielt der Nutzer (Seite `color`)? Die Seite am Zug kommt aus FEN[0]
 * (Linien können mitten in der Partie beginnen), danach wechselt sie je Halbzug. 0 = die Linie hat
 * für diese Farbe nichts zu üben (auch: keine Züge — dann wird FEN[0] gar nicht erst gelesen).
 */
export function userMoveCount(line: ParsedGame, color: TrainColor): number {
  if (line.moves.length === 0) return 0;
  let side: 'w' | 'b' = new Chess(line.fens[0]).turn();
  let n = 0;
  for (let i = 0; i < line.moves.length; i++) {
    if (side === color) n++;
    side = side === 'w' ? 'b' : 'w';
  }
  return n;
}

/** Legale Zielfelder in dieser Stellung fürs Brett; eine unlesbare FEN gibt ein leeres Brett-Angebot. */
export function destsAt(fen: string): Map<Key, Key[]> {
  try { return calcDests(new Chess(fen)); } catch { return new Map(); }
}
