import { Component, OnInit, OnDestroy, HostListener, ChangeDetectionStrategy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { BoardFsActionsComponent } from './board-fs-actions.component';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { PuzzleTagsComponent } from './puzzle-tags.component';
import { CommentSegment } from './comment-variation.util';
import { CourseProgressSplitComponent } from '../courses/course-progress-split.component';
import { CommentDialogComponent, commentLength, LONG_COMMENT_CHARS } from './comment-dialog.component';
import { CommentBlockCache } from './line-step.util';
import {
  displayComment as computeDisplayComment, buildCommentLines, hasTrailingSolutionComment as computeHasTrailingComment,
} from './book-comment.util';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleSettingsDialogComponent, PuzzleSettingsDialogData, PuzzleSettingsDialogResult } from './puzzle-settings-dialog.component';
import { PuzzleStatusCardComponent } from './puzzle-status-card.component';
import { ChallengeFriendsComponent } from './challenge-friends.component';
import { PuzzleActionBarComponent } from './puzzle-action-bar.component';
import { ChallengeService } from '../../core/challenge.service';
import { PuzzleService, BookPuzzleDto } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { PreferencesService } from '../../core/preferences.service';
import { BOARD_THEMES, PIECE_SETS, ThemeMode, applyThemeMode, clearCrazyStyles, clearVisualizationHide, parseShareViewParams } from './board-theme.util';
import { Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { parseAltMoves } from './alt-moves.util';
import { tryLoadFen, fenSideToMove } from './puzzle-move.util';
import { BookReviewController, ReviewFrame } from './book-review';
import { FirstMoveHint, buildStagedHints } from './puzzle-hints.util';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { CourseService } from '../courses/course.service';
import { LongSolveService } from './long-solve.service';
import { isElementFullscreen } from '../../shared/fullscreen/fullscreen.util';
import { AuthService } from '../../core/auth.service';
import { CourseLanguageService, CourseRef } from '../courses/course-language.service';
import { labelOr } from '../courses/course-language.util';
import { CourseLangPickerComponent, MachineNoteComponent } from '../courses/course-lang-picker.component';
import { loadDailyElapsed, saveDailyElapsed } from './daily-elapsed.util';
import { loadSolveElapsed, saveSolveElapsed } from './solve-elapsed.util';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { FavoritesService } from '../../core/favorites.service';
import { loadLastSolved, saveLastSolved } from './last-solved-store';
import { WorksheetService } from '../worksheets/worksheet.service';
import { taskItemFromPuzzle } from '../worksheets/worksheet-items.util';
import { FavoriteTracker } from './favorite-tracker';
import { WeeklyService } from '../weekly/weekly.service';
import { SolveMode, SolveModeService } from '../../core/solve-mode.service';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { BookModeHost, BookModeStrategy } from './book-mode-host';
import { BookCourseMode, courseTime, courseEta } from './book-course-mode';
import { BookWeeklyMode } from './book-weekly-mode';
import { BookDailyMode } from './book-daily-mode';
import { BookStandaloneMode } from './book-standalone-mode';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-book-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, MatProgressBarModule, MatTooltipModule, IconLabelDirective, MatDialogModule,
    PuzzleBoardComponent, BoardFsActionsComponent, PuzzleTagsComponent,
    TranslatePipe, PuzzleStatusCardComponent, ChallengeFriendsComponent, PuzzleActionBarComponent,
    RouterLink, CourseLangPickerComponent, MachineNoteComponent, CourseProgressSplitComponent,
  ],
  templateUrl: './book-puzzle.component.html',
  styleUrls: ['./book-puzzle.component.scss'],
})
export class BookPuzzleComponent extends BasePuzzleSolver implements OnInit, OnDestroy, BookModeHost {
  // state, boardFen, orientation, turnColor, dests, lastMove, isCheck, chess, solutionMoves,
  // moveIndex, startPly, autoAdvanceTimer, aborted, onSolutionPath, alternativeSolve,
  // mouseslipUsed, visualizationMode, vizMoves → BasePuzzleSolver
  puzzle: BookPuzzleDto | null = null;

  // Kursmodus: Komponente wird über /courses/:bookId/:mode aufgerufen und arbeitet ein
  // Buch puzzleweise durch; Fortschritt liegt user-bezogen in der DB (Zustand + Logik: BookCourseMode).
  inCourse = false;
  // Wochenpost-Modus: /weekly/:weeklyId — Puzzles eines Posts sequenziell durchspielen (BookWeeklyMode).
  inWeekly = false;
  /** Modus-Strategien (Zerlegung Z18): Laden der nächsten Aufgabe, Ergebnis melden, Navigation je Modus. */
  readonly course: BookCourseMode;
  readonly weekly: BookWeeklyMode;
  readonly daily: BookDailyMode;
  readonly book: BookStandaloneMode;
  /** Zeit-/Restzeit-Anzeige der Kurs-Statistik. */
  readonly courseTime = courseTime;
  readonly courseEta = courseEta;

  // ===== Spielweise (Kurs / Tagespuzzle / einzelnes Buch-Puzzle) =====
  // Der Wochenpost-Modus hat seine EIGENE Abfrage (weeklyMode, s. o.) und bleibt davon unberührt:
  // dort wird gewertet, es gilt zwingend Stufe 1 und die „Auge nach dem ersten Zug"-Regel.
  /** Gewählte Spielweise des aktuellen Bereichs. Wirkt über {@link applySolveMode} beim Aufsetzen
   *  eines Puzzles — ein Umschalten mitten im Versuch ändert das laufende Puzzle NICHT. */
  solveModeChoice: SolveMode = 'training';
  /** Bereichs-Schlüssel, für den die Spielweise schon geholt wurde (verhindert erneutes Fragen
   *  bei jedem Puzzle; beim Wechsel des Bereichs/Kurses wird wieder geholt). */
  private solveModeScopeAsked: string | null = null;
  /** Kein Dialog: die Ansicht ist per Link festgelegt (`?visualmode=`) bzw. es ist ein direkt
   *  geteiltes Einzel-Puzzle / eine Challenge / eine Revanche — dort hat der Öffnende die Wahl
   *  schon getroffen, und es ist kein Trainingsbereich. Dann bleibt es beim bisherigen Verhalten. */
  private solveModeSuppressed = false;

  private dailySub?: Subscription;
  private courseSub?: Subscription;

  stockfishDepth = 16;
  boardTheme = 'brown';
  readonly boardThemes = BOARD_THEMES;

  pieceSet = 'cburnett';
  readonly pieceSets = PIECE_SETS;

  // elapsedSeconds / Stoppuhr / start-/stopTimer / formatTime: jetzt in BasePuzzleSolver (geteilt).

  /** Die zu wertende Lösezeit — i.d.R. = elapsedSeconds, bei „war weg" auf den Schwellwert gekappt. */
  solveSeconds = 0;
  /** alternative-Flag des aktuellen Solves (durchgereicht an finalizeSolve nach der Nachfrage). */
  private solveAlternative = false;

  // Eval (Stockfish-Bewertung) — wie Standard/Endless; currentEval kommt aus BasePuzzleSolver.
  showEval = false;
  evalLoading = false;
  initialEval = '';
  private initialFen = '';


  /** True, wenn dieses Puzzle vorberechnete (sprach-keyed) Tipps mitbringt (echtes Buch-Puzzle). */
  get hasPrecomputedHints(): boolean { return !!this.puzzle?.hints; }

  /** Tipps sind als „dumm/schlecht" flaggbar, sobald mindestens einer aufgedeckt wurde — egal ob
   *  vorberechnet oder on-the-fly (wie im Standard-/Endless-Solver). Voraussetzung: das Puzzle hat eine
   *  echte BookPuzzle-Id (Buch/Kurs/Tagespuzzle), die der Flag-Endpoint adressieren kann. Wochenpost-
   *  Puzzles werden on-the-fly aus dem PGN geparst (puzzle.id = Index, KEINE echte BookPuzzle-Id) →
   *  nicht flaggbar. */
  get canFlagHints(): boolean {
    // Off-Path zeigt der Tipp-Knopf Navigations-Hinweise, keine (LLM-)Puzzle-Tipps → nicht flaggbar.
    return this.isLoggedIn && this.onSolutionPath && this.hasHints && this.hintLevel > 0 && !this.inWeekly;
  }

