import { Component, OnInit, OnDestroy, HostListener } from '@angular/core';
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
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleSettingsDialogComponent, PuzzleSettingsDialogData, PuzzleSettingsDialogResult } from './puzzle-settings-dialog.component';
import { PuzzleStatusCardComponent } from './puzzle-status-card.component';
import { ChallengeFriendsComponent } from './challenge-friends.component';
import { ChallengeService } from '../../core/challenge.service';
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
import { WeeklyService, WeeklyProgress } from '../weekly/weekly.service';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

type BookPuzzleState = 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE' | 'THINKING' | 'PLAYING' | 'SOLVED' | 'FAILED' | 'COURSE_DONE';

@Component({
  selector: 'app-book-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, MatProgressBarModule, MatTooltipModule, MatDialogModule,
    PuzzleBoardComponent, PuzzleTagsComponent,
    TranslateModule, PuzzleStatusCardComponent, ChallengeFriendsComponent
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

  stockfishDepth = 16;
  boardTheme = 'brown';
  readonly boardThemes = BOARD_THEMES;

  pieceSet = 'cburnett';
  themeMode: ThemeMode = 'fixed';
  readonly pieceSets = PIECE_SETS;

  elapsedSeconds = 0;
  private timerInterval?: ReturnType<typeof setInterval>;
  private startTime = 0;

  // Eval (Stockfish-Bewertung) — wie Standard/Endless; currentEval kommt aus BasePuzzleSolver.
  showEval = false;
  evalLoading = false;
  initialEval = '';
  private initialFen = '';

  /** True nach Give Up. Status-Panel zeigt einen Hinweis statt "Your turn!". */
  gaveUp = false;

  // Review-Modus „Ganze Partie" vs. Lösungs-Step-Through (komponentenspezifisch;
  // reviewMode/reviewIndex in BasePuzzleSolver).
  solutionReview = false;

  /** Standalone-Buch-Puzzle (/puzzles/book/:id) — nicht Kurs-/Wochenpost-Kontext. */
  get standalone(): boolean { return !this.inCourse && !this.inWeekly; }
  bookNavLoading = false;
  loadError = false;
  private retryFn: (() => void) | null = null;
  private bookAttemptRecorded = false;   // pro Puzzle nur ein Versuch melden (Tagespuzzle-Statistik)
  private courseAttemptRecorded = false; // pro Puzzle-Durchgang nur ein Kurs-Versuch melden (Zeit-Inflation vermeiden)
  /** Gesetzt, wenn dieses Buch-Puzzle aus einer Freundes-Challenge geöffnet wurde (?challengeId=…). */
  private challengeId: number | null = null;
  private challengeResolved = false;

  get isLoggedIn(): boolean { return this.auth.isLoggedIn; }

  get displayBookName(): string {
    if (!this.puzzle) return '';
    return this.puzzle.bookFileName.replace(/_firstkey\.pgn$/, '').replace(/_/g, ' ');
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
    private challengeService: ChallengeService
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
    this.initialFen = this.chess.fen();
    this.startTimer();
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

  protected override handleSolved(): void {
    this.state = 'SOLVED';
    this.stopTimer();
    this.updateBoard();
    this.enterSolutionReview();
    this.recordCourseAttempt(true);
    this.recordWeeklyAttempt(true);
    this.recordBookAttempt(true);
    // Einheitlicher Auto-Advance wie Standard/Endless: nach kurzem Countdown zum nächsten
    // (kontextabhängig Kurs/Wochenpost/Standalone); per „Weiter"-Klick sofort überspringbar.
    this.startSolvedCountdown(() => this.solvedAutoNext());
  }

  /** Nächstes Puzzle je nach Modus (Auto-Advance-Ziel). */
  solvedAutoNextPublic(): void { this.solvedAutoNext(); }
  private solvedAutoNext(): void {
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
    const y = +this.dailyDate.slice(0, 4), m = +this.dailyDate.slice(4, 6), d = +this.dailyDate.slice(6, 8);
    this.router.navigate(['/puzzles/daily', this.fmtUtc(new Date(Date.UTC(y, m - 1, d + delta)))]);
  }

  private todayUtc(): string { return this.fmtUtc(new Date()); }
  private fmtUtc(d: Date): string {
    const p = (n: number) => String(n).padStart(2, '0');
    return `${d.getUTCFullYear()}${p(d.getUTCMonth() + 1)}${p(d.getUTCDate())}`;
  }

  protected override handleFailed(): void {
    this.state = 'FAILED';
    this.stopTimer();
    this.updateBoard();
    this.enterSolutionReview();
    this.recordCourseAttempt(false);
    this.recordWeeklyAttempt(false);
    this.recordBookAttempt(false);
  }

  /**
   * Meldet einen Lösungsversuch ans Backend — nur im Standalone-Buch-Modus und nur eingeloggt
   * (Basis für die Tagespuzzle-Visualisierung auf Discord). Pro Puzzle nur einmal.
   */
  private recordBookAttempt(solved: boolean): void {
    if (!this.standalone || this.bookAttemptRecorded || !this.puzzle) return;
    this.bookAttemptRecorded = true;
    // Ergebnis an eine ggf. offene Freundes-Challenge zurückmelden (nur Standalone, einmalig).
    this.resolveChallengeIfNeeded(solved);
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
      const chapterParam = this.route.snapshot.paramMap.get('chapterIndex');
      this.courseChapterIndex = chapterParam != null ? Number(chapterParam) : null;
      this.loadCourseNext();
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
      this.loadPuzzle(Number(idParam));
    }
  }

  /** Meldet das Ergebnis genau einmal an eine offene Buch-Challenge zurück (fire-and-forget). */
  private resolveChallengeIfNeeded(solved: boolean): void {
    if (this.challengeId == null || this.challengeResolved) return;
    this.challengeResolved = true;
    this.challengeService.resolve(this.challengeId, solved, this.elapsedSeconds).subscribe({ next: () => {}, error: () => {} });
  }

  ngOnDestroy(): void {
    this.dailySub?.unsubscribe();
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
    this.loadError = false;
    this.retryFn = () => this.loadCourseNext(after, exclude);
    if (!hadPuzzle) this.state = 'LOADING';

    this.courseService.getNext(this.courseBookId, this.courseModeKind, after, exclude, this.courseChapterIndex ?? undefined).subscribe({
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
      error: () => { this.state = 'LOADING'; this.loadError = true; }
    });
  }

  /** „Nächstes Puzzle" / „Überspringen": eins weiter im jeweiligen Modus. */
  courseNext(): void {
    const cur = this.puzzle?.id;
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
  get weeklyTimeDisplay(): string {
    const s = Math.max(0, Math.floor(this.weeklySeconds));
    const sec = s % 60, m = Math.floor(s / 60) % 60, h = Math.floor(s / 3600);
    const p2 = (n: number) => n.toString().padStart(2, '0');
    return h > 0 ? `${h}:${p2(m)}:${p2(sec)}` : `${m}:${p2(sec)}`;
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
        // Fortschritt laden → Anzeige + Einstieg beim ersten NEUEN Puzzle (bei 100% von vorne).
        if (this.auth.isLoggedIn && this.weeklyId != null) {
          this.weeklyService.getProgress(this.weeklyId).subscribe({
            next: p => {
              this.weeklyPlayed = p.playedCount;
              this.weeklySolved = p.solvedCount;
              this.weeklySeconds = p.totalSeconds;
              this.loadWeeklyAt(this.weeklyStartIndex(p));
            },
            error: () => this.loadWeeklyAt(0),
          });
        } else {
          this.loadWeeklyAt(0);
        }
      },
      error: () => { this.state = 'COURSE_DONE'; this.puzzle = null; }
    });
  }

  /**
   * Einstiegs-Index: erstes noch NICHT gespieltes Puzzle (Sprung über bereits Gemachtes).
   * Bei 100% gespielt → 0 (von vorne). Ohne Fortschritt → 0.
   */
  private weeklyStartIndex(p: WeeklyProgress): number {
    if (p.completed) return 0;
    const played = new Set(p.playedIndices ?? []);
    const idx = this.weeklyPuzzles.findIndex(pz => !played.has(pz.id));
    return idx >= 0 ? idx : 0;
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
    const url = `/api/weekly-posts/${this.weeklyId}/attempt`;
    const body = { puzzleIndex, solved, timeSeconds: this.elapsedSeconds };
    if (!navigator.onLine) {
      this.offlineQueue.enqueue('POST', url, body);
      this.weeklyPlayed = Math.min(this.weeklyPlayed + 1, this.weeklyTotal || this.weeklyPlayed + 1);
      if (solved) this.weeklySolved += 1;
      this.weeklySeconds += this.elapsedSeconds;
      return;
    }
    this.weeklyService.recordAttempt(this.weeklyId, puzzleIndex, solved, this.elapsedSeconds).subscribe({
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
    const url = `/api/courses/${this.courseBookId}/results`;
    const body = { bookPuzzleId: this.puzzle.id, solved, mode: this.courseModeKind, timeSeconds: this.elapsedSeconds, chapterIndex: this.courseChapterIndex ?? undefined };
    if (!navigator.onLine) {
      // Offline → Server-Aufzeichnung vormerken; bei Solve zusätzlich lokalen Fortschritt hochzählen.
      this.offlineQueue.enqueue('POST', url, body);
      if (solved) this.courseSolved = Math.min(this.courseSolved + 1, this.courseTotal || this.courseSolved + 1);
      return;
    }
    this.courseService.recordResult(this.courseBookId, this.puzzle.id, solved, this.courseModeKind, this.elapsedSeconds, this.courseChapterIndex ?? undefined).subscribe({
      next: p => { this.courseSolved = p.solvedCount; this.courseTotal = p.total; },
      error: () => this.offlineQueue.enqueue('POST', url, body),
    });
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
    this.puzzleService.getDailyPuzzle(date).subscribe({
      next: puzzle => {
        this.puzzle = puzzle;
        this.setupPuzzle(puzzle);
      },
      error: () => {
        this.state = 'LOADING';
        this.puzzle = null;
        this.loadError = true;
      }
    });
  }

  private loadPuzzle(id: number): void {
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
        this.puzzle = puzzle;
        this.setupPuzzle(puzzle);
      },
      error: () => {
        this.state = 'LOADING';
        this.puzzle = null;
        this.loadError = true;
      }
    });
  }

  private setupPuzzle(puzzle: BookPuzzleDto): void {
    this.clearSolutionPlay();
    this.bookAttemptRecorded = false;
    this.courseAttemptRecorded = false;
    this.reviewMode = false;
    this.solutionReview = false;
    this.showEval = false;
    this.initialEval = '';
    this.currentEval = '';
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
    this.recordCourseAttempt(false);   // Aufgeben = Versuch mit verbrachter Zeit (zählt fürs Trainingsziel)
    this.recordWeeklyAttempt(false);   // Aufgeben zählt im Wochenpost als ✗ (gespielt, nicht gelöst)
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
    // Daily-Fairness: ein Reset nach mindestens einem gespielten Zug verbraucht den Tag.
    // Der erste Versuch zählt, spätere Solves auf demselben Tagespuzzle ändern das nicht mehr.
    if (this.isDaily && this.moveLog.length > 0) this.recordBookAttempt(false);
    // Wochenpost: Reset nach mind. einem Zug zählt als ✗ (gespielt, nicht gelöst).
    if (this.inWeekly && this.moveLog.length > 0) this.recordWeeklyAttempt(false);
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.setupPuzzle(this.puzzle);
  }

  override mouseslip(): void {
    // Im Tagespuzzle ist der „Mausrutscher" kein straffreier Undo: der Tag gilt damit als verbraucht.
    if (this.isDaily && !this.mouseslipUsed && !this.onSolutionPath) this.recordBookAttempt(false);
    super.mouseslip();
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
    });
  }
}
