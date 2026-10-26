import { Component, OnInit, OnDestroy, HostListener, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule } from '@angular/material/menu';
import { Router, ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { BoardFsActionsComponent } from './board-fs-actions.component';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { PuzzleActionBarComponent } from './puzzle-action-bar.component';
import { PuzzleStatusCardComponent } from './puzzle-status-card.component';
import { ChallengeFriendsComponent } from './challenge-friends.component';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleService, PuzzleDto, PuzzleStatsDto } from './puzzle.service';
import { OfflineService } from '../../core/offline.service';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { PuzzleDifficulty, StandardViewSettings } from './standard-view-settings';
import { StandardPuzzleSource } from './standard-puzzle-source';
import { StockfishService } from './stockfish.service';
import { AuthService } from '../../core/auth.service';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { ChallengeService } from '../../core/challenge.service';
import { RevengeService } from '../../core/revenge.service';
import { FavoritesService } from '../../core/favorites.service';
import { loadLastSolved, saveLastSolved } from './last-solved-store';
import { WorksheetService } from '../worksheets/worksheet.service';
import { taskItemFromPuzzle } from '../worksheets/worksheet-items.util';
import { FavoriteTracker } from './favorite-tracker';
import { PuzzleRevengeRound } from './puzzle-revenge-round';
import { sendStandardAttempt } from './standard-attempt';
import { BOARD_THEMES, PIECE_SETS, ThemeMode, parseShareViewParams } from './board-theme.util';
import { Key } from 'chessground/types';
import { buildStagedHints } from './puzzle-hints.util';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { epForcedHintTexts, offPathHintTexts, offPathWarningText } from './solver-texts';
import { LongSolveService } from './long-solve.service';
import { SolveMode, SolveModeService } from '../../core/solve-mode.service';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { PctPipe } from '../../shared/format/pct';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';


/** Endzustände dieses Lösers (die übrigen setzt {@link BasePuzzleSolver}). */
export type PuzzleEndState = 'SOLVED' | 'FAILED' | 'ERROR';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, MatMenuModule, MatDialogModule, TranslatePipe, PuzzleBoardComponent, BoardFsActionsComponent,
    PuzzleActionBarComponent, PuzzleStatusCardComponent, ChallengeFriendsComponent, PctPipe, IconLabelDirective
  ],
  templateUrl: './puzzle.component.html',
  styleUrls: ['./puzzle.component.scss'],
})
export class PuzzleComponent extends BasePuzzleSolver<PuzzleEndState> implements OnInit, OnDestroy {
  // state, boardFen, orientation, turnColor, dests, lastMove, isCheck, onSolutionPath,
  // alternativeSolve, mouseslipUsed, currentEval, visualizationMode, vizMoves, chess,
  // solutionMoves, moveIndex, autoAdvanceTimer, aborted, moveLog, moveStartTime → BasePuzzleSolver
  puzzle: PuzzleDto | null = null;
  stats: PuzzleStatsDto | null = null;

  boardTheme = 'brown';

  difficulty: PuzzleDifficulty = 'normal';
  excludeSolved = false;
  /** „5 schwächste Themen trainieren" — filtert Puzzles auf die schwächsten Themen des Users (ODER). */
  worstTagsEnabled = false;
  stockfishDepth = 16;

  // elapsedSeconds / Stoppuhr / start-/stopTimer / formatTime: jetzt in BasePuzzleSolver (geteilt).

  private attemptRecorded = false;
  /** Woher das nächste Puzzle kommt (Deep-Link, Vorab, Offline-Pool, Zufall) samt Lade-/Filter-Epochen. */
  readonly source: StandardPuzzleSource;
  lastEloChange: number | null = null;

  private routePuzzleId: number | null = null;

  /** Direkt geteiltes Einzel-Puzzle (?single=1): nach dem Lösen kein Auto-Weiter (der „Weiter"-Knopf
   *  bleibt aber, der Empfänger kann selbst weiterblättern). */
  singlePuzzle = false;

