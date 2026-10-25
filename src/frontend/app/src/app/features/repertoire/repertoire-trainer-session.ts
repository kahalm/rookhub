import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { applyUci, tryFreeMove, tryLoadFen } from '../puzzles/puzzle-move.util';
import { ExpectedMove, judgeMove, resolveExpectedUci } from '../../shared/chess/line-solver';
import { whiteToMove } from '../../shared/chess/fen-side';
import type { ParsedGame } from '../../shared/pgn-viewer/pgn-parser';
import type { LineReviewRequest } from './repertoire-training.service';
import type { TrainColor } from './repertoire-color.util';
import { normFen, RepertoireGraph } from './repertoire-tree.util';
import { destsAt, userMoveCount } from './repertoire-trainer.util';
import { lineKeyOf } from './repertoire-trainer-queue.util';
import {
  LineMoveToken, commentParagraphs, lineChapterTitle, lineLabel, lineMoveTokens, plyComment, plyPrettyLabel,
} from './repertoire-trainer-line-view.util';

export type TrainerPhase = 'LOADING' | 'EMPTY' | 'PLAYING' | 'FEEDBACK' | 'DONE' | 'LINE_DONE' | 'LEARN_SHOW' | 'COMMENT';
export type TrainerOutcome = 'correct' | 'tolerated' | 'wrong';
export type TrainerMode = 'quiz' | 'learn';
export interface BoardMove { orig: Key; dest: Key; promotion?: string }

// Wie lange das „Correct/Tolerated"-Feedback nach einem Zug stehen bleibt, bevor automatisch
// weitergerückt wird (der User kann per Klick/Leertaste/Enter überspringen). correct: 1,5 s → 600 →
// 300 ms (der grüne Haken blitzt kurz, dann kommt zügig der Gegnerzug). Geduldet bleibt länger
// stehen (der Alternativzug soll wirken).
export const ADVANCE_MS: Record<TrainerOutcome, number> = { correct: 300, tolerated: 1500, wrong: 0 };
export const OPP_MOVE_DELAY_MS = 250;   // kurze Pause vor jedem automatischen Gegnerzug
export const LEARN_SHOW_MS = 1000;      // Zug im Learn-Modus ohne Kommentar so lange zeigen, dann zurücknehmen
export const LEARN_GAP_MS = 400;        // Learn: Pause zwischen Gegnerzug und dem Zeigen des eigenen Zugs (fühlte sich beim Wiederholen zu lang an, halbiert von 800)
// Learn-Modus: wie oft eine Linie durchgespielt werden muss, bevor sie in den Übungspool wandert
// (1× geführt lernen + 2× „durchklicken").
export const LEARN_REPEATS = 3;

const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

/** Uhr der Sitzung — im Trainer die des Browsers, in Specs eine von Hand weitergestellte. */
export interface TrainerClock {
  setTimeout(fn: () => void, ms: number): unknown;
  clearTimeout(handle: unknown): void;
}

export const browserClock: TrainerClock = {
  setTimeout: (fn, ms) => setTimeout(fn, ms),
  clearTimeout: h => clearTimeout(h as ReturnType<typeof setTimeout>),
};

/** Was die Sitzung nach außen meldet (die Komponente verdrahtet SR-Abgleich, Bewertungsvergleich, Change Detection). */
export interface TrainerSessionHooks {
  /** Anzeige geändert (OnPush: markForCheck). */
  changed(): void;
  /** Trainingsfarbe einer Linie (aus ihrem Kapitel). */
  colorOf(line: ParsedGame): TrainColor;
  /** Lern-Modus: Linie oft genug durchgespielt → in den Pool. */
  lineLearned(lineKey: string): void;
  /** Abfragen: Urteil über die Linie (fehlerfrei oder nicht). Die Fälligkeit kommt über {@link TrainerSession.showNextRepeat} zurück. */
  lineReviewed(req: LineReviewRequest): void;
  /** Fehlzug: Bewertungsvergleich Spielerzug ↔ Repertoirezug starten. */
  wrongMove(startFen: string, fenAfterPlayer: string, expectedSan: string, color: TrainColor): void;
  /** Neuversuch nach dem Fehlzug: laufenden Vergleich verwerfen. */
  retryAfterWrong(): void;
}

