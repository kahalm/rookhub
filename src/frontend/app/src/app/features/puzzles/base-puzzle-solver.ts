import { Chess, Square } from 'chess.js';
import { Color, Key } from 'chessground/types';
import { StockfishService } from './stockfish.service';
import { applyUci, tryFreeMove, calcDests, formatSanList, formatSanListHtml } from './puzzle-move.util';
import { applyVisualizationHide, clearVisualizationHide } from './board-theme.util';
import { VisibilityStopwatch } from './visibility-stopwatch';
import { formatPuzzleTime } from './puzzle-format.util';

export interface MoveLogEntry { i: number; uci: string; exp: string; ms: number; ok: boolean; }

/**
 * Gemeinsamer Lös-Automat für alle 3 Puzzle-Modi (Normal, Endless, Buch).
 * Kapselt chess.js-State, den Zustandsautomaten (SETUP→AWAITING→THINKING→PLAYING),
 * Lösungsvergleich, Stockfish-Übernahme bei Fehlzug, Mouseslip, Brett-Präsentation und
 * den Visualisierungs-/Blindfold-Modus. Vorher in jeder Komponente ~400 Zeilen dupliziert.
 *
 * Modus-Unterschiede laufen über Hooks: {@link handleSolved}/{@link handleFailed} (Pflicht),
 * {@link depth}, {@link stockfishErrorContinues}, {@link onSetupStart}, {@link onSolvingBegins}.
 * Die konkrete Komponente setzt am Ende ihren eigenen `state` (SOLVED/CORRECT/WRONG/...).
 */
export abstract class BasePuzzleSolver {
  // ---- Brett-Präsentation (von Templates gelesen) ----
  boardFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  /**
   * Tatsächlicher chess.js-Zustand (im Viz-Modus weicht boardFen ab — dort bleibt das Brett
   * auf frozenFen). Das Brett-Component nutzt das fürs Erkennen von Promotion-Zügen und
   * für die Legalitäts-Prüfung im Viz-Klick-Modus.
   */
  actualFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  orientation: Color = 'white';
  turnColor: Color = 'white';
  dests: Map<Key, Key[]> = new Map();
  /** Anarchy-Modus (`?anarchy=max`): erzwingt En passant, wenn verfügbar (schränkt `dests` ein). */
  enPassantForced = false;
  /** Crazy-Figuren-Modus: 'piece' = je Figur eigenes Set (Default), 'square' = das Feld bestimmt
   *  den Stil (`?anarchy=max+1`). Wird ans `app-puzzle-board` durchgereicht. */
  crazyPieceMode: import('./board-theme.util').CrazyPieceMode = 'piece';
  lastMove?: [Key, Key];
  isCheck = false;

  /** Gemeinsamer Zustand; Endzustände (SOLVED/FAILED/CORRECT/WRONG/...) setzt die Komponente. */
  state = 'LOADING';

  onSolutionPath = true;
  alternativeSolve = false;
  mouseslipUsed = false;
  currentEval = '';

  // ---- Off-Path-Warnung: Weicht der User von der Lösung ab und spielt gegen Stockfish weiter,
  //      wird — sobald die Eval nicht mehr mind. +2 für den Spieler ist — ab dem N-ten off-path-Zug
  //      einmalig gewarnt (evtl. falsch abgebogen → besser zurücksetzen/aufgeben). N kommt aus den
  //      Einstellungen (0 = nie); der Hinweis selbst zeigt die überschriebene onOffPathWarning(). ----
  /** Anzahl der bisher off-path gespielten Züge des Users (seit dem Abweichen). */
  protected offPathUserPlies = 0;
  /** In dieser Off-Path-Episode bereits gewarnt (einmalig). */
  protected offPathWarned = false;
  /** True, sobald gewarnt wurde (für eine optionale Dauer-Anzeige im Template). */
  offPathWarning = false;

  /** Anarchy per URL erzwungen (`?anarchy=max`): e.p.-Zwang gilt dann unabhängig von der Einstellung.
   *  Ohne URL-Zwang folgt der e.p.-Zwang im Crazy-Brett der Einstellung `prefs.enPassantForced`. */
  protected anarchyForcedByUrl = false;

