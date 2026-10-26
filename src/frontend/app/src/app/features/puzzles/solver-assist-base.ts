import { StockfishService } from './stockfish.service';
import { HintLadder } from './hint-ladder';
import { OffPathPolicy, playerEvalText, whiteEvalToPlayerPawns } from './off-path-policy';
import { SolverTimer } from './solver-timer';
import { formatPuzzleTime } from './puzzle-format.util';
import { classifyMoveFromFen, FirstMoveHint } from './puzzle-hints.util';
import { SolverBoardBase } from './solver-board-base';

/**
 * Zweite Schicht des Lös-Automaten ({@link BasePuzzleSolver}): alles, was dem Löser beim Rechnen hilft oder
 * ihn misst — gestufte Tipps ({@link HintLadder}), Eval-Leiste (Stockfish, Tiefe über {@link depth}),
 * Off-Path-Warnung ({@link OffPathPolicy}) und die Lösezeit-Stoppuhr ({@link SolverTimer}); dazu der Riegel
 * gegen verspätete Stockfish-Antworten ({@link aborted}/{@link solverEpoch}).
 */
export abstract class SolverAssistBase<S extends string = never> extends SolverBoardBase<S> {
  onSolutionPath = true;
  currentEval = '';

  // ---- Off-Path-Warnung (OffPathPolicy): Weicht der User von der Lösung ab und spielt gegen Stockfish weiter,
  //      wird ab dem N-ten off-path-Zug (Einstellung, 0 = nie) einmalig gewarnt; der Hinweis selbst zeigt die
  //      überschriebene onOffPathWarning(). ----
  protected readonly offPath = new OffPathPolicy();
  /** True, sobald gewarnt wurde (für eine optionale Dauer-Anzeige im Template). */
  get offPathWarning(): boolean { return this.offPath.warning; }

  // ---- Tipps (gestuft 1→3, HintLadder). Wie viele Stufen aktuell aufgedeckt sind. ----
  protected readonly hintLadder = new HintLadder();
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

  constructor(protected stockfish: StockfishService) { super(); }

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

  /** Stockfish-Suchtiefe (Komponente liefert ihre Einstellung). */
  protected get depth(): number { return 16; }

  // ---- Stockfish-Aufrufe: Abbruch + Epoche (verspätete Antworten verwerfen) ----
  protected aborted = false;
  /**
   * Jeder Lös-Vorgang hat eine Epoch. Bei setupSolver/mouseslip/abortSolver wird sie inkrementiert.
   * Laufende Stockfish-Aufrufe ({@link opponentRespond}) merken sich ihre Epoch und brechen ab,
   * wenn sie verstrichen ist — so kann ein verspätet ankommender Stockfish-Zug das frisch
   * resettete Brett nicht mehr verschmutzen (`aborted` allein reicht nicht, weil reset/mouseslip
   * es direkt wieder auf false setzen).
   */
  protected solverEpoch = 0;

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

  protected resetOffPathTracking(): void {
    this.offPath.reset();
    this.hintLevel = 0;   // zurück auf dem Pfad → Tipp-Stufe für den (wieder) erwarteten Zug verdeckt
  }

  /** Merkt EINMAL je Off-Path-Episode, der wievielte eigene Löserzug die falsche Abzweigung war
   *  (1-basiert), und setzt die Tipp-Stufe zurück, damit die Off-Path-Tipps von Stufe 1 aufdecken. */
  protected markOffPathDeviation(): void {
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
}
