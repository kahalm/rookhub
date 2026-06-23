import { Component, OnDestroy, OnInit, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatChipsModule, MatChipInputEvent } from '@angular/material/chips';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { COMMA, ENTER, SPACE } from '@angular/cdk/keycodes';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { PuzzleBoardComponent } from './puzzle-board.component';
import { PuzzleRatingCardComponent } from './puzzle-rating-card.component';
import { SharePuzzleDialogComponent } from './share-puzzle-dialog.component';
import { PuzzleSettingsDialogComponent, PuzzleSettingsDialogData, PuzzleSettingsDialogResult } from './puzzle-settings-dialog.component';
import { PuzzleStatusCardComponent } from './puzzle-status-card.component';
import { ChallengeFriendsComponent } from './challenge-friends.component';
import { PuzzleService, PuzzleDto, PuzzleRatingRange } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { EndlessStorageService, EndlessConfig, EndlessSession } from './endless-storage.service';
import { buildChainWindows, chainRatingAt, ENDLESS_RATING_WINDOW, ENDLESS_CHAIN_BLOCK, CHAIN_T1_INDEX, CHAIN_T2_INDEX, CHAIN_FLAT_STEP, FIRST_RUN_ANCHOR1_INDEX, FIRST_RUN_ANCHOR1_RATING, FIRST_RUN_ANCHOR2_INDEX, FIRST_RUN_ANCHOR2_RATING } from './endless-prefetch.util';
import { EndlessFasttrackState } from './endless-fasttrack-state';
import { OfflineService } from '../../core/offline.service';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { AuthService } from '../../core/auth.service';
import { PreferencesService } from '../../core/preferences.service';
import { BOARD_THEMES, PIECE_SETS, ThemeMode, applyThemeMode, clearCrazyStyles, clearVisualizationHide } from './board-theme.util';
import { applyUci } from './puzzle-move.util';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { VisibilityStopwatch } from './visibility-stopwatch';
import { LongSolveService } from './long-solve.service';
import { classifyStandardFirstMove, FirstMoveHint } from './puzzle-hints.util';
import { Chess } from 'chess.js';
import { Key } from 'chessground/types';

// AWAITING_USER_MOVE = first move only (no buttons)
// THINKING = opponent responding (buttons visible, board locked)
// PLAYING = user's turn after first move (buttons visible, board active)
type EndlessState = 'CONFIG' | 'LOADING' | 'SETUP' | 'AWAITING_USER_MOVE'
  | 'THINKING' | 'PLAYING' | 'SOLVED' | 'FAILED' | 'GAME_OVER' | 'EXHAUSTED' | 'WON';

interface EndlessPuzzleAttempt {
  puzzleNumber: number;
  puzzleId: number;
  lichessId: string;
  rating: number;
  solved: boolean;
  themes?: string;
  /** Start-/Endzeit dieses Puzzles als Unix-Millis (fürs serverseitige Logging). */
  startedAt: number;
  endedAt: number;
}

@Component({
  selector: 'app-endless-puzzle',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatProgressSpinnerModule, MatSlideToggleModule,
    MatChipsModule, MatAutocompleteModule,
    MatDialogModule, TranslateModule, PuzzleBoardComponent,
    PuzzleRatingCardComponent, PuzzleStatusCardComponent, ChallengeFriendsComponent
  ],
  templateUrl: './endless-puzzle.component.html',
  styleUrls: ['./endless-puzzle.component.scss'],
})
export class EndlessPuzzleComponent extends BasePuzzleSolver implements OnDestroy, OnInit {
  get screen(): 'config' | 'play' | 'gameover' | 'exhausted' | 'won' | 'loading' {
    // History-Detail wird geladen → Spinner statt leerem Brett (kein gültiges Puzzle vorhanden).
    if (this.historyView && this.state === 'LOADING') return 'loading';
    if (this.state === 'WON') return 'won';
    if (this.state === 'EXHAUSTED') return 'exhausted';
    if (this.state === 'GAME_OVER') return 'gameover';
    if (this.state === 'CONFIG') return 'config';
    return 'play';
  }

  config: EndlessConfig = { startElo: 700, themes: '', stockfishDepth: 16 };
  /** Schwächste Themen des Users (für „5 schwächste Themen trainieren"). */
  private worstThemes: string[] = [];

  /** Für das Config-Template: „schwächste Themen"-Option nur eingeloggt anbieten. */
  get isLoggedIn(): boolean { return this.authService.isLoggedIn; }
  /** Manuell eingegebene Themen, gemerkt während „schwächste Themen" aktiv ist (Wiederherstellung beim Abschalten). */
  private savedManualThemes = '';

  /** Aktive Themen-Filter für die Sichtbar-Anzeige (Config + Run). Leer = kein Filter. */
  get activeFilterThemes(): string[] {
    const src = (this.config.worstTags ? this.worstThemes.join(' ') : this.config.themes) || '';
    return src.trim().split(/\s+/).filter(Boolean);
  }

  /** True, wenn das gerade GELÖSTE Puzzle dieses Thema trägt — fürs Einfärben des passenden Chips. */
  themeMatched(theme: string): boolean {
    return this.state === 'SOLVED' && !!this.puzzle?.themes
      && this.puzzle.themes.split(/\s+/).includes(theme);
  }

  /** Toggle „5 schwächste Themen trainieren": schwächste Themen laden und in die Editbox schreiben (sichtbar). */
  onWorstTagsToggle(enabled: boolean): void {
    if (enabled) {
      this.savedManualThemes = this.config.themes;
      this.ensureWorstThemes(() => { this.config.themes = this.worstThemes.join(' '); this.saveConfig(); });
    } else {
      this.config.themes = this.savedManualThemes;
      this.worstThemes = [];   // beim nächsten Aktivieren frisch laden
      this.saveConfig();
    }
  }

  // ── Themen-Auswahl (durchsuchbare Multiselect-Dropdown) ───────────────────────────
  /** Alle verfügbaren Themen (vom Backend, alphabetisch) — Optionen des Autocomplete-Dropdowns. */
  allThemes: string[] = [];
  /** Freitext im Sucheingabefeld der Themen-Auswahl (filtert das Dropdown). */
  themeInput = '';
  /** Mit diesen Tasten wird der getippte Text als Chip übernommen (erlaubt freie Themen). */
  readonly themeSeparatorKeys: number[] = [ENTER, COMMA, SPACE];

  /** Aktuell gewählte Themen als Liste (Backing-Store bleibt der leerzeichengetrennte `config.themes`-String). */
  get selectedThemes(): string[] {
    return (this.config.themes || '').trim().split(/\s+/).filter(Boolean);
  }
  private setSelectedThemes(themes: string[]): void {
    // Duplikate raus, Reihenfolge erhalten.
    this.config.themes = [...new Set(themes)].join(' ');
    this.saveConfig();
  }

  /** Optionen fürs Dropdown: noch nicht gewählte Themen, nach dem Suchtext gefiltert. */
  get filteredThemes(): string[] {
    const q = this.themeInput.trim().toLowerCase();
    const selected = new Set(this.selectedThemes);
    return this.allThemes
      .filter(t => !selected.has(t))
      .filter(t => !q || t.toLowerCase().includes(q));
  }

  /** Lädt die verfügbaren Themen einmalig (best effort; Freitext bleibt auch ohne Liste möglich). */
  private loadAllThemes(): void {
    if (this.allThemes.length) return;
    this.puzzleService.getAllThemes().subscribe({
      next: t => this.allThemes = t,
      error: () => {},   // Liste optional — manuelles Tippen funktioniert weiterhin
    });
  }