  /** Tipps in der aktiven UI-Sprache (Fallback en→de). Vorberechnete Tipps haben Vorrang; fehlen sie
   *  (Wochenpost u. a.), werden on-the-fly gestufte Tipps erzeugt wie im Standard-/Endless-Solver.
   *  Mechanik (hintLevel/shownHints/showNextHint) in BasePuzzleSolver. */
  override get availableHints(): string[] {
    // Vorberechnete LLM-Tipps gelten NUR für den ersten/Schlüssel-Löserzug (dafür erzeugt) — bei den
    // Folgezügen würden sie den falschen Zug beschreiben. Für JEDEN Zug greifen sonst die on-the-fly
    // gestuften Tipps zum AKTUELL erwarteten Zug (currentMoveHint), sodass Tipps überall funktionieren.
    const h = this.puzzle?.hints;
    if (h && this.atFirstSolverMove) {
      const lang = this.translate.currentLang() || this.translate.getFallbackLang() || 'en';
      return h[lang] ?? h['en'] ?? h['de'] ?? [];
    }
    return this.hintsForMove(this.currentMoveHint);
  }

  /** Baut die 3 gestuften Tipp-Strings (Typ → Figur → SAN) aus einer Zug-Klassifikation. */
  private hintsForMove(f: FirstMoveHint | null): string[] {
    return buildStagedHints(f, (k, p) => this.translate.instant(k, p) as string);
  }

  flagSaving = false;

  /** Tipps dieses Puzzles als „dumm/schlecht" markieren bzw. die Markierung aufheben (jeder eingeloggte User). */
  toggleHintsFlag(): void {
    if (!this.puzzle || this.flagSaving) return;
    const next = !this.puzzle.hintsFlagged;
    this.flagSaving = true;
    this.puzzleService.flagBookPuzzleHints(this.puzzle.id, next).subscribe({
      next: () => {
        if (this.puzzle) this.puzzle.hintsFlagged = next;
        this.flagSaving = false;
        this.snackbar.success(this.translate.instant(next ? 'book.hints.flagSaved' : 'book.hints.flagCleared'),
          { duration: 2000 });
      },
      error: () => {
        this.flagSaving = false;
        this.snackbar.warn(this.translate.instant('book.hints.flagError'), { duration: 3000 });
      }
    });
  }

  /** True nach Give Up. Status-Panel zeigt einen Hinweis statt "Your turn!". */
  gaveUp = false;

  // Review-Modus „Ganze Partie" vs. Lösungs-Step-Through (komponentenspezifisch;
  // reviewMode/reviewIndex in BasePuzzleSolver).
  solutionReview = false;

  /** Kommentar des zuletzt durchgespielten Zugs (Bücher kommentieren oft jeden Zug). Wird im
   *  Review/Durchspielen passend zur aktuellen Stellung gesetzt; null = kein Kommentar hier. */
  moveComment: string | null = null;
  /** Board-Annotationen (Chessable-Pfeile/Feld-Markierungen) zum aktuellen Review-Zug — ans Brett gebunden. */
  reviewShapes: DrawShape[] = [];
  /** Durchsicht (Ganze Partie / Lösung) und Brett-Annotationen je Halbzug — reine Logik in `book-review.ts`. */
  private readonly review = new BookReviewController();

  /** Standalone-Buch-Puzzle (/puzzles/book/:id) — nicht Kurs-/Wochenpost-Kontext. */
  get standalone(): boolean { return !this.inCourse && !this.inWeekly; }
  /** Buch-Navigation (nächstes/zufälliges im selben Buch) anbieten — entfällt für ein direkt
   *  geteiltes Einzel-Puzzle (`?single=1`) und im Tagespuzzle (Datums-Navigation). */
  get browseInBook(): boolean { return this.standalone && !this.isDaily && !this.singlePuzzle; }
  loadError = false;
  /** Monotone Epoche je Ladevorgang (Buch/Kurs/Daily/Wochenpost). Schnelle Navigation kann
   *  mehrere Lade-Requests gleichzeitig in der Luft haben; eine ältere, langsamer auflösende
   *  Antwort darf das inzwischen geladene Puzzle nicht überschreiben → wird per Epoche verworfen. */
  loadEpoch = 0;
  retryFn: (() => void) | null = null;
  /** Direkt geteiltes Einzel-Puzzle (Teilen-Link `?single=1`): nach dem Lösen am Puzzle stehen
   *  bleiben statt automatisch weiterzuspringen, und keine Buch-Navigation anbieten. */
  singlePuzzle = false;
  // Zuletzt gelöstes Puzzle merken (überlebt den Auto-Advance) — analog Standard/Endless:
  // ermöglicht „Letztes Puzzle analysieren" + „Letztes teilen" im Share-Dialog.
  lastSolvedPuzzleId: number | null = null;
  private lastSolvedFen: string | null = null;
  private lastSolvedMoves = '';
  private lastSolvedOrientation: 'white' | 'black' = 'white';
  /** Vorspiel-Halbzug der letzten Linie — ohne ihn liefe die Aufgaben-Stellung fürs Blatt falsch. */
  private lastSolvedStartPly = 0;
  /** Themen der letzten Linie — Vorschlagsquelle für die Themen eines Aufgabenblatts. */
  private lastSolvedThemes = '';
  /** Gibt es ein „letztes Puzzle" (⋮-Menü: ansehen/♥/Blatt)? Hängt an der Stellung, nicht an der Id:
   *  im Wochenpost bleibt `lastSolvedPuzzleId` leer (Index statt BookPuzzle-Id), ansehen geht trotzdem. */
  get hasLastSolved(): boolean { return !!this.lastSolvedFen; }
  /** „Geliebtes Puzzle"-Zustand (Herz). In Wochenpost-Modus deaktiviert (keine echte Id). */
  readonly favoriteTracker: FavoriteTracker;

  get isLoggedIn(): boolean { return this.auth.isLoggedIn; }

  // ===== Wirt der Modus-Strategien (BookModeHost) =====
  /** Höchste aufgedeckte Tipp-Stufe des aktuellen Versuchs (gemeldet als hintsUsed). */
  get hintsUsed(): number { return this.maxHintLevel; }
  /** Frischer Ladevorgang: Uhr anhalten und auf 0, Alternativ-/Aufgeben-Marker zurück. */
  resetForLoad(): void {
    this.stopTimer();
    this.elapsedSeconds = 0;
    this.alternativeSolve = false;
    this.gaveUp = false;
  }
  override clearSolutionPlay(): void { super.clearSolutionPlay(); }

  get displayBookName(): string {
    if (!this.puzzle) return '';
    if (this.puzzle.bookTitle) return this.puzzle.bookTitle;
    return this.puzzle.bookFileName
      .replace(/_firstkey\.pgn$/i, '')
      .replace(/\.pgn$/i, '')
      .replace(/[_-]+/g, ' ')
      .trim();
  }

  constructor(
    readonly puzzleService: PuzzleService,
    stockfish: StockfishService,
    private prefs: PreferencesService,
    private route: ActivatedRoute,
    private dialog: MatDialog,
    readonly courseService: CourseService,
    readonly weeklyService: WeeklyService,
    readonly router: Router,
    readonly translate: TranslateService,
    readonly auth: AuthService,
    readonly snackbar: SnackbarService,
    readonly offlineQueue: OfflineQueueService,
    readonly challengeService: ChallengeService,
    private longSolve: LongSolveService,
    private favorites: FavoritesService,
    readonly solveMode: SolveModeService,
    private worksheets: WorksheetService,
    readonly courseLang: CourseLanguageService,
  ) {
    super(stockfish);
    this.course = new BookCourseMode(this);
    this.weekly = new BookWeeklyMode(this);
    this.daily = new BookDailyMode(this);
    this.book = new BookStandaloneMode(this);
    // Wochenpost-Puzzles haben keine echte BookPuzzle-Id (Index) → nie favorisierbar.
    this.favoriteTracker = new FavoriteTracker(
      this.favorites, 'book',
      () => this.puzzle?.id, () => this.lastSolvedPuzzleId,
      () => this.isLoggedIn && !this.inWeekly,
    );
    this.loadConfig();
    this.loadSettingsOpen();
    this.stockfish.init().catch(() => {});
  }

