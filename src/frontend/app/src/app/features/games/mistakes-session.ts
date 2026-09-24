import { computed, signal } from '@angular/core';
import { UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { fenAfterUci } from '../../shared/pgn-viewer/board-moves.util';
import { sameMove } from '../../shared/chess/line-solver';
import { Mistake, MistakesBySide } from './mistakes.util';

/** ask = du bist dran · checking = die Browser-Engine rechnet nach · wrong = daneben · right = selbst gefunden ·
 *  shown = Lösung gezeigt · done = durch. */
export type MistakesPhase = 'ask' | 'checking' | 'right' | 'wrong' | 'shown' | 'done';

/** Urteil über einen nicht gelisteten Zug: `true` gleichwertig, `false` schlechter, `null` nicht prüfbar. */
export type UnlistedMoveJudge = (m: Mistake, fenAfter: string) => Promise<boolean | null>;

/**
 * Der Zustand von „Eigene Fehler nachspielen" — getrennt von jeder Anzeige, damit das BRETT der
 * Partie-Seite ihn spielen kann (seit 0.518.1; vorher ein Dialog mit eigenem, kleinerem Brett, auf Wunsch
 * des Nutzers abgelöst). Die Seite bindet `boardFen`/`lastMove`/`flipped` an ihr Brett und reicht dessen
 * Züge an {@link onMove}; das Panel unter dem Brett (`MistakesTrainerComponent`) zeigt Aufgabe und Knöpfe.
 *
 * Geurteilt wird mit `sameMove` aus dem gemeinsamen Löser-Kern — das Brett wandelt ohne Rückfrage in
 * eine Dame um, und die Regel dort lässt eine fehlende Umwandlungsfigur gelten (eine Unterverwandlung
 * als Lösung ließe sich auf diesem Brett gar nicht eingeben). Als richtig zählt der Bestzug der Engine
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
  readonly solved = signal(0);
  /**
   * Die Halbzüge der selbst gefundenen Aufgaben — sie gehen an den Server (`POST /api/games/{id}/mistakes`),
   * damit die Übersicht „4 von 7 · 3 offen" zeigen kann. Gezählt wird dieselbe Regel wie bei `solved`:
   * wer erst danebengreift oder die Lösung zeigen lässt, bekommt die Aufgabe nicht gutgeschrieben.
   */
  readonly solvedPlies = signal<number[]>([]);

  readonly list = computed<Mistake[]>(() => this.side() === 'white' ? this.bySide.white : this.bySide.black);
  readonly current = computed<Mistake | null>(() => this.list()[this.index()] ?? null);
  readonly flipped = computed(() => this.side() === 'black');
  readonly last = computed(() => this.index() >= this.list().length - 1);
  /** Nimmt das Brett gerade einen Zug an? */
  readonly playable = computed(() => this.phase() === 'ask' && this.current() !== null);
  readonly bothSides: boolean;

  /** Auf dieser Aufgabe schon danebengegriffen? Dann zählt sie nicht als selbst gefunden. */
  private missedHere = false;
  /** Zählt jeden Wechsel der Aufgabe/Stellung hoch — ein Engine-Urteil, das danach eintrifft, gehört zu
   *  einem Zug, der nicht mehr auf dem Brett steht, und wird verworfen. */
  private epoch = 0;

  constructor(readonly bySide: MistakesBySide, side: 'white' | 'black', private readonly judge?: UnlistedMoveJudge) {
    this.bothSides = bySide.white.length > 0 && bySide.black.length > 0;
    this.side.set(side);
    this.start(0);
  }

  onMove(e: UserBoardMove): void {
    const m = this.current();
    if (!m || this.phase() !== 'ask') return;
    this.boardFen.set(e.fen);
    this.lastMove.set([e.from, e.to]);
    const accepted = m.acceptUci?.length ? m.acceptUci : [m.bestUci];
    if (accepted.some(uci => sameMove(e.from + e.to, uci))) {
      this.found(e.san, sameMove(e.from + e.to, m.bestUci), false);
    } else if (m.checkUnlisted && this.judge) {
      this.tried.set(e.san);
      this.phase.set('checking');
      const token = ++this.epoch;
      this.judge(m, e.fen).then(ok => {
        if (token !== this.epoch) return;
        if (ok) { this.found(e.san, false, true); return; }
        this.checkFailed.set(ok === null);
        this.missed(e.san);
      }, () => {
        if (token !== this.epoch) return;
        this.checkFailed.set(true);
        this.missed(e.san);
      });
    } else {
      this.missed(e.san);
    }
  }

  private found(san: string, best: boolean, byEngine: boolean): void {
    if (!this.missedHere) {
      this.solved.update(n => n + 1);
      const ply = this.current()?.ply;
      if (ply != null) this.solvedPlies.update(p => p.includes(ply) ? p : [...p, ply]);
    }
    this.foundSan.set(san);
    this.foundBest.set(best);
    this.foundByEngine.set(byEngine);
    this.phase.set('right');
  }

  private missed(san: string): void {
    this.missedHere = true;
    this.tried.set(san);
    this.phase.set('wrong');
  }

  /** Zurück auf die Ausgangsstellung — das Brett übernimmt den Nutzerzug selbst, es MUSS neu gebunden werden. */
  retry(): void {
    const m = this.current();
    if (!m) return;
    this.epoch++;
    this.checkFailed.set(false);
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

  next(): void { this.start(this.index() + 1); }

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
    this.tried.set('');
    this.foundSan.set('');
    this.foundBest.set(false);
    this.lastMove.set(undefined);
    this.boardFen.set(m?.fenBefore ?? '');
    this.phase.set(m ? 'ask' : 'done');
  }
}
