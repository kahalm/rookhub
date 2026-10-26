import { TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { BookPuzzleDto, PuzzleService } from './puzzle.service';
import { FirstMoveHint, buildStagedHints } from './puzzle-hints.util';

/** Was die Tipps vom Buch-Löser brauchen. */
export interface BookHintsHost {
  readonly puzzle: BookPuzzleDto | null;
  readonly isLoggedIn: boolean;
  readonly inWeekly: boolean;
  readonly onSolutionPath: boolean;
  readonly hasHints: boolean;
  readonly hintLevel: number;
  readonly translate: TranslateService;
  readonly puzzleService: PuzzleService;
  readonly snackbar: SnackbarService;
}

/**
 * Tipps des Buch-Lösers (Zerlegung Z18): vorberechnete, sprach-keyed LLM-Tipps für den Schlüsselzug, sonst on-the-fly
 * gestufte Tipps zum erwarteten Zug (wie Standard/Endless), dazu „Tipps melden“. Die Mechanik
 * (hintLevel/shownHints/showNextHint) liegt in BasePuzzleSolver.
 */
export class BookHints {
  flagSaving = false;

  constructor(private readonly h: BookHintsHost) {}

  /** True, wenn dieses Puzzle vorberechnete (sprach-keyed) Tipps mitbringt (echtes Buch-Puzzle). */
  get hasPrecomputed(): boolean { return !!this.h.puzzle?.hints; }

  /** Tipps sind als „dumm/schlecht" flaggbar, sobald mindestens einer aufgedeckt wurde — egal ob
   *  vorberechnet oder on-the-fly (wie im Standard-/Endless-Solver). Voraussetzung: das Puzzle hat eine
   *  echte BookPuzzle-Id (Buch/Kurs/Tagespuzzle), die der Flag-Endpoint adressieren kann. Wochenpost-
   *  Puzzles werden on-the-fly aus dem PGN geparst (puzzle.id = Index, KEINE echte BookPuzzle-Id) →
   *  nicht flaggbar. */
  get canFlag(): boolean {
    const h = this.h;
    // Off-Path zeigt der Tipp-Knopf Navigations-Hinweise, keine (LLM-)Puzzle-Tipps → nicht flaggbar.
    return h.isLoggedIn && h.onSolutionPath && h.hasHints && h.hintLevel > 0 && !h.inWeekly;
  }

  /** Tipps in der aktiven UI-Sprache (Fallback en→de). Vorberechnete Tipps haben Vorrang; fehlen sie
   *  (Wochenpost u. a.), werden on-the-fly gestufte Tipps erzeugt wie im Standard-/Endless-Solver.
   *  Vorberechnete LLM-Tipps gelten NUR für den ersten/Schlüssel-Löserzug (dafür erzeugt) — bei den
   *  Folgezügen würden sie den falschen Zug beschreiben. Für JEDEN Zug greifen sonst die on-the-fly
   *  gestuften Tipps zum AKTUELL erwarteten Zug, sodass Tipps überall funktionieren. */
  available(atFirstSolverMove: boolean, currentMoveHint: FirstMoveHint | null): string[] {
    const hints = this.h.puzzle?.hints;
    const t = this.h.translate;
    if (hints && atFirstSolverMove) {
      const lang = t.currentLang() || t.getFallbackLang() || 'en';
      return hints[lang] ?? hints['en'] ?? hints['de'] ?? [];
    }
    // Die 3 gestuften Tipp-Strings (Typ → Figur → SAN) aus einer Zug-Klassifikation.
    return buildStagedHints(currentMoveHint, (k, p) => t.instant(k, p) as string);
  }

  /** Tipps dieses Puzzles als „dumm/schlecht" markieren bzw. die Markierung aufheben (jeder eingeloggte User). */
  toggleFlag(): void {
    const { h } = this;
    const p = h.puzzle;
    if (!p || this.flagSaving) return;
    const next = !p.hintsFlagged;
    this.flagSaving = true;
    h.puzzleService.flagBookPuzzleHints(p.id, next).subscribe({
      next: () => {
        if (h.puzzle) h.puzzle.hintsFlagged = next;
        this.flagSaving = false;
        h.snackbar.success(h.translate.instant(next ? 'solver.hints.flagSaved' : 'solver.hints.flagCleared'),
          { duration: 2000 });
      },
      error: () => {
        this.flagSaving = false;
        h.snackbar.warn(h.translate.instant('solver.hints.flagError'), { duration: 3000 });
      }
    });
  }
}
