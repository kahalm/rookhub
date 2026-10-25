import { Chess } from 'chess.js';
import { Color, Key } from 'chessground/types';
import { StockfishService } from './stockfish.service';
import { applyUci, tryFreeMove, calcDests, formatSanList, formatSanListHtml } from './puzzle-move.util';
import { applyVisualizationHide, clearCrazyStyles, clearVisualizationHide, ThemeMode } from './board-theme.util';
import { HintLadder } from './hint-ladder';
import { OffPathPolicy, playerEvalText, whiteEvalToPlayerPawns } from './off-path-policy';
import { OpponentArrowMode, opponentArrowModeFor, VizController } from './viz-controller';
import { SecondsCountdown, SolverTimer } from './solver-timer';
import { lineStepAt } from './line-step.util';
import { clampStartPly, previewPosition, solverOrientation } from './solver-setup.util';
import { formatPuzzleTime } from './puzzle-format.util';
import { classifyMoveFromFen, FirstMoveHint } from './puzzle-hints.util';
import { ExpectedMove, judgeMove } from '../../shared/chess/line-solver';
import { whiteToMove } from '../../shared/chess/fen-side';

export interface MoveLogEntry { i: number; uci: string; exp: string; ms: number; ok: boolean; }

/** Wie lange ein gespielter (geduldeter) Alternativzug auf dem Brett stehen bleibt, bevor er
 *  zurückgenommen wird und der Hauptzug weiter erwartet wird. */
