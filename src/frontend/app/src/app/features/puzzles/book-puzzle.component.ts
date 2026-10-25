import { Component, OnInit, OnDestroy, HostListener, ChangeDetectionStrategy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { BoardFsActionsComponent } from './board-fs-actions.component';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { hasTrailingSolutionComment as computeHasTrailingComment } from './book-comment.util';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleStatusCardComponent } from './puzzle-status-card.component';
import { ChallengeFriendsComponent } from './challenge-friends.component';
import { PuzzleActionBarComponent } from './puzzle-action-bar.component';
import { ChallengeService } from '../../core/challenge.service';
import { PuzzleService, BookPuzzleDto } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { PreferencesService } from '../../core/preferences.service';
import { parseShareViewParams } from './board-theme.util';
import { DrawShape } from 'chessground/draw';
import { parseAltMoves } from './alt-moves.util';
import { tryLoadFen, fenSideToMove } from './puzzle-move.util';
import { BookReviewController, ReviewFrame } from './book-review';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { CourseService } from '../courses/course.service';
import { LongSolveService } from './long-solve.service';
import { isElementFullscreen } from '../../shared/fullscreen/fullscreen.util';
import { AuthService } from '../../core/auth.service';
import { CourseLanguageService, CourseRef } from '../courses/course-language.service';
import { labelOr } from '../courses/course-language.util';
import { loadDailyElapsed, saveDailyElapsed } from './daily-elapsed.util';
import { loadSolveElapsed, saveSolveElapsed } from './solve-elapsed.util';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { FavoritesService } from '../../core/favorites.service';
import { WorksheetService } from '../worksheets/worksheet.service';
import { FavoriteTracker } from './favorite-tracker';
import { WeeklyService } from '../weekly/weekly.service';
import { SolveMode, SolveModeService } from '../../core/solve-mode.service';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { BookModeHost, BookModeStrategy } from './book-mode-host';
import { BookCourseMode } from './book-course-mode';
import { BookContextCardComponent } from './book-context-card.component';
import { BookCommentBlockComponent } from './book-comment-block.component';
import { BookBoardHintComponent } from './book-board-hint.component';
import { BookWeeklyMode } from './book-weekly-mode';
import { BookDailyMode } from './book-daily-mode';
import { BookStandaloneMode } from './book-standalone-mode';
import { BookSolveModeController } from './book-solve-mode';
import { BookLanguageController } from './book-language';
import { BookLastSolved, bookShareData } from './book-last-solved';
import { BookCommentView, VariationPreview } from './book-comment-view';
import { BookSettingsController } from './book-settings';
import { BookHints } from './book-hints';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-book-puzzle',
  standalone: true,
  imports: [
    CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatDialogModule,
    PuzzleBoardComponent, BoardFsActionsComponent,
    TranslatePipe, PuzzleStatusCardComponent, ChallengeFriendsComponent, PuzzleActionBarComponent,
    BookContextCardComponent, BookCommentBlockComponent, BookBoardHintComponent,
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

  /** Spielweise (Training/Einfach) für Kurs, Tagespuzzle und Buch-Puzzle — der Wochenpost hat seine eigene Abfrage. */
  readonly solveModes: BookSolveModeController;
  /** Sprache der Kurs-Kommentare (Kurs-Übersetzung, Stufe C). */
  readonly lang: BookLanguageController;
  /** Zuletzt gelöstes Puzzle (analysieren / ♥ / teilen / Aufgabenblatt), überlebt den Auto-Advance. */
  readonly lastSolved = new BookLastSolved();
  /** Info-/Zug-Kommentar: geltende Absätze, Zug-Chips, Fenster für lange Kommentare. */
  readonly comments: BookCommentView;

  private dailySub?: Subscription;
  private courseSub?: Subscription;

  stockfishDepth = 16;
  boardTheme = 'brown';
  pieceSet = 'cburnett';
  /** Einstellungen (Startwerte, Dialog, Speichern je Wert). */
  readonly settings: BookSettingsController;

  // elapsedSeconds / Stoppuhr / start-/stopTimer / formatTime: jetzt in BasePuzzleSolver (geteilt).

  /** Die zu wertende Lösezeit — i.d.R. = elapsedSeconds, bei „war weg" auf den Schwellwert gekappt. */
  solveSeconds = 0;
  /** alternative-Flag des aktuellen Solves (durchgereicht an finalizeSolve nach der Nachfrage). */
  private solveAlternative = false;


  /** Tipps: vorberechnete bzw. on-the-fly gestufte, „Tipps melden“. */
  readonly hints: BookHints;
  override get availableHints(): string[] { return this.hints.available(this.atFirstSolverMove, this.currentMoveHint); }

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
  // Für BookSettingsController: Lös-Automat-Teile der Basis (dort geschützt).
  get solving(): boolean { return this.isSolving; }
  get urlForcesEnPassant(): boolean { return this.anarchyForcedByUrl; }
  dropVizArrow(): void { this.clearVizOpponentArrow(); }
  refreshBoard(): void { this.updateBoard(); }

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
    readonly prefs: PreferencesService,
    private route: ActivatedRoute,
    readonly dialog: MatDialog,
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
    this.solveModes = new BookSolveModeController(this);
    this.lang = new BookLanguageController(this);
    this.comments = new BookCommentView(this);
    this.hints = new BookHints(this);
    // Wochenpost-Puzzles haben keine echte BookPuzzle-Id (Index) → nie favorisierbar.
    this.favoriteTracker = new FavoriteTracker(
      this.favorites, 'book',
      () => this.puzzle?.id, () => this.lastSolved.id,
      () => this.isLoggedIn && !this.inWeekly,
    );
    this.settings = new BookSettingsController(this);
    this.settings.load();
    this.loadSettingsOpen();
    this.stockfish.init().catch(() => {});
  }

  // ===== Sprache der Kommentare (BookLanguageController) — für die Modus-Strategien =====
  get langRef(): CourseRef | null { return this.lang.ref; }
  get requestLang(): string | null { return this.lang.requestLang; }
  afterLineLoaded(p: BookPuzzleDto, sentLang: string | null): void { this.lang.afterLineLoaded(p, sentLang); }
  /** Im Durchklicken die angezeigte Stelle neu ziehen (Kommentar nach einem Sprachwechsel). */
  redrawReview(): void {
    if (!this.reviewMode) return;
    if (this.solutionReview) this.solutionReviewGoTo(this.reviewIndex);
    else this.reviewGoTo(this.reviewIndex);
  }

  /** Angezeigter Titel/Kapitel der Linie: die Übersetzung, sonst das Original (Schlüssel bleiben). */
  get titleDisplay(): string | null { return labelOr(this.puzzle?.titleLabel, this.puzzle?.title); }
  get chapterDisplay(): string | null { return labelOr(this.puzzle?.chapterLabel, this.puzzle?.chapter); }

  sharePuzzle(): void {
    if (!this.puzzle) return;
    this.dialog.open(SharePuzzleDialogComponent, {
      data: bookShareData(window.location.origin, this.puzzle.id, this.lastSolved.id, this.weekly.id,
        this.inWeekly, this.isLoggedIn),
      width: '400px',
      maxWidth: '95vw',
    });
  }

  /** Die zuletzt gelöste Linie aufs AUFGABENBLATT — dieselbe Auswahl wie „♥ letztes Puzzle". */
  sendLastToWorksheet(target: number | null): void {
    this.stopCountdown();
    if (!this.lastSolved.fen) return;
    const item = this.lastSolved.worksheetItem(this.course.bookId);
    this.worksheets.sendAndNotify(target, item ? [item] : []);
  }

  /** Zuletzt gelöstes Puzzle im Analysemodus öffnen (auch nach dem Auto-Advance verfügbar). */
  reviewLastPuzzle(): void {
    this.stopCountdown();
    // zurück zum aktuellen Buch-/Kurs-/Wochenpost-Puzzle
    const queryParams = this.lastSolved.analysisQuery(this.router.url.split('?')[0]);
    if (queryParams) this.router.navigate(['/analysis'], { queryParams });
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

  protected override onSetupStart(): void { this.settings.applyThemeForSetup(); }

  protected override onSolvingBegins(): void {
    // Tagespuzzle/Kurs: früher verbrachte (aktive) Zeit fortführen — wer die Seite mitten im Lösen
    // verlässt oder neu lädt, zählt kumuliert weiter statt bei 0 (Daily: fair für die „schnellster
    // Löser"-Wertung; Kurs: ehrliche Kurs-/Trainingszeit-Statistik).
    const courseKey = this.course.elapsedKey;
    this.startTimer(this.isDaily && this.daily.date ? loadDailyElapsed(this.daily.date)
      : courseKey ? loadSolveElapsed(courseKey) : 0);
  }

  /** Zwischenstand der Lösezeit im Sekunden-Tick persistieren (für den Wiederbesuch/Refresh). */
  protected override onTimerTick(): void { this.persistElapsed(); }

  /** Lösezeit des Tagespuzzles bzw. der Kurs-Aufgabe für den Wiederbesuch festhalten. */
  private persistElapsed(): void {
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

  protected override handleSolved(alternative: boolean): void {
    this.state = 'SOLVED';
    this.stopTimer();
    this.updateBoard();
    // Für „Letztes Puzzle analysieren"/„Letztes teilen" merken (überlebt den Auto-Advance, per sessionStorage
    // auch die Navigation zu /analysis und zurück; Wochenpost ohne Id und ohne Persistenz).
    if (this.puzzle) this.lastSolved.remember(this.puzzle, this.orientation, this.startPly, this.inWeekly);
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
    // Kommentar wird über comments.display angezeigt (enterSolutionReview springt ans Ende + setzt ihn).
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
    if (!weeklyIdParam && this.lastSolved.restore()) this.favoriteTracker.refresh();

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
      if (this.singlePuzzle) this.solveModes.suppressed = true;
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
      this.solveModes.suppressed = true;
    }
    // Challenge/Revanche: „schau dir das an"-Kontext aus einem Link, kein Trainingsbereich.
    const q = this.route.snapshot.queryParamMap;
    if (q.get('challengeId') || q.get('revengeUserId')) this.solveModes.suppressed = true;
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
    if (this.isSolving) this.persistElapsed();
    this.releaseSolver();   // bricht auch die Lösungs-Wiedergabe ab
  }

  /** Brett-Vorschau einer angeklickten Kommentar-Variante (überlagert boardFen/lastMove); null = aus. */
  variationPreview: VariationPreview | null = null;
  /** Kurs-Fortschritt (ganzes Buch + Kapitel) aufgeklappt? Zu bis zum ⓘ neben der Sprachwahl (Wunsch 2026-10-20). */
  readonly statsOpen = signal(false);
  // Für BookCommentView: Lös-Automat-Stand (in der Basis geschützt).
  get solverMoveIndex(): number { return this.moveIndex; }
  get solverStartPly(): number { return this.startPly; }

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

  /** Vor der Spielweisen-Frage: Kommt der Dialog nach einer Info-Linie (Kurs-Einführung, „Weiter im Buch"), steht
   *  noch deren Review-/INFO-Zustand: Pfeile, Kommentar, Varianten-Vorschau und die INFO-Karte würden über bzw. statt
   *  der neuen Aufgabe stehen. Darum hier verlassen; LOADING lässt die Karte um die Wahl bitten. */
  holdForSolveMode(puzzle: BookPuzzleDto): void {
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
  }

  /** Spielweise des laufenden Puzzles (gemeldeter Wert, siehe BookSolveModeController.attemptMode). */
  get attemptSolveMode(): SolveMode { return this.solveModes.attemptMode; }

  setupPuzzle(puzzle: BookPuzzleDto): void {
    // Info-/Erklärlinien werden nicht gelöst → keine Spielweise nötig (weder fragen noch anwenden).
    if (!puzzle.isInfoOnly) {
      if (this.solveModes.askIfNeeded(puzzle)) return;
      this.solveModes.apply();
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

  override reviewNext(): void {
    this.clearSolutionPlay();   // manuelles Klicken stoppt die Auto-Wiedergabe
    if (this.solutionReview) this.solutionReviewGoTo(this.reviewIndex + 1);
    else this.reviewGoTo(this.reviewIndex + 1);
  }
  override reviewPrev(): void {
    this.clearSolutionPlay();
    if (this.solutionReview) this.solutionReviewGoTo(this.reviewIndex - 1);
    else this.reviewGoTo(this.reviewIndex - 1);
  }

  protected override reviewGoTo(index: number): void {
    if (!this.puzzle) return;
    this.variationPreview = null;   // Stellungswechsel → Varianten-Vorschau beenden
    this.applyReviewFrame(this.review.fullGameFrame(this.puzzle, index));
  }

  override exitReview(): void {
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
    if (this.reviewArrowKey(e)) return;
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
}
