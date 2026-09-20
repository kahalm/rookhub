import { BookPuzzleDto } from '../puzzles/puzzle.service';
import { applyUci, tryLoadFen } from '../puzzles/puzzle-move.util';
import { buildFlashcard } from '../courses/flashcards/flashcard.util';
import { NewWorksheetItem } from './worksheet.service';

/**
 * Nur ABGEFRAGTE Linien gehören auf ein Aufgabenblatt: Info-/Erklärseiten (Kurseinleitung,
 * Muster-Diagramme) haben keine Lösung, und eine Linie ohne Züge stellt keine Frage.
 */
export function isQuizLine(p: BookPuzzleDto): boolean {
  return !p.isInfoOnly && (p.moves ?? '').trim().length > 0;
}

/**
 * Kurs-Linien → Aufgaben. Die Stellung ist die, in der die Linie GEFRAGT wird (Vorspielzüge
 * eingespielt) — dieselbe Rechnung wie bei den Karteikarten, damit Blatt und Karte nie
 * auseinanderlaufen. Überschrift und Begleittext bleiben leer: der Linientitel („Matt in 3")
 * verriete die Aufgabe, die Worte schreibt der Ersteller selbst dazu.
 *
 * <p>Die LÖSUNG (die Züge ab der Aufgabenstellung) wandert mit — nicht fürs Papier, sondern für
 * den geteilten Link: dort wird das Blatt durchgespielt, und ohne Lösung bliebe es ein Bilderbogen.
 * Auf dem Ausdruck steht sie nie.</p>
 */
export function itemsFromLines(bookId: number, puzzles: BookPuzzleDto[]): NewWorksheetItem[] {
  const items: NewWorksheetItem[] = [];
  for (const p of puzzles) {
    if (!isQuizLine(p)) continue;
    const card = buildFlashcard(p);
    if (!card?.frontFen) continue;   // nicht aufbaubar (kaputte Zugliste) → überspringen
    items.push({
      fen: card.frontFen,
      orientation: card.orientation,
      solutionMoves: solutionFrom(p.moves, typeof p.startPly === 'number' ? p.startPly : 0),
      source: 'Book',
      sourceId: p.id,
      bookId,
    });
  }
  return items;
}

/**
 * Ein gelöstes Puzzle → Aufgabe. `startPly` folgt dem Solver: −1 = die FEN IST schon die Aufgabe,
 * sonst sind die Züge bis einschließlich `startPly` Vorspiel (Standard/Endlos: der eine
 * Gegnerzug `moves[0]`). Gibt `null` zurück, wenn sich die Stellung nicht aufbauen lässt.
 */
export function taskItemFromPuzzle(
  info: { fen: string; moves: string; orientation: 'white' | 'black'; startPly?: number },
  source: 'Standard' | 'Book',
  ids: { sourceId?: number | null; bookId?: number | null } = {},
): NewWorksheetItem | null {
  const chess = tryLoadFen(info.fen);
  if (!chess) return null;

  const moves = (info.moves || '').split(' ').filter(m => m.length >= 4);
  const startPly = typeof info.startPly === 'number' ? info.startPly : 0;
  try {
    for (let i = 0; i <= Math.min(startPly, moves.length - 1); i++) applyUci(chess, moves[i]);
  } catch {
    return null;
  }

  return {
    fen: chess.fen(),
    orientation: info.orientation,
    solutionMoves: solutionFrom(info.moves, startPly),
    source,
    sourceId: ids.sourceId ?? null,
    bookId: ids.bookId ?? null,
  };
}

/**
 * Die Züge NACH dem Vorspiel — also die Lösung ab der Aufgabenstellung, Gegnerantworten
 * eingeschlossen (der Solver hinter dem geteilten Link spielt sie selbst). `startPly` folgt dem
 * Solver: −1 = kein Vorspiel, 0 = `moves[0]` ist Vorspiel.
 */
export function solutionFrom(moves: string | null | undefined, startPly: number): string {
  const list = (moves || '').split(' ').filter(m => m.length >= 4);
  return list.slice(Math.max(startPly + 1, 0)).join(' ');
}