  // ===== Sprache der Kommentare (Kurs-Übersetzung, Stufe C) =====

  /** Gibt es hier eine Sprachwahl? Kurs und Einzel-Linie im Buch — nicht Tagespuzzle/Wochenpost
   *  (die bleiben bewusst im Original). */
  get langEnabled(): boolean {
    return (this.inCourse || this.standalone) && !this.isDaily && !this.inWeekly;
  }

  /** Der Kurs dieser Ansicht für die Sprachwahl: im Kurs über die bookId, beim Einzel-Puzzle über den
   *  Dateinamen der Linie (`BookPuzzleDto` trägt keine Kurs-Id). */
  get langRef(): CourseRef | null {
    if (!this.langEnabled) return null;
    return { bookId: this.inCourse ? this.course.bookId : null, fileName: this.puzzle?.bookFileName ?? null };
  }

  /** `?lang=` für die nächste Anfrage — `null` außerhalb von Kurs/Buch (Tagespuzzle, Wochenpost). */
  get requestLang(): string | null {
    return this.langEnabled ? this.courseLang.requestLang(this.langRef) : null;
  }

  /** Angezeigter Titel/Kapitel der Linie: die Übersetzung, sonst das Original (Schlüssel bleiben). */
  /** Router-Ziel der Partie, aus der die Aufgabe geerntet wurde (Taktik-Ernte); `null` = keine. */
  get sourceGameLink(): unknown[] | null {
    const g = this.puzzle?.sourceGame;
    return g ? [g.kind === 'club' ? '/club-games' : '/games', g.id] : null;
  }
  get titleDisplay(): string | null { return labelOr(this.puzzle?.titleLabel, this.puzzle?.title); }
  get chapterDisplay(): string | null { return labelOr(this.puzzle?.chapterLabel, this.puzzle?.chapter); }

  /** Sprache gewählt: merken, die Texte der aktuellen Linie tauschen (ohne den Versuch neu zu
   *  starten) und die Offline-Kopie in der neuen Sprache nachziehen. */
  pickLanguage(lang: string): void {
    const ref = this.langRef;
    if (!ref) return;
    this.courseLang.setChoice(ref, lang);
    this.refreshTexts();
    if (this.inCourse && this.course.bookId != null) {
      if (this.course.isAnon) this.course.refreshAnonCache(this.course.bookId);
      else this.course.autoCache();
    }
  }

  /** „maschinell übersetzt" angeklickt → aufs Original (die Quelle ist die erste Sprache der Linie). */
  showOriginal(): void {
    const source = this.courseLang.source(this.langRef) ?? this.puzzle?.commentLanguages?.[0] ?? null;
    if (source) this.pickLanguage(source);
  }

  /** Nur die TEXTE der aktuellen Linie in der gewählten Sprache holen — der laufende Versuch bleibt. */
  private refreshTexts(): void {
    const p = this.puzzle;
    const lang = this.requestLang;
    if (!p || !lang || (typeof navigator !== 'undefined' && !navigator.onLine)) return;
    this.puzzleService.getBookPuzzleById(p.id, lang).subscribe({
      next: fresh => { if (this.puzzle?.id === fresh?.id) this.applyTexts(this.puzzle, fresh); },
      error: () => { /* Texte bleiben, wie sie sind — die nächste Linie kommt in der neuen Sprache */ },
    });
  }

  /** Texte einer frisch geholten Fassung übernehmen und die gerade angezeigte Stelle neu ziehen. */
  private applyTexts(target: BookPuzzleDto, fresh: BookPuzzleDto): void {
    target.comment = fresh.comment;
    target.moveComments = fresh.moveComments;
    target.titleLabel = fresh.titleLabel ?? null;
    target.chapterLabel = fresh.chapterLabel ?? null;
    target.commentLanguage = fresh.commentLanguage ?? null;
    target.commentLanguages = fresh.commentLanguages ?? null;
    target.commentMachine = !!fresh.commentMachine;
    this.courseLang.noteLines(this.langRef, [fresh]);
    // Im Durchklicken steht der Kommentar schon als `moveComment` fest — an derselben Stelle neu holen.
    if (this.reviewMode) {
      if (this.solutionReview) this.solutionReviewGoTo(this.reviewIndex);
      else this.reviewGoTo(this.reviewIndex);
    }
  }

  /** Nach dem Laden einer Linie: Sprachen merken; kam sie in einer anderen als der gewählten
   *  (Einzel-Puzzle: Wahl erst mit dem Dateinamen bekannt), die Texte nachholen. */
  afterLineLoaded(p: BookPuzzleDto, sentLang: string | null): void {
    if (!this.langEnabled) return;
    if (this.inCourse) this.courseLang.rememberFile(this.course.bookId, p.bookFileName);
    this.courseLang.noteLines(this.langRef, [p]);
    const want = this.requestLang;
    if (want && sentLang && want !== sentLang && (p.commentLanguages?.length ?? 0) > 1) this.refreshTexts();
  }

  sharePuzzle(): void {
    if (!this.puzzle) return;
    // Wochenpost: puzzle.id ist nur der Sequenz-Index, keine BookPuzzle-Id — /puzzles/book/<index>
    // zeigte ein fremdes Puzzle. Geteilt wird deshalb der Post selbst, ohne „vorheriges Puzzle".
    const weekly = this.inWeekly;
    // „single=1" markiert den Link als direkt geteiltes Einzel-Puzzle → Empfänger bleibt nach
    // dem Lösen auf dem Puzzle stehen (kein Auto-Advance, keine Buch-Navigation).
    const url = weekly
      ? `${window.location.origin}/weekly/${this.weekly.id}`
      : `${window.location.origin}/puzzles/book/${this.puzzle.id}?single=1`;
    // Nach dem Auto-Advance kann zusätzlich das zuletzt gelöste Puzzle geteilt werden.
    const hasPrevious = !weekly && this.lastSolvedPuzzleId != null && this.lastSolvedPuzzleId !== this.puzzle.id;
    const previousUrl = hasPrevious
      ? `${window.location.origin}/puzzles/book/${this.lastSolvedPuzzleId}?single=1`
      : undefined;
    this.dialog.open(SharePuzzleDialogComponent, {
      data: {
        url, previousUrl,
        puzzleId: weekly ? undefined : this.puzzle.id,
        previousPuzzleId: hasPrevious ? (this.lastSolvedPuzzleId ?? undefined) : undefined,
        source: 'book',
        // Wochenpost-Puzzles haben keine dauerhafte ID → dort keine Challenge.
        canChallenge: this.isLoggedIn && !this.inWeekly,
      },
      width: '400px',
      maxWidth: '95vw',
    });
  }

  /**
   * Die zuletzt gelöste Linie aufs AUFGABENBLATT — dieselbe Auswahl wie „♥ letztes Puzzle".
   * Geschickt wird die AUFGABEN-Stellung: Vorspielzüge bis `startPly` sind eingespielt, sonst
   * stünde auf dem Blatt die Stellung vor dem Vorspiel und die Frage wäre eine andere.
   */
  sendLastToWorksheet(target: number | null): void {
    this.stopCountdown();
    if (!this.lastSolvedFen) return;
    const item = taskItemFromPuzzle(
      {
        fen: this.lastSolvedFen, moves: this.lastSolvedMoves,
        orientation: this.lastSolvedOrientation, startPly: this.lastSolvedStartPly,
        themes: this.lastSolvedThemes,
      },
      'Book', { sourceId: this.lastSolvedPuzzleId, bookId: this.course.bookId },
    );
    this.worksheets.sendAndNotify(target, item ? [item] : []);
  }

  /** Zuletzt gelöstes Puzzle im Analysemodus öffnen (auch nach dem Auto-Advance verfügbar). */
  reviewLastPuzzle(): void {
    this.stopCountdown();
    if (!this.lastSolvedFen) return;
    this.router.navigate(['/analysis'], {
      queryParams: {
        fen: this.lastSolvedFen,
        moves: this.lastSolvedMoves.split(' ').filter(m => m).join(','),
        orientation: this.lastSolvedOrientation,
        from: this.router.url.split('?')[0],   // zurück zum aktuellen Buch-/Kurs-/Wochenpost-Puzzle
      },
    });
  }

