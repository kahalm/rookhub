import { SecondsCountdown } from './solver-timer';
import { lineStepAt } from './line-step.util';
import { clearCrazyStyles, clearVisualizationHide } from './board-theme.util';
import { SolverAssistBase } from './solver-assist-base';

/**
 * Dritte Schicht des Lös-Automaten ({@link BasePuzzleSolver}): Lösungs-Durchsicht (Review, Aufgeben-Wiedergabe,
 * Matt stehen lassen), der Offen-Zustand des Einstellungs-Panels, der Auto-Weiter-Countdown nach dem Lösen und
 * der Abbruch eines Lösungsgangs ({@link abortSolver}/{@link releaseSolver}).
 * Wie die Durchsicht ein Brett aufbaut, liefert jeder Modus über {@link reviewTotal}/{@link reviewGoTo}.
 */
export abstract class SolverReviewBase<S extends string = never> extends SolverAssistBase<S> {
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

  // ---- Lebenszyklus des Lösungsgangs (Seite verlassen, Auto-Weiter-Timer) ----
  /** Ist die Komponente zerstört (Seite verlassen)? `MatDialogConfig.closeOnNavigation` ist
   *  standardmäßig true — die Lange-Lösezeit-Nachfrage schließt bei einem Routenwechsel also
   *  trotz `disableClose`, und ihr `afterClosed()` feuert NACH `ngOnDestroy`. Ohne diesen Riegel
   *  lief danach die komplette Abschluss-Kette weiter: Versuch erfassen, Countdown, nächstes
   *  Puzzle laden — samt neuem Sekunden-Timer auf einer zerstörten Komponente; im Endless-Modus
   *  schrieb derselbe Pfad einen beendeten Lauf wieder als „aktiv" auf den Server. */
  protected destroyed = false;
  protected autoAdvanceTimer?: ReturnType<typeof setTimeout>;

  /** Anzahl Schritte in der Lösungs-Durchsicht (mode-spezifisch). */
  abstract get reviewTotal(): number;
  /** Brett auf Schritt `index` der Lösungs-Durchsicht aufbauen (mode-spezifisch). */
  protected abstract reviewGoTo(index: number): void;

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
    try { localStorage.setItem(SolverReviewBase.SETTINGS_OPEN_KEY, String(this.showSettings)); } catch { /* ignore */ }
  }

  /** Gemerkten Offen-Zustand laden — von der Komponente beim Init aufrufen. */
  protected loadSettingsOpen(): void {
    try { this.showSettings = localStorage.getItem(SolverReviewBase.SETTINGS_OPEN_KEY) === 'true'; } catch { /* ignore */ }
  }

  // ===== Auto-Advance-Countdown =====
  /** Kurzen Countdown bis zum nächsten Puzzle starten; `onElapsed` läuft bei 0. Jederzeit
   *  per {@link stopCountdown} (z.B. „Weiter"-Klick oder Review-Interaktion) überspringbar. */
  protected startSolvedCountdown(onElapsed: () => void): void {
    this.solvedTicker.start(SolverReviewBase.SOLVED_COUNTDOWN_SECONDS, onElapsed);
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
