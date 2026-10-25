import { BookPuzzleDto } from './puzzle.service';
import { loadLastSolved, saveLastSolved } from './last-solved-store';
import { taskItemFromPuzzle } from '../worksheets/worksheet-items.util';
import { NewWorksheetItem } from '../worksheets/worksheet.service';

/**
 * Zuletzt gelöstes Puzzle des Buch-Lösers (Zerlegung Z18) — überlebt den Auto-Advance (analog Standard/Endless)
 * und per sessionStorage auch eine Navigation zu /analysis und zurück: „Letztes Puzzle analysieren“, „Letztes
 * teilen“, ♥ und „aufs Aufgabenblatt“. Im Wochenpost bleibt die Id leer (Sequenz-Index, keine BookPuzzle-Id).
 */
export class BookLastSolved {
  id: number | null = null;
  fen: string | null = null;
  moves = '';
  orientation: 'white' | 'black' = 'white';
  /** Vorspiel-Halbzug der letzten Linie — ohne ihn liefe die Aufgaben-Stellung fürs Blatt falsch. */
  startPly = 0;
  /** Themen der letzten Linie — Vorschlagsquelle für die Themen eines Aufgabenblatts. */
  themes = '';

  /** Gibt es ein „letztes Puzzle" (⋮-Menü: ansehen/♥/Blatt)? Hängt an der Stellung, nicht an der Id:
   *  im Wochenpost bleibt `id` leer (Index statt BookPuzzle-Id), ansehen geht trotzdem. */
  get has(): boolean { return !!this.fen; }

  /** Gelöstes Puzzle merken. Wochenpost: keine Id und nichts persistieren — sonst favorisierte, teilte oder legte
   *  ein späterer Kurs das fremde BookPuzzle <index> aufs Blatt. */
  remember(p: BookPuzzleDto, orientation: 'white' | 'black', startPly: number, weekly: boolean): void {
    this.id = weekly ? null : p.id;
    this.fen = p.fen;
    this.moves = p.moves ?? '';
    this.orientation = orientation;
    this.startPly = startPly;
    this.themes = p.tags ?? '';
    if (!weekly) {
      saveLastSolved('book', {
        id: p.id, fen: p.fen, moves: p.moves ?? '', orientation, startPly, themes: p.tags ?? '',
      });
    }
  }

  /** Aus sessionStorage wiederherstellen (nach /analysis und zurück); true, wenn es einen Eintrag gab. */
  restore(): boolean {
    const restored = loadLastSolved('book');
    if (!restored) return false;
    this.id = restored.id;
    this.fen = restored.fen;
    this.moves = restored.moves;
    this.orientation = restored.orientation;
    this.startPly = restored.startPly ?? 0;
    this.themes = restored.themes ?? '';
    return true;
  }

  /** Die AUFGABEN-Stellung fürs Aufgabenblatt: Vorspielzüge bis `startPly` sind eingespielt, sonst stünde auf dem
   *  Blatt die Stellung vor dem Vorspiel und die Frage wäre eine andere. */
  worksheetItem(bookId: number | null): NewWorksheetItem | null {
    if (!this.fen) return null;
    return taskItemFromPuzzle(
      { fen: this.fen, moves: this.moves, orientation: this.orientation, startPly: this.startPly, themes: this.themes },
      'Book', { sourceId: this.id, bookId },
    );
  }

  /** Query für /analysis (Rückweg `from` = aktuelles Buch-/Kurs-/Wochenpost-Puzzle). */
  analysisQuery(from: string): Record<string, string> | null {
    if (!this.fen) return null;
    return { fen: this.fen, moves: this.moves.split(' ').filter(m => m).join(','), orientation: this.orientation, from };
  }
}

/**
 * Daten des Teilen-Dialogs. Wochenpost: `puzzle.id` ist nur der Sequenz-Index, keine BookPuzzle-Id —
 * /puzzles/book/<index> zeigte ein fremdes Puzzle; geteilt wird deshalb der Post selbst, ohne „vorheriges Puzzle“
 * und ohne Challenge. „single=1“ markiert den Link als direkt geteiltes Einzel-Puzzle → der Empfänger bleibt nach
 * dem Lösen stehen (kein Auto-Advance, keine Buch-Navigation).
 */
export function bookShareData(origin: string, puzzleId: number, lastSolvedId: number | null,
  weeklyId: number | null, weekly: boolean, loggedIn: boolean) {
  const url = weekly ? `${origin}/weekly/${weeklyId}` : `${origin}/puzzles/book/${puzzleId}?single=1`;
  // Nach dem Auto-Advance kann zusätzlich das zuletzt gelöste Puzzle geteilt werden.
  const hasPrevious = !weekly && lastSolvedId != null && lastSolvedId !== puzzleId;
  return {
    url,
    previousUrl: hasPrevious ? `${origin}/puzzles/book/${lastSolvedId}?single=1` : undefined,
    puzzleId: weekly ? undefined : puzzleId,
    previousPuzzleId: hasPrevious ? (lastSolvedId ?? undefined) : undefined,
    source: 'book' as const,
    // Wochenpost-Puzzles haben keine dauerhafte ID → dort keine Challenge.
    canChallenge: loggedIn && !weekly,
  };
}