  /** Aktuelle Stellung + komplette Zugfolge des Puzzles im Analysemodus öffnen. */
  analyze(): void {
    if (!this.puzzle) return;
    const moves = (this.puzzle.moves || '').split(' ').filter(m => m);
    this.router.navigate(['/analysis'], {
      queryParams: {
        fen: this.puzzle.fen, moves: moves.join(','), orientation: this.orientation,
        from: this.router.url.split('?')[0],   // zurück zum aktuellen Buch-/Kurs-/Wochenpost-Puzzle
      },
    });
  }

  // ===== Hooks für BasePuzzleSolver =====
  protected override get depth(): number { return this.stockfishDepth; }
  protected override get stockfishErrorContinues(): boolean { return false; }  // Buch: Fehlzug → verloren

  protected override onSetupStart(): void {
    const applied = applyThemeMode(this.themeMode, this.prefs.boardTheme, this.prefs.pieceSet);
    this.boardTheme = applied.boardTheme;
    this.pieceSet = applied.pieceSet;
    this.enPassantForced = this.anarchyForcedByUrl || (this.themeMode === 'crazy' && this.prefs.enPassantForced);
  }

  protected override onSolvingBegins(): void {
    this.initialFen = this.chess.fen();
    // Tagespuzzle/Kurs: früher verbrachte (aktive) Zeit fortführen — wer die Seite mitten im Lösen
    // verlässt oder neu lädt, zählt kumuliert weiter statt bei 0 (Daily: fair für die „schnellster
    // Löser"-Wertung; Kurs: ehrliche Kurs-/Trainingszeit-Statistik).
    const courseKey = this.course.elapsedKey;
    this.startTimer(this.isDaily && this.daily.date ? loadDailyElapsed(this.daily.date)
      : courseKey ? loadSolveElapsed(courseKey) : 0);
  }

  /** Zwischenstand der Lösezeit im Sekunden-Tick persistieren (für den Wiederbesuch/Refresh). */
  protected override onTimerTick(): void {
    if (this.isDaily && this.daily.date) { saveDailyElapsed(this.daily.date, this.elapsedSeconds); return; }
    const courseKey = this.course.elapsedKey;
    if (courseKey) saveSolveElapsed(courseKey, this.elapsedSeconds);
  }

  protected override get offPathWarnThreshold(): number { return this.prefs.offPathWarnMoves; }
  protected override onOffPathWarning(): void {
    const evalStr = this.playerEvalDisplay();
    const base = this.translate.instant(
      this.startWasBalanced() ? 'puzzles.offPathWarningEqual' : 'puzzles.offPathWarning');
    this.snackbar.info(evalStr ? `${base} (${evalStr})` : base, { action: 'common.ok', duration: 7000 });
  }
  protected override onAlternativeMove(_userUci: string): void {
    this.snackbar.info(this.translate.instant('book.alternativeMove'), { duration: 3000 });
  }
  protected override get epForcedHints(): string[] {
    return [1, 2, 3].map(i => this.translate.instant('puzzles.anarchyHint' + i));
  }
  protected override get offPathHints(): string[] {
    const t = (k: string, p?: object) => this.translate.instant(k, p) as string;
    const n = this.offPathUserMoveNumber;
    return [t('puzzles.offPath.h1'), t('puzzles.offPath.h2'),
      n != null ? t('puzzles.offPath.h3', { n }) : t('puzzles.offPath.h3any')];
  }

  toggleEval(): void {
    this.showEval = !this.showEval;
    if (this.showEval) {
      this.markEvalShown();
      if (this.state === 'PLAYING' || this.state === 'AWAITING_USER_MOVE') this.refreshEval();
    }
  }

  private async refreshEval(): Promise<void> {
    this.evalLoading = true;
    try {
      if (!this.initialEval && this.initialFen) {
        this.initialEval = await this.stockfish.getEval(this.initialFen, this.stockfishDepth);
      }
      this.currentEval = await this.stockfish.getEval(this.chess.fen(), this.stockfishDepth);
    } catch { /* ignore */ }
    this.evalLoading = false;
  }

  protected override refreshEvalIfShown(): void {
    if (this.showEval) this.refreshEval();
  }

  protected override handleSolved(alternative: boolean): void {
    this.state = 'SOLVED';
    this.stopTimer();
    this.updateBoard();
    // Für „Letztes Puzzle analysieren"/„Letztes teilen" merken (überlebt den Auto-Advance).
    // Zusätzlich in sessionStorage persistieren, damit die Info eine Navigation zu /analysis
    // und zurück übersteht (sonst wird der Component-Destroy den Zustand wegwerfen).
    // Wochenpost: puzzle.id ist der Sequenz-Index, keine BookPuzzle-Id → keine Id merken und nichts
    // persistieren; sonst favorisierte, teilte oder legte ein späterer Kurs das fremde BookPuzzle <index> aufs Blatt.
    if (this.puzzle) {
      this.lastSolvedPuzzleId = this.inWeekly ? null : this.puzzle.id;
      this.lastSolvedFen = this.puzzle.fen;
      this.lastSolvedMoves = this.puzzle.moves ?? '';
      this.lastSolvedOrientation = this.orientation;
      this.lastSolvedStartPly = this.startPly;
      this.lastSolvedThemes = this.puzzle.tags ?? '';
      if (!this.inWeekly) {
        saveLastSolved('book', {
          id: this.puzzle.id, fen: this.puzzle.fen,
          moves: this.puzzle.moves ?? '', orientation: this.orientation, startPly: this.startPly,
          themes: this.puzzle.tags ?? '',
        });
      }
    }
    this.favoriteTracker.refresh();
    this.enterSolutionReview();
    this.solveAlternative = alternative;
    // Auffällig lange Lösezeit (Tab lag vermutlich offen) → nachfragen, bevor gewertet wird; der
    // Dialog ist modal und blockiert „Weiter" dahinter. Aufzeichnen + Auto-Advance erst danach.
    this.longSolve.resolve(this.elapsedSeconds).subscribe(seconds => {
      if (this.destroyed) return;   // Seite verlassen (Dialog schließt bei Navigation)
      this.solveSeconds = seconds;
      this.finalizeSolve();
    });
  }

  /** Aufzeichnung + Auto-Advance nach dem Lösen (ggf. nach der Lange-Lösezeit-Nachfrage). */
  private finalizeSolve(): void {
    this.recordAttempt(true);
    this.book.recordTrack(true);   // „Track solves": Erstversuch gelöst
    // Im BRETT-Vollbild gibt es die „Weiter"-Knöpfe (im Panel unter dem Brett) nicht — dort würde der
    // Spieler bei den „stehen bleiben"-Fällen (alternative Lösung / Abschlusstext) festhängen. Deshalb
    // im Brett-Vollbild IMMER auto-weiterspringen; außerhalb bleibt das bewusste Stehenbleiben erhalten.
    // NUR das Brett-Vollbild zählt: im App-Vollbild (`<html>` im Vollbild) sind Abschlusstext und
    // „Weiter" sichtbar — `!!document.fullscreenElement` sprang dort trotz Abschlusstext weiter.
    const inBoardFullscreen = isElementFullscreen();
    // Bei alternativer (eigener) Lösung NICHT automatisch weiterspringen — wie im Endless-Modus:
    // der Spieler entscheidet selbst (Weiter / Originallösung zeigen). Im Vollbild dennoch weiter.
    if (this.solveAlternative && !inBoardFullscreen) return;
    // Direkt geteiltes Einzel-Puzzle: am Ende stehen bleiben, kein Auto-Advance (auch im Vollbild —
    // es gibt kein „nächstes").
    if (this.singlePuzzle) return;
    // Steht nach dem letzten Lösungszug noch ein Abschlusstext (Kommentar NACH dem Zug), NICHT
    // automatisch weiterspringen — der Spieler soll ihn lesen und selbst „Weiter" klicken. Der
    // Kommentar wird über displayComment angezeigt (enterSolutionReview springt ans Ende + setzt ihn).
    // Im Vollbild ist der Text ohnehin nicht erreichbar → dennoch weiter.
    if (this.hasTrailingSolutionComment && !inBoardFullscreen) return;
    // Sonst einheitlicher Auto-Advance: nach kurzem Countdown zum nächsten (kontextabhängig
    // Kurs/Wochenpost/Standalone); per „Weiter"-Klick sofort überspringbar.
    this.startSolvedCountdown(() => this.solvedAutoNext());
  }