  /** Freundes-Challenge + Revanche-Runde (?challengeId= / ?revengeUserId=). */
  readonly revenge: PuzzleRevengeRound;
  lastSolvedPuzzleId: number | null = null;
  private lastSolvedFen: string | null = null;
  private lastSolvedMoves = '';
  private lastSolvedOrientation: 'white' | 'black' = 'white';
  /** Themen des letzten Puzzles — Vorschlagsquelle für die Themen eines Aufgabenblatts. */
  private lastSolvedThemes = '';
  /** „Geliebtes Puzzle"-Zustand (Herz) für aktuelles + zuletzt gelöstes Puzzle. */
  readonly favoriteTracker: FavoriteTracker;
  /** True wenn der User aufgegeben hat. Brett wird zurueckgesetzt damit er die Loesung
   *  selber durchspielen kann; im AWAITING/PLAYING/THINKING-State zeigt das Status-Panel
   *  einen Hinweis statt "Your turn!". Reset bei loadNext/retry. */
  gaveUp = false;

  /** Ansicht, Filter-Einstellungen und Spielweise (Training/Einfach) — siehe {@link StandardViewSettings}. */
  readonly view: StandardViewSettings;
  /** Gewählte Spielweise für „Puzzles"; `null` = bewusst nicht gefragt (fester Link, Einzelkontext). */
  get solveModeChoice(): SolveMode | null { return this.view.solveModeChoice; }

  constructor(
    readonly puzzleService: PuzzleService,
    stockfish: StockfishService,
    private authService: AuthService,
    readonly prefs: PreferencesService,
    private router: Router,
    private route: ActivatedRoute,
    readonly dialog: MatDialog,
    private offline: OfflineService,
    private offlineQueue: OfflineQueueService,
    readonly snackbar: SnackbarService,
    private challengeService: ChallengeService,
    private revengeService: RevengeService,
    readonly translate: TranslateService,
    private http: HttpClient,
    private longSolve: LongSolveService,
    private favorites: FavoritesService,
    readonly solveMode: SolveModeService,
    private worksheets: WorksheetService
  ) {
    super(stockfish);
    this.revenge = new PuzzleRevengeRound(this.http);
    this.favoriteTracker = new FavoriteTracker(
      this.favorites, 'standard',
      () => this.puzzle?.id, () => this.lastSolvedPuzzleId, () => this.isLoggedIn,
    );
    this.view = new StandardViewSettings(this);
    this.view.loadConfig();
    this.source = new StandardPuzzleSource(this.puzzleService, this.offline, {
      elo: () => this.stats?.puzzleElo ?? 1500,
      difficulty: () => this.difficulty,
      excludeSolved: () => this.excludeSolved,
      worstTagsEnabled: () => this.worstTagsEnabled,
      isLoggedIn: () => this.isLoggedIn,
    });
    this.stockfish.init().catch(() => {});
  }

  /** „Offline, nichts gespeichert" — Pool war noch nie befüllt (z.B. Seite erstmals offline geöffnet). */
  offlineNoCache = false;
  /** „Offline, alle gespielt" — Pool ist leer geworden, aber wir hatten zuvor mindestens 1 Puzzle gezeigt.
   *  In dem Fall bietet das UI explizit an, das letzte Puzzle nochmal zu spielen — sonst sitzt der Nutzer
   *  vor einer „Verbinden Sie sich erneut"-Wand, obwohl er noch mit dem zuletzt gesehenen Puzzle üben könnte. */
  offlinePoolExhausted = false;
  /** Zuletzt geladenes Puzzle (egal ob gelöst, fehlgeschlagen, oder noch im Spiel). Wird für „Letztes nochmal" verwendet. */
  private lastShownPuzzle: PuzzleDto | null = null;
  /** ID des zuvor angezeigten Puzzles (für „vorheriges Puzzle teilen"). */
  private previousPuzzleId: number | null = null;

  // ===== Hooks für BasePuzzleSolver =====
  protected override get depth(): number { return this.stockfishDepth; }

  protected override onSetupStart(): void { this.view.applyLook(); }

  protected override onSolvingBegins(): void {
    this.startTimer();
    this.moveStartTime = Date.now();
  }

  protected override get offPathWarnThreshold(): number { return this.prefs.offPathWarnMoves; }
  protected override onOffPathWarning(): void {
    this.snackbar.info(offPathWarningText(this.t, this.startWasBalanced(), this.playerEvalDisplay()),
      { action: 'common.ok', duration: 7000 });
  }
  private readonly t = (k: string, p?: object) => this.translate.instant(k, p) as string;
  protected override get epForcedHints(): string[] { return epForcedHintTexts(this.t); }
  protected override get offPathHints(): string[] { return offPathHintTexts(this.t, this.offPathUserMoveNumber); }

