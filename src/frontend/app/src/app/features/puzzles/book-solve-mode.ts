import { TranslateService } from '@ngx-translate/core';
import { SolveMode, SolveModeService } from '../../core/solve-mode.service';
import { SnackbarService } from '../../core/snackbar.service';
import { BookPuzzleDto } from './puzzle.service';

/** Was die Spielweisen-Steuerung vom Buch-Löser braucht. */
export interface BookSolveModeHost {
  readonly inCourse: boolean;
  readonly inWeekly: boolean;
  readonly isDaily: boolean;
  readonly course: { readonly bookId: number | null };
  readonly displayBookName: string;
  readonly translate: TranslateService;
  readonly solveMode: SolveModeService;
  readonly snackbar: SnackbarService;
  visualizationMode: number;
  awaitingSolveMode: boolean;
  /** Vor der Frage: Durchsicht/INFO verlassen, Uhr anhalten und die Aufgabe schon als Vorschau zeigen. */
  holdForSolveMode(puzzle: BookPuzzleDto): void;
  setupPuzzle(puzzle: BookPuzzleDto): void;
}

/**
 * Spielweise (Training/Einfach) des Buch-Lösers für Kurs, Tagespuzzle und einzelnes Buch-Puzzle (Zerlegung Z18).
 * Der Wochenpost-Modus hat seine EIGENE Abfrage (`BookWeeklyMode.mode`) und bleibt davon unberührt: dort wird
 * gewertet, es gilt zwingend Stufe 1 und die „Auge nach dem ersten Zug"-Regel.
 */
export class BookSolveModeController {
  /** Gewählte Spielweise des aktuellen Bereichs. Wirkt über {@link apply} beim Aufsetzen
   *  eines Puzzles — ein Umschalten mitten im Versuch ändert das laufende Puzzle NICHT. */
  choice: SolveMode = 'training';
  /** Bereichs-Schlüssel, für den die Spielweise schon geholt wurde (verhindert erneutes Fragen
   *  bei jedem Puzzle; beim Wechsel des Bereichs/Kurses wird wieder geholt). */
  private scopeAsked: string | null = null;
  /** Kein Dialog: die Ansicht ist per Link festgelegt (`?visualmode=`) bzw. es ist ein direkt
   *  geteiltes Einzel-Puzzle / eine Challenge / eine Revanche — dort hat der Öffnende die Wahl
   *  schon getroffen, und es ist kein Trainingsbereich. Dann bleibt es beim bisherigen Verhalten. */
  suppressed = false;

  constructor(private readonly h: BookSolveModeHost) {}

  /**
   * Bereichs-Schlüssel der Spielweise. Je Kurs ein eigener (ein Taktikbuch trainiert man anders
   * als einen Eröffnungskurs), sonst „daily" bzw. „book". `null` = kein eigener Bereich:
   * Wochenpost (eigene Abfrage) bzw. Kurs ohne bekannte Buch-Id.
   */
  get scope(): string | null {
    const h = this.h;
    if (h.inWeekly) return null;
    if (h.inCourse) return h.course.bookId != null ? SolveModeService.scopeCourse(h.course.bookId) : null;
    return h.isDaily ? 'daily' : 'book';
  }

  /** Wird der Umschalt-Knopf angeboten? (Nicht im Wochenpost und nicht in per Link festgelegten Ansichten.) */
  get showSwitch(): boolean {
    return !!this.scope && !this.suppressed;
  }

  /** Was im Dialog benannt wird (außerhalb von Kursen): der Bereichsname. */
  private get label(): string {
    return this.h.translate.instant(this.h.isDaily ? 'solveMode.scope.daily' : 'solveMode.scope.book');
  }

  /**
   * Klärt die Spielweise des Bereichs, BEVOR das erste Puzzle aufgesetzt wird — sonst liefe die
   * Stoppuhr hinter dem (blockierenden) Dialog mit. Gefragt wird nur beim ersten Mal je Bereich;
   * danach liefert der Service die gemerkte Wahl ohne Dialog.
   * Liefert true, wenn das Aufsetzen zurückgestellt wurde (läuft im Callback weiter).
   */
  askIfNeeded(puzzle: BookPuzzleDto): boolean {
    const h = this.h;
    const scope = this.scope;
    if (!scope || this.suppressed || this.scopeAsked === scope) return false;
    this.scopeAsked = scope;
    h.holdForSolveMode(puzzle);
    // UI-Sweep 2026-10-11 (r2-rp-mode-title): im Kurs „Wie möchtest du Kurse spielen?", der Buchtitel nur als Unterzeile.
    const courseDialog = h.inCourse
      ? { title: h.translate.instant('solveMode.titleCourses'), subtitle: h.displayBookName || undefined }
      : { scopeLabel: this.label };
    h.solveMode.ensure(scope, courseDialog).subscribe(mode => {
      h.awaitingSolveMode = false;
      this.choice = mode;
      // Jetzt mit der gewählten Spielweise aufsetzen (der Guard oben greift nicht mehr).
      h.setupPuzzle(puzzle);
    });
    return true;
  }

  /** Visualisierungsstufe aus der gewählten Spielweise setzen — je Puzzle neu, damit ein
   *  Umschalten ab dem nächsten Puzzle greift. Per Link festgelegte Ansichten bleiben unberührt. */
  apply(): void {
    if (this.suppressed || !this.scope) return;
    this.h.visualizationMode = this.h.solveMode.levelFor(this.choice);
  }

  /**
   * Spielweise umschalten — wirkt ab dem NÄCHSTEN Puzzle, damit ein laufender Versuch nicht
   * mitten im Rechnen die Regeln wechselt (die Stufe kommt beim nächsten Aufsetzen über
   * {@link apply}). Bereits gemeldete Versuche bleiben, wie sie gewertet wurden.
   */
  toggle(): void {
    const scope = this.scope;
    if (!scope || this.suppressed) return;
    this.choice = this.choice === 'training' ? 'easy' : 'training';
    this.h.solveMode.set(scope, this.choice);
    this.h.snackbar.info(this.h.translate.instant(
      this.choice === 'easy' ? 'solveMode.switchedEasy' : 'solveMode.switchedTraining'),
      { duration: 3000 });
  }

  /** Visualisierungsstufe von Hand gesetzt: gemerkte Spielweise mitziehen, sonst widersprechen sich Wahl und
   *  tatsächliche Stufe (Stufe 0 = Einfachmodus, alles darüber = Trainingsmodus). */
  followLevel(level: number): void {
    const scope = this.scope;
    if (scope && !this.suppressed) {
      this.choice = this.h.solveMode.modeForLevel(level);
      this.h.solveMode.set(scope, this.choice);
    }
  }

  /** Spielweise, unter der das AKTUELLE Puzzle tatsächlich läuft (aus der wirksamen
   *  Visualisierungsstufe abgeleitet) — das ist der Wert, der gemeldet wird. Bewusst nicht
   *  `choice`: ein Umschalten mitten im Versuch gilt erst fürs nächste Puzzle, und
   *  per Link festgelegte Ansichten sollen ehrlich gewertet werden. */
  get attemptMode(): SolveMode {
    return this.h.solveMode.modeForLevel(this.h.visualizationMode);
  }
}
