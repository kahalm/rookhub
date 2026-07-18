import { Component, OnInit, OnDestroy, HostListener, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { PuzzleTagsComponent } from './puzzle-tags.component';
import { buildCommentSegments, CommentSegment } from './comment-variation.util';
import {
  latestCommentUpTo as latestCommentUpToUtil,
  displayComment as computeDisplayComment, buildCommentLines, hasTrailingSolutionComment as computeHasTrailingComment,
} from './book-comment.util';
import { formatUtcDate, shiftDailyDate, weeklyStartIndex as computeWeeklyStartIndex, formatSecondsClock, formatEtaShort, estimateRemainingSeconds } from './book-nav.util';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleSettingsDialogComponent, PuzzleSettingsDialogData, PuzzleSettingsDialogResult } from './puzzle-settings-dialog.component';
import { PuzzleStatusCardComponent } from './puzzle-status-card.component';
import { ChallengeFriendsComponent } from './challenge-friends.component';
import { ChallengeService } from '../../core/challenge.service';
import { PuzzleService, BookPuzzleDto, SharedPuzzleCounts } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { PreferencesService } from '../../core/preferences.service';
import { BOARD_THEMES, PIECE_SETS, ThemeMode, applyThemeMode, clearCrazyStyles, clearVisualizationHide, parseShareViewParams } from './board-theme.util';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { parseMoveShapes } from './move-shapes.util';
import { parseAltMoves } from './alt-moves.util';
import { applyUci } from './puzzle-move.util';
import { FirstMoveHint, buildStagedHints } from './puzzle-hints.util';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { CourseService, CourseMode, CourseScopeStats } from '../courses/course.service';
import { LongSolveService } from './long-solve.service';
import { AuthService } from '../../core/auth.service';
import { getBookOffline, findCachedBookPuzzle, getBookOfflineByBookId, saveBookOffline, saveDailyOffline, getDailyOffline, loadCourseLocalSolved, saveCourseLocalSolved, clearCourseLocalSolved } from './book-offline.util';
import { loadDailyElapsed, saveDailyElapsed, clearDailyElapsed } from './daily-elapsed.util';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { FavoritesService } from '../../core/favorites.service';
import { loadLastSolved, saveLastSolved } from './last-solved-store';
import { FavoriteTracker } from './favorite-tracker';
import { WeeklyService } from '../weekly/weekly.service';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

// 'INFO' = Chessable-Info-/Erklärlinie: kein Quiz, nur Durchklicken (Review-Modus ab Stellung 0).
type BookPuzzleState = 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE' | 'THINKING' | 'PLAYING' | 'SOLVED' | 'FAILED' | 'COURSE_DONE' | 'INFO';

// Seitengröße fürs anonyme öffentliche Kurs-Laden: erste Seite → sofort spielbar, Rest im Hintergrund.
const ANON_COURSE_PAGE_SIZE = 300;

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-book-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, MatProgressBarModule, MatTooltipModule, MatDialogModule,
    PuzzleBoardComponent, PuzzleTagsComponent,
    TranslatePipe, PuzzleStatusCardComponent, ChallengeFriendsComponent
  ],
  templateUrl: './book-puzzle.component.html',
  styleUrls: ['./book-puzzle.component.scss'],
})
export class BookPuzzleComponent extends BasePuzzleSolver implements OnInit, OnDestroy {
  // state, boardFen, orientation, turnColor, dests, lastMove, isCheck, chess, solutionMoves,
  // moveIndex, startPly, autoAdvanceTimer, aborted, onSolutionPath, alternativeSolve,
  // mouseslipUsed, visualizationMode, vizMoves → BasePuzzleSolver
  puzzle: BookPuzzleDto | null = null;

  // Kursmodus: Komponente wird über /courses/:bookId/:mode aufgerufen und arbeitet ein
  // Buch puzzleweise durch; Fortschritt liegt user-bezogen in der DB.
  inCourse = false;
  courseBookId: number | null = null;
  courseModeKind: CourseMode = 'sequential';
  /** Im Kapitel-Modus gesetzt: 0-basierter Kapitel-Index → Pool + Fortschritt aufs Kapitel beschränkt. */
  courseChapterIndex: number | null = null;
  courseSolved = 0;
  courseTotal = 0;
  courseCompleted = false;
  /** Fortschritt/Zeit/Trefferquote fürs ganze Buch bzw. das aktuelle Kapitel (vom Backend). */
  courseBookStats: CourseScopeStats | null = null;
  courseChapterStats: CourseScopeStats | null = null;
  courseChapterName: string | null = null;
  /** Offline gelöste/übersprungene Kurs-Puzzles dieser Sitzung — vermeidet Dauerschleife auf
   *  demselben Puzzle, solange der Server-Fortschritt nicht erreichbar ist. */
  private offlineCourseSolvedIds = new Set<number>();

  // Wochenpost-Modus: /weekly/:weeklyId — Puzzles eines Posts sequenziell durchspielen.
  // Per-User-Fortschritt wird serverseitig gemerkt: jedes gespielte Puzzle (gelöst oder nicht)
  // zählt; erledigt = alle gespielt.
  inWeekly = false;
  weeklyId: number | null = null;
  weeklyTitle = '';
  weeklyPuzzles: BookPuzzleDto[] = [];
  weeklyIndex = 0;
  weeklyCompleted = false;
  weeklyPlayed = 0;                       // gespielte Puzzles (serverseitiger Stand)
  weeklySolved = 0;                       // davon gelöst
  weeklySeconds = 0;                      // Gesamtzeit über alle gespielten Puzzles (serverseitiger Stand)
  private weeklyAttemptRecorded = false;  // pro Puzzle nur einmal aufzeichnen

  // Tagespuzzle-Modus: /puzzles/daily/:date — Datums-Navigation (zurück/vor) statt Buch-Nav.
  dailyDate: string | null = null;
  private dailySub?: Subscription;
  private courseSub?: Subscription;
  /** Verknüpfter Partner-Kurs (Buch↔Workbook) für den Schnellwechsel; null = keiner. */
  linkedCourseBookId: number | null = null;
  linkedCourseName: string | null = null;

  stockfishDepth = 16;
  boardTheme = 'brown';
  readonly boardThemes = BOARD_THEMES;

  pieceSet = 'cburnett';
  readonly pieceSets = PIECE_SETS;

  // elapsedSeconds / Stoppuhr / start-/stopTimer / formatTime: jetzt in BasePuzzleSolver (geteilt).

  /** Die zu wertende Lösezeit — i.d.R. = elapsedSeconds, bei „war weg" auf den Schwellwert gekappt. */
  private solveSeconds = 0;
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
  /** Ply → Shapes des aktuellen Puzzles (aus `puzzle.moveShapes` geparst; -1 = Einleitung). */
  private moveShapesByPly: Record<number, DrawShape[]> = {};

