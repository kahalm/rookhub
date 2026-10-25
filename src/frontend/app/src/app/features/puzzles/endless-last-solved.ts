import { PuzzleDto } from './puzzle.service';
import { loadLastSolved, saveLastSolved } from './last-solved-store';
import { taskItemFromPuzzle } from '../worksheets/worksheet-items.util';
import { NewWorksheetItem } from '../worksheets/worksheet.service';

/**
 * Zuletzt gelöstes Puzzle des Endlos-Lösers (Zerlegung A4, Muster `BookLastSolved`) — für „Letztes Puzzle analysieren",
 * ♥ und „aufs Aufgabenblatt". Bleibt nach dem Auto-Advance erhalten (der SOLVED-Status ist nur kurz sichtbar) und per
 * sessionStorage auch über eine Navigation zu /analysis und zurück.
 */
export class EndlessLastSolved {
  id: number | null = null;
  fen: string | null = null;
  moves = '';
  orientation: 'white' | 'black' = 'white';
  /** Themen des letzten Puzzles — Vorschlagsquelle für die Themen eines Aufgabenblatts. */
  themes = '';

  /** Gelöstes Puzzle merken (auch in sessionStorage). */
  remember(p: PuzzleDto, orientation: 'white' | 'black'): void {
    this.id = p.id;
    this.fen = p.fen;
    this.moves = p.moves;
    this.orientation = orientation;
    this.themes = p.themes ?? '';
    saveLastSolved('endless', { id: p.id, fen: p.fen, moves: p.moves, orientation, themes: p.themes ?? '' });
  }

  /** Aus sessionStorage wiederherstellen; true, wenn es einen Eintrag gab. Ohne das würde `id` beim Verlassen der
   *  Seite (Analyse) null und die Knöpfe verschwänden. */
  restore(): boolean {
    const restored = loadLastSolved('endless');
    if (!restored) return false;
    this.id = restored.id;
    this.fen = restored.fen;
    this.moves = restored.moves;
    this.orientation = restored.orientation;
    this.themes = restored.themes ?? '';
    return true;
  }

  /** Die AUFGABEN-Stellung fürs Aufgabenblatt (der Gegnerzug ist eingespielt), nicht die rohe FEN. */
  worksheetItem(): NewWorksheetItem | null {
    if (!this.fen) return null;
    return taskItemFromPuzzle(
      { fen: this.fen, moves: this.moves, orientation: this.orientation, themes: this.themes },
      'Standard', { sourceId: this.id },
    );
  }
}

/** Query für /analysis aus Stellung + Lösungszügen (Rückweg `from`). */
export function endlessAnalysisQuery(fen: string, moves: string, orientation: 'white' | 'black', from: string): Record<string, string> {
  return { fen, moves: moves.split(' ').filter(m => m).join(','), orientation, from };
}