  /** Thema aus dem Dropdown übernommen. */
  onThemeSelected(event: MatAutocompleteSelectedEvent): void {
    this.addThemeValue(event.option.value);
    this.themeInput = '';
  }

  /** Frei getipptes Thema per Enter/Komma/Leertaste übernehmen (nicht in der Vorschlagsliste). */
  onThemeInputTokenEnd(event: MatChipInputEvent): void {
    const value = (event.value || '').trim();
    if (value) this.addThemeValue(value);
    event.chipInput?.clear();
    this.themeInput = '';
  }

  private addThemeValue(theme: string): void {
    const normalized = theme.trim();
    if (!normalized || this.selectedThemes.includes(normalized)) return;
    this.setSelectedThemes([...this.selectedThemes, normalized]);
  }

  /** Thema-Chip entfernen. */
  removeTheme(theme: string): void {
    this.setSelectedThemes(this.selectedThemes.filter(t => t !== theme));
  }

  lives = 3;
  level = 0;
  solved = 0;
  maxRatingReached = 0;
  isNewHighscore = false;
  highscore = 0;

  // Board theme
  boardTheme = 'brown';
  readonly boardThemes = BOARD_THEMES;

  pieceSet = 'cburnett';
  themeMode: ThemeMode = 'fixed';
  readonly pieceSets = PIECE_SETS;

  // Help
  showHelp = false;

  // Eval
  showEval = false;
  evalLoading = false;
  initialEval = '';

  // Session timer — beide Stoppuhren zählen nur sichtbare Tab-Zeit (Hintergrund pausiert).
  sessionSeconds = 0;
  private sessionInterval?: ReturnType<typeof setInterval>;
  private readonly sessionStopwatch = new VisibilityStopwatch();
  private readonly puzzleStopwatch = new VisibilityStopwatch();

  // Session history
  sessionHistory: EndlessSession[] = [];
  currentSessionMistakes: number[] = [];
  currentSessionPuzzles: EndlessPuzzleAttempt[] = [];
  /** ID des zuvor gespielten Puzzles (für „vorheriges Puzzle teilen"). */
  private previousPuzzleId: number | null = null;

  // Fasttrack-State (in eigene Klasse ausgelagert); Template/interne Reads gehen über die Getter/Setter.
  private readonly fasttrack = new EndlessFasttrackState();
  /** T1-Mittelwert (ngModel, zwei-Wege). */
  get fasttrackAvgFirst(): number { return this.fasttrack.avgFirst; }
  set fasttrackAvgFirst(v: number) { this.fasttrack.avgFirst = v; }
  /** T2-Mittelwert (ngModel, zwei-Wege). */
  get fasttrackAvgSecond(): number { return this.fasttrack.avgSecond; }
  set fasttrackAvgSecond(v: number) { this.fasttrack.avgSecond = v; }
  get fasttrackAutoFirst(): number { return this.fasttrack.autoFirst; }
  get fasttrackAutoSecond(): number { return this.fasttrack.autoSecond; }
  get fasttrackPhase1Step(): number { return this.fasttrack.phase1Step; }
  get fasttrackPhase2Step(): number { return this.fasttrack.phase2Step; }

  // Dynamic rating
  _currentMinRating = 0;

  // Resume
  activeGameState: any = null;

  // Archive
  lastSessionId: number | null = null;
  lastSessionArchived = false;
  /** True, wenn die Game-Over-Ansicht einen abgeschlossenen Lauf aus der History zeigt
   * (Aufruf mit ?session=ID) und nicht einen gerade beendeten Run. */
  historyView = false;
  archiving = false;

  /** Der laufende/zuletzt beendete Run wurde bereits als Session aufgezeichnet. Verhindert
   *  Doppel-Posts, wenn mehrere Pfade (Game-Over → „Weiter", Analyse-Absprung, Verlassen der
   *  Seite) dieselbe Session aufzeichnen wollen. Wird beim Start/Resume eines Runs zurückgesetzt. */
  private sessionRecorded = false;

  // Puzzle DB range
  puzzleRange: PuzzleRatingRange = { min: 100, max: 3000 };

  // Board (Brett/Viz/Lös-State in BasePuzzleSolver)
  puzzle: PuzzleDto | null = null;
  private initialFen = '';
  /**
   * Gauntlet-Kette: die beim Run-Start (serverseitig per getRandomBatch) generierte, vollständig
   * vordefinierte Puzzle-Folge entlang der ~logarithmischen Kurve. `chainIndex` zeigt auf das aktuell
   * gespielte Puzzle. Lösen UND Fehler rücken eine Stelle weiter (höher); ein Fehler kostet ein Leben.
   * Die Kette liegt lokal (Offline/Refresh → exakt dasselbe Puzzle); `chainIndex`/`seed` werden
   * zusätzlich im (synchronisierten) Spielstand abgelegt, der Seed + die Ketten-IDs zusätzlich beim
   * Session-Record am Server (für ein späteres Replay).
   */
  private chain: PuzzleDto[] = [];
  chainIndex = 0;
  /** Eindeutiger Run-Seed (crypto.randomUUID) — identifiziert die Kette lokal + am Server. */
  private seed = '';
  /** Lokal gecachte Kette (Offline-Start / Resume); identisch zu {@link chain} während eines Runs. */
  private offlinePool: PuzzleDto[] = [];
  /** Wird bei jedem Run-Start (startGame/resumeGame) erhöht. Ein im Hintergrund laufender
   *  prefetchRun darf den Pool/Seed eines inzwischen gestarteten Runs NICHT überschreiben →
   *  späte Prefetch-Antwort wird verworfen, wenn die Generation nicht mehr passt. */
  private runGeneration = 0;
  reviewingWrongPuzzle = false;
  gaveUp = false;
  private puzzleLifeLost = false;
  private puzzleStartTime = 0;
  elapsedSeconds = 0;

  // Zuletzt gelöstes Puzzle — für „Letztes Puzzle analysieren" (bleibt auch nach dem
  // Auto-Advance auf das nächste Puzzle erhalten, da der SOLVED-Status nur kurz sichtbar ist).
  lastSolvedPuzzleId: number | null = null;
  private lastSolvedFen: string | null = null;
  private lastSolvedMoves = '';
  private lastSolvedOrientation: 'white' | 'black' = 'white';