  // ---- Lösungs-Durchsicht (Review) — von allen Modi geteilt ----
  reviewMode = false;
  reviewIndex = 0;
  protected solutionPlayTimer?: ReturnType<typeof setInterval>;

  // ---- Einstellungs-Panel (Zahnrad) — Offen-Zustand über alle Modi gemerkt ----
  showSettings = false;
  private static readonly SETTINGS_OPEN_KEY = 'rookhub_puzzle_settings_open';

  // ---- Auto-Advance nach dem Lösen: kurzer, sichtbarer, überspringbarer Countdown ----
  solvedCountdown = 0;
  private countdownInterval?: ReturnType<typeof setInterval>;
  protected static readonly SOLVED_COUNTDOWN_SECONDS = 2;

  // ---- Visualisierungs-/Blindfold-Modus (0 = aus, 1-4 = aktiv) ----
  visualizationMode = 0;
  vizMoves: string[] = [];
  protected frozenFen = '';
  protected vizStartWhite = true;
  protected vizStartNum = 1;
  /** Level 2-4: Figuren versteckt (nach Countdown). */
  vizPiecesHidden = false;
  /** Show-Button aktiv (deckt Figuren/Steine auf — Toggle, läuft NICHT automatisch ab). */
  vizShowPressed = false;
  /** Wie oft der Show-Button in diesem Versuch zum Aufdecken gedrückt wurde. */
  vizShowCount = 0;
  /** Eval wurde in diesem Versuch mindestens einmal eingeblendet. */
  evalShown = false;
  /** Countdown-Sekunden bis Figuren verschwinden (Level 2-4). */
  vizCountdownSeconds = 0;
  protected vizCountdownInterval?: ReturnType<typeof setInterval>;
  /** Letzter Gegnerzug als Pfeil (Viz-Modus 1-4: immer nur der zuletzt gespielte Gegnerzug). */
  vizOpponentLastMove?: [Key, Key];
  /** Pfeil-Anzeige ein/aus (Nutzer-Einstellung, aus Prefs geladen). */
  vizArrowEnabled = true;
  private vizOpponentArrowTimer?: ReturnType<typeof setTimeout>;

  // ---- Tipps (gestuft 1→3). Wie viele Stufen aktuell aufgedeckt sind. ----
  hintLevel = 0;
  /**
   * Tipps in der aktiven Sprache (0–3). Default keine; die Modi überschreiben das:
   * Buch/Kurs liefert vorberechnete Tipps aus dem DTO, Standard berechnet sie on-the-fly.
   */
  get availableHints(): string[] { return []; }
  /** Anarchy-Modus (e.p. forciert): 3 (unterschiedlich formulierte) „En passant ist Pflicht"-Hinweise
   *  ersetzen die normalen Tipps. Komponenten überschreiben mit ihren übersetzten Strings. */
  protected get epForcedHints(): string[] { return []; }
  /** Effektive Tipp-Liste: im e.p.-Zwang die Anarchy-Hinweise, sonst die normalen Tipps. */
  private get effectiveHints(): string[] {
    return this.enPassantForced && this.epForcedHints.length ? this.epForcedHints : this.availableHints;
  }
  get hasHints(): boolean { return this.effectiveHints.length > 0; }
  get shownHints(): string[] { return this.effectiveHints.slice(0, this.hintLevel); }
  get canShowMoreHints(): boolean { return this.hintLevel < this.effectiveHints.length; }
  /** Nächste Tipp-Stufe aufdecken. */
  showNextHint(): void { if (this.canShowMoreHints) this.hintLevel++; }

  // ---- intern ----
  protected chess = new Chess();
  protected solutionMoves: string[] = [];
  protected moveIndex = 0;
  protected startPly = 0;
  protected aborted = false;
  protected autoAdvanceTimer?: ReturnType<typeof setTimeout>;
  protected moveLog: MoveLogEntry[] = [];
  protected moveStartTime = 0;
  /**
   * Jeder Lös-Vorgang hat eine Epoch. Bei setupSolver/mouseslip/abortSolver wird sie inkrementiert.
   * Laufende Stockfish-Aufrufe ({@link opponentRespond}) merken sich ihre Epoch und brechen ab,
   * wenn sie verstrichen ist — so kann ein verspätet ankommender Stockfish-Zug das frisch
   * resettete Brett nicht mehr verschmutzen (`aborted` allein reicht nicht, weil reset/mouseslip
   * es direkt wieder auf false setzen).
   */
  protected solverEpoch = 0;