const ALT_HOLD_MS = 1400;

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
  /** Brett-Themen-Modus (fixed/crazy/…), aus den Prefs bzw. Share-View-Params gesetzt. Steuert u.a.
   *  {@link opponentArrowMode} (crazy → bleibender Gegnerzug-Pfeil). Geteilt über alle Modi. */
  themeMode: ThemeMode = 'fixed';
  lastMove?: [Key, Key];
  isCheck = false;

  /** Gemeinsamer Zustand; Endzustände (SOLVED/FAILED/CORRECT/WRONG/...) setzt die Komponente. */
  state = 'LOADING';
  /** Die Komponente wartet auf die Spielweise-Wahl (blockierender Dialog vor dem ersten Puzzle eines
   *  Bereichs): die Aufgabe steht schon auf dem Brett ({@link previewSetup}), aber ohne Uhr und Setup-Zug,
   *  und die Status-Karte bittet um die Wahl statt „lädt…" zu zeigen. */
  awaitingSolveMode = false;

  onSolutionPath = true;
  alternativeSolve = false;
  mouseslipUsed = false;
  /** Anzahl Fehlzüge (Züge, die weder der erwartete Lösungszug noch eine geduldete Alternative waren)
   *  in diesem Puzzle. Für die Wochenpost-Aufzeichnung (Fehlversuche); über alle Modi verfügbar. */
  wrongMoveCount = 0;
  currentEval = '';

  /** Von Chessable geduldete Alternativzüge je Halbzug als UCI (Schlüssel = Index in `solutionMoves`;
   *  aus `puzzle.altMoves` geparst, nur im Buch-/Kurs-Modus befüllt). Spielt der User an einem eigenen
   *  Zug eine dieser Alternativen statt des Hauptzugs, wird sie NICHT als Fehler gewertet: kurz gezeigt
   *  („auch eine Alternative"), dann zurückgenommen — der Hauptzug wird weiterhin verlangt. */
  protected altMovesByPly: Record<number, string[]> = {};
  /** Timer, der einen kurz gezeigten Alternativzug wieder zurücknimmt. */
  protected altHoldTimer?: ReturnType<typeof setTimeout>;
  /** Läuft gerade ein Alternativzug-Hold? Der Zustand ist dabei `THINKING`, obwohl der Solver seine
   *  Antwort längst gespielt hat und `this.chess` unangetastet ist — ohne dieses Flag hielt der
   *  Mausrutscher genau das für „Solver hat noch nicht geantwortet" und nahm einen Halbzug zu wenig
   *  zurück: die Stellung landete beim Gegner am Zug, kein Timer lief mehr, es gab keine legalen
   *  Züge und der Mausrutscher war verbraucht — nur noch Zurücksetzen oder Aufgeben. */
  protected altHolding = false;

  // ---- Off-Path-Warnung (OffPathPolicy): Weicht der User von der Lösung ab und spielt gegen Stockfish weiter,
  //      wird ab dem N-ten off-path-Zug (Einstellung, 0 = nie) einmalig gewarnt; der Hinweis selbst zeigt die
  //      überschriebene onOffPathWarning(). ----
  protected readonly offPath = new OffPathPolicy();
  /** True, sobald gewarnt wurde (für eine optionale Dauer-Anzeige im Template). */
  get offPathWarning(): boolean { return this.offPath.warning; }

  /** Anarchy per URL erzwungen (`?anarchy=max`): e.p.-Zwang gilt dann unabhängig von der Einstellung.
   *  Ohne URL-Zwang folgt der e.p.-Zwang im Crazy-Brett der Einstellung `prefs.enPassantForced`. */
  anarchyForcedByUrl = false;

  // ---- Lösungs-Durchsicht (Review) — von allen Modi geteilt ----
  reviewMode = false;
  reviewIndex = 0;
  protected solutionPlayTimer?: ReturnType<typeof setInterval>;

  // ---- Einstellungs-Panel (Zahnrad) — Offen-Zustand über alle Modi gemerkt ----
  showSettings = false;
  private static readonly SETTINGS_OPEN_KEY = 'rookhub_puzzle_settings_open';

  // ---- Auto-Advance nach dem Lösen: kurzer, sichtbarer, überspringbarer Countdown ----
  private readonly solvedTicker = new SecondsCountdown();
  /** Sekunden bis zum Auto-Weiter (0 = kein Countdown). */
  get solvedCountdown(): number { return this.solvedTicker.remaining; }
  protected static readonly SOLVED_COUNTDOWN_SECONDS = 2;

  // ---- Visualisierungs-/Blindfold-Modus (0 = aus, 1-4 = aktiv) ----
  visualizationMode = 0;
  vizMoves: string[] = [];
  protected frozenFen = '';
  protected vizStartWhite = true;
  protected vizStartNum = 1;
  /** Verdeck-Countdown, „Anzeigen"-Knopf und Gegnerzug-Pfeil (verdeckt wird mit der Stufe beim Ablauf). */
  private readonly viz = new VizController(() => applyVisualizationHide(this.visualizationMode));
  /** Level 2-4: Figuren versteckt (nach Countdown). */
  get vizPiecesHidden(): boolean { return this.viz.piecesHidden; }
  /** Show-Button aktiv (deckt Figuren/Steine auf — Toggle, läuft NICHT automatisch ab). */
  get vizShowPressed(): boolean { return this.viz.showPressed; }
  /** Wie oft der Show-Button in diesem Versuch zum Aufdecken gedrückt wurde. */
  get vizShowCount(): number { return this.viz.showCount; }
  /** Countdown-Sekunden bis Figuren verschwinden (Level 2-4). */
  get vizCountdownSeconds(): number { return this.viz.countdownSeconds; }
  /** Letzter Gegnerzug als Pfeil (Viz-Modus 1-4: immer nur der zuletzt gespielte Gegnerzug). */
  get vizOpponentLastMove(): [Key, Key] | undefined { return this.viz.opponentLastMove; }
  /** Eval wurde in diesem Versuch mindestens einmal eingeblendet. */
  evalShown = false;
  /** Pfeil-Anzeige ein/aus (Nutzer-Einstellung, aus Prefs geladen). */
  vizArrowEnabled = true;

  // ---- Tipps (gestuft 1→3, HintLadder). Wie viele Stufen aktuell aufgedeckt sind. ----
  private readonly hintLadder = new HintLadder();
  get hintLevel(): number { return this.hintLadder.level; }
  set hintLevel(level: number) { this.hintLadder.level = level; }
  /** Höchste je aufgedeckte Tipp-Stufe im GESAMTEN Solve (über alle Züge). Da `hintLevel` je Zug
   *  zurückgesetzt wird (Tipps gelten pro erwartetem Zug), ist DIES der Wert fürs `hintsUsed`-Statistikfeld. */
  protected get maxHintLevel(): number { return this.hintLadder.maxLevel; }
  protected set maxHintLevel(level: number) { this.hintLadder.maxLevel = level; }
  /**
   * Tipps in der aktiven Sprache (0–3). Default keine; die Modi überschreiben das:
   * Buch/Kurs liefert vorberechnete Tipps aus dem DTO, Standard berechnet sie on-the-fly.
   */
  get availableHints(): string[] { return []; }
  /** Anarchy-Modus (e.p. forciert): 3 (unterschiedlich formulierte) „En passant ist Pflicht"-Hinweise
   *  ersetzen die normalen Tipps. Komponenten überschreiben mit ihren übersetzten Strings. */
  protected get epForcedHints(): string[] { return []; }
  /** Off-Path-Tipps (3 Stufen): sobald der User vom Lösungsweg abgebogen ist, bleibt der Tipp-Knopf
   *  sichtbar und deckt gestuft auf, DASS falsch abgebogen wurde — Stufe 3 nennt die Zug-Nummer
   *  ({@link offPathUserMoveNumber}). Komponenten überschreiben mit ihren übersetzten Strings. */
  protected get offPathHints(): string[] { return []; }
  /** 1-basierte Nummer des eigenen Löserzugs, bei dem der User ZUERST vom Lösungsweg abbog; null
   *  solange auf dem Pfad. Speist Stufe 3 der Off-Path-Tipps. */
  get offPathUserMoveNumber(): number | null { return this.offPath.userMoveNumber; }
  /** Effektive Tipp-Liste: off-path die „falsch abgebogen"-Hinweise, im e.p.-Zwang die Anarchy-
   *  Hinweise, sonst die normalen Tipps. */
  private get effectiveHints(): string[] {
    return HintLadder.effective(this.onSolutionPath, this.offPathHints, this.enPassantForced, this.epForcedHints,
      this.availableHints);
  }
  get hasHints(): boolean { return this.effectiveHints.length > 0; }
  get shownHints(): string[] { return this.hintLadder.shown(this.effectiveHints); }
  get canShowMoreHints(): boolean { return this.hintLadder.canReveal(this.effectiveHints.length); }
  /** Gesamtzahl der aktuell relevanten Tipp-Stufen (für die „x/N"-Anzeige im Knopf). */
  get totalHints(): number { return this.effectiveHints.length; }
  /** Nächste Tipp-Stufe aufdecken (nur ON-PATH-Tipps zählen fürs `hintsUsed`-Statistikfeld). */
  showNextHint(): void {
    this.hintLadder.reveal(this.effectiveHints.length, this.onSolutionPath);
  }

  /** Klassifikation des AKTUELL erwarteten Löserzugs (`solutionMoves[moveIndex]` aus der aktuellen
   *  Stellung) — Basis für die on-the-fly-Tipps zu JEDEM Zug, nicht nur dem ersten. `null` außerhalb
   *  des Lösungspfads / am Ende / bei nicht spielbarem Zug. */
  protected get currentMoveHint(): FirstMoveHint | null {
    const uci = this.solutionMoves[this.moveIndex];
    if (!uci) return null;
    return classifyMoveFromFen(this.chess.fen(), uci);
  }

  /** Ist der aktuell erwartete Zug der ERSTE Löserzug? (Für Buch: nur dann passen die vorberechneten
   *  LLM-Tipps, die für den Schlüsselzug erzeugt wurden.) */
  protected get atFirstSolverMove(): boolean {
    return this.moveIndex === (this.startPly < 0 ? 0 : this.startPly + 1);
  }

  // ---- intern ----
  protected chess = new Chess();
  protected solutionMoves: string[] = [];
  /** Index des nächsten erwarteten Zugs in `solutionMoves` (von Hilfsklassen der Modi gelesen, nur die Basis schreibt). */
  moveIndex = 0;
  /** Vorspiel-Halbzug (siehe {@link setupSolver}); gelesen wie {@link moveIndex}. */
  startPly = 0;
  protected aborted = false;
  /** Ist die Komponente zerstört (Seite verlassen)? `MatDialogConfig.closeOnNavigation` ist
   *  standardmäßig true — die Lange-Lösezeit-Nachfrage schließt bei einem Routenwechsel also
   *  trotz `disableClose`, und ihr `afterClosed()` feuert NACH `ngOnDestroy`. Ohne diesen Riegel
   *  lief danach die komplette Abschluss-Kette weiter: Versuch erfassen, Countdown, nächstes
   *  Puzzle laden — samt neuem Sekunden-Timer auf einer zerstörten Komponente; im Endless-Modus
   *  schrieb derselbe Pfad einen beendeten Lauf wieder als „aktiv" auf den Server. */
  protected destroyed = false;
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
  /** Zählt nur aktive Tab-Zeit; meldet sich jede Sekunde ({@link onTimerTick}). */
  private readonly timer = new SolverTimer(seconds => {
    this.elapsedSeconds = seconds;
    this.onTimerTick();
  });

  constructor(protected stockfish: StockfishService) {}

  /** Lösezeit kompakt formatieren („1:05" / „45s"). Geteilt über alle Modi + Karten. */
  formatTime(seconds: number): string {
    return formatPuzzleTime(seconds);
  }

  /** Lösezeit-Timer starten (Standard-/Buch-Modus), idempotent (siehe {@link SolverTimer.start}).
   * `initialSeconds` setzt eine frühere (persistierte) Lösezeit fort — Tagespuzzle-Wiederbesuch. */
  protected startTimer(initialSeconds = 0): void {
    this.timer.start(initialSeconds);
    this.elapsedSeconds = initialSeconds;
  }

  /** Hook je 1-s-Anzeige-Tick des laufenden Lösezeit-Timers (Default: nichts) — z. B. um den
   * Zwischenstand zu persistieren, damit ein Wiederbesuch die Zeit fortführen kann. */
  protected onTimerTick(): void {}

  /** Lösezeit-Timer stoppen + finalen aktiven Stand festhalten. */
  protected stopTimer(): void {
    this.elapsedSeconds = this.timer.stop();
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
  /** Läuft gerade ein Lösungsversuch (Lös-Automat offen)? */
  get isSolving(): boolean {
    return this.state === 'AWAITING_USER_MOVE' || this.state === 'THINKING' || this.state === 'PLAYING';
  }

  get hasMadeFirstMove(): boolean { return this.moveLog.length > 0; }

  /** SAN-Zugliste (Visualisierungs-Modus) mit korrekten Zugnummern ab der Trainingsstellung. */
  get vizMoveText(): string {
    return formatSanList(this.vizMoves, this.vizStartWhite, this.vizStartNum);
  }

  /** Wie `vizMoveText`, aber Gegnerzüge in `<strong>` (für die [innerHTML]-Bindung der Solver-Templates). */
  get vizMoveHtml(): string {
    return formatSanListHtml(this.vizMoves, this.vizStartWhite, this.vizStartNum);
  }

  // ===== Setup =====
  /**
   * Zeigt die Stellung, die {@link setupSolver} gleich aufbaut (Vorspiel bis `startPly`, Blickrichtung des
   * Lösers) — OHNE etwas zu starten: kein Setup-Zug, keine Uhr, keine Zugziele, `state` bleibt. Für die Zeit
   * hinter dem Spielweise-Dialog ({@link awaitingSolveMode}); setupSolver setzt danach alles neu auf.
   */
  protected previewSetup(fen: string, movesStr: string, startPly = 0): void {
    const p = previewPosition(fen, movesStr, startPly);
    if (p) {
      this.orientation = p.orientation;
      this.boardFen = this.actualFen = p.fen;
      this.turnColor = p.turnColor;
    } else {
      this.boardFen = this.actualFen = fen;   // chess.js lehnt die FEN ab: wenigstens die Stellung zeigen
    }
    this.lastMove = undefined;
    this.isCheck = false;
    this.dests = new Map();
  }

  /**
   * Puzzle aufsetzen. `startPly`: -1 = FEN ist Trainingsstellung (lösen ab moves[0]);
   * 0 = klassisch (moves[0] Setup, lösen ab moves[1]); k = Vorspiel bis moves[k].
   */
  protected setupSolver(fen: string, movesStr: string, startPly = 0): void {
    if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
    if (this.altHoldTimer) clearTimeout(this.altHoldTimer);
    this.altHolding = false;
    this.altMovesByPly = {};    // wird von der Komponente nach setupSolver neu gesetzt (nur Buch/Kurs)
    this.clearSolutionPlay();   // evtl. laufende Lösungs-Wiedergabe (nach Aufgeben) stoppen
    this.stopCountdown();
    this.reviewMode = false;
    this.solverEpoch++;
    this.solutionMoves = movesStr.split(' ');
    this.startPly = clampStartPly(startPly, this.solutionMoves.length);
    this.moveIndex = 0;
    this.maxHintLevel = 0;   // Tipp-Nutzung fürs neue Puzzle zurücksetzen (hintLevel setzen die Modi)
    this.chess = new Chess(fen);
    this.onSolutionPath = true;
    this.aborted = false;
    this.mouseslipUsed = false;
    this.wrongMoveCount = 0;
    this.alternativeSolve = false;
    this.resetOffPathTracking();
    this.offPath.startEvalPawns = null;
    this.moveLog = [];
    this.evalShown = false;
    this.viz.showCount = 0;
    this.frozenFen = fen;
    this.vizMoves = [];
    this.clearVizOpponentArrow();
    this.endVisualizationHide();
    this.solutionHidden = false;
    this.onSetupStart();

    for (let i = 0; i < this.startPly; i++) applyUci(this.chess, this.solutionMoves[i]);
    this.lastMove = undefined;

    if (this.startPly < 0) {
      // FEN ist bereits die Trainingsstellung → sofort lösen, kein Setup-Zug.
      this.moveIndex = 0;
      this.orientation = solverOrientation(this.chess, this.solutionMoves, this.startPly);
      this.beginSolving();
      this.state = 'AWAITING_USER_MOVE';
      this.solvingBegins();
      this.updateBoard();
      return;
    }

    this.orientation = solverOrientation(this.chess, this.solutionMoves, this.startPly);
    this.updateBoard();
    this.state = 'SETUP';

    this.autoAdvanceTimer = setTimeout(() => {
      if (this.state !== 'SETUP') return;
      this.playMove(this.solutionMoves[this.startPly]);
      this.moveIndex = this.startPly + 1;
      this.beginSolving();
      this.state = 'AWAITING_USER_MOVE';
      this.solvingBegins();
      // Auch der ERSTE (automatische) Gegnerzug bekommt den Pfeil — genau der Zug, der die
      // Puzzle-Stellung erzeugt und den der Löser sehen will (v.a. auf dem Crazy-Brett, wo der
      // Zug kaum erkennbar ist). Vorher nur die Antwortzüge (advanceAfterCorrectMove/opponentRespond).
      this.showVizOpponentArrow();
      this.moveStartTime = Date.now();
      this.updateBoard();
    }, 600);
  }

  /** Trainingsstellung merken, dann den Hook der Komponente rufen. */
  private solvingBegins(): void {
    this.initialFen = this.chess.fen();
    this.onSolvingBegins();
  }

  protected beginSolving(): void {
    this.frozenFen = this.chess.fen();
    const f = this.frozenFen.split(' ');
    this.vizStartWhite = whiteToMove(this.frozenFen);
    this.vizStartNum = parseInt(f[5], 10) || 1;
    this.vizMoves = [];
    if (this.visualizationMode >= 2) this.viz.startCountdown();
    this.captureStartEval();
  }

  /** Einmalige Hintergrund-Eval der Trainings-Startstellung (nur wenn die Off-Path-Warnung aktiv
   *  ist), damit {@link maybeWarnOffPath} später weiß, ob die Ausgangsstellung ausgeglichen/remis
   *  war. Läuft asynchron und ist bis zur ersten Abzweigung (Default 3 Züge) längst fertig; die
   *  Stockfish-Suchen sind serialisiert, kollidieren also nicht mit den Antwort-Evals. */
  protected captureStartEval(): void {
    if (this.offPathWarnThreshold <= 0) return;
    const epoch = this.solverEpoch;
    const fen = this.chess.fen();
    const orient = this.orientation;
    this.stockfish.getBestMove(fen, this.depth)
      .then(r => {
        if (this.aborted || epoch !== this.solverEpoch) return;
        this.offPath.startEvalPawns = whiteEvalToPlayerPawns(r.eval, orient);
      })
      .catch(() => { /* Eval optional — Warntext fällt sonst auf den Standard zurück */ });
  }

  // ===== Zug-Handling =====
  onMoveMade(event: { orig: Key; dest: Key; promotion?: string }): void {
    // User zieht → einen noch stehenden Gegnerzug-Pfeil (Crazy-„persist") entfernen.
    if (this.vizOpponentLastMove) this.clearVizOpponentArrow();
    if (this.state === 'PLAYING') { this.handleOffPathMove(event); return; }
    if (this.state !== 'AWAITING_USER_MOVE') return;

    if (this.onSolutionPath) {
      const expectedUci = this.solutionMoves[this.moveIndex];
      const userUci = event.orig + event.dest + (event.promotion || '');
      const thinkMs = Date.now() - this.moveStartTime;
      // Das Urteil kommt aus dem gemeinsamen Kern (shared/chess/line-solver): Feldvergleich UND
      // [%alt]-Alternativen in EINEM Schritt, ohne den Zug anzuwenden — angewendet wird hier
      // weiterhin der ERWARTETE Zug (seine Umwandlungsfigur gehört der Lösung, nicht dem Dialog).
      // `illegal`/`not-your-turn` landen im selben Zweig wie `wrong`: dort fängt playFreeMove sie
      // ab, wie eh und je. Über die Bretter kommt das Brett-Component ohnehin nur mit legalen
      // Zügen herein.
      const verdict = judgeMove(this.chess, { uci: expectedUci }, this.altsAt(this.moveIndex),
                                event.orig, event.dest, event.promotion);

      if (verdict === 'correct') {
        this.moveLog.push({ i: this.moveIndex, uci: expectedUci, exp: expectedUci, ms: thinkMs, ok: true });
        this.playMove(expectedUci);
        this.moveIndex++;
        this.advanceAfterCorrectMove();
      } else if (verdict === 'alternative') {
        // Von Chessable geduldeter Alternativzug (softFail → [%alt]): kein Fehler. Kurz zeigen,
        // als Alternative würdigen, dann zurücknehmen und weiter auf den Hauptzug warten.
        this.handleAlternativeMove(event, userUci);
      } else {
        this.moveLog.push({ i: this.moveIndex, uci: userUci, exp: expectedUci, ms: thinkMs, ok: false });
        if (!this.playFreeMove(event.orig, event.dest, event.promotion)) return;
        this.onSolutionPath = false;
        this.wrongMoveCount++;     // Fehlzug (Abweichung vom Lösungszug)
        this.offPath.userPlies++;   // erster Abweich-Zug
        this.markOffPathDeviation();   // Zug-Nr der Abzweigung merken + Off-Path-Tipps von vorn
        if (this.chess.isGameOver()) { this.handleGameOver(); return; }
        this.opponentRespond();
      }
    } else {
      this.handleOffPathMove(event);
    }
  }

  protected handleOffPathMove(event: { orig: Key; dest: Key; promotion?: string }): void {
    if (!this.playFreeMove(event.orig, event.dest, event.promotion)) return;
    this.offPath.userPlies++;   // weiterer off-path-Zug des Users
    if (this.chess.isGameOver()) { this.handleGameOver(); return; }
    this.opponentRespond();
  }

  /** Die geduldeten Alternativen eines Halbzugs in der Form des Kerns ({@link ExpectedMove}).
   *  `altMovesByPly` hält sie als UCI — das bleibt so, der Import liefert nichts anderes. */
  protected altsAt(ply: number): ExpectedMove[] {
    const alts = this.altMovesByPly[ply];
    return alts ? alts.map(uci => ({ uci })) : [];
  }

  /**
   * Geduldeten Alternativzug behandeln (kein Fehler, kein Fortschritt): den Zug kurz auf dem Brett
   * zeigen, die Komponente darüber informieren ({@link onAlternativeMove} → Hinweis „auch eine
   * Alternative"), dann zurücknehmen und dieselbe Stellung wieder spielbar lassen — der Hauptzug
   * wird weiterhin verlangt. `this.chess` bleibt unangetastet (kein moveIndex-Fortschritt).
   */
  protected handleAlternativeMove(event: { orig: Key; dest: Key; promotion?: string }, userUci: string): void {
    // Zug testweise auf einer Kopie anwenden, um ihn kurz sichtbar zu machen.
    const probe = new Chess(this.chess.fen());
    tryFreeMove(probe, event.orig, event.dest, event.promotion);
    this.lastMove = [event.orig, event.dest];
    this.boardFen = probe.fen();
    this.turnColor = probe.turn() === 'w' ? 'white' : 'black';
    this.isCheck = probe.isCheck();
    this.dests = new Map();       // während des Holds gesperrt
    this.state = 'THINKING';
    this.altHolding = true;
    this.onAlternativeMove(userUci);
    if (this.altHoldTimer) clearTimeout(this.altHoldTimer);
    this.altHoldTimer = setTimeout(() => {
      this.altHolding = false;
      if (this.aborted) return;
      this.lastMove = undefined;
      this.state = 'AWAITING_USER_MOVE';
      this.moveStartTime = Date.now();
      this.updateBoard();         // schnappt auf die Stellung VOR dem Zug zurück (Hauptzug erwartet)
    }, ALT_HOLD_MS);
  }

  /** Hook: der User hat einen geduldeten Alternativzug gespielt. Komponente zeigt einen Hinweis. */
  protected onAlternativeMove(_userUci: string): void { /* von der Komponente überschrieben */ }

  protected advanceAfterCorrectMove(): void {
    if (this.moveIndex >= this.solutionMoves.length) { this.solvedInternal(false); return; }
    this.state = 'THINKING';
    this.updateBoard();
    this.autoAdvanceTimer = setTimeout(() => {
      if (this.aborted) return;
      this.playMove(this.solutionMoves[this.moveIndex]);
      this.moveIndex++;
      this.showVizOpponentArrow();
      this.updateBoard();
      if (this.moveIndex >= this.solutionMoves.length) { this.solvedInternal(false); return; }
      // Solver-Antwort im Lösungspfad → User soll seinen nächsten Lösungszug machen.
      // PLAYING ist reserviert für off-path/freies Spiel; in onMoveMade wird PLAYING
      // direkt als off-path interpretiert und der Lösungsvergleich übersprungen.
      this.state = 'AWAITING_USER_MOVE';
      this.hintLevel = 0;   // Tipps gelten PRO Zug → für den neuen erwarteten Zug wieder verdeckt (maxHintLevel bleibt)
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
      this.showVizOpponentArrow();
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
    // Zu einem Zug alle drei Tipps geholt: der Zug war vorgesagt — gewertet wie ein Fehlversuch (Lösung zeigen, nicht gelöst).
    if (this.hintLadder.failsSolve) { this.handleFailed(); return; }
    this.handleSolved(alternative);
  }

  mouseslip(): void {
    if (this.mouseslipUsed) return;
    if (this.onSolutionPath) {
      // Lösungspfad-Mouseslip: letzten Korrekt-Zug (+ ggf. bereits gespielte Solver-Antwort) rückgängig.
      if (this.moveLog.length === 0) return;
      if (this.autoAdvanceTimer) clearTimeout(this.autoAdvanceTimer);
      // Läuft ein Alternativzug-Hold, ihn abbrechen: sein Timer würde sonst nach Ablauf den
      // zurückgenommenen Stand mit `AWAITING_USER_MOVE` überschreiben.
      if (this.altHoldTimer) clearTimeout(this.altHoldTimer);
      const wasAltHold = this.altHolding;
      this.altHolding = false;
      this.aborted = false;
      this.solverEpoch++;
      // Im THINKING-State hat der Solver noch nicht geantwortet → nur 1 Zug zurück;
      // im AWAITING-State hat der Solver bereits gespielt → 2 Züge zurück. Der Alternativzug-Hold
      // steht ebenfalls auf THINKING, DORT hat der Solver aber schon geantwortet (und `this.chess`
      // ist unangetastet) — er gehört also zu den 2.
      const undoCount = (this.state === 'AWAITING_USER_MOVE' || wasAltHold) ? 2 : 1;
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

  /** Brett-Präsentation (FEN, Zugziele, Schach) aus dem Lös-Zustand neu ableiten. */
  updateBoard(): void {
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

  // ===== Visualization Level 2-4: Countdown + Hide (VizController) =====
  /** Toggle: deckt die Figuren auf bzw. blendet sie wieder aus. Läuft bewusst NICHT automatisch
   *  ab — der Spieler entscheidet selbst, wann wieder verdeckt wird. */
  onVizShow(): void {
    this.viz.toggleShow();   // nur das Aufdecken zählt
    // Level 1: Brett auf die tatsächliche Stellung umstellen bzw. wieder einfrieren
    // (Level 2–4 decken über die CSS-Klasse `.viz-hidden` auf, kein Board-Wechsel nötig).
    if (this.visualizationMode === 1) this.updateBoard();
  }

  protected markEvalShown(): void {
    this.evalShown = true;
  }

  // ===== Eval-Anzeige (alle Modi gleich; Tiefe über den Hook {@link depth}) =====
  /** Eval-Leiste eingeblendet? */
  showEval = false;
  evalLoading = false;
  /** Eval der Trainingsstellung (für den Vergleich in der Status-Karte), faul beim ersten Einblenden. */
  initialEval = '';
  /** Trainingsstellung — gesetzt, sobald gelöst werden kann (vor {@link onSolvingBegins}). */
  protected initialFen = '';

  /** Eval-Leiste umschalten; beim Einblenden während des Lösens sofort nachrechnen. */
  toggleEval(): void {
    this.showEval = !this.showEval;
    if (this.showEval) {
      this.markEvalShown();
      if (this.state === 'PLAYING' || this.state === 'AWAITING_USER_MOVE') this.refreshEval();
    }
  }

  protected async refreshEval(): Promise<void> {
    this.evalLoading = true;
    try {
      if (!this.initialEval && this.initialFen) {
        this.initialEval = await this.stockfish.getEval(this.initialFen, this.depth);
      }
      this.currentEval = await this.stockfish.getEval(this.chess.fen(), this.depth);
    } catch { /* Engine nicht verfügbar: Anzeige bleibt leer */ }
    this.evalLoading = false;
  }

  /** Nach jeder Stellungsänderung (Lösungspfad-Antwort, Gegnerzug, Mouseslip-Rücknahme) aufgerufen:
   *  zieht die eingeblendete Eval auf die aktuelle Stellung nach. */
  protected refreshEvalIfShown(): void {
    if (this.showEval) this.refreshEval();
  }

  // ===== Off-Path-Warnung =====

  /** Ab dem wievielten off-path-Zug gewarnt wird (0 = nie). Komponenten überschreiben mit dem
   *  Einstellungswert (`prefs.offPathWarnMoves`). Default 0 (aus), damit Tests/Basis nicht warnen. */
  protected get offPathWarnThreshold(): number { return 0; }

  /** Hook: wird EINMAL je Off-Path-Episode aufgerufen, sobald die Warn-Bedingung greift.
   *  Default no-op; Komponenten zeigen z. B. einen Snackbar-Hinweis. */
  protected onOffPathWarning(): void { /* von der Komponente überschrieben */ }

  private resetOffPathTracking(): void {
    this.offPath.reset();
    this.hintLevel = 0;   // zurück auf dem Pfad → Tipp-Stufe für den (wieder) erwarteten Zug verdeckt
  }

  /** Merkt EINMAL je Off-Path-Episode, der wievielte eigene Löserzug die falsche Abzweigung war
   *  (1-basiert), und setzt die Tipp-Stufe zurück, damit die Off-Path-Tipps von Stufe 1 aufdecken. */
  private markOffPathDeviation(): void {
    if (this.offPath.markDeviation(this.moveIndex, this.startPly)) this.hintLevel = 0;
  }

  /** Prüft nach einem off-path-Zug (Eval steht in currentEval, Weiß-Sicht) und warnt einmalig. */
  protected maybeWarnOffPath(): void {
    if (this.offPath.checkWarn(this.offPathWarnThreshold, this.playerEvalPawns())) this.onOffPathWarning();
  }

  /** War die Trainings-Startstellung ausgeglichen (Eval ≈ 0, Spieler-Sicht |x| < 1)? Dann lautet der
   *  Off-Path-Warntext „nicht mehr ausgeglichen" statt „nicht mehr am Gewinnen". */
  protected startWasBalanced(): boolean {
    return this.offPath.startWasBalanced;
  }

  /** currentEval (Weiß-Sicht) → Bauerneinheiten aus Spieler-Sicht; Matt = ±100, null ohne Eval. */
  protected playerEvalPawns(): number | null {
    return whiteEvalToPlayerPawns(this.currentEval, this.orientation);
  }

  /** Aktuelle Eval aus SPIELER-Sicht als kurzer Anzeige-String, oder null (Klammer im Off-Path-Hinweis). */
  protected playerEvalDisplay(): string | null {
    return playerEvalText(this.currentEval, this.orientation);
  }

  protected endVisualizationHide(): void {
    this.viz.endHide();
  }

  /** Wann der Gegnerzug-Pfeil gezeigt wird und wie lange (siehe {@link opponentArrowModeFor}). */
  protected get opponentArrowMode(): OpponentArrowMode {
    return opponentArrowModeFor(this.themeMode, this.visualizationMode);
  }

  /** Zeigt den letzten Gegnerzug als Pfeil — je nach {@link opponentArrowMode} zeitlich begrenzt
   *  (Viz, respektiert die Pfeil-Einstellung) oder bleibend bis zum nächsten User-Zug (Crazy). */
  private showVizOpponentArrow(): void {
    this.viz.showOpponentArrow(this.opponentArrowMode, this.vizArrowEnabled, this.lastMove);
  }

  /** Gegnerzug-Pfeil sofort entfernen. */
  clearVizOpponentArrow(): void {
    this.viz.clearOpponentArrow();
  }

  // ===== Lösungs-Durchsicht (geteilt) =====
  /** Endzustand-Review: ans Ende springen (keine Auto-Wiedergabe) — für gelöst/falsch. */
  protected enterSolutionReview(): void {
    this.reviewMode = true;
    this.reviewIndex = this.reviewTotal;
  }

  /**
   * Gemeinsamer Review-Aufbau (Lösungs-Durchsicht): Brett auf `index` gespielte Halbzüge ab
   * `fen` setzen. Genutzt von Standard- und Endless-Solver (Rumpf war dort byte-identisch
   * kopiert, beide OHNE FEN-Guard: `new Chess(fen)` wirft bei nicht ladbaren FENs mitten im
   * Render-Pfad und reißt die halbe Seite mit — Fehlerklasse aus 0.316.3/0.317.2). Der
   * Buch-Solver bleibt bei seiner EIGENEN, reicheren Variante (statischer Info-Fallback für
   * illegale Diagramm-FENs, Varianten-Vorschau, Zug-Kommentare, Pfeil-Shapes); den Brett-Stand
   * rechnen alle drei — und die Kurs-Durchsicht — über `lineStepAt` (`line-step.util`).
   * Liefert `false`, wenn die FEN nicht ladbar ist (der Aufrufer rendert dann statisch bzw.
   * lässt das Brett stehen); `reviewIndex` ist dann bereits geklemmt gesetzt.
   */
  protected reviewGoToCore(fen: string, moves: string[], index: number): boolean {
    const step = lineStepAt(fen, moves, index);
    this.reviewIndex = step.index;
    if (!step.chess) return false;
    this.chess = step.chess;
    this.lastMove = step.lastMove;
    this.boardFen = step.fen;
    this.turnColor = step.turnColor;
    this.isCheck = step.isCheck;
    this.dests = new Map();
    return true;
  }

  /**
   * Fehlzug, und die Antwort setzt den Spieler MATT (Wunsch 2026-10-23, alle Modi gleich): das Matt bleibt auf dem Brett
   * stehen statt in die Musterlösung zu springen — „Nochmal“ oder „Lösung zeigen“ entscheidet der Spieler selbst.
   * Die Komponenten prüfen in ihrem `handleFailed` {@link userIsMated} statt sofort `enterSolutionReview()`.
   */
  solutionHidden = false;

  /** Steht der Spieler auf dem Brett matt (die Antwort auf seinen Fehlzug)? */
  protected get userIsMated(): boolean {
    return this.chess.isCheckmate() && this.chess.turn() === (this.orientation === 'white' ? 'w' : 'b');
  }

  /** Nach `handleFailed`: Matt stehen lassen ODER wie bisher in die Lösungsansicht. */
  protected showFailureOrHoldMate(): void {
    if (this.userIsMated) this.solutionHidden = true;
    else this.enterSolutionReview();
  }

  /** „Lösung zeigen“ nach einem Matt gegen den Spieler: Lösung wie beim Aufgeben vorspielen (ohne erneuten Versuch). */
  revealSolution(): void {
    this.solutionHidden = false;
    this.playSolutionFromStart();
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

  /** Durchsicht einen Schritt vor (manuell: Countdown und Auto-Wiedergabe stoppen). Endless/Buch überschreiben. */
  reviewNext(): void { this.stopCountdown(); this.clearSolutionPlay(); this.reviewGoTo(this.reviewIndex + 1); }
  /** Durchsicht einen Schritt zurück. */
  reviewPrev(): void { this.stopCountdown(); this.clearSolutionPlay(); this.reviewGoTo(this.reviewIndex - 1); }
  /** Durchsicht verlassen. */
  exitReview(): void { this.reviewMode = false; }
  /** Nach alternativem (eigenem) Mattweg die vom Puzzle vorgesehene Lösung von vorne durchspielen. */
  showOriginalSolution(): void { this.stopCountdown(); this.playSolutionFromStart(); }

  /** Pfeil links/rechts → {@link reviewPrev}/{@link reviewNext}; liefert true, wenn die Taste eine davon war.
   *  Den `window:keydown`-Listener und die Bedingung, wann geblättert wird, hält die Komponente. */
  protected reviewArrowKey(e: KeyboardEvent): boolean {
    if (e.key === 'ArrowLeft') { this.reviewPrev(); return true; }
    if (e.key === 'ArrowRight') { this.reviewNext(); return true; }
    return false;
  }

  /** Laufende Lösungs-Wiedergabe (nach Aufgeben) anhalten. */
  clearSolutionPlay(): void {
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
    this.solvedTicker.start(BasePuzzleSolver.SOLVED_COUNTDOWN_SECONDS, onElapsed);
  }

  protected stopCountdown(): void {
    this.solvedTicker.stop();
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

  /** Beim Verlassen der Seite (ngOnDestroy): Löser abbrechen und die globalen Brett-Stile (Crazy-Figuren,
   *  Visualisierungs-Verdeckung) wieder entfernen. */
  protected releaseSolver(): void {
    this.abortSolver();
    clearCrazyStyles();
    clearVisualizationHide();
  }
}
