import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { applyUci, tryFreeMove, tryLoadFen } from '../../shared/chess/puzzle-move.util';
import { ExpectedMove, judgeMove, resolveExpectedUci } from '../../shared/chess/line-solver';
import type { ParsedGame } from '../../shared/pgn-viewer/pgn-parser';
import { normFen } from './repertoire-tree.util';
import { destsAt, userMoveCount } from './repertoire-trainer.util';
import { lineKeyOf } from './repertoire-trainer-queue.util';
import {
  ADVANCE_MS, BoardMove, LEARN_GAP_MS, LEARN_REPEATS, OPP_MOVE_DELAY_MS,
} from './repertoire-trainer-state';
import { TrainerLearnMode } from './repertoire-trainer-learn';

// Typen, Zeiten und Uhr stehen seit W7a in repertoire-trainer-state.ts — hier weiter erreichbar wie bisher.
export {
  ADVANCE_MS, LEARN_GAP_MS, LEARN_REPEATS, LEARN_SHOW_MS, OPP_MOVE_DELAY_MS, browserClock,
} from './repertoire-trainer-state';
export type {
  BoardMove, TrainerClock, TrainerMode, TrainerOutcome, TrainerPhase, TrainerSessionHooks,
} from './repertoire-trainer-state';

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
 *
 * <p>Zustand, Anzeige-Werte und Timer erbt sie von {@link TrainerSessionState}, den Lern-Modus von
 * {@link TrainerLearnMode}.</p>
 */
export class TrainerSession extends TrainerLearnMode {
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
  protected override advanceToUserMove(): void {
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

  protected override finishLine(): void {
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
}