  /** Ob seit dem letzten Fehlzug wirklich ein Gegnerzug gespielt wurde (fuer Mouseslip-Undo). */
  protected lastOpponentReplied = false;

  // ===== Lösezeit-Timer (Einzel-Stoppuhr, von Standard- + Buch-Modus genutzt) =====
  // Endless verwendet eigene Doppel-Stoppuhren (Session + Puzzle) und ruft start/stopTimer NICHT auf,
  // teilt sich aber das `elapsedSeconds`-Feld (für die Anzeige) mit der Basis.
  /** Aktuelle (aktive) Lösezeit in Sekunden — Anzeige im Status-/Du-bist-dran-Card. */
  elapsedSeconds = 0;
  private timerInterval?: ReturnType<typeof setInterval>;
  /** Pausiert bei verstecktem Tab (zählt nur aktive Zeit). */
  protected readonly stopwatch = new VisibilityStopwatch();

  constructor(protected stockfish: StockfishService) {}

  /** Lösezeit kompakt formatieren („1:05" / „45s"). Geteilt über alle Modi + Karten. */
  formatTime(seconds: number): string {
    return formatPuzzleTime(seconds);
  }

  /** Lösezeit-Timer starten (Standard-/Buch-Modus): Stoppuhr nullen + 1-s-Anzeige-Tick. */
  protected startTimer(): void {
    this.stopwatch.start();
    this.elapsedSeconds = 0;
    this.timerInterval = setInterval(() => {
      this.elapsedSeconds = this.stopwatch.elapsedSeconds;
    }, 1000);
  }

  /** Lösezeit-Timer stoppen + finalen aktiven Stand festhalten. */
  protected stopTimer(): void {
    if (this.timerInterval) {
      clearInterval(this.timerInterval);
      this.timerInterval = undefined;
    }
    this.elapsedSeconds = this.stopwatch.elapsedSeconds;
    this.stopwatch.stop();
  }

  // ===== Hooks (von den Komponenten überschrieben) =====
  /** Puzzle gelöst — Komponente setzt Endzustand + Mode-Logik (Elo/Leben/Review/…). */
  protected abstract handleSolved(alternative: boolean): void;
  /** Puzzle verloren — Komponente setzt Endzustand + Mode-Logik. */
  protected abstract handleFailed(): void;
  /** Anzahl Schritte in der Lösungs-Durchsicht (mode-spezifisch). */
  abstract get reviewTotal(): number;
  /** Brett auf Schritt `index` der Lösungs-Durchsicht aufbauen (mode-spezifisch). */
  protected abstract reviewGoTo(index: number): void;
  /** Stockfish-Suchtiefe (Komponente liefert ihre Einstellung). */
  protected get depth(): number { return 16; }
  /** Bei Stockfish-Fehler weiterspielen (true) oder als verloren werten (false, z.B. Buch). */
  protected get stockfishErrorContinues(): boolean { return true; }
  /** Am Anfang von setupSolver (z.B. Theme-Modus anwenden). */
  protected onSetupStart(): void {}
  /** Sobald gelöst werden kann (Trainingsstellung erreicht) — z.B. Timer/initialFen. */
  protected onSolvingBegins(): void {}

  // ===== abgeleitete Helfer =====
  protected get isSolving(): boolean {
    return this.state === 'AWAITING_USER_MOVE' || this.state === 'THINKING' || this.state === 'PLAYING';
  }

  get hasMadeFirstMove(): boolean { return this.moveLog.length > 0; }

  /** SAN-Zugliste (Visualisierungs-Modus) mit korrekten Zugnummern ab der Trainingsstellung. */
  get vizMoveText(): string {
    return formatSanList(this.vizMoves, this.vizStartWhite, this.vizStartNum);
  }

  /** Wie `vizMoveText`, aber Gegnerzüge in `<strong>` (für [innerHTML]-Bindung in VizCard). */
  get vizMoveHtml(): string {
    return formatSanListHtml(this.vizMoves, this.vizStartWhite, this.vizStartNum);
  }