  /** Standalone-Buch-Puzzle (/puzzles/book/:id) — nicht Kurs-/Wochenpost-Kontext. */
  get standalone(): boolean { return !this.inCourse && !this.inWeekly; }
  /** Buch-Navigation (nächstes/zufälliges im selben Buch) anbieten — entfällt für ein direkt
   *  geteiltes Einzel-Puzzle (`?single=1`) und im Tagespuzzle (Datums-Navigation). */
  get browseInBook(): boolean { return this.standalone && !this.isDaily && !this.singlePuzzle; }
  bookNavLoading = false;
  loadError = false;
  /** Monotone Epoche je Ladevorgang (Buch/Kurs/Daily/Wochenpost). Schnelle Navigation kann
   *  mehrere Lade-Requests gleichzeitig in der Luft haben; eine ältere, langsamer auflösende
   *  Antwort darf das inzwischen geladene Puzzle nicht überschreiben → wird per Epoche verworfen. */
  private loadEpoch = 0;
  private retryFn: (() => void) | null = null;
  private bookSolveRecorded = false;     // Solve nur einmal je Puzzle-Durchgang melden (Tagespuzzle-Statistik)
  private bookChallengeResolved = false; // Challenge-Ergebnis nur mit dem ERSTEN gemeldeten Versuch aufloesen
  private courseAttemptRecorded = false; // pro Puzzle-Durchgang nur ein Kurs-Versuch melden (Zeit-Inflation vermeiden)
  /** Gesetzt, wenn dieses Buch-Puzzle aus einer Freundes-Challenge geöffnet wurde (?challengeId=…). */
  private challengeId: number | null = null;
  private challengeResolved = false;
  /** Direkt geteiltes Einzel-Puzzle (Teilen-Link `?single=1`): nach dem Lösen am Puzzle stehen
   *  bleiben statt automatisch weiterzuspringen, und keine Buch-Navigation anbieten. */
  singlePuzzle = false;
  /** „Track solves": für jedes direkt geteilte Einzel-Puzzle (= `singlePuzzle`) aktiv — Erstversuche
   *  der Besucher zählen + unter dem Puzzle anzeigen (solved/failed). Failed = alles inkl. Reset/Aufgeben;
   *  nur der erste Versuch zählt. */
  trackSolves = false;
  sharedCounts: SharedPuzzleCounts | null = null;
  private trackRecorded = false;

  // Zuletzt gelöstes Puzzle merken (überlebt den Auto-Advance) — analog Standard/Endless:
  // ermöglicht „Letztes Puzzle analysieren" + „Letztes teilen" im Share-Dialog.
  lastSolvedPuzzleId: number | null = null;
  private lastSolvedFen: string | null = null;
  private lastSolvedMoves = '';
  private lastSolvedOrientation: 'white' | 'black' = 'white';
  /** „Geliebtes Puzzle"-Zustand (Herz). In Wochenpost-Modus deaktiviert (keine echte Id). */
  readonly favoriteTracker: FavoriteTracker;

  get isLoggedIn(): boolean { return this.auth.isLoggedIn; }