  protected override handleSolved(alternative: boolean): void {
    this.state = 'SOLVED';
    this.stopTimer();
    this.updateBoard();
    this.lastSolvedPuzzleId = this.puzzle?.id ?? null;
    this.lastSolvedFen = this.puzzle?.fen ?? null;
    this.lastSolvedMoves = this.puzzle?.moves ?? '';
    this.lastSolvedOrientation = this.orientation;
    if (this.puzzle) {
      this.lastSolvedThemes = this.puzzle.themes ?? '';
      saveLastSolved('standard', {
        id: this.puzzle.id, fen: this.puzzle.fen,
        moves: this.puzzle.moves ?? '', orientation: this.orientation,
        themes: this.puzzle.themes ?? '',
      });
    }
    this.favoriteTracker.refresh();
    this.enterSolutionReview();
    // Auffällig lange Lösezeit (Tab lag vermutlich offen) → nachfragen, bevor gewertet wird; der
    // Dialog ist modal und blockiert „Weiter" dahinter. Aufzeichnen + Auto-Advance erst danach.
    this.longSolve.resolve(this.elapsedSeconds).subscribe(seconds => {
      if (this.destroyed) return;   // Seite verlassen (Dialog schließt bei Navigation)
      this.recordAttempt(true, seconds);
      // Bei alternativer (eigener) Lösung NICHT automatisch weiterspringen — wie im Endless-Modus:
      // der Spieler entscheidet selbst (Weiter / Originallösung zeigen).
      if (alternative) return;
      // Direkt geteiltes Einzel-Puzzle (?single=1): kein Auto-Weiter. Der „Weiter"-Knopf bleibt,
      // der Empfänger entscheidet selbst, ob er ins nächste (Zufalls-)Puzzle wechselt.
      if (this.singlePuzzle) return;
      this.startSolvedCountdown(() => this.loadNext());
    });
  }

  /** P-1: der erste Fehlzug ist der gewertete Fehlversuch (Elo/Statistik sofort). Danach bleibt `attemptRecorded`
   *  bis zum nächsten Puzzle gesetzt — Zurücksetzen, Mausrutscher und „Nochmal“ melden nichts mehr. */
  protected override onFirstWrongMove(): void {
    this.recordAttempt(false);
  }

  protected override handleFailed(): void {
    this.state = 'FAILED';
    this.stopTimer();
    this.updateBoard();
    this.recordAttempt(false);
    this.favoriteTracker.refresh();
    this.showFailureOrHoldMate();
  }

  readonly boardThemes = BOARD_THEMES;

  pieceSet = 'cburnett';
  readonly pieceSets = PIECE_SETS;

  get isLoggedIn(): boolean { return this.authService.isLoggedIn; }

  goEndless(): void {
    this.router.navigate(['/puzzles/endless']);
  }

  sharePuzzle(): void {
    if (!this.puzzle) return;
    const url = `${window.location.origin}/puzzles/${this.puzzle.id}`;
    const previousUrl = this.previousPuzzleId
      ? `${window.location.origin}/puzzles/${this.previousPuzzleId}`
      : undefined;
    this.dialog.open(SharePuzzleDialogComponent, {
      data: {
        url, previousUrl,
        puzzleId: this.puzzle.id,
        previousPuzzleId: this.previousPuzzleId ?? undefined,
        source: 'standard',
        canChallenge: this.isLoggedIn,
      },
      width: '400px',
      maxWidth: '95vw',
    });
  }

  /** Aktuelle Stellung + komplette Zugfolge des Puzzles im Analysemodus öffnen. */
  analyze(): void {
    if (!this.puzzle) return;
    const moves = this.puzzle.moves.split(' ').filter(m => m);
    // Zurück zur Puzzle-Liste (ohne ID) – mit ID würde dasselbe Puzzle neu geladen statt das nächste.
    this.router.navigate(['/analysis'], {
      queryParams: { fen: this.puzzle.fen, moves: moves.join(','), orientation: this.orientation, from: '/puzzles' },
    });
  }

