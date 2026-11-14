import { computed, signal } from '@angular/core';
import { UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { fenAfterUci } from '../../shared/pgn-viewer/board-moves.util';
import { sameMove } from '../../shared/chess/line-solver';
import { Mistake, MistakesBySide, analysisBest, unlistedBound } from './mistakes.util';
import { EvalScore } from './game-review.util';
import { UnlistedVerdict } from './mistake-judge.service';

/** ask = du bist dran · checking = die Browser-Engine rechnet nach · wrong = daneben · right = selbst gefunden ·
 *  shown = Lösung gezeigt · done = durch. */
export type MistakesPhase = 'ask' | 'checking' | 'right' | 'wrong' | 'shown' | 'done';

/** Urteil über einen nicht gelisteten Zug (Browser-Engine), samt den beiden Bewertungen dieser Prüfung. */
export type UnlistedMoveJudge = (m: Mistake, fenAfter: string) => Promise<UnlistedVerdict>;

/**
 * Was die Zeile unter „… is not it" über den Fehlversuch sagt (alle Bewertungen Weiß-Sicht). Je Fall stammen ALLE
 * Zahlen aus EINER Rechnung — nie eine flache Browser-Zahl neben einer der tiefen Server-Analyse (0.972.1; vorher
 * las sich „After a5: +0.72 (best move: +0.77)" bei Schwarz am Zug, als wäre der abgelehnte Zug der bessere):
 * - `listed`: der Zug steht unter den Kandidaten der Analyse — sein Wert und der des Bestzugs aus derselben Suche.
 * - `bound`: nicht gelistet und sicher schlechter — belegt ist nur die Schranke: höchstens so gut wie der schwächste
 *   der `count` Kandidaten.
 * - `browser`: nicht gelistet, von der Browser-Engine geprüft und verworfen — die zwei Werte dieser Prüfung.
 */
export type TriedRating =
  | { kind: 'listed'; tried: EvalScore; best: EvalScore }
  | { kind: 'bound'; count: number; worst: EvalScore; best: EvalScore }
  | { kind: 'browser'; tried: EvalScore; best: EvalScore };

/** „Diesen Fehler nicht mehr zeigen" (0.748.0): die Seite merkt es sich auf dem Server. */
export type MistakeDismisser = (m: Mistake) => void;

/**
 * Der Zustand von „Eigene Fehler nachspielen" — getrennt von jeder Anzeige, damit das BRETT der
 * Partie-Seite ihn spielen kann (seit 0.518.1; vorher ein Dialog mit eigenem, kleinerem Brett, auf Wunsch
 * des Nutzers abgelöst). Die Seite bindet `boardFen`/`lastMove`/`flipped` an ihr Brett und reicht dessen
 * Züge an {@link onMove}; das Panel unter dem Brett (`MistakesTrainerComponent`) zeigt Aufgabe und Knöpfe.
 *
 * Geurteilt wird mit `sameMove` aus dem gemeinsamen Löser-Kern — mit der am Brett gewählten
 * Umwandlungsfigur (`UserBoardMove.promotion`, seit dem Umwandlungs-Wähler des geteilten Bretts): eine
 * andere Figur als die erwartete ist ein anderer Zug; fehlt sie, gilt jede. Als richtig zählt der Bestzug der Engine
 * UND jeder gleichwertige Kandidat derselben Suche (`Mistake.acceptUci`, siehe `EQUIVALENT_LIMIT`). Ein Zug
 * ausserhalb der Kandidaten geht an den {@link UnlistedMoveJudge} (Browser-Engine) — aber nur, wenn schon der
 * schwächste Kandidat gleichwertig war (`Mistake.checkUnlisted`); sonst ist er sicher daneben.
 */
export class MistakesSession {
  readonly side = signal<'white' | 'black'>('white');
  readonly index = signal(0);
  readonly phase = signal<MistakesPhase>('ask');
  readonly boardFen = signal('');
  readonly lastMove = signal<[string, string] | undefined>(undefined);
  /** SAN des zuletzt danebengegangenen Zuges — steht in der Rückmeldung. */
  readonly tried = signal('');
  /** SAN des gefundenen Zuges — kann ein gleichwertiger sein, nicht nur der Bestzug. */
  readonly foundSan = signal('');
  /** War der gefundene Zug der Bestzug selbst? Sonst nennt die Rückmeldung ihn zusätzlich. */
  readonly foundBest = signal(false);
  /** Hat die Browser-Engine den gefundenen Zug bestätigt (statt der Kandidatenliste der Analyse)? */
  readonly foundByEngine = signal(false);
  /** Die Browser-Engine konnte einen nicht gelisteten Zug nicht prüfen (Fehler, Zeitlimit). */
  readonly checkFailed = signal(false);
  /**
   * Einordnung des Fehlversuchs (seit 0.526.2 — gewünscht: „Bxf6 is not it" soll sagen, was der Zug kostet);
   * `null` = nichts Belegtes zu sagen. Siehe {@link TriedRating}.
   */
  readonly triedRating = signal<TriedRating | null>(null);
  readonly solved = signal(0);
  /** Aufgedeckte Tipp-Stufen der aktuellen Aufgabe (0–3, wie beim Puzzle: Zugart → Figur → Zug; seit 0.526.2). */
  readonly hintLevel = signal(0);
  /**
   * Die Halbzüge der selbst gefundenen Aufgaben — sie gehen an den Server (`POST /api/games/{id}/mistakes`),
   * damit die Übersicht „4 von 7 · 3 offen" zeigen kann. Gezählt wird dieselbe Regel wie bei `solved`:
   * wer erst danebengreift oder die Lösung zeigen lässt, bekommt die Aufgabe nicht gutgeschrieben.
   */
  readonly solvedPlies = signal<number[]>([]);
  /** Die selbst gefundenen Aufgaben selbst — die Sammlung über mehrere Partien meldet sie je Partie (`Mistake.gameId`). */
  readonly solvedMistakes = signal<Mistake[]>([]);
  /** In dieser Sitzung ausgeblendet („nicht mehr zeigen") — fallen aus der Liste, auch bei „Von vorn". */
  private readonly hidden = signal<ReadonlySet<Mistake>>(new Set());
  readonly dismissedCount = computed(() => this.hidden().size);

  readonly list = computed<Mistake[]>(() => {
    const all = this.side() === 'white' ? this.bySide.white : this.bySide.black;
    const hidden = this.hidden();
    return hidden.size ? all.filter(m => !hidden.has(m)) : all;
  });
  readonly current = computed<Mistake | null>(() => this.list()[this.index()] ?? null);
  /** Brett aus Sicht des Ziehenden der Aufgabe — in der Sammlung über mehrere Partien wechselt das je Partie. */
  readonly flipped = computed(() => {
    const m = this.current();
    return m ? !m.white : this.side() === 'black';
  });
  readonly last = computed(() => this.index() >= this.list().length - 1);
  /** Nimmt das Brett gerade einen Zug an? */
  readonly playable = computed(() => this.phase() === 'ask' && this.current() !== null);
  readonly bothSides: boolean;
  /** Gibt es „nicht mehr zeigen"? Nur mit einem {@link MistakeDismisser} (eigene Partie). */
  readonly canDismiss: boolean;

  /** Auf dieser Aufgabe schon danebengegriffen? Dann zählt sie nicht als selbst gefunden. */
  private missedHere = false;
  /** Zählt jeden Wechsel der Aufgabe/Stellung hoch — ein Engine-Urteil, das danach eintrifft, gehört zu
   *  einem Zug, der nicht mehr auf dem Brett steht, und wird verworfen. */
  private epoch = 0;

  constructor(readonly bySide: MistakesBySide, side: 'white' | 'black', private readonly judge?: UnlistedMoveJudge,
              private readonly dismisser?: MistakeDismisser) {
    this.bothSides = bySide.white.length > 0 && bySide.black.length > 0;
    this.canDismiss = !!dismisser;
    this.side.set(side);
    this.start(0);
  }

  onMove(e: UserBoardMove): void {
    const m = this.current();
    if (!m || this.phase() !== 'ask') return;
    this.boardFen.set(e.fen);
    this.lastMove.set([e.from, e.to]);
    const accepted = m.acceptUci?.length ? m.acceptUci : [m.bestUci];
    const userUci = e.from + e.to + (e.promotion ?? '');
    if (accepted.some(uci => sameMove(userUci, uci))) {
      this.found(e.san, sameMove(userUci, m.bestUci), false);
    } else if (m.checkUnlisted && this.judge) {
      this.tried.set(e.san);
      this.phase.set('checking');
      const token = ++this.epoch;
      this.judge(m, e.fen).then(v => {
        if (token !== this.epoch) return;
        if (v.equivalent) { this.found(e.san, false, true); return; }
        this.checkFailed.set(v.equivalent === null);
        this.missed(e.san, v);
      }, () => {
        if (token !== this.epoch) return;
        this.checkFailed.set(true);
        this.missed(e.san, { equivalent: null });
      });
    } else {
      this.missed(e.san);
    }
  }

  private found(san: string, best: boolean, byEngine: boolean): void {
    if (!this.missedHere) {
      this.solved.update(n => n + 1);
      const cur = this.current();
      const ply = cur?.ply;
      if (ply != null) this.solvedPlies.update(p => p.includes(ply) ? p : [...p, ply]);
      if (cur) this.solvedMistakes.update(l => l.includes(cur) ? l : [...l, cur]);
    }
    this.foundSan.set(san);
    this.foundBest.set(best);
    this.foundByEngine.set(byEngine);
    this.phase.set('right');
  }

  /** `checked` = das Urteil der Browser-Engine, wenn sie gefragt wurde. */
  private missed(san: string, checked?: UnlistedVerdict): void {
    this.missedHere = true;
    this.tried.set(san);
    this.phase.set('wrong');
    this.triedRating.set(this.rateTried(checked));
  }

  /**
   * Einordnung des Fehlversuchs, je Fall aus EINER Rechnung: gelistet → Server-Werte; von der Browser-Engine geprüft →
   * deren zwei Werte (die Prüfung hat sie ohnehin gerechnet); sonst die Schranke aus der Analyse. Gerechnet wird hier
   * nichts — eine eigene flache Bewertung neben dem Server-Wert des Bestzugs war der Widerspruch von 0.526.2.
   */
  private rateTried(checked?: UnlistedVerdict): TriedRating | null {
    const m = this.current();
    const move = this.lastMove();
    if (!m || !move) return null;
    const listed = (m.candidates ?? []).find(c => sameMove(move[0] + move[1], c.uci));
    if (listed) return { kind: 'listed', tried: listed.score, best: analysisBest(m) };
    if (checked) {
      return checked.equivalent === false && checked.tried && checked.best
        ? { kind: 'browser', tried: checked.tried, best: checked.best } : null;
    }
    const bound = unlistedBound(m);
    return bound ? { kind: 'bound', ...bound } : null;
  }

  /** Zurück auf die Ausgangsstellung — das Brett übernimmt den Nutzerzug selbst, es MUSS neu gebunden werden. */
  retry(): void {
    const m = this.current();
    if (!m) return;
    this.epoch++;
    this.checkFailed.set(false);
    this.triedRating.set(null);
    this.boardFen.set(m.fenBefore);
    this.lastMove.set(undefined);
    this.phase.set('ask');
  }

  showSolution(): void {
    const m = this.current();
    if (!m) return;
    this.epoch++;
    this.missedHere = true;
    this.boardFen.set(fenAfterUci(m.fenBefore, m.bestUci) ?? m.fenBefore);
    this.lastMove.set([m.bestUci.slice(0, 2), m.bestUci.slice(2, 4)]);
    this.phase.set('shown');
  }

  /** Nächste Tipp-Stufe. Die dritte nennt den Zug — danach zählt die Aufgabe nicht mehr als selbst gefunden,
   *  wie bei „Lösung zeigen". */
  showHint(max = 3): void {
    if (this.hintLevel() >= max) return;
    this.hintLevel.update(h => h + 1);
    if (this.hintLevel() >= max) this.missedHere = true;
  }

  next(): void { this.start(this.index() + 1); }

  /** „Diesen Fehler nicht mehr zeigen": der Seite melden, aus der Liste nehmen — die nächste Aufgabe rückt nach. */
  dismiss(): void {
    const m = this.current();
    if (!m || !this.dismisser) return;
    this.dismisser(m);
    // Gerade selbst gefunden und dann ausgeblendet: zählt in der Zusammenfassung nicht mehr mit (sonst „3 von 2").
    if (this.phase() === 'right' && !this.missedHere) this.solved.update(n => Math.max(0, n - 1));
    this.hidden.update(h => new Set([...h, m]));
    this.start(this.index());
  }

  restart(): void {
    this.solved.set(0);
    this.start(0);
  }

  chooseSide(s: 'white' | 'black'): void {
    if (s === this.side()) return;
    this.side.set(s);
    this.solved.set(0);
    this.start(0);
  }

  private start(i: number): void {
    const m = this.list()[i] ?? null;
    this.index.set(i);
    this.epoch++;
    this.missedHere = false;
    this.foundByEngine.set(false);
    this.checkFailed.set(false);
    this.triedRating.set(null);
    this.hintLevel.set(0);
    this.tried.set('');
    this.foundSan.set('');
    this.foundBest.set(false);
    this.lastMove.set(undefined);
    this.boardFen.set(m?.fenBefore ?? '');
    this.phase.set(m ? 'ask' : 'done');
  }
}