/**
 * Die Zustandsmaschine des Repertoire-Trainers (Zerlegung Z21, Muster `LineSolver`): eine Warteschlange von Linien wird
 * Linie für Linie durchgespielt — Gegnerzüge automatisch, an jedem eigenen Zug wird der Nutzerzug gegen die Linie
 * geurteilt (erwarteter Zug + [%alt]-geduldete Alternativen über `shared/chess/line-solver`). Phasen:
 * `PLAYING` (am Zug bzw. Gegnerzug-Fenster `oppMoving`) → `FEEDBACK` (richtig/geduldet/falsch) → … → `LINE_DONE` →
 * nächste Linie bzw. `DONE`; im Lern-Modus zusätzlich `LEARN_SHOW` (Zug wird vorgezeigt) und `COMMENT` (Kommentar
 * eines Gegnerzugs hält an). `LOADING`/`EMPTY` setzt der Aufrufer. Ohne Angular; Timer laufen über die {@link TrainerClock}.
 *
 * <p>Das Brett (`chess`) führt die Sitzung selbst und WEIST es neu zu (jede Linie, „Lösung zeigen"); verglichen werden
 * FELDER statt Zug-TEXT. Bei der ANZEIGE (`expectedDisplay`, `movesInLine`, `currentMovePrettyLabel`) und beim
 * Eval-Vergleich bleibt es bei der SAN — dort ist sie Text für Menschen, kein Urteil.</p>
 */
export class TrainerSession {
  phase: TrainerPhase = 'LOADING';
  /** 'quiz' = fällige Pool-Linien abfragen; 'learn' = neue Linien durchspielen → in Pool. */
  mode: TrainerMode = 'quiz';
  /** Trainingsfarbe der AKTUELL laufenden Linie (Brett-Orientierung / „X am Zug" / Eval-Vorzeichen). */
  color: TrainColor = 'w';
  queue: ParsedGame[] = [];
  qIndex = 0;
  /** Repertoire-Graph für die geduldeten Alternativen je Stellung. */
  graph: RepertoireGraph | null = null;

  fen = START_FEN;
  dests = new Map<Key, Key[]>();
  lastMove?: [Key, Key];
  currentPly = 0;
  startFen = '';
  correct = 0;
  wrong = 0;
  currentStreak = 0;   // fortlaufende ✓/geduldet-Zug-Serie; echter Fehler setzt zurück (Mausrutscher nicht).
  bestStreak = 0;      // Session-Rekord.
  sessionUserMoves = 0;   // Zähler für Progress-Balken (grob geschätzt)

  outcome: TrainerOutcome = 'correct';
  expectedDisplay = '';
  wrongRevealed = false;
  /** Ergebnis der AKTUELLEN Linie: true, sobald ein NICHT als Mausrutscher verziehener Fehler passiert ist. */
  lineHadWrong = false;
  /** Ein offener Fehler an der aktuellen Stellung, der noch als Mausrutscher verziehen werden kann. */
  pendingWrong = false;
  /** LINE_DONE (Abfragen): wann die gerade beendete Linie erneut fällig wird (ISO); null solange offen bzw. Lernen. */
  nextRepeatAt: string | null = null;
  /** true, während der Gegnerzug automatisch gespielt wird — erlaubt einen Premove. */
  oppMoving = false;
  /** Learn-Modus: Kommentar des gerade gezeigten Zugs (hält die Anzeige, bis der User weitertippt). */
  learnComment = '';
  /** COMMENT-Phase: Kommentar eines gerade gespielten Gegnerzugs, hält bis der User „Weiter" bestätigt. */
  holdComment = '';
  /** Learn-Modus: wie oft die AKTUELLE Linie schon komplett durchgespielt wurde (bis {@link LEARN_REPEATS}). */
  learnPass = 0;
  /** LINE_DONE: true = beim „Weiter" wird DIESELBE Linie erneut gestartet (Learn-Wiederholung). */
  pendingRepeat = false;

  advanceTimer: unknown = null;
  private oppTimer: unknown = null;
  private learnTimer: unknown = null;
  private chess = new Chess();

  constructor(private readonly hooks: TrainerSessionHooks, private readonly clock: TrainerClock = browserClock) {}

  // ===== Warteschlange =====

  /** Neue Warteschlange von vorn (Zähler zurück). Leer → `EMPTY`. */
  start(queue: ParsedGame[]): void {
    this.queue = queue;
    this.qIndex = 0;
    this.learnPass = 0;
    this.correct = 0;
    this.wrong = 0;
    this.currentStreak = 0;
    this.bestStreak = 0;
    this.sessionUserMoves = this.countUserMoves(queue);
    if (queue.length === 0) {
      this.phase = 'EMPTY';
      this.hooks.changed();
      return;
    }
    this.startCurrentLine();
  }