  ngOnInit(): void {
    // Optionale Anzeige-Overrides aus dem (geteilten) Link (`?crazy=1`, `?visualmode=0–4`),
    // bevor das Puzzle aufgebaut wird. Transient — verändert keine gespeicherten Einstellungen.
    const ov = parseShareViewParams(this.route.snapshot.queryParamMap);
    if (ov.themeMode) this.themeMode = ov.themeMode;
    if (ov.visualization != null) this.visualizationMode = ov.visualization;
    this.anarchyForcedByUrl = !!ov.enPassantForced;   // Anarchy per URL: e.p. immer forciert (sonst folgt es der Einstellung)
    if (ov.crazyPieceMode) this.crazyPieceMode = ov.crazyPieceMode;   // ?anarchy=max+1 → Feld bestimmt Stil

    // Direkt geteiltes Einzel-Puzzle: nach dem Lösen nicht automatisch weiterspringen.
    this.singlePuzzle = this.route.snapshot.queryParamMap.get('single') === '1';

    // Offen-Zustand der Einstellungen über Puzzle-Wechsel/Re-Init hinweg behalten.
    this.loadSettingsOpen();

    // „Letztes Puzzle" (analysieren / ♥ / teilen) über Navigation zu /analysis hinweg wiederherstellen —
    // ohne Persistenz wird `lastSolvedPuzzleId` beim Component-Destroy null und die Knöpfe verschwinden.
    const restored = loadLastSolved('standard');
    if (restored) {
      this.lastSolvedPuzzleId = restored.id;
      this.lastSolvedFen = restored.fen;
      this.lastSolvedMoves = restored.moves;
      this.lastSolvedOrientation = restored.orientation;
      this.lastSolvedThemes = restored.themes ?? '';
      this.favoriteTracker.refresh();
    }

    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam) {
      this.routePuzzleId = Number(idParam);
    }

    // Aus einer Freundes-Challenge geöffnet → nach dem Versuch das Ergebnis zurückmelden.
    const challengeParam = this.route.snapshot.queryParamMap.get('challengeId');
    if (challengeParam) {
      this.revenge.challengeId = Number(challengeParam) || null;
    }

    // Als Revanche an einem gescheiterten Puzzle eines Freundes geöffnet → den Freund informieren
    // und im Revenge-Modus bleiben (offene Puzzles des Freundes nacheinander durchspielen).
    const revengeParam = this.route.snapshot.queryParamMap.get('revengeUserId');
    if (revengeParam) {
      this.revenge.userId = Number(revengeParam) || null;
      if (this.revenge.userId) this.revenge.loadQueue(this.revenge.userId, () => [this.routePuzzleId, this.puzzle?.id]);
    }

    // Spielweise für den Bereich „Puzzles" einmalig erfragen — aber nur beim normalen Einstieg
    // ins Training. Nicht gefragt wird, wenn die Ansicht per Link festgelegt ist (?visualmode=)
    // oder ein Einzelkontext geöffnet wurde (geteiltes Puzzle, Challenge, Revanche): dort hat der
    // Absender die Ansicht bereits gewählt bzw. es ist kein Trainingseinstieg, und ein
    // blockierender Dialog stünde nur im Weg.
    this.view.askSuppressed = ov.visualization != null || this.singlePuzzle
      || this.revenge.challengeId != null || this.revenge.userId != null;
    this.view.askSolveMode();

    const stats$ = this.isLoggedIn
      ? this.puzzleService.getStats(this.visualizationMode)
      : this.puzzleService.getAnonymousStats();

    if (this.routePuzzleId) {
      // Deep-Link auf ein konkretes Puzzle → sofort laden; Stats/Range nebenher.
      this.loadNext();
      stats$.subscribe({ next: s => this.stats = s, error: () => {} });
      this.puzzleService.getRatingRange().subscribe({ next: r => this.source.ratingRangeBounds = r, error: () => {} });
      return;
    }