  // ===== Setup =====
  /**
   * Puzzle aufsetzen. `startPly`: -1 = FEN ist Trainingsstellung (lösen ab moves[0]);
   * 0 = klassisch (moves[0] Setup, lösen ab moves[1]); k = Vorspiel bis moves[k].
   */
  protected setupSolver(fen: string, movesStr: string, startPly = 0): void {
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.clearSolutionPlay();   // evtl. laufende Lösungs-Wiedergabe (nach Aufgeben) stoppen
    this.stopCountdown();
    this.reviewMode = false;
    this.solverEpoch++;
    this.solutionMoves = movesStr.split(' ');
    this.startPly = startPly;
    if (this.startPly > this.solutionMoves.length - 2) this.startPly = 0;
    if (this.startPly < -1) this.startPly = -1;
    this.moveIndex = 0;
    this.chess = new Chess(fen);
    this.onSolutionPath = true;
    this.aborted = false;
    this.mouseslipUsed = false;
    this.alternativeSolve = false;
    this.resetOffPathTracking();
    this.moveLog = [];
    this.evalShown = false;
    this.vizShowCount = 0;
    this.frozenFen = fen;
    this.vizMoves = [];
    this.clearVizOpponentArrow();
    this.endVisualizationHide();
    this.onSetupStart();

    for (let i = 0; i < this.startPly; i++) applyUci(this.chess, this.solutionMoves[i]);
    this.lastMove = undefined;

    if (this.startPly < 0) {
      // FEN ist bereits die Trainingsstellung → sofort lösen, kein Setup-Zug.
      this.moveIndex = 0;
      this.orientation = this.chess.turn() === 'w' ? 'white' : 'black';
      this.beginSolving();
      this.state = 'AWAITING_USER_MOVE';
      this.onSolvingBegins();
      this.updateBoard();
      return;
    }

    const setupMove = this.solutionMoves[this.startPly];
    const piece = this.chess.get(setupMove.substring(0, 2) as Square);
    this.orientation = piece?.color === 'w' ? 'black' : 'white';
    this.updateBoard();
    this.state = 'SETUP';

    this.autoAdvanceTimer = setTimeout(() => {
      if (this.state !== 'SETUP') return;
      this.playMove(this.solutionMoves[this.startPly]);
      this.moveIndex = this.startPly + 1;
      this.beginSolving();
      this.state = 'AWAITING_USER_MOVE';
      this.onSolvingBegins();
      this.moveStartTime = Date.now();
      this.updateBoard();
    }, 600);
  }

  protected beginSolving(): void {
    this.frozenFen = this.chess.fen();
    const f = this.frozenFen.split(' ');
    this.vizStartWhite = f[1] !== 'b';
    this.vizStartNum = parseInt(f[5], 10) || 1;
    this.vizMoves = [];
    if (this.visualizationMode >= 2) this.startVizCountdown();
  }

  // ===== Zug-Handling =====
  onMoveMade(event: { orig: Key; dest: Key; promotion?: string }): void {
    if (this.state === 'PLAYING') { this.handleOffPathMove(event); return; }
    if (this.state !== 'AWAITING_USER_MOVE') return;

    if (this.onSolutionPath) {
      const expectedUci = this.solutionMoves[this.moveIndex];
      const userUci = event.orig + event.dest + (event.promotion || '');
      const thinkMs = Date.now() - this.moveStartTime;

      if (userUci === expectedUci.substring(0, userUci.length)) {
        this.moveLog.push({ i: this.moveIndex, uci: expectedUci, exp: expectedUci, ms: thinkMs, ok: true });
        this.playMove(expectedUci);
        this.moveIndex++;
        this.advanceAfterCorrectMove();
      } else {
        this.moveLog.push({ i: this.moveIndex, uci: userUci, exp: expectedUci, ms: thinkMs, ok: false });
        if (!this.playFreeMove(event.orig, event.dest, event.promotion)) return;
        this.onSolutionPath = false;
        this.offPathUserPlies++;   // erster Abweich-Zug
        if (this.chess.isGameOver()) { this.handleGameOver(); return; }
        this.opponentRespond();
      }
    } else {
      this.handleOffPathMove(event);
    }
  }

