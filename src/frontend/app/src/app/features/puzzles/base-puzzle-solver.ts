import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { applyUci, tryFreeMove } from './puzzle-move.util';
import { whiteEvalToPlayerPawns } from './off-path-policy';
import { clampStartPly, solverOrientation } from './solver-setup.util';
import { ExpectedMove, judgeMove } from '../../shared/chess/line-solver';
import { whiteToMove } from '../../shared/chess/fen-side';
import { SolverReviewBase } from './solver-review-base';

export interface MoveLogEntry { i: number; uci: string; exp: string; ms: number; ok: boolean; }

export type { SolverCoreState } from './solver-board-base';

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
 * {@link depth}, {@link stockfishErrorContinues}, {@link onSetupStart}, {@link onSolvingBegins}, {@link onFirstWrongMove}.
 * Die konkrete Komponente setzt am Ende ihren eigenen `state` (SOLVED/FAILED/...) — die erlaubten Endzustände
 * gibt sie als Typ-Parameter an (`extends BasePuzzleSolver<'SOLVED' | 'FAILED'>`).
 *
 * Aufbau in Schichten (je eine Datei, ohne Angular): {@link SolverBoardBase} (Brett, chess.js, Visualisierung)
 * → {@link SolverAssistBase} (Tipps, Eval, Off-Path, Stoppuhr) → {@link SolverReviewBase} (Durchsicht,
 * Einstellungs-Panel, Countdown, Abbruch) → diese Klasse (Setup, Zug-Bewertung, Gegnerzug, Mausrutscher).
 */
export abstract class BasePuzzleSolver<S extends string = never> extends SolverReviewBase<S> {
  alternativeSolve = false;
  mouseslipUsed = false;
  /** Anzahl Fehlzüge (Züge, die weder der erwartete Lösungszug noch eine geduldete Alternative waren)
   *  in diesem Puzzle. Für die Wochenpost-Aufzeichnung (Fehlversuche); über alle Modi verfügbar. */
  wrongMoveCount = 0;

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

  protected moveLog: MoveLogEntry[] = [];
  protected moveStartTime = 0;

  /** Ob seit dem letzten Fehlzug wirklich ein Gegnerzug gespielt wurde (fuer Mouseslip-Undo). */
  protected lastOpponentReplied = false;

  // ===== Hooks (von den Komponenten überschrieben) =====
  /** Puzzle gelöst — Komponente setzt Endzustand + Mode-Logik (Elo/Leben/Review/…). */
  protected abstract handleSolved(alternative: boolean): void;
  /** Puzzle verloren — Komponente setzt Endzustand + Mode-Logik. */
  protected abstract handleFailed(): void;
  /** Bei Stockfish-Fehler weiterspielen (true) oder als verloren werten (false, z.B. Buch). */
  protected get stockfishErrorContinues(): boolean { return true; }
  /** Am Anfang von setupSolver (z.B. Theme-Modus anwenden). */
  protected onSetupStart(): void {}
  /** Sobald gelöst werden kann (Trainingsstellung erreicht) — z.B. Timer/initialFen. */
  protected onSolvingBegins(): void {}
  /**
   * Erster Fehlzug dieses Lösungsgangs (Produktentscheidung P-1, 2026-10-25): die Komponente meldet SOFORT den
   * Fehlversuch (Standard `recordAttempt(false)`, Kurs `recordCourseAttempt(false)`, Endless über den Leben-Mechanismus …).
   * Danach darf weitergespielt werden (Übung) — die Komponente sorgt dafür, dass derselbe Aufgaben-Durchgang weder ein
   * zweites Mal noch am Ende als „gelöst“ gemeldet wird (auch nicht nach „Zurücksetzen“/„Nochmal“). Der Mausrutscher
   * meldet nichts: er nimmt den Zug auf dem Brett zurück, der Fehlversuch bleibt gezählt. Läuft je
   * {@link setupSolver} höchstens einmal (bei `wrongMoveCount` 0 → 1).
   */
  protected onFirstWrongMove(): void { /* Basis: nichts melden — die Komponenten überschreiben */ }

  get hasMadeFirstMove(): boolean { return this.moveLog.length > 0; }

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
        // P-1: der ERSTE Fehlzug meldet sofort (wie Lichess) — sonst umgingen „Zurücksetzen“ und Neuladen die
        // Wertung. Ein abweichender Zug, der selbst mattsetzt, ist eine Lösung (handleGameOver), kein Fehlzug.
        if (this.wrongMoveCount === 1 && !this.chess.isCheckmate()) this.onFirstWrongMove();
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
}