    // Sonst erst Elo (stats) + DB-Rating-Bereich laden, DANN das erste Zufallspuzzle –
    // sonst würde es mit Default-Elo 1500 / ungeklemmtem Fenster gezogen.
    const loadFirst = () => {
      this.puzzleService.getRatingRange().subscribe({
        next: r => this.source.ratingRangeBounds = r,
        // Schwächste Themen vor dem ersten Puzzle laden, damit der Filter schon greift.
        error: () => this.source.ensureWorstThemes(() => this.loadNext()),
        complete: () => this.source.ensureWorstThemes(() => this.loadNext()),
      });
    };
    stats$.subscribe({
      next: s => this.stats = s,
      error: () => loadFirst(),
      complete: () => loadFirst(),
    });
  }

  /** Spielweise umschalten (⋮-Menü) — wirkt ab dem NÄCHSTEN Puzzle. */
  toggleSolveMode(): void { this.view.toggleSolveMode(); }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.stopTimer();
    this.releaseSolver();   // bricht auch Countdown und Lösungs-Wiedergabe ab
  }

  loadNext(): void {
    const epoch = ++this.source.loadEpoch;
    this.view.applySolveModeLevel();   // ein Umschalten der Spielweise greift ab diesem Puzzle
    this.state = 'LOADING';
    this.offlineNoCache = false;
    this.offlinePoolExhausted = false;
    this.attemptRecorded = false;
    this.revenge.notified = false;
    this.gaveUp = false;
    this.stopTimer();
    this.stopCountdown();
    this.clearSolutionPlay();
    this.elapsedSeconds = 0;
    this.alternativeSolve = false;
    this.lastEloChange = null;
    this.showEval = false;
    this.initialEval = '';
    this.currentEval = '';

    const routeId = this.routePuzzleId;
    this.routePuzzleId = null;
    const source$ = this.source.pick(routeId);
    if (!source$) {
      // Offline und der Pool ist leer. Falls wir vorher schon mindestens ein Puzzle hatten, ist der Vorrat
      // verbraucht (Pool exhausted) — dann „Letztes nochmal spielen" anbieten statt einfach
      // „nichts gespeichert" zu zeigen. Sonst (Erstaufruf offline ohne Cache): no-cache.
      if (this.lastShownPuzzle) this.offlinePoolExhausted = true;
      else this.offlineNoCache = true;
      this.state = 'ERROR';
      this.puzzle = null;
      return;
    }

    source$.subscribe({
        next: puzzle => {
          if (epoch !== this.source.loadEpoch) return;   // veraltete Antwort eines früheren Ladevorgangs
          // Bisher angezeigtes Puzzle als „vorheriges" merken (für Teilen-Dialog).
          if (this.puzzle && this.puzzle.id !== puzzle.id) this.previousPuzzleId = this.puzzle.id;
          this.puzzle = puzzle;
          this.lastShownPuzzle = puzzle;
          // Spielweise-Dialog noch offen → nur zeigen; askSolveMode setzt nach der Wahl auf.
          if (this.awaitingSolveMode) this.previewSetup(puzzle.fen, puzzle.moves, 0);
          else this.setupPuzzle(puzzle);
          this.source.prefetchNext();
          this.source.prefetchOfflinePool();
        },
        error: () => {
          if (epoch !== this.source.loadEpoch) return;
          this.state = 'ERROR';
          this.puzzle = null;
        }
      });
  }

  /** Spielt das zuletzt geladene Puzzle nochmal (Fallback wenn der Offline-Pool aufgebraucht ist). */
  replayLastPuzzle(): void {
    if (!this.lastShownPuzzle) return;
    ++this.source.loadEpoch;   // evtl. noch laufenden loadNext entwerten
    this.offlineNoCache = false;
    this.offlinePoolExhausted = false;
    this.attemptRecorded = false;
    this.gaveUp = false;
    this.elapsedSeconds = 0;
    this.alternativeSolve = false;
    this.lastEloChange = null;
    this.showEval = false;
    this.initialEval = '';
    this.currentEval = '';
    this.puzzle = this.lastShownPuzzle;
    this.setupPuzzle(this.lastShownPuzzle);
  }

  /** Puzzle-Filter geändert: vorab geladenes Puzzle + Offline-Pool galten für die alten Filter → verwerfen
   *  (auch noch laufende Anfragen) und den Pool mit den neuen Filtern füllen. `loadNow` = gleich ein neues
   *  Puzzle laden (Schwierigkeit/Themen); sonst bleibt das laufende stehen und erst das nächste kommt gefiltert. */
  onPuzzleFilterChange(loadNow: boolean): void {
    this.source.discardForFilterChange();
    this.source.ensureWorstThemes(() => {
      this.source.prefetchOfflinePool();
      if (loadNow) this.loadNext();
    });
  }

  /** On-the-fly klassifizierter erster Löserzug (Schach/Schlag/ruhig) — Basis der gestuften Tipps. */
  /**
   * On-the-fly-Tipps für Standard-Puzzles (kein vorberechneter Speicher) — zum AKTUELL erwarteten Zug
   * (jeder Zug, nicht nur der erste): Stufe 1 = Check-Capture-Threat-Hinweis je nach Zugtyp, Stufe 2 =
   * welche Figur zieht, Stufe 3 = der Zug (SAN).
   */
  override get availableHints(): string[] {
    return buildStagedHints(this.currentMoveHint, (k, p) => this.translate.instant(k, p) as string);
  }

  flagSaving = false;

  /** „Dumme Tipps" markieren/aufheben (jeder eingeloggte User; nur möglich, wenn schon ein Tipp aufgedeckt war). */
  toggleHintsFlag(): void {
    if (!this.puzzle || this.flagSaving) return;
    const next = !this.puzzle.hintsFlagged;
    this.flagSaving = true;
    this.puzzleService.flagPuzzleHints(this.puzzle.id, next).subscribe({
      next: () => {
        if (this.puzzle) this.puzzle.hintsFlagged = next;
        this.flagSaving = false;
        this.snackbar.success(this.translate.instant(next ? 'solver.hints.flagSaved' : 'solver.hints.flagCleared'), { duration: 2000 });
      },
      error: () => {
        this.flagSaving = false;
        this.snackbar.warn(this.translate.instant('solver.hints.flagError'), { duration: 3000 });
      }
    });
  }

  setupPuzzle(puzzle: PuzzleDto): void {
    this.reviewMode = false;
    this.reviewIndex = 0;
    this.hintLevel = 0;
    // Tipps werden pro erwartetem Zug on-the-fly aus der aktuellen Stellung erzeugt (currentMoveHint).
    // Lös-Automat (Setup, Zug-Handling, Stockfish, Viz) kommt aus BasePuzzleSolver.
    this.setupSolver(puzzle.fen, puzzle.moves, 0);
  }

  giveUp(): void {
    if (!this.puzzle) return;
    this.abortSolver();
    this.stopTimer();
    // Fehlversuch aufzeichnen (Elo-Loss + Statistik), falls noch nicht geschehen.
    if (!this.attemptRecorded) this.recordAttempt(false);
    this.gaveUp = true;
    this.state = 'FAILED';
    this.playSolutionFromStart();
  }

  retry(): void {
    if (!this.puzzle) return;
    this.clearSolutionPlay();
    // P-1: „Nochmal“ ist Übung am selben Puzzle — der Fehlversuch ist gemeldet, ein zweiter Versuch zählt nicht
    // (bis 0.839 setzte retry `attemptRecorded` zurück und meldete das Lösen als neuen Versuch).
    this.gaveUp = false;
    this.setupPuzzle(this.puzzle);
  }

  override get reviewTotal(): number {
    return this.puzzle ? this.puzzle.moves.split(' ').filter(m => m).length : 0;
  }

  protected override reviewGoTo(index: number): void {
    if (!this.puzzle) return;
    // Gemeinsamer Review-Aufbau der Basisklasse (mit FEN-Guard) statt eigener Kopie — Rumpf war
    // byte-identisch. Standard-Puzzles haben immer eine legale FEN; der Guard schadet nicht.
    this.reviewGoToCore(this.puzzle.fen, this.puzzle.moves.split(' ').filter(m => m), index);
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    if (this.state !== 'SOLVED' && this.state !== 'FAILED') return;
    if (!isBoardHotkey(e)) return;
    this.reviewArrowKey(e);
  }


  /**
   * Das zuletzt gelöste Puzzle aufs AUFGABENBLATT — dieselbe Auswahl wie „♥ letztes Puzzle",
   * nur fürs Papier. Geschickt wird die AUFGABEN-Stellung (der Gegnerzug ist eingespielt),
   * nicht die rohe Puzzle-FEN: gedruckt wird die Frage, nicht der Moment davor.
   */
  sendLastToWorksheet(target: number | null): void {
    if (!this.lastSolvedFen) return;
    const item = taskItemFromPuzzle(
      {
        fen: this.lastSolvedFen, moves: this.lastSolvedMoves,
        orientation: this.lastSolvedOrientation, themes: this.lastSolvedThemes,
      },
      'Standard', { sourceId: this.lastSolvedPuzzleId },
    );
    this.worksheets.sendAndNotify(target, item ? [item] : []);
  }

  reviewLastPuzzle(): void {
    // Direkt in den Analysemodus mit dem zuletzt gelösten Puzzle (Stellung + Zugfolge).
    if (this.lastSolvedFen) {
      this.router.navigate(['/analysis'], {
        queryParams: {
          fen: this.lastSolvedFen,
          moves: this.lastSolvedMoves.split(' ').filter(m => m).join(','),
          orientation: this.lastSolvedOrientation,
          from: this.puzzle?.id ? '/puzzles/' + this.puzzle.id : undefined,
        },
      });
      return;
    }
    if (this.lastSolvedPuzzleId) {
      this.router.navigate(['/puzzles', this.lastSolvedPuzzleId]);
    }
  }


  /** `seconds` = zu wertende Lösezeit; default = gemessene Zeit (Fehlversuche), beim Lösen ggf. wegen
   *  überlanger Zeit gekappt (siehe {@link LongSolveService}). */
  private recordAttempt(solved: boolean, seconds: number = this.elapsedSeconds): void {
    if (!this.puzzle || this.attemptRecorded) return;
    this.attemptRecorded = true;
    sendStandardAttempt({
      puzzleService: this.puzzleService, offlineQueue: this.offlineQueue,
      challengeService: this.challengeService, revengeService: this.revengeService,
      loggedIn: this.isLoggedIn, revenge: this.revenge,
      visualizationMode: () => this.visualizationMode,
      onEloChange: change => { this.lastEloChange = change; },
      onStats: stats => { this.stats = stats; },
    }, {
      id: this.puzzle.id, solved, seconds, moveLog: this.moveLog,
      visualizationLevel: this.visualizationMode, evalShown: this.evalShown, vizShowCount: this.vizShowCount,
      hintsUsed: this.maxHintLevel,
    });
  }

  /** „Nächstes": im Revenge-Modus das nächste offene Puzzle des Freundes, sonst normales Zufallspuzzle. */
  onNext(): void {
    if (this.revenge.userId != null) { this.advanceRevenge(); return; }
    this.loadNext();
  }

  /** Nächstes Puzzle der Revanche-Runde laden; ist die Schlange leer → Glückwunsch/Feuerwerk. */
  private advanceRevenge(): void {
    const nextId = this.revenge.takeNext();
    if (nextId == null) {
      this.stopTimer();
      this.stopCountdown();
      return;
    }
    this.routePuzzleId = nextId;
    this.loadNext();
  }

  /** Von der Feuerwerk-Card zurück zur Revanche-Liste des Freundes (zeigt jetzt die erledigten). */
  goToRevengeList(): void {
    if (this.revenge.userId != null) this.router.navigate(['/friends', this.revenge.userId, 'revenge']);
  }

  resetPuzzle(): void {
    if (!this.puzzle) return;
    this.aborted = true;
    this.clearSolutionPlay();
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.currentEval = '';
    this.initialEval = '';
    this.showEval = false;
    this.setupPuzzle(this.puzzle);
  }

  /** Aktive Schwächen-Themen für die Sichtbar-Anzeige während des Lösens (leer, wenn Filter aus). */
  get activeWorstThemes(): string[] { return this.source.activeWorstThemes; }

  /** True, wenn das gerade GELÖSTE Puzzle dieses Thema trägt — fürs Einfärben des passenden Chips. */
  themeMatched(theme: string): boolean {
    return this.state === 'SOLVED' && !!this.puzzle?.themes
      && this.puzzle.themes.split(/\s+/).includes(theme);
  }

  setVisualizationLevel(level: number): void { this.view.setVisualizationLevel(level); }
  setVizArrowEnabled(val: boolean): void { this.view.setVizArrowEnabled(val); }
  setBoardTheme(theme: string): void { this.view.setBoardTheme(theme); }
  setPieceSet(set: string): void { this.view.setPieceSet(set); }
  setThemeMode(mode: ThemeMode): void { this.view.setThemeMode(mode); }
  openSettingsDialog(): void { this.view.openDialog(); }
}