  /** Warteschlange ersetzen, ohne die laufende Linie anzufassen (Gegner-Reihung kam nach). */
  extend(queue: ParsedGame[]): void {
    this.queue = queue;
    this.sessionUserMoves = this.countUserMoves(queue);
    this.hooks.changed();
  }

  private countUserMoves(queue: ParsedGame[]): number {
    return queue.reduce((sum, l) => sum + userMoveCount(l, this.hooks.colorOf(l)), 0);
  }

  startCurrentLine(): void {
    const line = this.queue[this.qIndex];
    if (!line) { this.phase = 'DONE'; this.hooks.changed(); return; }
    // Timer der VORHERIGEN Linie unbedingt wegräumen — sonst kann ein spät feuernder
    // advance/opp/learn-Timer die neue Linie desynchronisieren (currentPly-Overshoot).
    this.clearTimers();
    // Brett-Orientierung / „am Zug"-Text / Eval-Vorzeichen richten sich nach der Farbe DIESER Linie.
    this.color = this.hooks.colorOf(line);
    this.chess = new Chess(line.fens[0]);
    this.currentPly = 0;
    this.lineHadWrong = false;
    this.pendingWrong = false;
    this.oppMoving = false;
    this.nextRepeatAt = null;
    this.holdComment = '';
    this.fen = this.chess.fen();
    this.lastMove = undefined;
    this.advanceToUserMove();
  }

  /** Spielt Gegnerzüge nacheinander, bis der User dran ist — oder die Linie zu Ende ist. */
  private advanceToUserMove(): void {
    const line = this.queue[this.qIndex];
    if (!line) { this.phase = 'DONE'; this.hooks.changed(); return; }
    if (this.currentPly >= line.moves.length) { this.finishLine(); return; }

    if (this.chess.turn() === this.color) {
      // User ist am Zug.
      this.oppMoving = false;
      this.startFen = this.fen;
      if (this.mode === 'learn') {
        // Nur der ERSTE Durchlauf (learnPass 0) zeigt den erwarteten Zug vor (geführtes Lernen);
        // die Wiederholungs-Durchläufe verlangen ihn aus dem Gedächtnis.
        if (this.learnPass === 0) { this.enterLearnShow(); }
        else { this.enterLearnPlay(); }
        return;
      }
      this.dests = destsAt(this.fen);
      this.phase = 'PLAYING';
      this.hooks.changed();
      return;
    }

    // Gegnerzug automatisch spielen (mit kleiner Verzögerung, damit man ihn wahrnimmt).
    // oppMoving=true erlaubt dem User in diesem Fenster einen Premove.
    this.oppMoving = true;
    this.phase = 'PLAYING';   // Zwischenzustand: view-only, kein Feedback-Kasten
    this.dests = new Map();
    this.hooks.changed();
    this.oppTimer = this.clock.setTimeout(() => {
      this.oppTimer = null;
      const m = line.moves[this.currentPly];
      try {
        const mv = this.chess.move(m.san);
        if (!mv) { this.finishLine(); return; }
        this.fen = this.chess.fen();
        this.lastMove = [mv.from as Key, mv.to as Key];
      } catch { this.finishLine(); return; }
      this.currentPly++;
      this.hooks.changed();
      // Learn: hat der gerade gespielte Gegnerzug einen Kommentar, halten wir an und zeigen ihn
      // dauerhaft (bis der User „Weiter" bestätigt) — sonst nur die kurze Pause bis zum nächsten Zug.
      if (this.mode === 'learn') {
        const playedComment = (line.comments?.[this.currentPly - 1] || '').trim();
        if (playedComment) { this.enterCommentHold(playedComment); return; }
        this.oppTimer = this.clock.setTimeout(() => { this.oppTimer = null; this.advanceToUserMove(); }, LEARN_GAP_MS);
      } else {
        this.advanceToUserMove();
      }
    }, OPP_MOVE_DELAY_MS);
  }