  constructor(
    private puzzleService: PuzzleService,
    stockfish: StockfishService,
    private storage: EndlessStorageService,
    public authService: AuthService,
    private prefs: PreferencesService,
    public router: Router,
    private route: ActivatedRoute,
    private dialog: MatDialog,
    private translate: TranslateService,
    private offline: OfflineService,
    private snackbar: SnackbarService,
    private offlineQueue: OfflineQueueService,
    private longSolve: LongSolveService
  ) {
    super(stockfish);
    this.state = 'CONFIG';
    this.loadSettingsOpen();
    // Load board theme from preferences service
    this.boardTheme = this.prefs.boardTheme;
    this.pieceSet = this.prefs.pieceSet;
    this.themeMode = this.prefs.themeMode;
    this.visualizationMode = this.prefs.visualization;
    this.vizArrowEnabled = this.prefs.vizArrow;
    // 1. Load from localStorage immediately (no latency)
    this.config = this.storage.loadConfig(this.config);
    this.highscore = this.storage.loadHighscore();
    this.sessionHistory = this.storage.loadSessionHistory();
    this.offlinePool = this.storage.loadOfflinePool();   // evtl. vorhandener Run-Cache (Offline/Resume)
    this.computeFasttrackSteps();

    // Load local active game state for immediate display
    const localGame = this.storage.loadActiveGameLocal();
    if (localGame) this.activeGameState = localGame;
    // Defensiv: ein resumebarer Run mit 0 Lives existiert nicht — entweder
    // Zombie-State aus aelterer Logik oder Race vor endGame(). Aufraeumen.
    if (this.activeGameState && this.activeGameState.lives <= 0) {
      this.activeGameState = null;
      this.storage.saveActiveGameLocal(null);
    }

    // 2. Load from server (async) and merge
    this.storage.loadFromServer().subscribe(serverData => {
      if (serverData) {
        if (serverData.progress || serverData.sessions.length > 0) {
          const merged = this.storage.mergeServerData(
            this.config, this.highscore, this.sessionHistory, serverData
          );
          this.config = this.storage.loadConfig(merged.config);
          this.highscore = merged.highscore;
          this.sessionHistory = merged.history;
          this.computeFasttrackSteps();

          // Server active game state takes priority
          if (serverData.progress?.activeGameState) {
            try { this.activeGameState = JSON.parse(serverData.progress.activeGameState); } catch {}
          }
          // Auch Server-State auf 0-Lives-Zombie pruefen (Legacy aus aelteren Builds).
          if (this.activeGameState && this.activeGameState.lives <= 0) {
            this.activeGameState = null;
            this.storage.saveActiveGameLocal(null);
            this.storage.saveProgressImmediate(this.config, this.highscore, null);
          }
        } else {
          // Server empty: migrate localStorage data up (one-time)
          this.storage.migrateLocalToServer(this.config, this.highscore, this.sessionHistory);
        }
      }
    });

    this.puzzleService.getRatingRange().subscribe({
      next: r => {
        this.puzzleRange = r;
        this.clampConfig();
        // Schon beim Öffnen der Config einen Run vorab laden, damit Endless später
        // auch offline gestartet werden kann (nur online + wenn noch kein Cache da ist).
        if (navigator.onLine && this.offlinePool.length === 0) this.prefetchRun();
      },
      error: () => {}
    });
    this.stockfish.init().catch(() => {});
  }

  ngOnInit(): void {
    // Verfügbare Themen fürs Auswahl-Dropdown laden (unabhängig vom Einstiegs-Modus).
    this.loadAllThemes();

    // History-Detail (?session=ID): abgeschlossenen Lauf wie den Game-Over-Screen anzeigen.
    // Stats + Puzzle-Review werden aus dem persistierten Lauf rekonstruiert.
    const sessionParam = this.route.snapshot.queryParamMap.get('session');
    if (sessionParam) {
      const id = parseInt(sessionParam, 10);
      if (id > 0) { this.loadHistorySession(id); return; }
    }

    // Rückkehr aus dem Analysemodus nach verlorenstem letzten Herz (?gameover=1):
    // Gameover-Snapshot aus sessionStorage wiederherstellen und Zusammenfassungs-Screen zeigen.
    if (this.route.snapshot.queryParamMap.get('gameover') === '1') {
      const raw = sessionStorage.getItem('rookhub_endless_gameover');
      if (raw) {
        try {
          const s = JSON.parse(raw);
          sessionStorage.removeItem('rookhub_endless_gameover');
          this.maxRatingReached = s.maxRatingReached ?? 0;
          this.solved = s.solved ?? 0;
          this.level = s.level ?? 0;
          this.lives = s.lives ?? 0;
          this.sessionSeconds = s.sessionSeconds ?? 0;
          this.isNewHighscore = s.isNewHighscore ?? false;
          this.currentSessionMistakes = s.currentSessionMistakes ?? [];
          this.currentSessionPuzzles = s.currentSessionPuzzles ?? [];
        } catch {}
      }
      // lastSessionId könnte inzwischen asynchron eingetroffen sein (recordSession-Subscribe)
      const pendingSid = sessionStorage.getItem('rookhub_endless_pending_sid');
      if (pendingSid) {
        this.lastSessionId = parseInt(pendingSid, 10) || null;
        sessionStorage.removeItem('rookhub_endless_pending_sid');
      }
      // Dieser Run wurde vor dem Absprung in die Analyse bereits via endGame() aufgezeichnet
      // → nicht erneut posten (sonst Doppel-Eintrag durch das ngOnDestroy-Sicherheitsnetz).
      this.sessionRecorded = true;
      this.state = 'GAME_OVER';
      return;
    }
    // Rückkehr aus dem Analysemodus (?resume=1): laufenden Run direkt fortsetzen statt in der
    // Übersicht zu landen. Bei normalem Einstieg (kein resume-Param) bleibt der Resume-Banner,
    // damit man Fortsetzen/Archivieren wählen kann. Der Konstruktor hat 0-Leben-Zombies bereits
    // bereinigt → activeGameState != null bedeutet: noch Leben übrig.
    if (this.route.snapshot.queryParamMap.get('resume') === '1' && this.activeGameState && this.state === 'CONFIG') {
      this.resumeGame();
    }
  }

  ngOnDestroy(): void {
    this.stopSessionTimer();
    this.abortSolver();
    clearCrazyStyles();
    clearVisualizationHide();
    this.rescueUnrecordedRun();
  }

  /** Tab/Fenster wird geschlossen oder in den Hintergrund verschoben. Best-effort-Variante des
   *  Sicherheitsnetzes für den Fall, dass ngOnDestroy nicht (rechtzeitig) läuft. */
  @HostListener('window:pagehide')
  onPageHide(): void { this.rescueUnrecordedRun(); }

  /** Sicherheitsnetz: Ein beendeter Lauf (0 Leben), den der User verlässt, BEVOR er im Game-Over
   *  „Weiter" geklickt hat, würde sonst nirgends landen — bei 0 Leben ist der aktive Lauf bereits
   *  vom Server gelöscht und endGame() lief nie. Hier wird er deshalb noch aufgezeichnet. Reine
   *  History-Ansichten (?session=ID) sind ausgenommen; ensureSessionRecorded() ist idempotent. */
  private rescueUnrecordedRun(): void {
    if (this.historyView) return;
    if (this.lives <= 0 && !this.sessionRecorded) this.ensureSessionRecorded();
  }

  // --- Config ---

  get currentMinRating(): number { return this._currentMinRating; }
  get currentMaxRating(): number { return this._currentMinRating + ENDLESS_RATING_WINDOW; }
  get currentRating(): number { return this._currentMinRating; }

  get currentPhaseLabel(): string {
    if (this.isFirstRun) {
      // Erster Lauf: Phasengrenzen folgen der steilen Erst-Lauf-Kurve (Anker 15/30),
      // step = durchschnittlicher Rating-Zuwachs je Puzzle in dieser Phase.
      const s = this.config.startElo;
      if (this.chainIndex < FIRST_RUN_ANCHOR1_INDEX)
        return this.translate.instant('endless.game.phaseLabel', { phase: 1, step: Math.round((FIRST_RUN_ANCHOR1_RATING - s) / FIRST_RUN_ANCHOR1_INDEX) });
      if (this.chainIndex < FIRST_RUN_ANCHOR2_INDEX)
        return this.translate.instant('endless.game.phaseLabel', { phase: 2, step: Math.round((FIRST_RUN_ANCHOR2_RATING - FIRST_RUN_ANCHOR1_RATING) / (FIRST_RUN_ANCHOR2_INDEX - FIRST_RUN_ANCHOR1_INDEX)) });
      return this.translate.instant('endless.game.phaseLabel', { phase: 3, step: CHAIN_FLAT_STEP });
    }
    if (this.chainIndex < CHAIN_T1_INDEX) return this.translate.instant('endless.game.phaseLabel', { phase: 1, step: this.fasttrackPhase1Step });
    if (this.chainIndex < CHAIN_T2_INDEX) return this.translate.instant('endless.game.phaseLabel', { phase: 2, step: this.fasttrackPhase2Step });
    return this.translate.instant('endless.game.phaseLabel', { phase: 3, step: 20 });
  }