  protected handleOffPathMove(event: { orig: Key; dest: Key; promotion?: string }): void {
    if (!this.playFreeMove(event.orig, event.dest, event.promotion)) return;
    this.offPathUserPlies++;   // weiterer off-path-Zug des Users
    if (this.chess.isGameOver()) { this.handleGameOver(); return; }
    this.opponentRespond();
  }

  protected advanceAfterCorrectMove(): void {
    if (this.moveIndex >= this.solutionMoves.length) { this.solvedInternal(false); return; }
    this.state = 'THINKING';
    this.updateBoard();
    this.autoAdvanceTimer = setTimeout(() => {
      if (this.aborted) return;
      this.playMove(this.solutionMoves[this.moveIndex]);
      this.moveIndex++;
      if (this.visualizationMode) this.showVizOpponentArrow();
      this.updateBoard();
      if (this.moveIndex >= this.solutionMoves.length) { this.solvedInternal(false); return; }
      // Solver-Antwort im Lösungspfad → User soll seinen nächsten Lösungszug machen.
      // PLAYING ist reserviert für off-path/freies Spiel; in onMoveMade wird PLAYING
      // direkt als off-path interpretiert und der Lösungsvergleich übersprungen.
      this.state = 'AWAITING_USER_MOVE';
      this.moveStartTime = Date.now();
      this.updateBoard();
      this.refreshEvalIfShown();   // Eval auf die neue Stellung nachziehen (auch im Lösungspfad)
    }, 400);
  }

  protected async opponentRespond(): Promise<void> {
    const epoch = this.solverEpoch;
    this.lastOpponentReplied = false;
    this.state = 'THINKING';
    this.updateBoard();
    try {
      const result = await this.stockfish.getBestMove(this.chess.fen(), this.depth);
      if (this.aborted || epoch !== this.solverEpoch) return;
      this.currentEval = result.eval;
      // Eval ist hier die der Stellung NACH dem (evtl. off-path) Spielerzug → jetzt prüfen,
      // ob der Spieler eine mögliche falsche Abbiegung merken sollte.
      if (!this.onSolutionPath) this.maybeWarnOffPath();
      this.playMove(result.move);
      this.lastOpponentReplied = true;
      if (this.visualizationMode) this.showVizOpponentArrow();
      this.updateBoard();
      if (this.chess.isGameOver()) { this.handleGameOver(); return; }
      this.autoAdvanceTimer = setTimeout(() => {
        if (this.aborted || epoch !== this.solverEpoch) return;
        this.state = 'PLAYING';
        this.moveStartTime = Date.now();
        this.updateBoard();
        this.refreshEvalIfShown();   // Eval auf die Stellung NACH dem Gegnerzug nachziehen
      }, 400);
    } catch {
      if (this.aborted || epoch !== this.solverEpoch) return;
      if (this.stockfishErrorContinues) {
        this.state = 'PLAYING';
        this.moveStartTime = Date.now();
        this.updateBoard();
        this.refreshEvalIfShown();
      } else {
        this.handleFailed();
      }
    }
  }

  protected handleGameOver(): void {
    if (this.chess.isCheckmate()) {
      const loserColor = this.chess.turn();
      const userColor = this.orientation === 'white' ? 'w' : 'b';
      if (loserColor !== userColor) { this.solvedInternal(true); return; }
    }
    this.handleFailed();
  }

  private solvedInternal(alternative: boolean): void {
    this.alternativeSolve = alternative;
    this.handleSolved(alternative);
  }