  private finishLine(): void {
    const line = this.queue[this.qIndex];
    this.oppMoving = false;
    this.nextRepeatAt = null;

    // Learn-Modus: eine Linie muss LEARN_REPEATS-mal durchgespielt werden, bevor sie in den Pool wandert.
    if (this.mode === 'learn' && line) {
      this.learnPass++;
      if (this.learnPass < LEARN_REPEATS) {
        // Noch nicht oft genug → beim „Weiter" dieselbe Linie erneut (noch nicht in den Pool).
        this.pendingRepeat = true;
        this.phase = 'LINE_DONE';
        this.hooks.changed();
        return;
      }
      this.hooks.lineLearned(lineKeyOf(line));
    } else if (line) {
      // Quiz: SR-Bewertung PRO LINIE. Erst in den „Linie fertig"-Zustand wechseln, DANN bewerten — eine
      // synchron eintreffende (oder lokal gerechnete) Fälligkeit darf sofort in den offenen Kasten.
      this.pendingRepeat = false;
      this.phase = 'LINE_DONE';
      const label = (line.headers['White'] || '').trim().slice(0, 120);
      this.hooks.lineReviewed({ lineKey: lineKeyOf(line), label, correct: !this.lineHadWrong });
    }
    // Nicht mehr automatisch weiter — der User rückt per „Weiter" vor. Timer werden BEWUSST hier NICHT
    // geräumt: `startCurrentLine` räumt sie beim Übergang zur nächsten Linie.
    this.pendingRepeat = false;
    this.phase = 'LINE_DONE';
    this.hooks.changed();
  }

  /** Fälligkeit der eben beendeten Linie anzeigen — nur solange ihr „Linie fertig"-Kasten offen ist. */
  showNextRepeat(dueAt: string): void {
    if (this.phase === 'LINE_DONE') { this.nextRepeatAt = dueAt; this.hooks.changed(); }
  }

  /** „Weiter" nach einer fertig gespielten Linie: dieselbe Linie erneut (Learn-Wiederholung) oder die nächste. */
  continueLine(): void {
    if (this.phase !== 'LINE_DONE') return;
    this.clearOppTimer();
    if (this.pendingRepeat) {
      this.pendingRepeat = false;
      this.startCurrentLine();          // dieselbe Linie (qIndex unverändert), learnPass bleibt
    } else {
      this.qIndex++;
      this.learnPass = 0;
      this.startCurrentLine();
    }
  }

  /** Tippen/Klick/Leertaste = „Weiter": true, wenn es in dieser Phase etwas bewirkt hat. */
  tap(): boolean {
    if (this.phase === 'LINE_DONE') this.continueLine();
    else if (this.phase === 'COMMENT') this.continueFromComment();
    else if (this.phase === 'LEARN_SHOW' && this.learnComment) this.learnRetract();
    else if (this.phase === 'FEEDBACK' && this.outcome !== 'wrong' && this.advanceTimer !== null) this.runAdvance();
    else return false;
    return true;
  }

  // ===== Anzeige =====

  get isPlayable(): boolean {
    if (this.phase === 'PLAYING') return true;
    // Nach falschem Zug UND vor „Lösung zeigen": weiter spielbar (sofortiger Retry).
    return this.phase === 'FEEDBACK' && this.outcome === 'wrong' && !this.wrongRevealed;
  }

  get progressPct(): number {
    if (this.sessionUserMoves === 0) return 0;
    return Math.round(((this.correct + this.wrong) / this.sessionUserMoves) * 100);
  }

  get currentLine(): ParsedGame | undefined { return this.queue[this.qIndex]; }

  get currentLineLabel(): string { return lineLabel(this.currentLine); }

  get currentLineChapter(): string { return lineChapterTitle(this.currentLine); }

  /** Züge der aktuellen Linie fürs Template. Im Abfragen-Modus, solange der User seinen Zug noch SUCHT (PLAYING),
   *  erscheint der aktuelle Halbzug NICHT — sonst stünde die Lösung in der Zugliste. */
  get movesInLine(): LineMoveToken[] {
    const revealCurrent = this.mode === 'learn' || this.phase !== 'PLAYING';
    return lineMoveTokens(this.currentLine, this.currentPly, revealCurrent);
  }

  /** PGN-Kommentar zum AKTUELL zu spielenden Halbzug (Lern-Modus). */
  get currentComment(): string { return plyComment(this.currentLine, this.currentPly); }

  get currentCommentParagraphs(): string[] { return commentParagraphs(this.currentComment); }

