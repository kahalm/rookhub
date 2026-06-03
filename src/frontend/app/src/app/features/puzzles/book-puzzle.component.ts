import { Component, OnInit, OnDestroy, ViewChild, ElementRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, Router } from '@angular/router';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { ReviewNavComponent } from './review-nav.component';
import { VizCardComponent } from './viz-card.component';
import { ThemePickerComponent } from './theme-picker.component';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleService, BookPuzzleDto } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { PreferencesService } from '../../core/preferences.service';
import { BOARD_THEMES, PIECE_SETS, ThemeMode, applyThemeMode, clearCrazyStyles, clearVisualizationHide } from './board-theme.util';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { applyUci } from './puzzle-move.util';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { CourseService, CourseMode } from '../courses/course.service';
import { AuthService } from '../../core/auth.service';
import { getBookOffline, findCachedBookPuzzle } from './book-offline.util';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { WeeklyService } from '../weekly/weekly.service';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

type BookPuzzleState = 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE' | 'THINKING' | 'PLAYING' | 'SOLVED' | 'FAILED' | 'COURSE_DONE';

@Component({
  selector: 'app-book-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, MatProgressBarModule, MatChipsModule, MatInputModule, MatFormFieldModule,
    MatTooltipModule, MatDialogModule, PuzzleBoardComponent, ReviewNavComponent, VizCardComponent, ThemePickerComponent, TranslateModule
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
  courseSolved = 0;
  courseTotal = 0;
  courseCompleted = false;

  // Wochenpost-Modus: /weekly/:weeklyId — Puzzles eines Posts sequenziell durchspielen
  // (kein Leben, beliebige Retrys, keine Fortschritts-Speicherung; client-seitige Liste).
  inWeekly = false;
  weeklyId: number | null = null;
  weeklyTitle = '';
  weeklyPuzzles: BookPuzzleDto[] = [];
  weeklyIndex = 0;
  weeklyCompleted = false;

  stockfishDepth = 16;
  boardTheme = 'brown';
  readonly boardThemes = BOARD_THEMES;

  pieceSet = 'cburnett';
  themeMode: ThemeMode = 'fixed';
  @ViewChild('settingsPanel', { read: ElementRef }) settingsPanel?: ElementRef<HTMLElement>;
  readonly pieceSets = PIECE_SETS;

  elapsedSeconds = 0;
  private timerInterval?: ReturnType<typeof setInterval>;
  private startTime = 0;

  /** True nach Give Up. Status-Panel zeigt einen Hinweis statt "Your turn!". */
  gaveUp = false;

  // Review-Modus „Ganze Partie" vs. Lösungs-Step-Through (komponentenspezifisch;
  // reviewMode/reviewIndex in BasePuzzleSolver).
  solutionReview = false;

  /** Standalone-Buch-Puzzle (/puzzles/book/:id) — nicht Kurs-/Wochenpost-Kontext. */
  get standalone(): boolean { return !this.inCourse && !this.inWeekly; }
  bookNavLoading = false;
  private bookAttemptRecorded = false;   // pro Puzzle nur ein Versuch melden (Tagespuzzle-Statistik)

  get displayBookName(): string {
    if (!this.puzzle) return '';
    return this.puzzle.bookFileName.replace(/_firstkey\.pgn$/, '').replace(/_/g, ' ');
  }

  override get vizLevelDescription(): string {
    switch (this.visualizationMode) {
      case 0: return this.translate.instant('book.viz.level0');
      case 1: return this.translate.instant('book.viz.level1');
      case 2: return this.translate.instant('book.viz.level2');
      case 3: return this.translate.instant('book.viz.level3');
      case 4: return this.translate.instant('book.viz.level4');
      default: return '';
    }
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
    private offlineQueue: OfflineQueueService
  ) {
    super(stockfish);
    this.loadConfig();
    this.loadSettingsOpen();
    this.stockfish.init().catch(() => {});
  }

  sharePuzzle(): void {
    if (!this.puzzle) return;
    const url = `${window.location.origin}/puzzles/book/${this.puzzle.id}`;
    this.dialog.open(SharePuzzleDialogComponent, { data: { url }, width: '400px' });
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
  }

  protected override onSolvingBegins(): void {
    this.startTimer();
  }

  protected override handleSolved(): void {
    this.state = 'SOLVED';
    this.stopTimer();
    this.updateBoard();
    this.enterSolutionReview();
    this.recordCourseSolved();
    this.recordBookAttempt(true);
  }

  protected override handleFailed(): void {
    this.state = 'FAILED';
    this.stopTimer();
    this.updateBoard();
    this.enterSolutionReview();
    this.recordBookAttempt(false);
  }

  /**
   * Meldet einen Lösungsversuch ans Backend — nur im Standalone-Buch-Modus und nur eingeloggt
   * (Basis für die Tagespuzzle-Visualisierung auf Discord). Pro Puzzle nur einmal.
   */
  private recordBookAttempt(solved: boolean): void {
    if (!this.standalone || this.bookAttemptRecorded || !this.puzzle) return;
    this.bookAttemptRecorded = true;
    if (this.auth.isLoggedIn) {
      const url = `/api/book-puzzles/${this.puzzle.id}/attempt`;
      const body = { solved, timeSeconds: this.elapsedSeconds };
      if (!navigator.onLine) { this.offlineQueue.enqueue('POST', url, body); return; }
      this.puzzleService.recordBookAttempt(this.puzzle.id, solved, this.elapsedSeconds)
        .subscribe({ error: () => this.offlineQueue.enqueue('POST', url, body) });
    } else if (solved) {
      // Anonym (nicht eingeloggt): nur Solves zählen fürs Tagespuzzle mit (namenlos).
      const url = `/api/book-puzzles/${this.puzzle.id}/attempt/anonymous`;
      const body = { solved, timeSeconds: this.elapsedSeconds, sessionId: this.puzzleService.ensureSessionId() };
      if (!navigator.onLine) { this.offlineQueue.enqueue('POST', url, body); return; }
      this.puzzleService.recordBookAttemptAnonymous(this.puzzle.id, solved, this.elapsedSeconds)
        .subscribe({ error: () => this.offlineQueue.enqueue('POST', url, body) });
    }
  }

  ngOnInit(): void {
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
      this.courseBookId = Number(bookIdParam);
      this.courseModeKind = modeParam === 'random' ? 'random' : 'sequential';
      this.loadCourseNext();
      return;
    }

    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam) {
      this.loadPuzzle(Number(idParam));
    }
  }

  ngOnDestroy(): void {
    this.stopTimer();
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

  /** Holt das nächste Puzzle des Kurses (sequential: after=, random: exclude=). */
  private loadCourseNext(after?: number, exclude?: number): void {
    if (this.courseBookId == null) return;
    const hadPuzzle = this.puzzle != null;
    if (!hadPuzzle) this.state = 'LOADING';

    this.courseService.getNext(this.courseBookId, this.courseModeKind, after, exclude).subscribe({
      next: res => {
        this.courseSolved = res.solvedCount;
        this.courseTotal = res.total;
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
      error: () => { this.state = 'LOADING'; }
    });
  }

  /** „Nächstes Puzzle" / „Überspringen": eins weiter im jeweiligen Modus. */
  courseNext(): void {
    const cur = this.puzzle?.id;
    if (this.courseModeKind === 'random') this.loadCourseNext(undefined, cur);
    else this.loadCourseNext(cur, undefined);
  }

  backToCourses(): void {
    this.router.navigate(['/courses']);
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

  private loadWeekly(): void {
    if (this.weeklyId == null) return;
    this.state = 'LOADING';
    this.weeklyService.getPlay(this.weeklyId).subscribe({
      next: play => {
        this.weeklyTitle = play.title;
        this.weeklyPuzzles = play.puzzles ?? [];
        this.weeklyIndex = 0;
        if (this.weeklyPuzzles.length === 0) {
          this.weeklyCompleted = false;
          this.puzzle = null;
          this.state = 'COURSE_DONE';   // Done-Panel; weekly-Card zeigt „keine Puzzles"
          return;
        }
        this.loadWeeklyAt(0);
      },
      error: () => { this.state = 'COURSE_DONE'; this.puzzle = null; }
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
    this.weeklyIndex = index;
    this.puzzle = this.weeklyPuzzles[index];
    this.setupPuzzle(this.puzzle);
  }

  weeklyNext(): void {
    this.loadWeeklyAt(this.weeklyIndex + 1);
  }

  backToWeekly(): void {
    this.router.navigate(['/weekly']);
  }

  private recordCourseSolved(): void {
    if (!this.inCourse || this.courseBookId == null || !this.puzzle) return;
    const url = `/api/courses/${this.courseBookId}/results`;
    const body = { bookPuzzleId: this.puzzle.id, solved: true, mode: this.courseModeKind, timeSeconds: this.elapsedSeconds };
    if (!navigator.onLine) {
      // Offline gelöst → lokalen Fortschritt hochzählen + Server-Aufzeichnung vormerken.
      this.offlineQueue.enqueue('POST', url, body);
      this.courseSolved = Math.min(this.courseSolved + 1, this.courseTotal || this.courseSolved + 1);
      return;
    }
    this.courseService.recordResult(this.courseBookId, this.puzzle.id, true, this.courseModeKind, this.elapsedSeconds).subscribe({
      next: p => { this.courseSolved = p.solvedCount; this.courseTotal = p.total; },
      error: () => this.offlineQueue.enqueue('POST', url, body),
    });
  }

  private loadPuzzle(id: number): void {
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
        this.puzzle = puzzle;
        this.setupPuzzle(puzzle);
      },
      error: () => {
        this.state = 'LOADING';
        this.puzzle = null;
      }
    });
  }

  private setupPuzzle(puzzle: BookPuzzleDto): void {
    this.clearSolutionPlay();
    this.bookAttemptRecorded = false;
    this.reviewMode = false;
    this.solutionReview = false;
    // Lös-Automat (Setup, StartPly-Vorspiel, Zug-Handling, Stockfish, Viz) aus BasePuzzleSolver.
    this.setupSolver(puzzle.fen, puzzle.moves, puzzle.startPly ?? 0);
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
    this.recordBookAttempt(false);
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
  }

  protected override enterSolutionReview(): void {
    this.solutionReview = true;
    super.enterSolutionReview();
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

  protected override reviewGoTo(index: number): void {
    if (!this.puzzle) return;
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
  }

  exitReview(): void {
    this.reviewMode = false;
    this.solutionReview = false;
    if (this.puzzle) this.setupPuzzle(this.puzzle);
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    if (this.state !== 'SOLVED' && this.state !== 'FAILED' && !this.reviewMode) return;
    if (e.key === 'ArrowLeft') this.reviewPrev();
    if (e.key === 'ArrowRight') this.reviewNext();
  }

  formatTime(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return m > 0 ? `${m}:${s.toString().padStart(2, '0')}` : `${s}s`;
  }

  resetPuzzle(): void {
    if (!this.puzzle) return;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.setupPuzzle(this.puzzle);
  }

  private startTimer(): void {
    this.startTime = Date.now();
    this.elapsedSeconds = 0;
    this.timerInterval = setInterval(() => {
      this.elapsedSeconds = Math.floor((Date.now() - this.startTime) / 1000);
    }, 1000);
  }

  private stopTimer(): void {
    if (this.timerInterval) {
      clearInterval(this.timerInterval);
      this.timerInterval = undefined;
    }
  }

  private loadConfig(): void {
    this.boardTheme = this.prefs.boardTheme;
    this.pieceSet = this.prefs.pieceSet;
    this.themeMode = this.prefs.themeMode;
    this.stockfishDepth = this.prefs.bookStockfishDepth;
    this.visualizationMode = this.prefs.visualization;
  }

  setVisualizationLevel(level: number): void {
    this.visualizationMode = level;
    this.prefs.setVisualization(level);
    if (this.puzzle) this.setupPuzzle(this.puzzle);  // Modus-Wechsel = Puzzle neu starten
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

  override toggleSettings(): void {
    super.toggleSettings();
    if (this.showSettings) {
      setTimeout(() => this.settingsPanel?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50);
    }
  }
}