  /** Gibt es nach dem letzten Zug der Linie noch einen (Abschluss-)Kommentar? (0-basierter Schlüssel
   *  des letzten Halbzugs in `moveComments`). Steuert, dass am Ende nicht auto-weitergesprungen wird. */
  private get hasTrailingSolutionComment(): boolean {
    return computeHasTrailingComment(this.puzzle?.moves, this.puzzle?.moveComments);
  }

  /** Nächstes Puzzle je nach Modus (Auto-Advance-Ziel). */
  solvedAutoNextPublic(): void { this.solvedAutoNext(); }
  private solvedAutoNext(): void {
    if (this.singlePuzzle) return;       // Direkt geteiltes Einzel-Puzzle: stehen bleiben
    if (this.isDaily) return;            // Tagespuzzle: kein Auto-Advance, Navigation via Datum
    this.modeStrategy.next();
  }

  /** Strategie des aktiven Modus mit eigener Puzzle-Folge (Tagespuzzle läuft über `book`, navigiert aber per Datum). */
  private get modeStrategy(): BookModeStrategy {
    return this.inCourse ? this.course : this.inWeekly ? this.weekly : this.book;
  }

  /** Versuch an alle Modi melden — jede Strategie prüft selbst, ob sie zuständig ist (Reihenfolge wie bisher). */
  private recordAttempt(solved: boolean): void {
    this.course.recordAttempt(solved);
    this.weekly.recordAttempt(solved);
    this.book.recordAttempt(solved);
  }

  // ===== Tagespuzzle (Datum statt Buch, BookDailyMode) =====
  get isDaily(): boolean { return this.daily.date != null; }

  protected override handleFailed(): void {
    this.state = 'FAILED';
    this.stopTimer();
    this.updateBoard();
    this.favoriteTracker.refresh();
    this.showFailureOrHoldMate();
    this.solveSeconds = this.elapsedSeconds;
    this.recordAttempt(false);
    this.book.recordTrack(false);   // „Track solves": Erstversuch nicht gelöst (Fehlzug)
  }