  /** Menschenlesbares Label des aktuellen Halbzugs, z. B. „3. exd5" oder „2… d6". */
  get currentMovePrettyLabel(): string { return plyPrettyLabel(this.currentLine, this.currentPly); }

  get userColorName(): 'white' | 'black' { return this.color === 'w' ? 'white' : 'black'; }

  get boardTurn(): 'white' | 'black' { return whiteToMove(this.fen) ? 'white' : 'black'; }

  /** Premove erlauben, solange der GEGNER am Zug ist — während des automatischen Gegnerzugs und im kurzen
   *  Feedback nach einem richtigen/geduldeten Zug. Nur im Abfragen-Modus. */
  get premovable(): boolean {
    if (this.mode !== 'quiz') return false;
    if (this.boardTurn === this.userColorName) return false;
    if (this.oppMoving) return true;
    return this.phase === 'FEEDBACK' && this.outcome !== 'wrong';
  }

  // ===== Abfragen =====

  private bumpStreak(): void {
    this.currentStreak++;
    if (this.currentStreak > this.bestStreak) this.bestStreak = this.currentStreak;
  }

  /** Die geduldeten Alternativen ([%alt]) DIESER Stellung in der Form des gemeinsamen Kerns. Der Hauptzug wird
   *  bewusst NICHT ausgesiebt: {@link judgeMove} prüft den ERWARTETEN Zug zuerst und antwortet dann `correct`. */
  altsAt(cardKey: string): ExpectedMove[] {
    const list = this.graph?.moves.get(cardKey);
    if (!list) return [];
    const out: ExpectedMove[] = [];
    const seen = new Set<string>();
    for (const m of list) {
      for (const a of m.alts) {
        if (seen.has(a)) continue;
        seen.add(a);
        out.push({ san: a });
      }
    }
    return out;
  }

  onMove(ev: BoardMove): void {
    const line = this.queue[this.qIndex];
    if (!line || this.currentPly >= line.moves.length) return;
    if (this.mode === 'learn') { this.onLearnMove(ev); return; }
    const wrongRetry = this.phase === 'FEEDBACK' && this.outcome === 'wrong' && !this.wrongRevealed;
    if (this.phase !== 'PLAYING' && !wrongRetry) return;
    if (wrongRetry) this.hooks.retryAfterWrong();

    // Geurteilt wird gegen GENAU diese Stellung — `this.fen`, nicht `this.chess`: im Wiederhol-Fall
    // nach einem Fehlzug ist `this.fen` bereits auf die Ausgangsstellung des Halbzugs zurückgesetzt.
    this.startFen = this.fen;
    const board = tryLoadFen(this.fen);
    if (!board) return;

    const expectedMove = line.moves[this.currentPly];
    const expected: ExpectedMove = { san: expectedMove.san };
    // EIN Urteil aus dem gemeinsamen Kern: erwarteter Zug UND geduldete Alternativen; `illegal`/`not-your-turn`:
    // kein Feedback, nichts passiert (über das Brett kommen ohnehin nur legale Züge herein).
    const verdict = judgeMove(board, expected, this.altsAt(normFen(this.fen)), ev.orig, ev.dest, ev.promotion);
    if (verdict === 'illegal' || verdict === 'not-your-turn') return;
    const expectedUci = resolveExpectedUci(board, expected);

    // Den Nutzerzug auf der KOPIE anwenden: seine Folgestellung braucht der Eval-Vergleich.
    if (!tryFreeMove(board, ev.orig, ev.dest, ev.promotion)) return;
    const fenAfterPlayer = board.fen();
    this.lastMove = [ev.orig, ev.dest];

    // SR wird PRO LINIE bewertet (finishLine); hier nur Feedback + ob die Linie schon einen Fehler hatte.
    if (verdict === 'correct') {
      this.outcome = 'correct'; this.correct++;
      this.countPendingWrong();
      this.bumpStreak();
      // Nachgeführt wird der ERWARTETE Zug, nicht der rohe Nutzerzug (Umwandlungsfigur der LINIE).
      if (expectedUci) { try { applyUci(this.chess, expectedUci); } catch { /* Brett steht woanders */ } }
      this.fen = this.chess.fen();
    } else if (verdict === 'alternative') {
      this.outcome = 'tolerated';
      this.countPendingWrong();
      this.bumpStreak();
      this.fen = fenAfterPlayer;
    } else {
      // Falscher Zug: NOCH nicht als Fehler zählen — der User kann „Mausrutscher" sagen.
      this.outcome = 'wrong'; this.pendingWrong = true;
      this.fen = this.startFen;
      this.lastMove = [ev.orig, ev.dest];
    }

    this.expectedDisplay = expectedMove.san;
    this.wrongRevealed = false;
    this.phase = 'FEEDBACK';

    if (this.outcome === 'wrong') {
      this.hooks.wrongMove(this.startFen, fenAfterPlayer, expectedMove.san, this.color);
      this.dests = destsAt(this.startFen);
    } else {
      this.scheduleAdvance(ADVANCE_MS[this.outcome]);
    }
    this.hooks.changed();
  }