  mouseslip(): void {
    if (this.mouseslipUsed) return;
    if (this.onSolutionPath) {
      // Lösungspfad-Mouseslip: letzten Korrekt-Zug (+ ggf. bereits gespielte Solver-Antwort) rückgängig.
      if (this.moveLog.length === 0) return;
      if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
      this.aborted = false;
      this.solverEpoch++;
      // Im THINKING-State hat der Solver noch nicht geantwortet → nur 1 Zug zurück;
      // im AWAITING-State hat der Solver bereits gespielt → 2 Züge zurück.
      const undoCount = this.state === 'AWAITING_USER_MOVE' ? 2 : 1;
      for (let i = 0; i < undoCount; i++) this.chess.undo();
      this.moveIndex -= undoCount;
      if (this.visualizationMode) this.vizMoves.splice(-undoCount);
      if (this.moveLog.length > 0 && this.moveLog[this.moveLog.length - 1].ok) this.moveLog.pop();
      this.mouseslipUsed = true;
      const hist = this.chess.history({ verbose: true });
      const lm = hist.length > 0 ? hist[hist.length - 1] : undefined;
      this.lastMove = lm ? [lm.from as Key, lm.to as Key] : undefined;
      this.state = 'AWAITING_USER_MOVE';
      this.moveStartTime = Date.now();
      this.updateBoard();
      this.refreshEvalIfShown();   // Eval auf die zurückgenommene Stellung nachziehen
      return;
    }
    this.mouseslipUsed = true;
    this.aborted = true;
    this.solverEpoch++;                         // verspätete Stockfish-Antwort verwerfen
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    // Zurueckzunehmende Plies: Fehlzug + (nur falls Stockfish wirklich geantwortet hat)
    // der Gegnerzug. Im Stockfish-Fehlerpfad ist state zwar PLAYING, aber es wurde KEIN
    // Gegnerzug gespielt -> dann nur 1 Ply zuruecknehmen, sonst faellt ein gueltiger
    // Loesungszug mit weg.
    const undoCount = (this.state === 'PLAYING' && this.lastOpponentReplied) ? 2 : 1;
    for (let i = 0; i < undoCount; i++) this.chess.undo();
    // Visualisierungs-Modus: Brett bleibt eingefroren → die zurückgenommenen Züge auch aus der
    // SAN-Zugliste entfernen, sonst „passiert" sichtbar nichts und die Liste ist inkonsistent.
    if (this.visualizationMode) this.vizMoves.splice(-undoCount);
    // Fehlzug aus dem Log streichen (war als ok:false eingetragen).
    if (this.moveLog.length > 0 && !this.moveLog[this.moveLog.length - 1].ok) this.moveLog.pop();
    // Zurück auf den Lösungspfad — sonst ginge der nächste User-Zug wieder in handleOffPathMove.
    this.onSolutionPath = true;
    this.resetOffPathTracking();
    this.aborted = false;
    this.state = 'AWAITING_USER_MOVE';
    this.moveStartTime = Date.now();
    this.updateBoard();
    this.refreshEvalIfShown();   // Eval auf die zurückgenommene Stellung nachziehen
  }

  // ===== Brett / Züge =====
  protected playMove(uci: string): void {
    const mv = applyUci(this.chess, uci);
    this.lastMove = [uci.substring(0, 2) as Key, uci.substring(2, 4) as Key];
    if (this.visualizationMode && mv && this.isSolving) this.vizMoves.push(mv.san);
  }

  protected playFreeMove(orig: Key, dest: Key, promotion?: string): boolean {
    const mv = tryFreeMove(this.chess, orig, dest, promotion);
    if (!mv) return false;
    this.lastMove = [orig, dest];
    if (this.visualizationMode && this.isSolving) this.vizMoves.push(mv.san);
    return true;
  }

  protected updateBoard(): void {
    this.actualFen = this.chess.fen();
    // Visualisierung: Brett auf der eingefrorenen Startstellung halten, solange gelöst wird;
    // am Ende (Endzustand der Komponente) das echte Brett aufdecken.
    if (this.visualizationMode && (this.isSolving || this.state === 'SETUP')) {
      // Level 1 („Blindspiel"): Beim Drücken von „Anzeigen" kurz die TATSÄCHLICHE aktuelle
      // Stellung zeigen (sonst bleibt das Brett auf der eingefrorenen Startstellung). Ab Level 2
      // deckt stattdessen CSS die Figuren auf — das Brett bleibt dort immer eingefroren.
      if (this.visualizationMode === 1 && this.vizShowPressed) {
        this.boardFen = this.actualFen;
        this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
        this.isCheck = this.chess.isCheck();
      } else {
        this.boardFen = this.frozenFen || this.actualFen;
        this.turnColor = this.orientation;
        this.isCheck = false;
      }
      this.dests = new Map();
      return;
    }
    // Endzustand → Figuren wieder einblenden
    if (this.vizPiecesHidden) this.endVisualizationHide();
    this.boardFen = this.actualFen;
    this.turnColor = this.chess.turn() === 'w' ? 'white' : 'black';
    this.isCheck = this.chess.isCheck();
    const interactive = (this.state === 'AWAITING_USER_MOVE' || this.state === 'PLAYING') && this.turnColor === this.orientation;
    this.dests = interactive ? calcDests(this.chess, this.enPassantForced) : new Map();
  }