  /**
   * Erster Lauf des Users (keine abgeschlossene Session in der Historie) → bewusst steile,
   * schnell tödliche Kurve (2000 nach 15, 3000 nach 30 Puzzles), bis genug Daten für die
   * adaptive Kurve vorliegen.
   */
  get isFirstRun(): boolean { return this.sessionHistory.length === 0; }

  /** Vorschau der Ketten-Kurve: Rating an markanten Stellen (Start, T1 ≈ Puzzle 6, T2 ≈ Puzzle 21, Block-Ende). */
  get chainPreview(): { puzzle: number; rating: number }[] {
    const s = this.config.startElo, t1 = this.fasttrackAvgFirst, t2 = this.fasttrackAvgSecond;
    const fr = this.isFirstRun;
    const marks = fr
      ? [0, FIRST_RUN_ANCHOR1_INDEX, FIRST_RUN_ANCHOR2_INDEX, ENDLESS_CHAIN_BLOCK - 1]
      : [0, CHAIN_T1_INDEX, CHAIN_T2_INDEX, ENDLESS_CHAIN_BLOCK - 1];
    return marks.map(i => ({ puzzle: i + 1, rating: chainRatingAt(i, s, t1, t2, fr) }));
  }

  private clampConfig(): void {
    this.config.startElo = Math.max(this.puzzleRange.min, Math.min(this.puzzleRange.max, this.config.startElo));
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

  setVisualizationLevel(level: number): void {
    this.visualizationMode = level;
    this.prefs.setVisualization(level);
    if (this.puzzle && this.isSolving) this.setupPuzzle(this.puzzle);  // laufendes Puzzle neu starten
  }

  setVizArrowEnabled(val: boolean): void {
    this.vizArrowEnabled = val;
    if (!val) this.clearVizOpponentArrow();
    this.prefs.setVizArrow(val);
  }

  openSettingsDialog(): void {
    const ref = this.dialog.open(PuzzleSettingsDialogComponent, {
      data: {
        mode: 'endless',
        boardTheme: this.prefs.boardTheme,
        pieceSet: this.prefs.pieceSet,
        themeMode: this.themeMode,
        visualizationMode: this.visualizationMode,
        vizArrowEnabled: this.vizArrowEnabled,
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
    });
  }

  // ===== Hooks für BasePuzzleSolver =====
  protected override get depth(): number { return this.config.stockfishDepth; }

  protected override onSetupStart(): void {
    const applied = applyThemeMode(this.themeMode, this.prefs.boardTheme, this.prefs.pieceSet);
    this.boardTheme = applied.boardTheme;
    this.pieceSet = applied.pieceSet;
  }

  protected override onSolvingBegins(): void {
    this.initialFen = this.chess.fen();
    this.puzzleStartTime = Date.now();   // Wanduhr-Timestamp (für startedAt der Session-Aufzeichnung)
    this.puzzleStopwatch.start();        // gewertete Dauer = nur aktive Tab-Zeit
    this.elapsedSeconds = 0;
  }

  protected override handleSolved(alternative: boolean): void { this.puzzleSolved(alternative); }
  protected override handleFailed(): void { this.loseLife(); }

  // --- Game lifecycle ---

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

  startGame(): void {
    ++this.runGeneration;   // laufenden Hintergrund-Prefetch für diesen Run entwerten
    this.clampConfig();
    this.saveConfig();
    this.lives = 3;
    this.level = 0;
    this.solved = 0;
    this.chainIndex = 0;
    this.seed = this.newSeed();
    this._currentMinRating = this.config.startElo;
    this.maxRatingReached = this.config.startElo;
    this.isNewHighscore = false;
    this.sessionSeconds = 0;
    this.currentSessionMistakes = [];
    this.currentSessionPuzzles = [];
    this.previousPuzzleId = null;
    this.activeGameState = null;
    this.lastSessionId = null;
    this.lastSessionArchived = false;
    this.sessionRecorded = false;   // frischer Run → noch nicht aufgezeichnet
    this.computeFasttrackSteps();   // fasttrackAvgFirst/Second = T1/T2 für die Ketten-Kurve
    this.startSessionTimer();

    if (navigator.onLine) {
      // Volle Kette serverseitig generieren (ein Batch) und beim Client ablegen.
      this.state = 'LOADING';
      this.chain = [];
      this.generateChainBlock(0, () => this.loadCurrent());
    } else if (this.offlinePool.length > 0) {
      // Offline-Start: vorab geladene Kette nutzen.
      this.chain = this.offlinePool;
      this.storage.saveOfflinePool(this.chain);
      this.storage.saveChainSeed(this.seed);
      this.persistRun();
      this.loadCurrent();
    } else {
      this.stopSessionTimer();
      this.snackbar.info(this.translate.instant('endless.offlineNoCache'), { action: 'common.ok', duration: 5000 });
      this.state = 'CONFIG';
    }
  }

  /**
   * Generiert (online) einen Ketten-Block für die absoluten Indizes [startIndex, startIndex+count)
   * via getRandomBatch entlang der Kurven-Fenster und hängt ihn an die Kette an (bzw. ersetzt sie
   * bei startIndex 0). `then` wird nach dem Eintreffen aufgerufen. Bleibt leer/leerer Block:
   * Run-Start → zurück zur Config; Verlängerung → „You win".
   */
  /** Themen-Filter für die Ketten-Generierung: „schwächste Themen" (ODER) hat Vorrang vor dem manuellen Themenfeld (UND). */
  private batchThemes(): { themes?: string; themesAny?: string } {
    // Beide Quellen ODER-verknüpft (themesAny): ein Puzzle muss MINDESTENS EINS der Themen tragen.
    // (Mehrere Themen UND-verknüpft hätten kaum Treffer — „fork pin" soll fork ODER pin liefern.)
    const src = this.config.worstTags && this.worstThemes.length
      ? this.worstThemes.join(' ')
      : this.config.themes.trim();
    return { themesAny: src || undefined };
  }

  /** Lädt die schwächsten Themen (nur eingeloggt + Option aktiv), ruft danach cb. */
  private ensureWorstThemes(cb: () => void): void {
    if (!this.config.worstTags || !this.authService.isLoggedIn || this.worstThemes.length) { cb(); return; }
    this.puzzleService.getWorstThemes().subscribe({
      next: t => { this.worstThemes = t; cb(); },
      error: () => cb(),
    });
  }

  private generateChainBlock(startIndex: number, then?: () => void, count = ENDLESS_CHAIN_BLOCK): void {
    const windows = buildChainWindows(
      this.config.startElo, this.fasttrackAvgFirst, this.fasttrackAvgSecond, this.puzzleRange.max, count, startIndex, this.isFirstRun
    );
    this.ensureWorstThemes(() => {
    const { themes, themesAny } = this.batchThemes();
    this.puzzleService.getRandomBatch(windows, themes, false, themesAny).subscribe({
      next: pool => {
        const block = pool || [];
        if (block.length === 0) {
          if (startIndex === 0) {
            this.stopSessionTimer();
            this.snackbar.info(this.translate.instant('endless.offlineNoCache'), { action: 'common.ok', duration: 5000 });
            this.state = 'CONFIG';
          } else {
            this.winRun();   // online, aber keine weiteren Puzzles mehr → Kette durchgespielt
          }
          return;
        }
        this.chain = startIndex === 0 ? block : this.chain.concat(block);
        this.offlinePool = this.chain;
        this.storage.saveOfflinePool(this.chain);
        this.storage.saveChainSeed(this.seed);
        this.persistRun();
        if (then) then();
      },
      error: () => {
        // Während der Generierung offline geworden: vorhandene Kette weiterspielen bzw. „You win".
        if (then) then();
      }
    });
    });
  }

  /**
   * Lädt im Hintergrund eine Kette vorab und legt sie im Storage ab, damit Endless auch offline
   * gestartet werden kann. Invalidiert den Ketten-Token (dieser Cache gehört zu keinem Run).
   */
  private prefetchRun(): void {
    const gen = this.runGeneration;
    this.computeFasttrackSteps();
    const windows = buildChainWindows(
      this.config.startElo, this.fasttrackAvgFirst, this.fasttrackAvgSecond, this.puzzleRange.max, ENDLESS_CHAIN_BLOCK, 0, this.isFirstRun
    );
    if (!windows.length) return;
    this.ensureWorstThemes(() => {
      const { themes, themesAny } = this.batchThemes();
      this.puzzleService.getRandomBatch(windows, themes, false, themesAny).subscribe({
        next: pool => {
          // Inzwischen ein Run gestartet? Dann gehört dessen Pool/Seed ihm — Prefetch verwerfen.
          if (gen !== this.runGeneration) return;
          this.offlinePool = pool || [];
          this.storage.saveOfflinePool(this.offlinePool);
          this.storage.saveChainSeed('');   // Prefetch gehört zu keinem laufenden Run
        },
        error: () => { /* offline/Fehler: bestehenden Pool behalten */ }
      });
    });
  }

  private persistRun(): void {
    this.syncActiveGameToServer();
  }

  /** Eindeutiger Seed für einen neuen Lauf (crypto.randomUUID, Fallback Zeit+Zufall). */
  private newSeed(): string {
    try {
      const c = (globalThis as any).crypto;
      if (c?.randomUUID) return c.randomUUID();
    } catch {}
    return `${Date.now().toString(36)}-${Math.floor(Math.random() * 1e9).toString(36)}`;
  }

  resumeGame(): void {
    if (!this.activeGameState) return;
    ++this.runGeneration;   // laufenden Hintergrund-Prefetch entwerten
    const g = this.activeGameState;
    this.lives = g.lives ?? 3;
    this.solved = g.solved ?? 0;
    this.chainIndex = g.chainIndex ?? 0;
    this.level = g.level ?? this.chainIndex;
    this.seed = g.seed ?? '';
    this._currentMinRating = g.currentMinRating ?? this.config.startElo;
    this.maxRatingReached = g.maxRatingReached ?? this._currentMinRating;
    this.sessionSeconds = g.sessionSeconds ?? 0;
    this.currentSessionMistakes = g.mistakes ?? [];
    this.currentSessionPuzzles = (g.puzzleAttempts ?? []).map((p: any) => ({
      puzzleNumber: p.puzzleNumber,
      puzzleId: p.puzzleId,
      lichessId: p.lichessId ?? '',
      rating: p.rating,
      solved: p.solved,
      themes: undefined,
      startedAt: 0,
      endedAt: 0,
    }));
    this.isNewHighscore = false;
    this.lastSessionId = null;
    this.lastSessionArchived = false;
    this.sessionRecorded = false;   // fortgesetzter Run läuft weiter → noch nicht aufgezeichnet
    this.computeFasttrackSteps();
    // Timer dort fortsetzen, wo er aufgehört hat (gewertet wird ab jetzt nur aktive Tab-Zeit).
    this.startSessionTimer(this.sessionSeconds);

    // Kette wiederherstellen: lokal nur, wenn der Token zu DIESEM Run passt (Refresh auf demselben
    // Gerät → exakt dasselbe Puzzle). Sonst (anderes Gerät / Cache überschrieben) die Kette von vorne
    // bis über die aktuelle Position neu generieren, damit chainIndex weiterhin korrekt zeigt.
    const localOk = !!this.seed && this.storage.loadChainSeed() === this.seed
      && this.offlinePool.length > this.chainIndex;
    if (localOk) {
      this.chain = this.offlinePool;
      this.loadCurrent();
    } else if (navigator.onLine) {
      this.state = 'LOADING';
      this.chain = [];
      this.seed = this.seed || this.newSeed();
      this.generateChainBlock(0, () => this.loadCurrent(), this.chainIndex + ENDLESS_CHAIN_BLOCK);
    } else if (this.offlinePool.length > this.chainIndex) {
      // Offline ohne passenden Token, aber genug Puzzles vorhanden → bestmöglich fortsetzen.
      this.chain = this.offlinePool;
      this.loadCurrent();
    } else {
      this.stopSessionTimer();
      this.snackbar.info(this.translate.instant('endless.offlineNoCache'), { action: 'common.ok', duration: 5000 });
      this.state = 'CONFIG';
    }
  }

  archiveAndStartNew(): void {
    if (!this.activeGameState) { this.startGame(); return; }
    const g = this.activeGameState;
    const session: EndlessSession = {
      timestamp: Date.now(),
      config: { ...this.config },
      totalSolved: g.solved ?? 0,
      maxRating: g.maxRatingReached ?? 0,
      durationSeconds: g.sessionSeconds ?? 0,
      mistakeAtRatings: g.mistakes ?? [],
      // Seed aus dem Spielstand; die Kette liegt hier nur lokal (offlinePool), falls Token passt.
      seed: g.seed ?? this.seed,
      chainPuzzleIds: this.offlinePool.length && this.storage.loadChainSeed() === (g.seed ?? this.seed)
        ? this.offlinePool.map(p => p.id).join(',') : undefined
    };
    this.storage.recordSessionToServer(session).subscribe(id => {
      if (id && this.authService.isLoggedIn) {
        this.storage.archiveSession(id).subscribe();
      }
      this.activeGameState = null;
      this.storage.saveActiveGameLocal(null);
      this.storage.saveProgressImmediate(this.config, this.highscore, null);
      this.sessionHistory = this.storage.recordSession(this.sessionHistory, session);
      this.startGame();
    });
  }

  archiveLastSession(): void {
    if (!this.lastSessionId || this.archiving) return;
    this.archiving = true;
    this.storage.archiveSession(this.lastSessionId).subscribe(() => {
      this.lastSessionArchived = true;
      this.archiving = false;
    });
  }

  playAgain(): void {
    // Beendeten Run vollständig verwerfen (in-memory + Speicher), damit der Config-Screen
    // ihn nicht erneut zum Fortsetzen anbietet — sonst „Resume → Aufgeben → Nochmal →
    // wieder fortsetzbar"-Schleife (nur der Storage war genullt, der in-memory-Snapshot nicht).
    this.activeGameState = null;
    this.storage.saveActiveGameLocal(null);
    this.state = 'CONFIG';
    this.lastSessionId = null;
    this.lastSessionArchived = false;
    this.computeFasttrackSteps();
  }

  backToPuzzles(): void { this.router.navigate(['/puzzles']); }

  backToHistory(): void { this.router.navigate(['/puzzles/endless/history']); }

  openPuzzle(id: number): void {
    this.router.navigate(['/puzzles', id]);
  }

  /** Lädt einen abgeschlossenen Lauf aus der History und zeigt ihn im Game-Over-Layout an
   * (gleiche Detail-Ansicht wie direkt nach Spielende). */
  private loadHistorySession(id: number): void {
    this.historyView = true;
    this.state = 'LOADING';
    this.storage.getSessionDetail(id).subscribe(detail => {
      if (!detail) { this.router.navigate(['/puzzles/endless/history']); return; }
      this.maxRatingReached = detail.maxRating;
      this.solved = detail.totalSolved;
      this.sessionSeconds = detail.durationSeconds;
      this.currentSessionMistakes = (detail.mistakeAtRatings || '')
        .split(',').map(Number).filter(n => !isNaN(n));
      this.currentSessionPuzzles = (detail.puzzles ?? []).map((p, i) => ({
        puzzleNumber: i + 1,
        puzzleId: p.puzzleId,
        lichessId: p.lichessId ?? '',
        rating: p.rating,
        solved: p.solved,
        startedAt: 0,
        endedAt: 0,
      }));
      // „Level" = Anzahl gespielter Puzzles (Kettenposition); fällt auf TotalSolved zurück,
      // falls für einen Altlauf keine Einzel-Puzzles persistiert sind.
      this.level = this.currentSessionPuzzles.length || detail.totalSolved;
      this.isNewHighscore = false;
      this.lastSessionId = detail.id;
      this.lastSessionArchived = detail.isArchived;
      this.sessionRecorded = true;   // reine History-Ansicht → nie (neu) aufzeichnen
      this.state = 'GAME_OVER';
    });
  }

  // --- Loading (Gauntlet: vordefinierte Kette) ---

  /** Lädt das aktuelle Ketten-Puzzle (`chain[chainIndex]`); am Kettenende wird verlängert/gewonnen. */
  private loadCurrent(): void {
    this.state = 'LOADING';
    this.alternativeSolve = false;
    this.showEval = false;
    this.initialEval = '';
    this.currentEval = '';

    if (this.chainIndex >= this.chain.length) { this.handleChainEnd(); return; }
    this.onPuzzleLoaded(this.chain[this.chainIndex]);
  }

  /** Kettenende erreicht: online → nächsten Block nachgenerieren; offline → „You win". */
  private handleChainEnd(): void {
    if (navigator.onLine) {
      this.state = 'LOADING';
      this.generateChainBlock(this.chain.length, () => this.loadCurrent());
    } else {
      this.winRun();
    }
  }

  /** Kette vollständig durchgespielt (offline am Ende) — Run als Sieg abschließen. */
  private winRun(): void {
    this.stopSessionTimer();
    this.checkHighscore();
    this.ensureSessionRecorded();
    this.storage.saveActiveGameLocal(null);
    this.storage.saveProgressImmediate(this.config, this.highscore, null);
    this.state = 'WON';
  }

  /** Eine Stelle in der Kette weiterrücken (nach Lösen ODER Fehler) und das nächste Puzzle laden. */
  private advance(): void {
    this.chainIndex++;
    this.level = this.chainIndex;
    this.persistRun();
    this.loadCurrent();
  }

  private onPuzzleLoaded(puzzle: PuzzleDto): void {
    this.puzzleLifeLost = false;
    // Bisher angezeigtes Puzzle als „vorheriges" merken (für Teilen-Dialog).
    if (this.puzzle && this.puzzle.id !== puzzle.id) this.previousPuzzleId = this.puzzle.id;
    this.puzzle = puzzle;
    this._currentMinRating = puzzle.rating;   // Anzeige/Fehler-Logging am tatsächlichen Puzzle-Rating
    this.trackMaxRating(puzzle.rating);
    this.setupPuzzle(puzzle);
  }

  // --- Puzzle setup ---

  /** On-the-fly klassifizierter erster Löserzug (Schach/Schlag/ruhig) — Basis der gestuften Tipps (wie Standard-Modus). */
  private firstMoveHint: FirstMoveHint | null = null;

  /**
   * On-the-fly-Tipps für die Standard-Puzzles des Endless-Modus (identisch zum Standard-Solver):
   * Stufe 1 = Check-Capture-Threat-Hinweis je Zugtyp, Stufe 2 = welche Figur zieht, Stufe 3 = der Zug (SAN).
   */
  override get availableHints(): string[] {
    const h = this.firstMoveHint;
    if (!h) return [];
    const t = (k: string, p?: object) => this.translate.instant(k, p) as string;
    const tier1 = h.type === 'check' ? t('puzzles.hints.t1Check')
      : h.type === 'capture' ? t('puzzles.hints.t1Capture')
      : t('puzzles.hints.t1Quiet');
    const PIECE: Record<string, string> = { p: 'pawn', n: 'knight', b: 'bishop', r: 'rook', q: 'queen', k: 'king' };
    const piece = t('puzzles.hints.pieces.' + (PIECE[h.pieceType] ?? 'piece'));
    return [tier1, t('puzzles.hints.t2Piece', { piece }), t('puzzles.hints.t3Move', { move: h.san })];
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
        this.snackbar.success(this.translate.instant(next ? 'puzzles.hints.flagSaved' : 'puzzles.hints.flagCleared'), { duration: 2000 });
      },
      error: () => {
        this.flagSaving = false;
        this.snackbar.warn(this.translate.instant('puzzles.hints.flagError'), { duration: 3000 });
      }
    });
  }

  private setupPuzzle(puzzle: PuzzleDto): void {
    this.reviewingWrongPuzzle = false;
    this.gaveUp = false;
    this.reviewMode = false;
    this.hintLevel = 0;
    this.firstMoveHint = classifyStandardFirstMove(puzzle.fen, puzzle.moves);
    // Lös-Automat (Setup, Zug-Handling, Stockfish, Viz) aus BasePuzzleSolver.
    this.setupSolver(puzzle.fen, puzzle.moves, 0);
  }

  private puzzleSolved(alternative: boolean): void {
    this.alternativeSolve = alternative;
    this.state = 'SOLVED';
    this.solved++;
    // Pro-Puzzle-Lösezeit JETZT festhalten (vor einer evtl. Nachfrage, die Zeit kosten würde).
    const perPuzzleSeconds = this.puzzleStartTime > 0 ? this.puzzleStopwatch.elapsedSeconds : 0;
    if (this.puzzle) {
      this.pushSessionPuzzle(true);
      // Für „Letztes Puzzle analysieren" merken (überlebt den Auto-Advance).
      this.lastSolvedPuzzleId = this.puzzle.id;
      this.lastSolvedFen = this.puzzle.fen;
      this.lastSolvedMoves = this.puzzle.moves;
      this.lastSolvedOrientation = this.orientation;
    }
    this.syncActiveGameToServer();
    this.updateBoard();
    this.enterSolutionReview();

    // Auffällig lange Lösezeit (Tab lag vermutlich offen) → nachfragen, bevor die Zeit gewertet wird;
    // der Dialog ist modal und blockiert „Weiter" dahinter. Aufzeichnen + Auto-Advance erst danach.
    this.longSolve.resolve(perPuzzleSeconds).subscribe(seconds => {
      this.recordAttempt(true, seconds);

      // Bei alternativer Lösung NICHT auto-advancen — der Spieler wählt Weiter / Lösung zeigen.
      if (alternative) return;
      // Bei 0 Herzen (gelöstes Retry am tödlichen Puzzle) gibt es kein nächstes Puzzle mehr →
      // kein Countdown; „Weiter" führt über continueAfterSolve ins Game Over.
      if (this.lives <= 0) return;
      // Kurzer, sichtbarer Countdown bis zum nächsten Puzzle — per „Weiter"-Klick überspringbar.
      this.startSolvedCountdown(() => this.continueAfterSolve());
    });
  }

  continueAfterSolve(): void {
    this.reviewMode = false;
    this.stopCountdown();
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);  // pending Auto-Advance verwerfen
    // 0 Herzen = Lauf vorbei. Das tödliche Puzzle darf per Retry nochmal gespielt werden,
    // aber ein gelöstes Retry belebt den Run NICHT wieder (kein Weiterspielen mit 0 Herzen).
    if (this.lives <= 0) {
      this.endGame();
      return;
    }
    this.advance();
  }

  continueAfterWrong(): void {
    this.reviewMode = false;
    this.reviewingWrongPuzzle = false;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    if (this.lives <= 0) {
      this.endGame();
    } else {
      // Gauntlet: ein Fehler kostet ein Leben UND rückt zum nächsten (höheren) Ketten-Puzzle.
      this.advance();
    }
  }

  /** Aktuelles Puzzle (z.B. nach dem Aufgeben) im Analysemodus öffnen. */
  analyzeCurrentPuzzle(): void {
    if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; }
    if (!this.puzzle) return;

    if ((this.state === 'FAILED' || this.reviewingWrongPuzzle) && this.lives <= 0) {
      // Letztes Herz verloren → Run ist vorbei. Session jetzt aufzeichnen, Snapshot sichern
      // und nach der Analyse direkt zum Zusammenfassungs-Screen zurückkehren (gameover=1).
      this.endGame();
      const snap = {
        maxRatingReached: this.maxRatingReached,
        solved: this.solved,
        level: this.level,
        lives: this.lives,
        sessionSeconds: this.sessionSeconds,
        isNewHighscore: this.isNewHighscore,
        currentSessionMistakes: [...this.currentSessionMistakes],
        currentSessionPuzzles: [...this.currentSessionPuzzles],
      };
      try { sessionStorage.setItem('rookhub_endless_gameover', JSON.stringify(snap)); } catch {}
      this.router.navigate(['/analysis'], {
        queryParams: {
          fen: this.puzzle.fen,
          moves: this.puzzle.moves.split(' ').filter(m => m).join(','),
          orientation: this.orientation,
          from: '/puzzles/endless?gameover=1',
        },
      });
      return;
    }

    // Wurde das Puzzle bereits gescheitert (Aufgeben oder Falschzug) aber chainIndex noch nicht
    // vorgerückt, würde resume=1 dasselbe Puzzle nochmals laden → zweiter Leben-Verlust.
    // Deshalb jetzt vorwärts schieben und persistieren, bevor wir wegnavigieren.
    if ((this.state === 'FAILED' || this.reviewingWrongPuzzle) && this.lives > 0) {
      this.chainIndex++;
      this.level = this.chainIndex;
      this.syncActiveGameToServer();
    }
    this.router.navigate(['/analysis'], {
      queryParams: {
        fen: this.puzzle.fen,
        moves: this.puzzle.moves.split(' ').filter(m => m).join(','),
        orientation: this.orientation,
        from: '/puzzles/endless?resume=1',   // Rückkehr setzt den laufenden Run fort (siehe ngOnInit)
      },
    });
  }

  /** Zuletzt gelöstes Puzzle im Analysemodus öffnen (auch nach dem Auto-Advance verfügbar). */
  reviewLastPuzzle(): void {
    if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; }
    if (!this.lastSolvedFen) return;
    this.router.navigate(['/analysis'], {
      queryParams: {
        fen: this.lastSolvedFen,
        moves: this.lastSolvedMoves.split(' ').filter(m => m).join(','),
        orientation: this.lastSolvedOrientation,
        from: '/puzzles/endless?resume=1',   // Rückkehr setzt den laufenden Run fort (siehe ngOnInit)
      },
    });
  }

  showIntendedSolution(): void {
    if (!this.puzzle) return;
    if (this.state === 'FAILED') this.reviewingWrongPuzzle = true;
    this.state = 'SOLVED';
    this.reviewMode = true;
    this.reviewGoTo(0);
  }

  override get reviewTotal(): number {
    return this.puzzle ? this.puzzle.moves.split(' ').filter(m => m).length : 0;
  }

  reviewNext(): void { this.stopCountdown(); if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; } this.reviewGoTo(this.reviewIndex + 1); }
  reviewPrev(): void { this.stopCountdown(); if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; } this.reviewGoTo(this.reviewIndex - 1); }

  /** Nach alternativem (eigenem) Mattweg die vom Puzzle vorgesehene Lösung von vorne durchspielen. */
  showOriginalSolution(): void { this.stopCountdown(); if (this.autoAdvanceTimer) { clearTimeout(this.autoAdvanceTimer); this.autoAdvanceTimer = undefined; } this.playSolutionFromStart(); }

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
  }

  /** Zug aufs Brett anwenden ohne lastMove-Highlight (Review-Aufbau). */
  private applyUci(uci: string): void {
    applyUci(this.chess, uci);
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    if (this.state !== 'SOLVED' && this.state !== 'FAILED') return;
    if (e.key === 'ArrowLeft') this.reviewPrev();
    if (e.key === 'ArrowRight') this.reviewNext();
  }

  private loseLife(): void {
    if (!this.puzzleLifeLost) {
      this.puzzleLifeLost = true;
      this.currentSessionMistakes.push(this._currentMinRating);
      this.pushSessionPuzzle(false);
      this.lives--;
      this.recordAttempt(false);
      // Bei 0 Lives ist der Run faktisch vorbei — nicht den Zombie-State (0 Lives) auf den
      // Server schreiben. endGame() raeumt nach Klick auf Continue endgueltig auf; falls der
      // User vorher die Seite verlaesst, ist dann kein 0-Lives-Run als "unfinished" gemerkt.
      if (this.lives > 0) {
        this.syncActiveGameToServer();
      } else {
        this.storage.saveActiveGameLocal(null);
        this.storage.saveProgressImmediate(this.config, this.highscore, null);
      }
    }
    this.state = 'FAILED';
    this.updateBoard();
    this.enterSolutionReview();
  }

  // --- Buttons ---

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
        this.initialEval = await this.stockfish.getEval(this.initialFen, this.config.stockfishDepth);
      }
      this.currentEval = await this.stockfish.getEval(this.chess.fen(), this.config.stockfishDepth);
    } catch {}
    this.evalLoading = false;
  }

  protected override refreshEvalIfShown(): void {
    if (this.showEval) this.refreshEval();
  }

  onDepthChange(): void {
    if (this.config.stockfishDepth < 1) this.config.stockfishDepth = 1;
    if (this.config.stockfishDepth > 24) this.config.stockfishDepth = 24;
    this.saveConfig();
  }

  resetPuzzle(): void {
    if (!this.puzzle) return;
    this.aborted = true;
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.currentEval = '';
    this.initialEval = '';
    this.showEval = false;
    if (!this.puzzleLifeLost) {
      this.puzzleLifeLost = true;
      this.lives--;
      if (this.lives <= 0) {
        this.currentSessionMistakes.push(this._currentMinRating);
        this.pushSessionPuzzle(false);
        this.recordAttempt(false);
        // 0 Lives = Run ist vorbei. Active-State (mit jetzt veralteten Werten) auf
        // dem Server loeschen, damit kein "Unfinished run | 0 lives"-Zombie zurueckbleibt.
        this.storage.saveActiveGameLocal(null);
        this.storage.saveProgressImmediate(this.config, this.highscore, null);
        this.state = 'FAILED';
        this.updateBoard();
        this.enterSolutionReview();
        return;
      }
    }
    this.setupPuzzle(this.puzzle);
  }

  giveUp(): void {
    this.abortSolver();
    this.gaveUp = true;
    this.loseLife();
    // Wie Standard/Buch: die Lösung nach dem Aufgeben automatisch von vorne durchspielen.
    this.playSolutionFromStart();
  }

  /** Dasselbe Puzzle erneut versuchen. Kostet KEIN (weiteres) Leben — der Fehlversuch hat
   *  bereits eines gekostet. Auch beim letzten verlorenen Leben erlaubt: man darf das Puzzle,
   *  an dem der Run scheiterte, nochmal probieren (Lösen → Sudden-Death weiter, sonst „Weiter"
   *  führt ins Game Over). */
  retry(): void {
    if (!this.puzzle) return;
    this.currentEval = '';
    this.initialEval = '';
    this.showEval = false;
    this.setupPuzzle(this.puzzle);
  }

  private endGame(): void {
    this.stopSessionTimer();
    this.checkHighscore();
    this.ensureSessionRecorded();
    // Clear active game and sync final state to server
    this.storage.saveActiveGameLocal(null);
    this.storage.saveProgressImmediate(this.config, this.highscore, null);
    this.state = 'GAME_OVER';
  }

  // --- Step calculation ---

  private computeFasttrackSteps(): void {
    this.fasttrack.compute(this.config, this.sessionHistory);
  }

  onThresholdChange(): void {
    this.fasttrack.applyOverrides(this.config);
  }

  resetThreshold(which: number): void {
    this.fasttrack.reset(which === 1 ? 1 : 2, this.config);
  }

  // --- Timer ---

  /** `initialSeconds` > 0 setzt einen fortgesetzten Lauf (Resume) zeitlich korrekt fort. */
  private startSessionTimer(initialSeconds = 0): void {
    this.sessionStopwatch.start(initialSeconds);
    this.sessionSeconds = initialSeconds;
    this.sessionInterval = setInterval(() => {
      this.sessionSeconds = this.sessionStopwatch.elapsedSeconds;
      this.elapsedSeconds = this.puzzleStartTime > 0 ? this.puzzleStopwatch.elapsedSeconds : 0;
    }, 1000);
  }

  private stopSessionTimer(): void {
    if (this.sessionInterval) {
      clearInterval(this.sessionInterval);
      this.sessionInterval = undefined;
    }
    this.sessionStopwatch.stop();
    this.puzzleStopwatch.stop();
  }

  formatTime(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return m > 0 ? `${m}:${s.toString().padStart(2, '0')}` : `${s}s`;
  }

  // --- Tracking ---

  private trackMaxRating(rating: number): void {
    if (rating > this.maxRatingReached) this.maxRatingReached = rating;
  }

  /** `seconds` = zu wertende Lösezeit; default = gemessene Zeit (Fehlversuche), beim Lösen ggf. wegen
   *  überlanger Zeit gekappt (siehe {@link LongSolveService}). */
  private recordAttempt(solved: boolean, seconds?: number): void {
    if (!this.puzzle) return;
    const timeSpent = seconds ?? (this.puzzleStartTime > 0 ? this.puzzleStopwatch.elapsedSeconds : 0);
    const log = this.moveLog.length > 0 ? JSON.stringify(this.moveLog) : undefined;
    const id = this.puzzle.id;
    const loggedIn = this.authService.isLoggedIn;
    const url = loggedIn ? `/api/puzzles/${id}/attempt` : `/api/puzzles/${id}/attempt/anonymous`;
    const body: Record<string, unknown> = {
      solved, timeSpentSeconds: timeSpent, moveLog: log ?? null, visualizationLevel: this.visualizationMode,
      evalShown: this.evalShown, vizShowCount: this.vizShowCount,
      screenWidth: window.innerWidth, screenHeight: window.innerHeight,
    };
    if (!loggedIn) body['sessionId'] = this.puzzleService.ensureSessionId();
    // Offline gelöste Endless-Puzzles nicht verlieren → vormerken (Sync bei Reconnect).
    if (!navigator.onLine) { this.offlineQueue.enqueue('POST', url, body); return; }
    const obs = loggedIn
      ? this.puzzleService.recordAttempt(id, solved, timeSpent, log, this.visualizationMode, this.evalShown, this.vizShowCount)
      : this.puzzleService.recordAnonymousAttempt(id, solved, timeSpent, log, this.visualizationMode, this.evalShown, this.vizShowCount);
    obs.subscribe({ error: () => this.offlineQueue.enqueue('POST', url, body) });
  }

  // --- localStorage ---

  private saveConfig(): void {
    this.storage.saveConfig(this.config);
    this.storage.saveProgressToServer(this.config, this.highscore, null);
  }

  private checkHighscore(): void {
    const result = this.storage.checkHighscore(this.maxRatingReached, this.highscore);
    this.highscore = result.highscore;
    if (result.isNew) this.isNewHighscore = true;
  }

  private syncActiveGameToServer(): void {
    const gameState = {
      lives: this.lives,
      solved: this.solved,
      level: this.level,
      chainIndex: this.chainIndex,
      seed: this.seed,
      currentMinRating: this._currentMinRating,
      maxRatingReached: this.maxRatingReached,
      sessionSeconds: this.sessionSeconds,
      mistakes: this.currentSessionMistakes,
      puzzleAttempts: this.currentSessionPuzzles.map(p => ({
        puzzleNumber: p.puzzleNumber,
        puzzleId: p.puzzleId,
        lichessId: p.lichessId,
        rating: p.rating,
        solved: p.solved,
      })),
    };
    this.storage.saveActiveGameLocal(gameState);
    this.storage.saveProgressToServer(this.config, this.highscore, gameState);
  }

  /** Zeichnet den beendeten Lauf genau EINMAL auf (idempotent). Egal über welchen Pfad der Run
   *  endet — „Weiter" im Game-Over, Absprung in die Analyse oder Verlassen der Seite — die Session
   *  landet so garantiert in der History (statt verloren zu gehen, wenn der aktive Lauf bei 0 Leben
   *  schon vom Server gelöscht wurde und endGame() nie aufgerufen wird). */
  private ensureSessionRecorded(): void {
    if (this.sessionRecorded) return;
    this.sessionRecorded = true;
    this.recordSession();
  }

  private recordSession(): void {
    const session: EndlessSession = {
      timestamp: Date.now(),
      config: { ...this.config },
      totalSolved: this.solved,
      maxRating: this.maxRatingReached,
      durationSeconds: this.sessionSeconds,
      mistakeAtRatings: [...this.currentSessionMistakes],
      // Seed + geordnete Ketten-IDs am Server persistieren → späteres Replay des Laufs.
      seed: this.seed,
      chainPuzzleIds: this.chain.map(p => p.id).join(',')
    };
    this.sessionHistory = this.storage.recordSession(this.sessionHistory, session);
    // Per-Puzzle-Daten (mit Start-/Lösungszeit) nur an den Server für das Logging mitgeben,
    // nicht in die lokale History (würde localStorage aufblähen).
    // Nur auf diesem Tab gespielte Puzzles (startedAt > 0) ans Server-Logging übergeben;
    // wiederhergestellte Puzzles (Tab-Wechsel-Resume) wurden bereits von Tab A geloggt.
    const newPuzzles = this.currentSessionPuzzles.filter(p => p.startedAt > 0);
    this.storage.recordSessionToServer(session, newPuzzles).subscribe(id => {
      if (id) {
        this.lastSessionId = id;
        // Für Rückkehr aus der Analyse nach 0-Leben-Situation (gameover=1): ID sichern,
        // damit der neue Component-Instanz-Konstruktor sie noch auslesen kann.
        try { sessionStorage.setItem('rookhub_endless_pending_sid', String(id)); } catch {}
      }
    });
  }

  /** Hängt das aktuelle Puzzle (mit Start-/Endzeit) an die Session-Liste an. */
  private pushSessionPuzzle(solved: boolean): void {
    if (!this.puzzle) return;
    const now = Date.now();
    this.currentSessionPuzzles.push({
      puzzleNumber: this.currentSessionPuzzles.length + 1,
      puzzleId: this.puzzle.id,
      lichessId: this.puzzle.lichessId,
      rating: this.puzzle.rating,
      solved,
      themes: this.puzzle.themes,
      startedAt: this.puzzleStartTime > 0 ? this.puzzleStartTime : now,
      endedAt: now,
    });
  }
}