  /** Anonymer (nicht eingeloggter) Nutzer in einem öffentlichen Kurs: der Kurs wird rein
   *  clientseitig aus den öffentlichen Puzzles bedient, Fortschritt bleibt lokal im Browser
   *  (keine auth-pflichtigen Kurs-Endpoints aufrufen). */
  get isAnonCourse(): boolean { return this.inCourse && !this.isLoggedIn; }

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
    private puzzleService: PuzzleService,
    stockfish: StockfishService,
    private prefs: PreferencesService,
    private route: ActivatedRoute,
    private dialog: MatDialog,
    private courseService: CourseService,
    private weeklyService: WeeklyService,
    private router: Router,
    private translate: TranslateService,
    private auth: AuthService,
    private snackbar: SnackbarService,
    private offlineQueue: OfflineQueueService,
    private challengeService: ChallengeService,
    private longSolve: LongSolveService,
    private favorites: FavoritesService
  ) {
    super(stockfish);
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

  sharePuzzle(): void {
    if (!this.puzzle) return;
    // „single=1" markiert den Link als direkt geteiltes Einzel-Puzzle → Empfänger bleibt nach
    // dem Lösen auf dem Puzzle stehen (kein Auto-Advance, keine Buch-Navigation).
    const url = `${window.location.origin}/puzzles/book/${this.puzzle.id}?single=1`;
    // Nach dem Auto-Advance kann zusätzlich das zuletzt gelöste Puzzle geteilt werden.
    const hasPrevious = this.lastSolvedPuzzleId != null && this.lastSolvedPuzzleId !== this.puzzle.id;
    const previousUrl = hasPrevious
      ? `${window.location.origin}/puzzles/book/${this.lastSolvedPuzzleId}?single=1`
      : undefined;
    this.dialog.open(SharePuzzleDialogComponent, {
      data: {
        url, previousUrl,
        puzzleId: this.puzzle.id,
        previousPuzzleId: hasPrevious ? (this.lastSolvedPuzzleId ?? undefined) : undefined,
        source: 'book',
        // Wochenpost-Puzzles haben keine dauerhafte ID → dort keine Challenge.
        canChallenge: this.isLoggedIn && !this.inWeekly,
      },
      width: '400px',
      maxWidth: '95vw',
    });
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

  /** Nächstes Puzzle aus demselben Buch laden (nur Standalone-Buch-Modus). */
  nextInBook(): void {
    if (!this.puzzle || this.bookNavLoading) return;
    if (!navigator.onLine) { this.navOfflineInBook(false); return; }
    this.bookNavLoading = true;
    this.puzzleService.getNextBookPuzzle(this.puzzle.id).subscribe({
      next: p => this.goToBookPuzzle(p),
      error: () => { this.bookNavLoading = false; }
    });
  }

  /** Zufälliges Puzzle aus demselben Buch laden (nur Standalone-Buch-Modus). */
  randomInBook(): void {
    if (!this.puzzle || this.bookNavLoading) return;
    if (!navigator.onLine) { this.navOfflineInBook(true); return; }
    this.bookNavLoading = true;
    this.puzzleService.getRandomBookPuzzle(this.puzzle.id).subscribe({
      next: p => this.goToBookPuzzle(p),
      error: () => { this.bookNavLoading = false; }
    });
  }

  /** Offline: nächstes/zufälliges Puzzle aus dem lokal gespeicherten Buch. */
  private navOfflineInBook(random: boolean): void {
    if (!this.puzzle) return;
    const book = getBookOffline(this.puzzle.bookFileName);
    if (!book || !book.length) {
      this.snackbar.info(this.translate.instant('book.offlineUnavailable'), { action: 'common.ok', duration: 2500 });
      return;
    }
    let next: BookPuzzleDto;
    if (random) {
      const others = book.filter(p => p.id !== this.puzzle!.id);
      next = (others.length ? others : book)[Math.floor(Math.random() * (others.length || book.length))];
    } else {
      const i = book.findIndex(p => p.id === this.puzzle!.id);
      next = book[((i < 0 ? -1 : i) + 1 + book.length) % book.length];   // Loop am Ende
    }
    this.goToBookPuzzle(next);
  }

  private goToBookPuzzle(p: BookPuzzleDto): void {
    ++this.loadEpoch;   // evtl. noch laufenden Ladevorgang entwerten
    this.bookNavLoading = false;
    this.clearSolutionPlay();
    this.router.navigate(['/puzzles/book', p.id]);   // URL aktualisieren (Komponente wird wiederverwendet)
    this.puzzle = p;
    this.setupPuzzle(p);
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
    // Tagespuzzle: früher verbrachte (aktive) Zeit fortführen — wer den Link schließt und später
    // wieder öffnet, zählt kumuliert weiter statt bei 0 (fair für die „schnellster Löser"-Wertung).
    this.startTimer(this.isDaily && this.dailyDate ? loadDailyElapsed(this.dailyDate) : 0);
  }

  /** Zwischenstand der Tagespuzzle-Lösezeit im Sekunden-Tick persistieren (für den Wiederbesuch). */
  protected override onTimerTick(): void {
    if (this.isDaily && this.dailyDate) saveDailyElapsed(this.dailyDate, this.elapsedSeconds);
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
    if (this.puzzle) {
      this.lastSolvedPuzzleId = this.puzzle.id;
      this.lastSolvedFen = this.puzzle.fen;
      this.lastSolvedMoves = this.puzzle.moves ?? '';
      this.lastSolvedOrientation = this.orientation;
      saveLastSolved('book', {
        id: this.puzzle.id, fen: this.puzzle.fen,
        moves: this.puzzle.moves ?? '', orientation: this.orientation,
      });
    }
    this.favoriteTracker.refresh();
    this.enterSolutionReview();
    this.solveAlternative = alternative;
    // Auffällig lange Lösezeit (Tab lag vermutlich offen) → nachfragen, bevor gewertet wird; der
    // Dialog ist modal und blockiert „Weiter" dahinter. Aufzeichnen + Auto-Advance erst danach.
    this.longSolve.resolve(this.elapsedSeconds).subscribe(seconds => {
      this.solveSeconds = seconds;
      this.finalizeSolve();
    });
  }

  /** Aufzeichnung + Auto-Advance nach dem Lösen (ggf. nach der Lange-Lösezeit-Nachfrage). */
  private finalizeSolve(): void {
    this.recordCourseAttempt(true);
    this.recordWeeklyAttempt(true);
    this.recordBookAttempt(true);
    this.recordTrack(true);   // „Track solves": Erstversuch gelöst
    // Bei alternativer (eigener) Lösung NICHT automatisch weiterspringen — wie im Endless-Modus:
    // der Spieler entscheidet selbst (Weiter / Originallösung zeigen).
    if (this.solveAlternative) return;
    // Direkt geteiltes Einzel-Puzzle: am Ende stehen bleiben, kein Auto-Advance.
    if (this.singlePuzzle) return;
    // Steht nach dem letzten Lösungszug noch ein Abschlusstext (Kommentar NACH dem Zug), NICHT
    // automatisch weiterspringen — der Spieler soll ihn lesen und selbst „Weiter" klicken. Der
    // Kommentar wird über displayComment angezeigt (enterSolutionReview springt ans Ende + setzt ihn).
    if (this.hasTrailingSolutionComment) return;
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
    if (this.inCourse) this.courseNext();
    else if (this.inWeekly) this.weeklyNext();
    else this.nextInBook();
  }

  // ===== Tagespuzzle-Navigation (Datum statt Buch) =====
  get isDaily(): boolean { return this.dailyDate != null; }
  /** „Vor" nur erlaubt, solange das angezeigte Datum vor heute (UTC) liegt — keine Zukunft. */
  get canGoForwardDaily(): boolean { return !!this.dailyDate && this.dailyDate < this.todayUtc(); }

  prevDaily(): void { this.goDaily(-1); }
  nextDaily(): void { if (this.canGoForwardDaily) this.goDaily(1); }

  private goDaily(delta: number): void {
    if (!this.dailyDate) return;
    this.router.navigate(['/puzzles/daily', shiftDailyDate(this.dailyDate, delta)]);
  }

  private todayUtc(): string { return formatUtcDate(new Date()); }

  protected override handleFailed(): void {
    this.state = 'FAILED';
    this.stopTimer();
    this.updateBoard();
    this.favoriteTracker.refresh();
    this.enterSolutionReview();
    this.solveSeconds = this.elapsedSeconds;
    this.recordCourseAttempt(false);
    this.recordWeeklyAttempt(false);
    this.recordBookAttempt(false);
    this.recordTrack(false);   // „Track solves": Erstversuch nicht gelöst (Fehlzug)
  }

  /**
   * Meldet einen Lösungsversuch ans Backend — nur im Standalone-Buch-Modus und nur eingeloggt
   * (Basis für die Tagespuzzle-Visualisierung auf Discord). Pro Puzzle nur einmal.
   */
  private recordBookAttempt(solved: boolean): void {
    if (!this.standalone || !this.puzzle) return;
    // Seit v0.309.0 wird JEDER Fehlversuch gemeldet (Fehlzug/Mouseslip/Restart → je ein rotes ✗
    // in der Löser-Anzeige); nur der Solve bleibt einmalig je Durchgang. Die Aufrufer stellen
    // sicher, dass ein Fehler nicht doppelt zählt (State-Guards in resetPuzzle/mouseslip).
    if (solved) {
      if (this.bookSolveRecorded) return;
      this.bookSolveRecorded = true;
    }
    // Ergebnis an eine ggf. offene Freundes-Challenge zurückmelden (nur Standalone; der ERSTE
    // gemeldete Versuch entscheidet — wie die bisherige Erstversuch-Semantik der Challenge).
    if (!this.bookChallengeResolved) {
      this.bookChallengeResolved = true;
      this.resolveChallengeIfNeeded(solved);
    }
    if (this.auth.isLoggedIn) {
      // Versuch wird erfasst → gemerkte Tagespuzzle-Zwischenzeit verfällt (Erstversuch ist gewertet).
      if (this.isDaily && this.dailyDate) clearDailyElapsed(this.dailyDate);
      const url = `/api/book-puzzles/${this.puzzle.id}/attempt`;
      const body = { solved, timeSeconds: this.solveSeconds, hintsUsed: this.maxHintLevel };
      if (!navigator.onLine) { this.offlineQueue.enqueue('POST', url, body); return; }
      this.puzzleService.recordBookAttempt(this.puzzle.id, solved, this.solveSeconds, this.maxHintLevel)
        .subscribe({ error: () => this.offlineQueue.enqueue('POST', url, body) });
    } else if (solved) {
      // Anonym (nicht eingeloggt): nur Solves zählen fürs Tagespuzzle mit (namenlos) — die
      // gemerkte Zwischenzeit kumuliert daher bis zum ersten SOLVE weiter.
      if (this.isDaily && this.dailyDate) clearDailyElapsed(this.dailyDate);
      const url = `/api/book-puzzles/${this.puzzle.id}/attempt/anonymous`;
      const body = { solved, timeSeconds: this.solveSeconds, sessionId: this.puzzleService.ensureSessionId() };
      if (!navigator.onLine) { this.offlineQueue.enqueue('POST', url, body); return; }
      this.puzzleService.recordBookAttemptAnonymous(this.puzzle.id, solved, this.solveSeconds)
        .subscribe({ error: () => this.offlineQueue.enqueue('POST', url, body) });
    }
  }

  /** „Track solves": meldet den ERSTEN Versuch des Besuchers (solved/failed) und aktualisiert die
   *  angezeigten Zähler. Nur einmal pro geöffnetem Puzzle (serverseitig zusätzlich erstversuch-dedupliziert).
   *  `solved=false` deckt Fehlzug, Aufgeben und Reset ab. */
  private recordTrack(solved: boolean): void {
    if (!this.trackSolves || this.trackRecorded || !this.puzzle) return;
    this.trackRecorded = true;
    // Genutzte Tipp-Stufe (0–3) mitschicken; serverseitig geklemmt. So zeigt der Zähler künftig auch,
    // wie viele Löser ohne/mit 1/2/3 Tipps gelöst haben.
    this.puzzleService.trackSharedAttempt(this.puzzle.id, solved, this.maxHintLevel).subscribe({
      next: c => this.sharedCounts = c,
      error: () => { this.trackRecorded = false; }   // bei Fehler erneut versuchbar
    });
  }

  ngOnInit(): void {
    // Optionale Anzeige-Overrides aus dem (geteilten) Link anwenden, BEVOR das Puzzle aufgebaut wird
    // (onSetupStart liest themeMode, der Solver-Setup liest visualizationMode). Gilt für alle Modi.
    this.applyShareViewOverrides();

    // „Letztes Puzzle" (analysieren / ♥ / teilen) über Navigation zu /analysis hinweg wiederherstellen —
    // ohne Persistenz wird `lastSolvedPuzzleId` beim Component-Destroy null und die Knöpfe verschwinden.
    const restored = loadLastSolved('book');
    if (restored) {
      this.lastSolvedPuzzleId = restored.id;
      this.lastSolvedFen = restored.fen;
      this.lastSolvedMoves = restored.moves;
      this.lastSolvedOrientation = restored.orientation;
      this.favoriteTracker.refresh();
    }

    const weeklyIdParam = this.route.snapshot.paramMap.get('weeklyId');
    if (weeklyIdParam) {
      this.inWeekly = true;
      this.weeklyId = Number(weeklyIdParam);
      this.loadWeekly();
      return;
    }

    const bookIdParam = this.route.snapshot.paramMap.get('bookId');
    const modeParam = this.route.snapshot.paramMap.get('mode');
    if (bookIdParam && modeParam) {
      this.inCourse = true;
      // Reaktiv auf die Kurs-Params hören → der Schnellwechsel zum verknüpften Kurs (Buch↔Workbook)
      // und Browser zurück/vor laden neu, ohne die Komponente neu aufzubauen (Snapshot würde nicht
      // erneut feuern). paramMap emittiert den aktuellen Stand synchron → Erst-Load wie bisher.
      this.courseSub = this.route.paramMap.subscribe(pm => {
        const bid = Number(pm.get('bookId'));
        if (!bid) return;
        this.courseBookId = bid;
        this.courseModeKind = pm.get('mode') === 'random' ? 'random' : 'sequential';
        const ch = pm.get('chapterIndex');
        this.courseChapterIndex = ch != null ? Number(ch) : null;
        // Anonym: lokal gemerkten Fortschritt DIESES Buchs übernehmen (übersteht Reload; ersetzt den
        // Set-Inhalt, damit beim Wechsel auf ein anderes Kurs-Buch keine fremden Ids stehen bleiben).
        if (this.isAnonCourse) this.offlineCourseSolvedIds = new Set(loadCourseLocalSolved(bid));
        this.loadCourseNext();
        this.autoCacheCourse();   // Kurs im Hintergrund offline vorhalten (ohne manuelles ☁)
        this.loadCourseLink();    // verknüpften Partner-Kurs für den Schnellwechsel laden
      });
      return;
    }

    if (this.route.snapshot.paramMap.has('date')) {
      // Reaktiv auf den :date-Param hören: prev/next-Daily (und Browser zurück/vor) wechseln
      // nur den Param, ohne die Komponente neu aufzubauen → Snapshot würde nicht neu laden.
      this.dailySub = this.route.paramMap.subscribe(pm => {
        const d = pm.get('date');
        if (d) this.loadDaily(d);
      });
      return;
    }

    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam) {
      // Aus einer Freundes-Challenge geöffnet (nur Standalone-Buch-Modus) → Ergebnis zurückmelden.
      const challengeParam = this.route.snapshot.queryParamMap.get('challengeId');
      if (challengeParam) this.challengeId = Number(challengeParam) || null;
      // Direkt geteiltes Einzel-Puzzle (Teilen-Link `?single=1`) → am Ende stehen bleiben, nicht
      // weiterspringen UND Erstversuche immer mitzählen + anzeigen (kein separates Opt-in mehr).
      this.singlePuzzle = this.route.snapshot.queryParamMap.get('single') === '1';
      this.trackSolves = this.singlePuzzle;
      if (this.trackSolves) {
        this.puzzleService.getSharedCounts(Number(idParam)).subscribe({ next: c => this.sharedCounts = c, error: () => {} });
      }
      this.loadPuzzle(Number(idParam));
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
    if (ov.visualization != null) this.visualizationMode = ov.visualization;
    this.anarchyForcedByUrl = !!ov.enPassantForced;   // Anarchy per URL: e.p. immer forciert (sonst folgt es der Einstellung)
    if (ov.crazyPieceMode) this.crazyPieceMode = ov.crazyPieceMode;   // ?anarchy=max+1 → Feld bestimmt Stil
  }

  /** Meldet das Ergebnis genau einmal an eine offene Buch-Challenge zurück (fire-and-forget). */
  private resolveChallengeIfNeeded(solved: boolean): void {
    if (this.challengeId == null || this.challengeResolved) return;
    this.challengeResolved = true;
    this.challengeService.resolve(this.challengeId, solved, this.solveSeconds).subscribe({ next: () => {}, error: () => {} });
  }

  /** Lädt den verknüpften Partner-Kurs (falls vorhanden) für den Schnellwechsel-Knopf. */
  private loadCourseLink(): void {
    this.linkedCourseBookId = null;
    this.linkedCourseName = null;
    if (this.courseBookId == null || this.isAnonCourse) return;   // Verknüpfung ist auth-only
    this.courseService.getLink(this.courseBookId).subscribe({
      next: l => { this.linkedCourseBookId = l.linkedBookId; this.linkedCourseName = l.linkedDisplayName; },
      error: () => {}
    });
  }

  /** Wechselt zum verknüpften Kurs (Buch↔Workbook) im selben Modus; die Param-Subscription lädt neu. */
  switchToLinkedCourse(): void {
    if (this.linkedCourseBookId == null) return;
    this.router.navigate(['/courses', this.linkedCourseBookId, this.courseModeKind]);
  }

  ngOnDestroy(): void {
    this.dailySub?.unsubscribe();
    this.courseSub?.unsubscribe();
    this.stopTimer();
    // Tagespuzzle mitten im Lauf verlassen → finalen Stand für den Wiederbesuch festhalten.
    // NUR bei offenem Lauf (isSolving): nach SOLVED/FAILED ist der Versuch erfasst und der Eintrag
    // gelöscht — ein Speichern hier würde ihn wieder aufleben lassen.
    if (this.isDaily && this.dailyDate && this.isSolving)
      saveDailyElapsed(this.dailyDate, this.elapsedSeconds);
    this.clearSolutionPlay();
    this.abortSolver();
    clearCrazyStyles();
    clearVisualizationHide();
  }

  // ===== Kursmodus =====
  get coursePercent(): number {
    return this.courseTotal > 0
      ? Math.round(100 * Math.min(this.courseSolved, this.courseTotal) / this.courseTotal)
      : 0;
  }

  /** Wirklich alle Kurs-Puzzles gelöst (nicht nur „Random-Pool leer"). Im Random-Modus signalisiert
   *  `courseCompleted` lediglich, dass der aktuelle Durchgang vorbei ist — gescheiterte/ungelöste
   *  Puzzles bleiben übrig, bis ein Reset den Pool wieder auffüllt. Erst wenn solved == total ist
   *  das Buch tatsächlich durch. */
  get courseFullyDone(): boolean {
    return this.courseTotal > 0 && this.courseSolved >= this.courseTotal;
  }

  /** Im Random-Modus übrige (nicht gelöste) Puzzles, wenn der Pool des aktuellen Durchgangs leer ist. */
  get courseRemaining(): number {
    return Math.max(0, this.courseTotal - this.courseSolved);
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
  private cmtCacheKey = '';
  private cmtCache: CommentSegment[][] = [];

  /** {@link commentLines} in klickbare Segmente zerlegt (Text + spielbare Zug-Chips). Gecacht je
   *  Kommentar-Inhalt + Puzzle, da die Auflösung nur von Puzzle-FEN/-Zügen abhängt (nicht vom Ply). */
  get commentBlocks(): CommentSegment[][] {
    const lines = this.commentLines;
    const key = (this.puzzle?.id ?? 0) + '|' + lines.join('');
    if (key !== this.cmtCacheKey) {
      const fen = this.puzzle?.fen ?? '';
      const ucis = (this.puzzle?.moves ?? '').split(' ').filter(m => m);
      this.cmtCache = fen ? lines.map(l => buildCommentSegments(l, fen, ucis)) : lines.map(l => [{ text: l }]);
      this.cmtCacheKey = key;
    }
    return this.cmtCache;
  }

  /** Spielt die angeklickte Variante bis zu diesem Zug als Brett-Vorschau (view-only). */
  previewVariationMove(seg: CommentSegment): void {
    if (!seg.fen || !seg.from || !seg.to) return;
    this.variationPreview = { fen: seg.fen, lastMove: [seg.from as Key, seg.to as Key] };
  }

  /** Vorschau beenden → zurück zur Puzzle-/Review-Stellung. */
  exitVariationPreview(): void { this.variationPreview = null; }

  /** Holt das nächste Puzzle des Kurses (sequential: after=, random: exclude=). */
  private loadCourseNext(after?: number, exclude?: number): void {
    if (this.courseBookId == null) return;
    const epoch = ++this.loadEpoch;
    const hadPuzzle = this.puzzle != null;
    this.loadError = false;
    this.retryFn = () => this.loadCourseNext(after, exclude);
    if (!hadPuzzle) this.state = 'LOADING';

    // Anonym (öffentlicher Kurs): rein clientseitig aus den öffentlichen Puzzles bedienen.
    if (this.isAnonCourse) { this.loadAnonCourseNext(after, exclude, hadPuzzle); return; }

    // Offline → direkt aus dem lokal gespeicherten Buch bedienen (kein Netz-Roundtrip).
    // Kein Cache vorhanden → Fehlerzustand (mit „Erneut versuchen") statt endlosem Spinner.
    if (!navigator.onLine) {
      if (!this.loadCourseOffline(after, exclude, hadPuzzle)) {
        this.state = 'LOADING'; this.loadError = true;
        this.snackbar.info(this.translate.instant('book.offlineUnavailable'), { action: 'common.ok', duration: 3500 });
      }
      return;
    }

    this.courseService.getNext(this.courseBookId, this.courseModeKind, after, exclude, this.courseChapterIndex ?? undefined).subscribe({
      next: res => {
        if (epoch !== this.loadEpoch) return;
        this.courseSolved = res.solvedCount;
        this.courseTotal = res.total;
        this.applyCourseStats(res.book, res.chapter, res.chapterName);
        if (res.completed || !res.puzzle) {
          this.courseCompleted = true;
          // Letztes Puzzle gerade gelöst: gelöstes Brett stehen lassen; sonst leeres Done-Panel.
          if (!hadPuzzle) { this.puzzle = null; this.state = 'COURSE_DONE'; }
          return;
        }
        this.courseCompleted = false;
        this.gaveUp = false;
        this.puzzle = res.puzzle;
        this.setupPuzzle(res.puzzle);
      },
      error: () => {
        if (epoch !== this.loadEpoch) return;
        // Netzfehler trotz onLine → letzter Versuch aus dem Offline-Cache.
        if (this.loadCourseOffline(after, exclude, hadPuzzle)) return;
        this.state = 'LOADING'; this.loadError = true;
      }
    });
  }

  /**
   * Anonymer öffentlicher Kurs: die öffentlichen Puzzles einmal laden + lokal vorhalten, dann rein
   * clientseitig (wie offline) bedienen. Fortschritt lebt in {@link offlineCourseSolvedIds} und wird
   * pro Buch im localStorage persistiert. Nicht öffentlich / kein Cache → „nicht verfügbar".
   */
  private loadAnonCourseNext(after: number | undefined, exclude: number | undefined, hadPuzzle: boolean): void {
    if (this.courseBookId == null) return;
    if (getBookOfflineByBookId(this.courseBookId)?.length) {
      if (!this.loadCourseOffline(after, exclude, hadPuzzle)) this.showCourseUnavailable();
      return;
    }
    // Kein lokaler Cache + offline → nicht ins Netz laufen, sondern „nicht verfügbar" zeigen.
    if (typeof navigator !== 'undefined' && !navigator.onLine) { this.showCourseUnavailable(); return; }
    // Seitenweise laden: erste (kleine) Seite → sofort spielbar; restliche Seiten im Hintergrund in
    // den Offline-Cache nachladen. Große öffentliche Kurse (Tausende Puzzles) starten so in ~1 s
    // statt erst nach dem kompletten (mehrere MB großen) Download.
    const bookId = this.courseBookId;
    const acc: BookPuzzleDto[] = [];
    const fetchPage = (skip: number, first: boolean): void => {
      this.courseService.getPublicCourse(bookId, skip, ANON_COURSE_PAGE_SIZE).subscribe({
        next: page => {
          if (this.courseBookId !== bookId) return;   // zwischenzeitlich weitergewechselt
          if (page?.length) {
            acc.push(...page);
            const fileName = acc[0]?.bookFileName;
            if (fileName) saveBookOffline(fileName, acc, bookId);   // wachsender Cache
          }
          if (first) {
            if (acc.length) { this.loadCourseOffline(after, exclude, hadPuzzle); }
            else { this.showCourseUnavailable(); return; }
          }
          // Volle Seite → es gibt vermutlich mehr: nächste Seite im Hintergrund.
          if (page && page.length === ANON_COURSE_PAGE_SIZE) fetchPage(skip + ANON_COURSE_PAGE_SIZE, false);
        },
        error: () => {
          if (this.courseBookId !== bookId) return;
          // Nur wenn schon die erste Seite scheitert (nichts geladen) → „nicht verfügbar".
          // Ein Fehler bei einer Folgeseite lässt das bereits Geladene spielbar.
          if (first && !acc.length) this.showCourseUnavailable();
        },
      });
    };
    fetchPage(0, true);
  }

  /** Öffentlicher Kurs anonym nicht (mehr) verfügbar (nicht public / gelöscht). */
  private showCourseUnavailable(): void {
    this.state = 'LOADING';
    this.loadError = true;
    this.snackbar.info(this.translate.instant('book.course.unavailable'), { action: 'common.ok', duration: 4000 });
  }

  /**
   * Offline-Fallback fürs Kurs-Laden: serviert Puzzles aus dem lokal gespeicherten Buch
   * (über die bookId aufgelöst). Versuche werden via Offline-Queue nachgemeldet; der
   * Server-Fortschritt („nächstes ungelöstes") lässt sich offline nicht exakt nachbilden,
   * darum meiden wir nur die in DIESER Sitzung schon gelösten Puzzles. Liefert true, wenn
   * ein Puzzle (oder das „fertig"-Panel) gesetzt wurde, sonst false (kein Cache vorhanden).
   */
  private loadCourseOffline(after: number | undefined, exclude: number | undefined, hadPuzzle: boolean): boolean {
    if (this.courseBookId == null) return false;
    const book = getBookOfflineByBookId(this.courseBookId);
    if (!book || !book.length) return false;

    // Fortschritt/Zähler nur über echte Quiz-Linien (Info-/Erklärlinien zählen nicht) — wie serverseitig.
    const quiz = book.filter(p => !p.isInfoOnly);
    this.courseTotal = quiz.length;
    this.courseSolved = quiz.filter(p => this.offlineCourseSolvedIds.has(p.id)).length;
    const cur = after ?? exclude;
    let next: BookPuzzleDto | undefined;

    if (this.courseModeKind === 'random') {
      const fresh = book.filter(p => p.id !== cur && !this.offlineCourseSolvedIds.has(p.id));
      const pick = fresh.length ? fresh : book.filter(p => p.id !== cur);
      next = (pick.length ? pick : book)[Math.floor(Math.random() * (pick.length || book.length))];
    } else {
      // sequenziell: ab Position nach `after` das erste in dieser Sitzung noch nicht gelöste.
      const start = after != null ? book.findIndex(p => p.id === after) : -1;
      for (let j = 1; j <= book.length; j++) {
        const cand = book[(Math.max(start, -1) + j + book.length) % book.length];
        if (!this.offlineCourseSolvedIds.has(cand.id)) { next = cand; break; }
      }
    }

    if (!next) {
      // Alle (lokal) gelöst → fertig-Panel.
      this.courseCompleted = true;
      if (!hadPuzzle) { this.puzzle = null; this.state = 'COURSE_DONE'; }
      return true;
    }
    this.courseCompleted = false;
    this.gaveUp = false;
    this.loadError = false;
    this.puzzle = next;
    this.setupPuzzle(next);
    return true;
  }

  /**
   * Lädt beim Öffnen eines Kurses (online) im Hintergrund alle Puzzles des Buchs und legt sie
   * offline ab — damit der Kurs offline weiterläuft, ohne dass man vorher manuell auf ☁ tippen
   * muss. Fire-and-forget, nur online, idempotent (überspringt, wenn schon gecacht).
   */
  private autoCacheCourse(): void {
    if (this.courseBookId == null || this.isAnonCourse) return;   // anonym cacht loadAnonCourseNext über den public-Endpoint
    if (typeof navigator !== 'undefined' && !navigator.onLine) return;
    if (getBookOfflineByBookId(this.courseBookId)?.length) return;   // schon offline vorhanden
    const bookId = this.courseBookId;
    this.courseService.getBookPuzzles(bookId).subscribe({
      next: puzzles => {
        const fileName = puzzles?.[0]?.bookFileName;
        if (fileName && puzzles.length) saveBookOffline(fileName, puzzles, bookId);
      },
      error: () => { /* offline/Fehler: ignorieren, ☁ bleibt als manueller Weg */ },
    });
  }

  /** „Nächstes Puzzle" / „Überspringen": eins weiter im jeweiligen Modus. */
  courseNext(): void {
    const cur = this.puzzle?.id;
    // Sequenziell durchgeklickte Info-/Erklärlinie merken → beim nächsten Wiedereinstieg wird sie
    // übersprungen (der Kurs setzt dahinter fort statt sie erneut zu zeigen).
    if (cur != null && this.puzzle?.isInfoOnly && this.inCourse && this.courseBookId != null) {
      this.offlineCourseSolvedIds.add(cur);   // (offline/anonym) dieselbe Info-Linie nicht erneut zeigen
      if (this.isAnonCourse) {
        saveCourseLocalSolved(this.courseBookId, this.offlineCourseSolvedIds);   // anonym: lokal persistieren
      } else if (typeof navigator === 'undefined' || navigator.onLine) {
        this.courseService.markInfoSeen(this.courseBookId, cur).subscribe({ next: () => {}, error: () => {} });
      }
    }
    if (this.courseModeKind === 'random') this.loadCourseNext(undefined, cur);
    else this.loadCourseNext(cur, undefined);
  }

  retryLoad(): void {
    this.loadError = false;
    this.retryFn?.();
  }

  backToCourses(): void {
    this.router.navigate(['/courses']);
  }

  /** Kurs zurücksetzen und von vorn beginnen — bringt im Random-Modus auch die falsch gelösten
   * Puzzles wieder in den Pool. Direkt vom „abgeschlossen"-Panel aus erreichbar. */
  restartCourse(): void {
    if (this.courseBookId == null) return;
    // Anonym: nur den lokalen Fortschritt verwerfen, kein Server-Reset.
    if (this.isAnonCourse) {
      this.offlineCourseSolvedIds.clear();
      clearCourseLocalSolved(this.courseBookId);
      this.courseCompleted = false;
      this.snackbar.success(this.translate.instant('book.course.restarted'), { duration: 2500 });
      this.loadCourseNext();
      return;
    }
    this.courseService.reset(this.courseBookId).subscribe({
      next: () => {
        this.courseCompleted = false;
        this.snackbar.success(this.translate.instant('book.course.restarted'), { duration: 2500 });
        this.loadCourseNext();
      },
      error: () => this.snackbar.warn(this.translate.instant('book.course.restartFailed'), { duration: 3000 }),
    });
  }

  // ===== Wochenpost-Modus =====
  get weeklyTotal(): number { return this.weeklyPuzzles.length; }
  get weeklyDisplayIndex(): number {
    return this.weeklyTotal === 0 ? 0 : Math.min(this.weeklyIndex + 1, this.weeklyTotal);
  }
  get weeklyPercent(): number {
    if (this.weeklyTotal === 0) return 0;
    return this.weeklyCompleted ? 100 : Math.round(100 * this.weeklyIndex / this.weeklyTotal);
  }
  /** Gesamtzeit über alle gespielten Puzzles als m:ss bzw. h:mm:ss. */
  get weeklyTimeDisplay(): string { return formatSecondsClock(this.weeklySeconds); }

  private loadWeekly(): void {
    if (this.weeklyId == null) return;
    const epoch = ++this.loadEpoch;
    this.state = 'LOADING';
    this.weeklyService.getPlay(this.weeklyId).subscribe({
      next: play => {
        if (epoch !== this.loadEpoch) return;
        this.weeklyTitle = play.title;
        this.weeklyPuzzles = play.puzzles ?? [];
        this.weeklyIndex = 0;
        if (this.weeklyPuzzles.length === 0) {
          this.weeklyCompleted = false;
          this.puzzle = null;
          this.state = 'COURSE_DONE';   // Done-Panel; weekly-Card zeigt „keine Puzzles"
          return;
        }
        // Fortschritt laden → Anzeige + Einstieg beim ersten NEUEN Puzzle (bei 100% von vorne).
        if (this.auth.isLoggedIn && this.weeklyId != null) {
          this.weeklyService.getProgress(this.weeklyId).subscribe({
            next: p => {
              if (epoch !== this.loadEpoch) return;
              this.weeklyPlayed = p.playedCount;
              this.weeklySolved = p.solvedCount;
              this.weeklySeconds = p.totalSeconds;
              this.loadWeeklyAt(computeWeeklyStartIndex(this.weeklyPuzzles, p));
            },
            error: () => { if (epoch === this.loadEpoch) this.loadWeeklyAt(0); },
          });
        } else {
          this.loadWeeklyAt(0);
        }
      },
      error: () => { if (epoch !== this.loadEpoch) return; this.state = 'COURSE_DONE'; this.puzzle = null; }
    });
  }

  private loadWeeklyAt(index: number): void {
    if (index >= this.weeklyPuzzles.length) {
      this.weeklyCompleted = true;
      // letztes Puzzle bleibt sichtbar (state SOLVED) — Card zeigt „abgeschlossen"
      return;
    }
    this.weeklyCompleted = false;
    this.gaveUp = false;
    this.weeklyAttemptRecorded = false;   // neues Puzzle → wieder aufzeichenbar
    this.weeklyIndex = index;
    this.puzzle = this.weeklyPuzzles[index];
    this.setupPuzzle(this.puzzle);
  }

  /**
   * Zeichnet das aktuelle Wochenpost-Puzzle als gespielt auf (gelöst oder nicht), pro Puzzle einmal.
   * Offline-fähig via Queue; nur eingeloggt (die Route ist authGuard — defensiv geprüft).
   */
  private recordWeeklyAttempt(solved: boolean): void {
    if (!this.inWeekly || this.weeklyId == null || !this.puzzle || this.weeklyAttemptRecorded) return;
    if (!this.auth.isLoggedIn) return;
    this.weeklyAttemptRecorded = true;
    const puzzleIndex = this.puzzle.id;   // = Parser-Index der Wochenpost-Sequenz
    const wrongAttempts = this.wrongMoveCount;
    const mouseslips = this.mouseslipUsed ? 1 : 0;
    const url = `/api/weekly-posts/${this.weeklyId}/attempt`;
    const body = { puzzleIndex, solved, timeSeconds: this.solveSeconds, hintsUsed: this.maxHintLevel, wrongAttempts, mouseslips };
    if (!navigator.onLine) {
      this.offlineQueue.enqueue('POST', url, body);
      this.weeklyPlayed = Math.min(this.weeklyPlayed + 1, this.weeklyTotal || this.weeklyPlayed + 1);
      if (solved) this.weeklySolved += 1;
      this.weeklySeconds += this.solveSeconds;
      return;
    }
    this.weeklyService.recordAttempt(this.weeklyId, puzzleIndex, solved, this.solveSeconds, this.maxHintLevel, wrongAttempts, mouseslips).subscribe({
      next: p => { this.weeklyPlayed = p.playedCount; this.weeklySolved = p.solvedCount; this.weeklySeconds = p.totalSeconds; },
      error: () => this.offlineQueue.enqueue('POST', url, body),
    });
  }

  weeklyNext(): void {
    this.loadWeeklyAt(this.weeklyIndex + 1);
  }

  backToWeekly(): void {
    this.router.navigate(['/weekly']);
  }

  /** Meldet jeden Kurs-Versuch (gelöst/fehlgeschlagen) ans Backend — Grundlage für die akkumulierte
   *  Kurs-/Studienzeit im Trainingsziele-Tracker. Pro Puzzle-Durchgang nur einmal (Zeit-Inflation
   *  durch Online+Offline-Retry vermeiden); Fortschritt wird nur bei `solved` hochgezählt. */
  private recordCourseAttempt(solved: boolean): void {
    if (!this.inCourse || this.courseAttemptRecorded || this.courseBookId == null || !this.puzzle) return;
    this.courseAttemptRecorded = true;

    // Anonym: Fortschritt nur lokal (localStorage). Kein Server-Call/Queue — Kurs-Endpoints sind
    // auth-pflichtig, und anonyme Versuche zählen bewusst nicht in Bestenlisten/Trainingsziele.
    if (this.isAnonCourse) {
      if (solved && !this.offlineCourseSolvedIds.has(this.puzzle.id)) {
        this.offlineCourseSolvedIds.add(this.puzzle.id);
        saveCourseLocalSolved(this.courseBookId, this.offlineCourseSolvedIds);
        this.courseSolved = Math.min(this.courseSolved + 1, this.courseTotal || this.courseSolved + 1);
      }
      return;
    }

    const url = `/api/courses/${this.courseBookId}/results`;
    const body = { bookPuzzleId: this.puzzle.id, solved, mode: this.courseModeKind, timeSeconds: this.solveSeconds, chapterIndex: this.courseChapterIndex ?? undefined, hintsUsed: this.maxHintLevel };
    if (!navigator.onLine) {
      // Offline → Server-Aufzeichnung vormerken; bei Solve zusätzlich lokalen Fortschritt hochzählen.
      this.offlineQueue.enqueue('POST', url, body);
      if (solved) {
        this.offlineCourseSolvedIds.add(this.puzzle.id);   // beim nächsten Laden überspringen
        this.courseSolved = Math.min(this.courseSolved + 1, this.courseTotal || this.courseSolved + 1);
      }
      return;
    }
    this.courseService.recordResult(this.courseBookId, this.puzzle.id, solved, this.courseModeKind, this.solveSeconds, this.courseChapterIndex ?? undefined, this.maxHintLevel).subscribe({
      next: p => { this.courseSolved = p.solvedCount; this.courseTotal = p.total; this.applyCourseStats(p.book, p.chapter, p.chapterName); },
      error: () => this.offlineQueue.enqueue('POST', url, body),
    });
  }

  /** Übernimmt die vom Backend gelieferte Buch-/Kapitel-Statistik (Fortschritt/Zeit/Trefferquote). */
  private applyCourseStats(book?: CourseScopeStats | null, chapter?: CourseScopeStats | null, chapterName?: string | null): void {
    this.courseBookStats = book ?? null;
    this.courseChapterStats = chapter ?? null;
    this.courseChapterName = chapterName ?? null;
  }

  /** mm:ss bzw. h:mm:ss aus Sekunden (für die Kurs-Zeitanzeige). */
  courseTime(seconds: number): string { return formatSecondsClock(seconds); }

  /** Geschätzte Restzeit für das ganze Buch/Kapitel bei bisherigem Tempo (z. B. „~1 h 20 min").
   *  `null`, solange nichts abschätzbar ist (nichts versucht bzw. alles gelöst). */
  courseEta(stats: CourseScopeStats): string | null {
    const secs = estimateRemainingSeconds(stats);
    return secs == null ? null : formatEtaShort(secs);
  }

  /** Tagespuzzle eines Datums laden (Route /puzzles/daily/:date) — danach wie ein Buch-Puzzle. */
  private loadDaily(dateParam: string): void {
    const date = dateParam === 'today' ? this.todayUtc() : dateParam;
    this.dailyDate = date;
    this.loadError = false;
    this.retryFn = () => this.loadDaily(dateParam);
    this.state = 'LOADING';
    this.stopTimer();
    this.elapsedSeconds = 0;
    this.alternativeSolve = false;
    this.gaveUp = false;
    const epoch = ++this.loadEpoch;

    // Offline → aus dem auto-gecachten Tagespuzzle bedienen (kein endloser Spinner).
    if (!navigator.onLine) {
      const cached = getDailyOffline(date);
      if (cached) { this.puzzle = cached; this.setupPuzzle(cached); return; }
    }

    this.puzzleService.getDailyPuzzle(date).subscribe({
      next: puzzle => {
        if (epoch !== this.loadEpoch) return;
        this.puzzle = puzzle;
        saveDailyOffline(date, puzzle);   // automatisch für Offline vorhalten
        this.setupPuzzle(puzzle);
      },
      error: () => {
        if (epoch !== this.loadEpoch) return;
        // Netzfehler trotz onLine → letzter Versuch aus dem Offline-Cache.
        const cached = getDailyOffline(date);
        if (cached) { this.puzzle = cached; this.setupPuzzle(cached); return; }
        this.state = 'LOADING';
        this.puzzle = null;
        this.loadError = true;
      }
    });
  }

  private loadPuzzle(id: number): void {
    const epoch = ++this.loadEpoch;
    this.loadError = false;
    this.retryFn = () => this.loadPuzzle(id);
    this.state = 'LOADING';
    this.stopTimer();
    this.elapsedSeconds = 0;
    this.alternativeSolve = false;
    this.gaveUp = false;

    // Offline: aus einem lokal gespeicherten Buch bedienen.
    if (!navigator.onLine) {
      const cached = findCachedBookPuzzle(id);
      if (cached) { this.puzzle = cached; this.setupPuzzle(cached); return; }
    }

    this.puzzleService.getBookPuzzleById(id).subscribe({
      next: puzzle => {
        if (epoch !== this.loadEpoch) return;
        this.puzzle = puzzle;
        this.setupPuzzle(puzzle);
      },
      error: () => {
        if (epoch !== this.loadEpoch) return;
        this.state = 'LOADING';
        this.puzzle = null;
        this.loadError = true;
      }
    });
  }

  private setupPuzzle(puzzle: BookPuzzleDto): void {
    this.clearSolutionPlay();
    this.variationPreview = null;   // etwaige Kommentar-Varianten-Vorschau aus dem Vorgänger-Puzzle beenden
    this.bookSolveRecorded = false;
    this.bookChallengeResolved = false;
    this.courseAttemptRecorded = false;
    this.hintLevel = 0;
    // Tipps werden pro erwartetem Zug on-the-fly aus der aktuellen Stellung erzeugt (currentMoveHint) —
    // kein vorab-klassifizierter erster Zug mehr nötig.
    this.reviewMode = false;
    this.solutionReview = false;
    this.moveComment = null;
    this.reviewShapes = [];
    this.moveShapesByPly = parseMoveShapes(puzzle.moveShapes);
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
    this.chess = new Chess(puzzle.fen);
    this.orientation = this.chess.turn() === 'w' ? 'white' : 'black';
    this.reviewGoTo(0);   // setzt boardFen/turnColor/Kommentar für die Ausgangsstellung
  }

  /** Zug aufs Brett anwenden ohne lastMove-Highlight (Vorspiel/Review-Aufbau). */
  private applyUci(uci: string): void {
    applyUci(this.chess, uci);
  }

  giveUp(): void {
    if (!this.puzzle) return;
    this.abortSolver();
    this.stopTimer();
    this.gaveUp = true;
    this.state = 'FAILED';
    this.solveSeconds = this.elapsedSeconds;
    this.recordCourseAttempt(false);   // Aufgeben = Versuch mit verbrachter Zeit (zählt fürs Trainingsziel)
    this.recordWeeklyAttempt(false);   // Aufgeben zählt im Wochenpost als ✗ (gespielt, nicht gelöst)
    this.recordBookAttempt(false);
    this.recordTrack(false);   // „Track solves": Aufgeben zählt als failed
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
    if (this.inCourse) this.courseNext();
    else if (this.inWeekly) this.weeklyNext();
    else this.nextInBook();
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
    const allMoves = this.puzzle.moves.split(' ').filter(m => m);
    const start = Math.max(0, this.startPly);
    const solutionMoves = allMoves.slice(start);
    index = Math.max(0, Math.min(index, solutionMoves.length));
    this.reviewIndex = index;
    this.chess = new Chess(this.puzzle.fen);
    // Vorspiel still aufs Brett
    for (let i = 0; i < start; i++) this.applyUci(allMoves[i]);
    // Loesungszuege bis index
    let last: [Key, Key] | undefined;
    for (let i = 0; i < index; i++) {
      this.applyUci(solutionMoves[i]);
      last = [solutionMoves[i].substring(0, 2) as Key, solutionMoves[i].substring(2, 4) as Key];
    }
    this.lastMove = last;
    this.boardFen = this.chess.fen();
    this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
    this.isCheck = this.chess.isCheck();
    this.dests = new Map();
    // Lösungs-Review: index = Anzahl Lösungszüge ab Trainingsstart; absoluter Halbzug = start + index.
    // Fällt der aktuelle Zug ohne Kommentar aus, den zuletzt vorher gesehenen behalten (statt zurück
    // auf die Einleitung zu fallen — das wirkte für den User wie ein „Sprung nach hinten").
    this.moveComment = latestCommentUpToUtil(this.puzzle.moveComments, start, start + index - 1);
    this.reviewShapes = this.shapesForPlyPlayed(start + index - 1);
  }

  /** Board-Annotationen (Pfeile/Feld-Markierungen) zum zuletzt gespielten Halbzug (gleiche Ply-Konvention). */
  private shapesForPlyPlayed(plyPlayed: number): DrawShape[] {
    return this.moveShapesByPly[plyPlayed] ?? [];
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
    if (!this.puzzle) return 0;
    const allMoves = this.puzzle.moves.split(' ').filter(m => m);
    return this.solutionReview ? allMoves.length - Math.max(0, this.startPly) : allMoves.length;
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
    const moves = this.puzzle.moves.split(' ').filter(m => m);
    index = Math.max(0, Math.min(index, moves.length));
    this.reviewIndex = index;
    this.chess = new Chess(this.puzzle.fen);
    let last: [Key, Key] | undefined;
    for (let i = 0; i < index; i++) {
      this.applyUci(moves[i]);
      last = [moves[i].substring(0, 2) as Key, moves[i].substring(2, 4) as Key];
    }
    this.lastMove = last;
    this.boardFen = this.chess.fen();
    this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
    this.isCheck = this.chess.isCheck();
    this.dests = new Map();
    // Ganze-Partie-Review: index = Anzahl gespielter Halbzüge ab FEN; letzter Zug = moves[index-1].
    // Bei Zügen ohne Kommentar den zuletzt gesehenen behalten (statt bis zur Einleitung zurückzufallen).
    this.moveComment = latestCommentUpToUtil(this.puzzle.moveComments, 0, index - 1);
    this.reviewShapes = this.shapesForPlyPlayed(index - 1);
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
    const t = e.target as HTMLElement | null;
    if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
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
    if (this.inWeekly) { this.weeklyNext(); return true; }
    if (this.inCourse) { this.courseNext(); return true; }
    if (this.browseInBook) { if (!this.bookNavLoading) this.nextInBook(); return true; }
    return false;
  }

  resetPuzzle(): void {
    if (!this.puzzle) return;
    // Daily: ein Reset mitten im Lauf (mind. ein Zug gespielt) zählt als Fehlversuch (rotes ✗).
    // Nach FAILED/SOLVED nicht erneut melden — der Fehlzug bzw. Solve wurde bereits erfasst.
    if (this.isDaily && this.moveLog.length > 0 && this.state !== 'FAILED' && this.state !== 'SOLVED')
      this.recordBookAttempt(false);
    // Wochenpost: Reset nach mind. einem Zug zählt als ✗ (gespielt, nicht gelöst).
    if (this.inWeekly && this.moveLog.length > 0) this.recordWeeklyAttempt(false);
    // „Track solves": Reset zählt als failed (alles mit Reset gilt als nicht gelöst).
    this.recordTrack(false);
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.setupPuzzle(this.puzzle);
  }

  override mouseslip(): void {
    // Im Tagespuzzle ist der „Mausrutscher" kein straffreier Undo: jede Off-Path-Zurücknahme
    // zählt als Fehlversuch (rotes ✗). Nach FAILED nicht erneut melden (Fehlzug schon erfasst).
    if (this.isDaily && !this.onSolutionPath && this.state !== 'FAILED') this.recordBookAttempt(false);
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
    if (this.puzzle) this.setupPuzzle(this.puzzle);  // Modus-Wechsel = Puzzle neu starten
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
      this.setVisualizationLevel(result.visualizationMode);
      this.setVizArrowEnabled(result.vizArrowEnabled);
      if (result.stockfishDepth !== undefined) {
        this.stockfishDepth = result.stockfishDepth;
        this.prefs.setBookStockfishDepth(this.stockfishDepth);
      }
      if (result.offPathWarnMoves !== undefined) this.prefs.setOffPathWarnMoves(result.offPathWarnMoves);
      if (result.enPassantForced !== undefined) {
        this.prefs.setEnPassantForced(result.enPassantForced);
        this.enPassantForced = this.themeMode === 'crazy' && result.enPassantForced;
      }
    });
  }
}