  ngOnInit(): void {
    // Optionale Anzeige-Overrides aus dem (geteilten) Link anwenden, BEVOR das Puzzle aufgebaut wird
    // (onSetupStart liest themeMode, der Solver-Setup liest visualizationMode). Gilt für alle Modi.
    this.applyShareViewOverrides();

    // „Letztes Puzzle" (analysieren / ♥ / teilen) über Navigation zu /analysis hinweg wiederherstellen —
    // ohne Persistenz wird `lastSolvedPuzzleId` beim Component-Destroy null und die Knöpfe verschwinden.
    // Nicht im Wochenpost: der Eintrag gehört zu Buch/Kurs, dort wäre er nicht das „letzte Puzzle".
    const weeklyIdParam = this.route.snapshot.paramMap.get('weeklyId');
    const restored = weeklyIdParam ? null : loadLastSolved('book');
    if (restored) {
      this.lastSolvedPuzzleId = restored.id;
      this.lastSolvedFen = restored.fen;
      this.lastSolvedMoves = restored.moves;
      this.lastSolvedOrientation = restored.orientation;
      this.lastSolvedStartPly = restored.startPly ?? 0;
      this.lastSolvedThemes = restored.themes ?? '';
      this.favoriteTracker.refresh();
    }

    if (weeklyIdParam) {
      this.inWeekly = true;
      this.weekly.id = Number(weeklyIdParam);
      this.weekly.load();
      return;
    }

    const bookIdParam = this.route.snapshot.paramMap.get('bookId');
    const modeParam = this.route.snapshot.paramMap.get('mode');
    if (bookIdParam && modeParam) {
      this.inCourse = true;
      // Reaktiv auf die Kurs-Params hören → der Schnellwechsel zum verknüpften Kurs (Buch↔Workbook)
      // und Browser zurück/vor laden neu, ohne die Komponente neu aufzubauen (Snapshot würde nicht
      // erneut feuern). paramMap emittiert den aktuellen Stand synchron → Erst-Load wie bisher.
      this.courseSub = this.route.paramMap.subscribe(pm => this.course.start(pm));
      return;
    }

    if (this.route.snapshot.paramMap.has('date')) {
      // Reaktiv auf den :date-Param hören: prev/next-Daily (und Browser zurück/vor) wechseln
      // nur den Param, ohne die Komponente neu aufzubauen → Snapshot würde nicht neu laden.
      this.dailySub = this.route.paramMap.subscribe(pm => {
        const d = pm.get('date');
        if (d) this.daily.load(d);
      });
      return;
    }

    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam) {
      // Aus einer Freundes-Challenge geöffnet (nur Standalone-Buch-Modus) → Ergebnis zurückmelden.
      const challengeParam = this.route.snapshot.queryParamMap.get('challengeId');
      if (challengeParam) this.book.challengeId = Number(challengeParam) || null;
      // Direkt geteiltes Einzel-Puzzle (Teilen-Link `?single=1`) → am Ende stehen bleiben, nicht
      // weiterspringen UND Erstversuche immer mitzählen + anzeigen (kein separates Opt-in mehr).
      this.singlePuzzle = this.route.snapshot.queryParamMap.get('single') === '1';
      this.book.trackSolves = this.singlePuzzle;
      // Direkt geteiltes Einzel-Puzzle: kein Trainingsbereich → keine Spielweisen-Abfrage.
      if (this.singlePuzzle) this.solveModeSuppressed = true;
      if (this.book.trackSolves) this.book.loadSharedCounts(Number(idParam));
      this.book.load(Number(idParam));
    }
  }

  /**
   * Transiente Anzeige-Overrides aus dem Link (`?crazy=1`, `?visualmode=0–4`) — verändert keine
   * gespeicherten Einstellungen. Praktisch, um ein geteiltes Puzzle als Blind-/Crazy-Variante zu
   * verlinken (Parameter manuell an den Teilen-Link anhängen).
   */
  private applyShareViewOverrides(): void {
    const ov = parseShareViewParams(this.route.snapshot.queryParamMap);
    if (ov.themeMode) this.themeMode = ov.themeMode;
    if (ov.visualization != null) {
      this.visualizationMode = ov.visualization;
      // Ansicht per Link festgelegt → nicht nach der Spielweise fragen (die Wahl steckt im Link).
      this.solveModeSuppressed = true;
    }
    // Challenge/Revanche: „schau dir das an"-Kontext aus einem Link, kein Trainingsbereich.
    const q = this.route.snapshot.queryParamMap;
    if (q.get('challengeId') || q.get('revengeUserId')) this.solveModeSuppressed = true;
    this.anarchyForcedByUrl = !!ov.enPassantForced;   // Anarchy per URL: e.p. immer forciert (sonst folgt es der Einstellung)
    if (ov.crazyPieceMode) this.crazyPieceMode = ov.crazyPieceMode;   // ?anarchy=max+1 → Feld bestimmt Stil
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.dailySub?.unsubscribe();
    this.courseSub?.unsubscribe();
    this.stopTimer();
    // Tagespuzzle/Kurs mitten im Lauf verlassen → finalen Stand für den Wiederbesuch festhalten.
    // NUR bei offenem Lauf (isSolving): nach SOLVED/FAILED ist der Versuch erfasst und der Eintrag
    // gelöscht — ein Speichern hier würde ihn wieder aufleben lassen.
    if (this.isSolving) {
      const courseKey = this.course.elapsedKey;
      if (this.isDaily && this.daily.date) saveDailyElapsed(this.daily.date, this.elapsedSeconds);
      else if (courseKey) saveSolveElapsed(courseKey, this.elapsedSeconds);
    }
    this.clearSolutionPlay();
    this.abortSolver();
    clearCrazyStyles();
    clearVisualizationHide();
  }

  /** Einzel-Kommentar für Review-/Info-Modus (Kommentar zum aktuell durchgespielten Zug, Fallback
   *  Einleitung) — Basis der Ply-Navigation; die Anzeige läuft über {@link commentLines}. */
  get displayComment(): string | null {
    if (!this.puzzle) return null;
    return computeDisplayComment(this.reviewMode, this.reviewIndex, this.moveComment, this.puzzle.comment);
  }

  /** Die im Kontext-Block angezeigten Kommentar-Absätze (gestapelt).
   *  - Review/Info: EIN Absatz (der zum durchgespielten Zug bzw. die Einleitung).
   *  - WÄHREND des Lösens: die Kommentare ALLER bereits gespielten Lösungszüge in Reihenfolge
   *    (jeder neue darunter). Züge ohne Kommentar fügen NICHTS hinzu; es gibt KEINEN Rückfall auf
   *    die Einleitung, sobald gespielt wird (leere Liste → Block ausgeblendet).
   *  - Vor dem ersten Zug: die Einleitung des Puzzles. */
  get commentLines(): string[] {
    if (!this.puzzle) return [];
    return buildCommentLines({
      reviewMode: this.reviewMode,
      reviewIndex: this.reviewIndex,
      moveComment: this.moveComment,
      puzzleComment: this.puzzle.comment,
      moveComments: this.puzzle.moveComments,
      onSolutionPath: this.onSolutionPath,
      moveIndex: this.moveIndex,
      solving: this.state === 'AWAITING_USER_MOVE' || this.state === 'THINKING',
      startPly: this.startPly,
    });
  }

  // ---- Klickbare Züge in Kommentaren (Variante auf dem Brett vorspielen) ----
  /** Brett-Vorschau einer angeklickten Kommentar-Variante (überlagert boardFen/lastMove); null = aus. */
  variationPreview: { fen: string; lastMove: [Key, Key] } | null = null;
  private readonly cmtCache = new CommentBlockCache();

  /** {@link commentLines} in klickbare Segmente zerlegt (Text + spielbare Zug-Chips). Gecacht je
   *  Kommentar-Inhalt + Puzzle, da die Auflösung nur von Puzzle-FEN/-Zügen abhängt (nicht vom Ply);
   *  derselbe Cache wie in der Kurs-Durchsicht (`line-step.util`). */
  get commentBlocks(): CommentSegment[][] {
    return this.cmtCache.get(this.puzzle?.id ?? 0, this.commentLines, this.puzzle?.fen ?? '',
      () => (this.puzzle?.moves ?? '').split(' ').filter(m => m));
  }

  /** Spielt die angeklickte Variante bis zu diesem Zug als Brett-Vorschau (view-only). */
  previewVariationMove(seg: CommentSegment): void {
    if (!seg.fen || !seg.from || !seg.to) return;
    this.variationPreview = { fen: seg.fen, lastMove: [seg.from as Key, seg.to as Key] };
  }

  /** Langer Kommentar (Vorwort, Einleitung): steht weiter VOLLSTÄNDIG im Kasten, ein Klick öffnet ihn zusätzlich im
   *  Fenster (Wunsch 2026-10-07: „modal, damit mehr Platz … trotzdem alles anzeigen, damit ich nicht klicken muss"). */
  /** Kurs-Fortschritt (ganzes Buch + Kapitel) aufgeklappt? Zu bis zum ⓘ neben der Sprachwahl (Wunsch 2026-10-20). */
  readonly statsOpen = signal(false);

  get commentIsLong(): boolean { return commentLength(this.commentBlocks) >= LONG_COMMENT_CHARS; }

  /** Kommentar im Fenster; ein Zug darin schließt es und spielt die Variante am Brett vor wie im Kasten. */
  openCommentDialog(): void {
    const subtitle = [this.chapterDisplay, this.displayBookName].filter(s => !!s).join(' · ') || null;
    this.dialog.open(CommentDialogComponent, {
      data: { title: this.titleDisplay, subtitle, blocks: this.commentBlocks },
      width: '760px', maxWidth: '96vw', maxHeight: '90vh', autoFocus: false,
    }).afterClosed().subscribe((seg?: CommentSegment) => { if (seg) this.previewVariationMove(seg); });
  }

  /** Klick in den Kommentartext: nur Text öffnet das Fenster — Zug-Chips und markierter Text nicht. */
  onCommentBodyClick(ev: MouseEvent): void {
    if (!this.commentIsLong) return;
    if ((ev.target as HTMLElement | null)?.closest('button')) return;
    if ((window.getSelection?.()?.toString() ?? '') !== '') return;
    this.openCommentDialog();
  }

  /** Vorschau beenden → zurück zur Puzzle-/Review-Stellung. */
  exitVariationPreview(): void { this.variationPreview = null; }

  retryLoad(): void {
    this.loadError = false;
    this.retryFn?.();
  }

  /** Auge im Wochenpost: NACH dem ersten Zug aufgedeckt → dieses Puzzle zählt als „einfach".
   *  Vor dem ersten Zug ist das Aufdecken die normale Vorbereitung (Stellung einprägen). */
  override onVizShow(): void {
    super.onVizShow();
    if (this.inWeekly && this.vizShowPressed && this.hasMadeFirstMove) this.weekly.revealedAfterMove = true;
  }

  // ===== Spielweise: Kurs / Tagespuzzle / einzelnes Buch-Puzzle =====

  /**
   * Bereichs-Schlüssel der Spielweise. Je Kurs ein eigener (ein Taktikbuch trainiert man anders
   * als einen Eröffnungskurs), sonst „daily" bzw. „book". `null` = kein eigener Bereich:
   * Wochenpost (eigene Abfrage) bzw. Kurs ohne bekannte Buch-Id.
   */
  get solveModeScope(): string | null {
    if (this.inWeekly) return null;
    if (this.inCourse) return this.course.bookId != null ? SolveModeService.scopeCourse(this.course.bookId) : null;
    return this.isDaily ? 'daily' : 'book';
  }

  /** Wird der Umschalt-Knopf angeboten? (Nicht im Wochenpost und nicht in per Link festgelegten Ansichten.) */
  get showSolveModeSwitch(): boolean {
    return !!this.solveModeScope && !this.solveModeSuppressed;
  }

  /** Was im Dialog benannt wird (außerhalb von Kursen): der Bereichsname. */
  private get solveModeLabel(): string {
    return this.translate.instant(this.isDaily ? 'solveMode.scope.daily' : 'solveMode.scope.book');
  }

  /**
   * Klärt die Spielweise des Bereichs, BEVOR das erste Puzzle aufgesetzt wird — sonst liefe die
   * Stoppuhr hinter dem (blockierenden) Dialog mit. Gefragt wird nur beim ersten Mal je Bereich;
   * danach liefert der Service die gemerkte Wahl ohne Dialog.
   * Liefert true, wenn das Aufsetzen zurückgestellt wurde (läuft im Callback weiter).
   */
  private askSolveModeIfNeeded(puzzle: BookPuzzleDto): boolean {
    const scope = this.solveModeScope;
    if (!scope || this.solveModeSuppressed || this.solveModeScopeAsked === scope) return false;
    this.solveModeScopeAsked = scope;
    // Kommt der Dialog nach einer Info-Linie (Kurs-Einführung, „Weiter im Buch"), steht noch deren
    // Review-/INFO-Zustand: Pfeile, Kommentar, Varianten-Vorschau und die INFO-Karte würden über bzw.
    // statt der neuen Aufgabe stehen. Darum hier verlassen; LOADING lässt die Karte um die Wahl bitten.
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.stopTimer();
    this.reviewMode = false;
    this.solutionReview = false;
    this.reviewShapes = [];
    this.variationPreview = null;
    this.moveComment = null;
    this.state = 'LOADING';
    // Bis zur Antwort die Aufgabe schon zeigen (ohne Uhr/Setup-Zug) statt Grundstellung + „lädt…".
    this.awaitingSolveMode = true;
    this.previewSetup(puzzle.fen, puzzle.moves, puzzle.startPly ?? 0);
    // UI-Sweep 2026-10-11 (r2-rp-mode-title): im Kurs „Wie möchtest du Kurse spielen?", der Buchtitel nur als Unterzeile.
    const courseDialog = this.inCourse
      ? { title: this.translate.instant('solveMode.titleCourses'), subtitle: this.displayBookName || undefined }
      : { scopeLabel: this.solveModeLabel };
    this.solveMode.ensure(scope, courseDialog).subscribe(mode => {
      this.awaitingSolveMode = false;
      this.solveModeChoice = mode;
      // Jetzt mit der gewählten Spielweise aufsetzen (der Guard oben greift nicht mehr).
      this.setupPuzzle(puzzle);
    });
    return true;
  }

  /** Visualisierungsstufe aus der gewählten Spielweise setzen — je Puzzle neu, damit ein
   *  Umschalten ab dem nächsten Puzzle greift. Per Link festgelegte Ansichten bleiben unberührt. */
  private applySolveMode(): void {
    if (this.solveModeSuppressed || !this.solveModeScope) return;
    this.visualizationMode = this.solveMode.levelFor(this.solveModeChoice);
  }

  /**
   * Spielweise umschalten — wirkt ab dem NÄCHSTEN Puzzle, damit ein laufender Versuch nicht
   * mitten im Rechnen die Regeln wechselt (die Stufe kommt beim nächsten Aufsetzen über
   * {@link applySolveMode}). Bereits gemeldete Versuche bleiben, wie sie gewertet wurden.
   */
  toggleSolveMode(): void {
    const scope = this.solveModeScope;
    if (!scope || this.solveModeSuppressed) return;
    this.solveModeChoice = this.solveModeChoice === 'training' ? 'easy' : 'training';
    this.solveMode.set(scope, this.solveModeChoice);
    this.snackbar.info(this.translate.instant(
      this.solveModeChoice === 'easy' ? 'solveMode.switchedEasy' : 'solveMode.switchedTraining'),
      { duration: 3000 });
  }

  /** Spielweise, unter der das AKTUELLE Puzzle tatsächlich läuft (aus der wirksamen
   *  Visualisierungsstufe abgeleitet) — das ist der Wert, der gemeldet wird. Bewusst nicht
   *  `solveModeChoice`: ein Umschalten mitten im Versuch gilt erst fürs nächste Puzzle, und
   *  per Link festgelegte Ansichten sollen ehrlich gewertet werden. */
  get attemptSolveMode(): SolveMode {
    return this.solveMode.modeForLevel(this.visualizationMode);
  }

  setupPuzzle(puzzle: BookPuzzleDto): void {
    // Info-/Erklärlinien werden nicht gelöst → keine Spielweise nötig (weder fragen noch anwenden).
    if (!puzzle.isInfoOnly) {
      if (this.askSolveModeIfNeeded(puzzle)) return;
      this.applySolveMode();
    }
    this.clearSolutionPlay();
    this.variationPreview = null;   // etwaige Kommentar-Varianten-Vorschau aus dem Vorgänger-Puzzle beenden
    this.book.resetAttempt();
    this.course.attemptRecorded = false;
    this.hintLevel = 0;
    // Tipps werden pro erwartetem Zug on-the-fly aus der aktuellen Stellung erzeugt (currentMoveHint) —
    // kein vorab-klassifizierter erster Zug mehr nötig.
    this.reviewMode = false;
    this.solutionReview = false;
    this.moveComment = null;
    this.reviewShapes = [];
    this.review.setShapes(puzzle.moveShapes);
    this.showEval = false;
    this.initialEval = '';
    this.currentEval = '';
    // Info-/Erklärlinie (Chessable IsInfo): nicht abfragen, nur durchklicken.
    if (puzzle.isInfoOnly) { this.enterInfoReview(puzzle); return; }
    // Lös-Automat (Setup, StartPly-Vorspiel, Zug-Handling, Stockfish, Viz) aus BasePuzzleSolver.
    this.setupSolver(puzzle.fen, puzzle.moves, puzzle.startPly ?? 0);
    // Geduldete Alternativzüge NACH setupSolver setzen (das leert altMovesByPly beim Aufsetzen).
    this.altMovesByPly = parseAltMoves(puzzle.altMoves);
  }

  /**
   * Info-/Erklärlinie statt Quiz aufsetzen: Brett auf die Startstellung der Linie, Review-Modus an
   * (Brett nur lesend, ◀/▶-Navigation + Zug-Kommentare wie beim „Ganze Partie"-Durchspielen). Es gibt
   * keinen Trainingsstart, kein Aufgeben/Lösen und keinen Timer — nur „Weiter" (sequenziell/Buch).
   */
  private enterInfoReview(puzzle: BookPuzzleDto): void {
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.clearSolutionPlay();
    this.stopTimer();
    this.gaveUp = false;
    this.solutionReview = false;
    this.reviewMode = true;
    this.state = 'INFO';
    // Chessable-Muster-/Erklär-Diagramme benutzen z. T. ILLEGALE FENs (z. B. ohne König), die
    // chess.js verwirft. Solche Info-Linien trotzdem als reine Durchklick-Seite zeigen (chessground
    // rendert die Stellung; es gibt ohnehin keine Züge). reviewGoTo unten ist gegen illegale FEN robust.
    const chess = tryLoadFen(puzzle.fen);
    this.orientation = (chess ? chess.turn() : fenSideToMove(puzzle.fen)) === 'w' ? 'white' : 'black';
    this.reviewGoTo(0);   // setzt boardFen/turnColor/Kommentar für die Ausgangsstellung
  }

  /** Brettstand einer Durchsicht-Stelle übernehmen (Brett nur lesend, Kommentar + Pfeile dazu). */
  private applyReviewFrame(f: ReviewFrame): void {
    this.reviewIndex = f.index;
    this.chess = f.chess;
    this.lastMove = f.lastMove;
    this.boardFen = f.fen;
    this.turnColor = f.turnColor;
    this.isCheck = f.isCheck;
    this.dests = new Map();
    this.moveComment = f.moveComment;
    this.reviewShapes = f.shapes;
  }

  giveUp(): void {
    if (!this.puzzle) return;
    this.abortSolver();
    this.stopTimer();
    this.gaveUp = true;
    this.state = 'FAILED';
    this.solveSeconds = this.elapsedSeconds;
    // Aufgeben = Versuch mit verbrachter Zeit (zählt fürs Trainingsziel), im Wochenpost als ✗ (gespielt, nicht gelöst).
    this.recordAttempt(false);
    this.book.recordTrack(false);   // „Track solves": Aufgeben zählt als failed
    // Auf die Anfangsstellung wechseln und die Lösung automatisch durchspielen.
    this.playSolutionFromStart();
  }

  /** Buch-Variante: spielt die LÖSUNG (ab Trainingsstart) durch, nicht die ganze Partie. */
  protected override playSolutionFromStart(): void {
    this.clearSolutionPlay();
    this.solutionReview = true;
    this.reviewMode = true;
    this.solutionReviewGoTo(0);
    this.solutionPlayTimer = setInterval(() => {
      if (this.reviewIndex >= this.reviewTotal) { this.clearSolutionPlay(); return; }
      this.solutionReviewGoTo(this.reviewIndex + 1);
    }, 900);
  }

  retry(): void {
    if (!this.puzzle) return;
    this.gaveUp = false;
    this.setupPuzzle(this.puzzle);
  }

  failedNext(): void {
    this.stopCountdown();
    if (this.isDaily) return;
    this.modeStrategy.next();
  }

  showSolution(): void {
    if (!this.puzzle) return;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.aborted = false;
    this.solutionReview = true;
    this.reviewMode = true;
    this.solutionReviewGoTo(0);
  }

  private solutionReviewGoTo(index: number): void {
    if (!this.puzzle) return;
    this.variationPreview = null;   // Stellungswechsel → Varianten-Vorschau beenden
    this.applyReviewFrame(this.review.solutionFrame(this.puzzle, this.startPly, index));
  }

  protected override enterSolutionReview(): void {
    this.solutionReview = true;
    this.reviewMode = true;
    // Nach dem Lösen in der ENDSTELLUNG stehen bleiben (zeigt einen etwaigen Abschlusstext nach dem
    // letzten Zug). Frühe Zug-Kommentare/Pfeile werden bereits WÄHREND des Lösens live gestapelt
    // (siehe 0.240.6) — ein Zurückspringen zum ersten annotierten Zug hier fühlte sich für den User
    // wie ein „irgendwohin zurück"-Sprung an; siehe Feedback zu 0.240.0.
    this.solutionReviewGoTo(this.reviewTotal);
  }

  // ---- „Ganze Partie" Review ---------------------------------------------
  /** Zeigt die komplette Partie zum Durchklicken (◀/▶), unabhängig vom Trainingsstart. */
  enterReview(): void {
    if (!this.puzzle) return;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.clearSolutionPlay();
    this.stopTimer();
    this.solutionReview = false;
    this.reviewMode = true;
    this.reviewGoTo(0);
  }

  override get reviewTotal(): number {
    return this.review.total(this.puzzle, this.solutionReview, this.startPly);
  }

  reviewNext(): void {
    this.clearSolutionPlay();   // manuelles Klicken stoppt die Auto-Wiedergabe
    if (this.solutionReview) this.solutionReviewGoTo(this.reviewIndex + 1);
    else this.reviewGoTo(this.reviewIndex + 1);
  }
  reviewPrev(): void {
    this.clearSolutionPlay();
    if (this.solutionReview) this.solutionReviewGoTo(this.reviewIndex - 1);
    else this.reviewGoTo(this.reviewIndex - 1);
  }

  /** Nach alternativem (eigenem) Mattweg die vom Puzzle vorgesehene Lösung von vorne durchspielen. */
  showOriginalSolution(): void { this.stopCountdown(); this.playSolutionFromStart(); }

  protected override reviewGoTo(index: number): void {
    if (!this.puzzle) return;
    this.variationPreview = null;   // Stellungswechsel → Varianten-Vorschau beenden
    this.applyReviewFrame(this.review.fullGameFrame(this.puzzle, index));
  }

  exitReview(): void {
    this.reviewMode = false;
    this.solutionReview = false;
    this.moveComment = null;
    this.reviewShapes = [];
    if (this.puzzle) this.setupPuzzle(this.puzzle);
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    if (this.state !== 'SOLVED' && this.state !== 'FAILED' && !this.reviewMode) return;
    // Auch nicht im offenen Einstellungsdialog/Menue: Enter auf einer Auswahl luede sonst das naechste Puzzle.
    if (!isBoardHotkey(e)) return;
    if (e.key === 'ArrowLeft') { this.reviewPrev(); return; }
    if (e.key === 'ArrowRight') { this.reviewNext(); return; }
    // Leertaste/Enter = „nächstes Puzzle" (wie ein Klick auf den primären Weiter-Knopf) im
    // Kurs-/Wochenpost-/Buch-Blätter-Modus.
    if (e.key === ' ' || e.key === 'Spacebar' || e.key === 'Enter') {
      if (this.advanceKeyAction()) e.preventDefault();
    }
  }

  /** Löst die primäre „Weiter/nächstes Puzzle"-Aktion des aktuellen Kontexts aus (Kurs/Wochenpost/
   *  Buch-Blättern), passend zum jeweils angezeigten Primär-Knopf. Liefert true, wenn etwas passierte. */
  private advanceKeyAction(): boolean {
    if (this.inWeekly) { this.weekly.next(); return true; }
    if (this.inCourse) { this.course.next(); return true; }
    if (this.browseInBook) { if (!this.book.navLoading) this.book.next(); return true; }
    return false;
  }

  resetPuzzle(): void {
    if (!this.puzzle) return;
    // Daily: ein Reset mitten im Lauf (mind. ein Zug gespielt) zählt als Fehlversuch (rotes ✗).
    // Nach FAILED/SOLVED nicht erneut melden — der Fehlzug bzw. Solve wurde bereits erfasst.
    if (this.isDaily && this.moveLog.length > 0 && this.state !== 'FAILED' && this.state !== 'SOLVED')
      this.book.recordAttempt(false);
    // Wochenpost: Reset nach mind. einem Zug zählt als ✗ (gespielt, nicht gelöst).
    if (this.inWeekly && this.moveLog.length > 0) this.weekly.recordAttempt(false);
    // „Track solves": Reset zählt als failed (alles mit Reset gilt als nicht gelöst).
    this.book.recordTrack(false);
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.setupPuzzle(this.puzzle);
  }

  override mouseslip(): void {
    // Im Tagespuzzle ist der „Mausrutscher" kein straffreier Undo: jede Off-Path-Zurücknahme
    // zählt als Fehlversuch (rotes ✗). Nach FAILED nicht erneut melden (Fehlzug schon erfasst).
    if (this.isDaily && !this.onSolutionPath && this.state !== 'FAILED') this.book.recordAttempt(false);
    super.mouseslip();
  }


  private loadConfig(): void {
    this.boardTheme = this.prefs.boardTheme;
    this.pieceSet = this.prefs.pieceSet;
    this.themeMode = this.prefs.themeMode;
    this.stockfishDepth = this.prefs.bookStockfishDepth;
    this.visualizationMode = this.prefs.visualization;
    this.vizArrowEnabled = this.prefs.vizArrow;
  }

  setVisualizationLevel(level: number): void {
    this.visualizationMode = level;
    this.prefs.setVisualization(level);
    // Gemerkte Spielweise mitziehen, sonst widersprechen sich Wahl und tatsächliche Stufe
    // (Stufe 0 = Einfachmodus, alles darüber = Trainingsmodus).
    const scope = this.solveModeScope;
    if (scope && !this.solveModeSuppressed) {
      this.solveModeChoice = this.solveMode.modeForLevel(level);
      this.solveMode.set(scope, this.solveModeChoice);
    }
    // Modus-Wechsel = laufendes Puzzle neu starten; nach Gelöst/Gescheitert gilt die Stufe ab dem
    // nächsten Puzzle (ein Neuaufsetzen spränge aus der Durchsicht zurück ins Setup).
    if (this.puzzle && this.isSolving) this.setupPuzzle(this.puzzle);
  }

  setVizArrowEnabled(val: boolean): void {
    this.vizArrowEnabled = val;
    if (!val) this.clearVizOpponentArrow();
    this.prefs.setVizArrow(val);
  }

  saveConfig(): void {
    this.prefs.setBookStockfishDepth(this.stockfishDepth);
  }

  setBoardTheme(theme: string): void {
    this.boardTheme = theme;
    this.prefs.setBoardTheme(theme);
  }

  setPieceSet(set: string): void {
    this.pieceSet = set;
    this.prefs.setPieceSet(set);
  }

  setThemeMode(mode: ThemeMode): void {
    this.themeMode = mode;
    this.prefs.setThemeMode(mode);
    const applied = applyThemeMode(mode, this.prefs.boardTheme, this.prefs.pieceSet);
    this.boardTheme = applied.boardTheme;
    this.pieceSet = applied.pieceSet;
  }

  openSettingsDialog(): void {
    const ref = this.dialog.open(PuzzleSettingsDialogComponent, {
      data: {
        mode: 'book',
        boardTheme: this.prefs.boardTheme,
        pieceSet: this.prefs.pieceSet,
        themeMode: this.themeMode,
        visualizationMode: this.visualizationMode,
        vizArrowEnabled: this.vizArrowEnabled,
        stockfishDepth: this.stockfishDepth,
        offPathWarnMoves: this.prefs.offPathWarnMoves,
        enPassantForced: this.prefs.enPassantForced,
      } as PuzzleSettingsDialogData,
      width: '360px',
      maxWidth: '95vw',
    });
    ref.afterClosed().subscribe((result: PuzzleSettingsDialogResult | null) => {
      if (!result) return;
      this.setBoardTheme(result.boardTheme);
      this.setPieceSet(result.pieceSet);
      this.setThemeMode(result.themeMode);
      // Nur eine GEÄNDERTE Stufe setzt neu auf — Brett/Figuren speichern darf den Versuch nicht neu starten.
      if (result.visualizationMode !== this.visualizationMode) this.setVisualizationLevel(result.visualizationMode);
      this.setVizArrowEnabled(result.vizArrowEnabled);
      if (result.stockfishDepth !== undefined) {
        this.stockfishDepth = result.stockfishDepth;
        this.prefs.setBookStockfishDepth(this.stockfishDepth);
      }
      if (result.offPathWarnMoves !== undefined) this.prefs.setOffPathWarnMoves(result.offPathWarnMoves);
      if (result.enPassantForced !== undefined) {
        this.prefs.setEnPassantForced(result.enPassantForced);
        // Der Link-Zwang (?anarchy=max) gilt unabhängig von der Einstellung (wie in onSetupStart).
        this.enPassantForced = this.anarchyForcedByUrl || (this.themeMode === 'crazy' && result.enPassantForced);
        if (this.isSolving) this.updateBoard();   // Zugziele des laufenden Versuchs nachziehen
      }
    });
  }
}