  // ===== Visualization Level 2-4: Countdown + Hide =====
  protected startVizCountdown(): void {
    this.clearVizCountdown();
    this.vizPiecesHidden = false;
    this.vizShowPressed = false;
    this.vizCountdownSeconds = 3;
    this.vizCountdownInterval = setInterval(() => {
      this.vizCountdownSeconds--;
      if (this.vizCountdownSeconds <= 0) {
        this.clearVizCountdown();
        this.vizPiecesHidden = true;
        applyVisualizationHide(this.visualizationMode);
      }
    }, 1000);
  }

  protected clearVizCountdown(): void {
    if (this.vizCountdownInterval) {
      clearInterval(this.vizCountdownInterval);
      this.vizCountdownInterval = undefined;
    }
    this.vizCountdownSeconds = 0;
  }

  /** Toggle: deckt die Figuren auf bzw. blendet sie wieder aus. Läuft bewusst NICHT automatisch
   *  ab — der Spieler entscheidet selbst, wann wieder verdeckt wird. */
  onVizShow(): void {
    this.vizShowPressed = !this.vizShowPressed;
    if (this.vizShowPressed) this.vizShowCount++;   // nur das Aufdecken zählen
    // Level 1: Brett auf die tatsächliche Stellung umstellen bzw. wieder einfrieren
    // (Level 2–4 decken über die CSS-Klasse `.viz-hidden` auf, kein Board-Wechsel nötig).
    if (this.visualizationMode === 1) this.updateBoard();
  }

  protected markEvalShown(): void {
    this.evalShown = true;
  }

  /** Hook: nach jeder Stellungsänderung (Lösungspfad-Antwort, Gegnerzug, Mouseslip-Rücknahme)
   *  aufgerufen, damit die Komponente die eingeblendete Eval auf die aktuelle Stellung nachzieht.
   *  Default no-op; Komponenten überschreiben: `if (this.showEval) this.refreshEval();`. */
  protected refreshEvalIfShown(): void { /* von der Komponente überschrieben */ }

  // ===== Off-Path-Warnung =====

  /** Ab dem wievielten off-path-Zug gewarnt wird (0 = nie). Komponenten überschreiben mit dem
   *  Einstellungswert (`prefs.offPathWarnMoves`). Default 0 (aus), damit Tests/Basis nicht warnen. */
  protected get offPathWarnThreshold(): number { return 0; }

  /** Hook: wird EINMAL je Off-Path-Episode aufgerufen, sobald die Warn-Bedingung greift.
   *  Default no-op; Komponenten zeigen z. B. einen Snackbar-Hinweis. */
  protected onOffPathWarning(): void { /* von der Komponente überschrieben */ }

  private resetOffPathTracking(): void {
    this.offPathUserPlies = 0;
    this.offPathWarned = false;
    this.offPathWarning = false;
  }

  /** Prüft nach einem off-path-Zug (Eval steht in currentEval, Weiß-Sicht): ab dem
   *  Schwellwert-Zug und wenn die Eval nicht mind. +2 für den Spieler ist → einmalig warnen. */
  protected maybeWarnOffPath(): void {
    const threshold = this.offPathWarnThreshold;
    if (threshold <= 0 || this.offPathWarned || this.offPathUserPlies < threshold) return;
    const pawns = this.playerEvalPawns();
    if (pawns == null || pawns >= 2) return;   // noch klar für den Spieler → keine Warnung
    this.offPathWarned = true;
    this.offPathWarning = true;
    this.onOffPathWarning();
  }