  /** Ein an dieser Stellung offener Fehler, den der User NICHT als Mausrutscher deklariert hat, zählt jetzt;
   *  die Serie bricht — der laufende Zug startet eine neue. */
  private countPendingWrong(): void {
    if (!this.pendingWrong) return;
    this.lineHadWrong = true; this.wrong++; this.pendingWrong = false;
    this.currentStreak = 0;
  }

  /** „Lösung zeigen": zählt als falsch, spielt den erwarteten Zug (über die Felder aufgelöst) aufs Brett. */
  showSolution(): void {
    if (this.wrongRevealed) return;
    this.wrongRevealed = true;
    this.wrong++;
    this.currentStreak = 0;   // „Lösung zeigen" bricht die Serie (Mausrutscher tut das NICHT).
    this.lineHadWrong = true;
    this.pendingWrong = false;
    const expected = this.queue[this.qIndex]?.moves[this.currentPly];
    const board = expected ? tryLoadFen(this.startFen) : null;
    // `null` = in dieser Stellung nicht auflösbar (mehrdeutige SAN, kaputte Linie) → nur Text-Reveal.
    const uci = board && expected ? resolveExpectedUci(board, { san: expected.san }) : null;
    if (uci) {
      try {
        this.chess.load(this.startFen);
        const mv = applyUci(this.chess, uci);
        this.fen = this.chess.fen();
        this.lastMove = [mv.from as Key, mv.to as Key];
        this.dests = new Map();
      } catch { /* Stellung nicht ladbar → nur Text-Reveal */ }
    }
    this.hooks.changed();
  }

  /** „Mausrutscher": den offenen Fehler NICHT zählen und dieselbe Stellung wieder spielbar machen. */
  mouseslip(): void {
    if (this.phase !== 'FEEDBACK' || this.outcome !== 'wrong' || this.wrongRevealed) return;
    this.pendingWrong = false;
    this.outcome = 'correct';   // Feedback-Kasten schließen
    this.wrongRevealed = false;
    this.fen = this.startFen;
    this.lastMove = undefined;
    this.dests = destsAt(this.startFen);
    this.phase = 'PLAYING';
    this.hooks.changed();
  }

  /** Nach „Lösung zeigen": der erwartete Zug ist gespielt → mit der Linie fortfahren. */
  continueAfterWrong(): void {
    this.currentPly++;
    this.wrongRevealed = false;
    this.advanceToUserMove();
  }

  scheduleAdvance(ms: number): void {
    this.clearAdvance();
    this.advanceTimer = this.clock.setTimeout(() => { this.advanceTimer = null; this.runAdvance(); }, ms);
  }

  /** Nach richtigem/geduldetem Zug → im PGN weiterrücken und Gegnerzug spielen. Den Timer zuerst wegräumen, sonst
   *  rückt er nach einem manuellen „Weiter" ein zweites Mal vor (currentPly doppelt). */
  runAdvance(): void {
    this.clearAdvance();
    if (this.outcome === 'tolerated') {
      // Geduldeten Zug NICHT zu Ende spielen: zurücknehmen, der User zieht den Hauptzug SELBST.
      this.retryCurrentPly();
      return;
    }
    this.currentPly++;
    this.advanceToUserMove();
  }

  /** Geduldeten Zug zurücknehmen und die aktuelle Stellung erneut spielbar machen (`chess` steht noch auf startFen). */
  private retryCurrentPly(): void {
    this.fen = this.startFen;
    this.lastMove = undefined;
    this.dests = destsAt(this.startFen);
    this.phase = 'PLAYING';
    this.hooks.changed();
  }

  // ===== Lern-Modus =====