  /** currentEval (String, Weiß-Sicht, z. B. "+1.5"/"#3"/"#-2") → Bauerneinheiten aus Spieler-Sicht;
   *  Matt = ±100. null, wenn (noch) keine Eval vorliegt. */
  protected playerEvalPawns(): number | null {
    const s = this.currentEval;
    if (!s) return null;
    let white: number;
    if (s.includes('#')) {
      const m = parseInt(s.replace(/[#+]/g, ''), 10);   // "#3"→3, "#-2"→-2
      if (isNaN(m)) return null;
      white = (m >= 0 ? 1 : -1) * 100;
    } else {
      white = parseFloat(s);
      if (isNaN(white)) return null;
    }
    return this.orientation === 'white' ? white : -white;
  }

  protected endVisualizationHide(): void {
    this.clearVizCountdown();
    this.vizPiecesHidden = false;
    this.vizShowPressed = false;
    clearVisualizationHide();
  }

  /** Zeigt den letzten Gegnerzug als Pfeil und blendet ihn nach 1s automatisch aus. */
  private showVizOpponentArrow(): void {
    if (!this.vizArrowEnabled) return;
    if (this.vizOpponentArrowTimer) { clearTimeout(this.vizOpponentArrowTimer); this.vizOpponentArrowTimer = undefined; }
    this.vizOpponentLastMove = this.lastMove;
    this.vizOpponentArrowTimer = setTimeout(() => {
      this.vizOpponentLastMove = undefined;
      this.vizOpponentArrowTimer = undefined;
    }, 1000);
  }

  protected clearVizOpponentArrow(): void {
    if (this.vizOpponentArrowTimer) { clearTimeout(this.vizOpponentArrowTimer); this.vizOpponentArrowTimer = undefined; }
    this.vizOpponentLastMove = undefined;
  }

  // ===== Lösungs-Durchsicht (geteilt) =====
  /** Endzustand-Review: ans Ende springen (keine Auto-Wiedergabe) — für gelöst/falsch. */
  protected enterSolutionReview(): void {
    this.reviewMode = true;
    this.reviewIndex = this.reviewTotal;
  }

  /** Aufgeben: Lösung ab dem ersten Zug automatisch durchspielen (alle Modi identisch). */
  protected playSolutionFromStart(): void {
    this.clearSolutionPlay();
    this.reviewMode = true;
    this.reviewGoTo(0);
    this.solutionPlayTimer = setInterval(() => {
      if (this.reviewIndex >= this.reviewTotal) { this.clearSolutionPlay(); return; }
      this.reviewGoTo(this.reviewIndex + 1);
    }, 900);
  }

  protected clearSolutionPlay(): void {
    if (this.solutionPlayTimer) {
      clearInterval(this.solutionPlayTimer);
      this.solutionPlayTimer = undefined;
    }
  }

  // ===== Einstellungs-Panel =====
  /** Offen-Zustand des Einstellungs-Panels umschalten + persistieren (modusübergreifend). */
  toggleSettings(): void {
    this.showSettings = !this.showSettings;
    try { localStorage.setItem(BasePuzzleSolver.SETTINGS_OPEN_KEY, String(this.showSettings)); } catch { /* ignore */ }
  }

  /** Gemerkten Offen-Zustand laden — von der Komponente beim Init aufrufen. */
  protected loadSettingsOpen(): void {
    try { this.showSettings = localStorage.getItem(BasePuzzleSolver.SETTINGS_OPEN_KEY) === 'true'; } catch { /* ignore */ }
  }

  // ===== Auto-Advance-Countdown =====
  /** Kurzen Countdown bis zum nächsten Puzzle starten; `onElapsed` läuft bei 0. Jederzeit
   *  per {@link stopCountdown} (z.B. „Weiter"-Klick oder Review-Interaktion) überspringbar. */
  protected startSolvedCountdown(onElapsed: () => void): void {
    this.stopCountdown();
    this.solvedCountdown = BasePuzzleSolver.SOLVED_COUNTDOWN_SECONDS;
    this.countdownInterval = setInterval(() => {
      this.solvedCountdown--;
      if (this.solvedCountdown <= 0) {
        this.stopCountdown();
        onElapsed();
      }
    }, 1000);
  }

  protected stopCountdown(): void {
    if (this.countdownInterval) {
      clearInterval(this.countdownInterval);
      this.countdownInterval = undefined;
    }
    this.solvedCountdown = 0;
  }

  /** Aufräumen (Timer) — Komponente ruft dies in ngOnDestroy/reset. */
  protected abortSolver(): void {
    this.aborted = true;
    this.solverEpoch++;                         // laufende Stockfish-Aufrufe verwerfen
    this.clearSolutionPlay();
    this.stopCountdown();
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    this.clearVizOpponentArrow();
    this.endVisualizationHide();
  }
}