  /** Kommentar eines gerade gespielten Gegnerzugs dauerhaft anzeigen (COMMENT), bis der User „Weiter" bestätigt. */
  private enterCommentHold(comment: string): void {
    this.clearLearn();
    this.clearOppTimer();
    this.holdComment = comment;
    this.dests = new Map();
    this.phase = 'COMMENT';
    this.hooks.changed();
  }

  /** „Weiter" aus der COMMENT-Phase. */
  continueFromComment(): void {
    if (this.phase !== 'COMMENT') return;
    this.holdComment = '';
    this.advanceToUserMove();
  }

  /** Wiederholungs-Durchläufe: den erwarteten Zug NICHT vorzeigen, das Brett ist sofort spielbar. */
  private enterLearnPlay(): void {
    this.clearLearn();
    this.fen = this.startFen;
    this.lastMove = undefined;
    this.dests = destsAt(this.startFen);
    this.learnComment = '';
    this.phase = 'PLAYING';
    this.hooks.changed();
  }

  /** Zeigt den erwarteten Zug (view-only). Ohne Kommentar nach {@link LEARN_SHOW_MS} zurücknehmen; MIT Kommentar
   *  stehen lassen, bis der User weitertippt. */
  private enterLearnShow(): void {
    this.clearLearn();
    const line = this.queue[this.qIndex];
    const expected = line?.moves[this.currentPly];
    if (!expected) { this.finishLine(); return; }
    let fenAfter = this.startFen;
    try {
      const c = new Chess(this.startFen);
      const mv = c.move(expected.san);
      if (mv) { fenAfter = c.fen(); this.lastMove = [mv.from as Key, mv.to as Key]; }
    } catch { /* nicht spielbar → nur Text */ }
    this.fen = fenAfter;
    this.dests = new Map();
    this.expectedDisplay = expected.san;
    // Kommentar nur beim ERSTEN Durchlauf zeigen; die Wiederholungen laufen ohne Kommentar-Halt durch.
    this.learnComment = this.learnPass === 0 ? (line.comments?.[this.currentPly] || '').trim() : '';
    this.phase = 'LEARN_SHOW';
    this.hooks.changed();
    if (!this.learnComment) {
      this.learnTimer = this.clock.setTimeout(() => { this.learnTimer = null; this.learnRetract(); }, LEARN_SHOW_MS);
    }
  }

  /** Gezeigten Zug zurücknehmen → der User spielt ihn selbst. */
  private learnRetract(): void {
    this.clearLearn();
    this.fen = this.startFen;
    this.lastMove = undefined;
    this.dests = destsAt(this.startFen);
    this.learnComment = '';
    this.phase = 'PLAYING';
    this.hooks.changed();
  }

  /** Learn-Zug: nur der gezeigte (erwartete) Zug führt weiter; falsch → Zug erneut zeigen. Keine Alternativen. */
  private onLearnMove(ev: BoardMove): void {
    const line = this.queue[this.qIndex];
    if (!line || this.phase !== 'PLAYING' || this.currentPly >= line.moves.length) return;
    const board = tryLoadFen(this.startFen);
    if (!board) return;
    const expected: ExpectedMove = { san: line.moves[this.currentPly].san };
    const verdict = judgeMove(board, expected, [], ev.orig, ev.dest, ev.promotion);
    if (verdict === 'illegal' || verdict === 'not-your-turn') return;
    if (verdict !== 'correct') { this.enterLearnShow(); return; }   // nicht der Zug → nochmal vormachen
    const uci = resolveExpectedUci(board, expected);
    if (uci) { try { applyUci(this.chess, uci); } catch { /* Brett steht woanders */ } }
    this.fen = this.chess.fen();
    this.lastMove = [ev.orig, ev.dest];
    this.currentPly++;
    this.advanceToUserMove();
  }

  // ===== Timer =====

  /** Alle Timer der laufenden Linie wegräumen (Weiter, Gegnerzug, Lern-Anzeige). */
  clearTimers(): void {
    this.clearAdvance(); this.clearOppTimer(); this.clearLearn();
  }

  private clearAdvance(): void {
    if (this.advanceTimer !== null) { this.clock.clearTimeout(this.advanceTimer); this.advanceTimer = null; }
  }

  private clearOppTimer(): void {
    if (this.oppTimer !== null) { this.clock.clearTimeout(this.oppTimer); this.oppTimer = null; }
  }

  private clearLearn(): void {
    if (this.learnTimer !== null) { this.clock.clearTimeout(this.learnTimer); this.learnTimer = null; }
  }
}
