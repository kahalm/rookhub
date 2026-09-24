import { computed, signal } from '@angular/core';
import { UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { fenAfterUci } from '../../shared/pgn-viewer/board-moves.util';
import { sameMove } from '../../shared/chess/line-solver';
import { Mistake, MistakesBySide } from './mistakes.util';

/** ask = du bist dran · wrong = daneben · right = selbst gefunden · shown = Lösung gezeigt · done = durch. */
export type MistakesPhase = 'ask' | 'right' | 'wrong' | 'shown' | 'done';

/**
 * Der Zustand von „Eigene Fehler nachspielen" — getrennt von jeder Anzeige, damit das BRETT der
 * Partie-Seite ihn spielen kann (seit 0.518.1; vorher ein Dialog mit eigenem, kleinerem Brett, auf Wunsch
 * des Nutzers abgelöst). Die Seite bindet `boardFen`/`lastMove`/`flipped` an ihr Brett und reicht dessen
 * Züge an {@link onMove}; das Panel unter dem Brett (`MistakesTrainerComponent`) zeigt Aufgabe und Knöpfe.
 *
 * Geurteilt wird mit `sameMove` aus dem gemeinsamen Löser-Kern — das Brett wandelt ohne Rückfrage in
 * eine Dame um, und die Regel dort lässt eine fehlende Umwandlungsfigur gelten (eine Unterverwandlung
 * als Lösung ließe sich auf diesem Brett gar nicht eingeben). Als richtig zählt der Bestzug der Engine
 * UND jeder gleichwertige Kandidat derselben Suche (`Mistake.acceptUci`, siehe `EQUIVALENT_LIMIT`);
 * ein Zug ausserhalb der Kandidaten lässt sich nicht beurteilen und gilt als daneben.
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
  readonly solved = signal(0);

  readonly list = computed<Mistake[]>(() => this.side() === 'white' ? this.bySide.white : this.bySide.black);
  readonly current = computed<Mistake | null>(() => this.list()[this.index()] ?? null);
  readonly flipped = computed(() => this.side() === 'black');
  readonly last = computed(() => this.index() >= this.list().length - 1);
  /** Nimmt das Brett gerade einen Zug an? */
  readonly playable = computed(() => this.phase() === 'ask' && this.current() !== null);
  readonly bothSides: boolean;

  /** Auf dieser Aufgabe schon danebengegriffen? Dann zählt sie nicht als selbst gefunden. */
  private missedHere = false;

  constructor(readonly bySide: MistakesBySide, side: 'white' | 'black') {
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
      if (!this.missedHere) this.solved.update(n => n + 1);
      this.foundSan.set(e.san);
      this.foundBest.set(sameMove(e.from + e.to, m.bestUci));
      this.phase.set('right');
    } else {
      this.missedHere = true;
      this.tried.set(e.san);
      this.phase.set('wrong');
    }
  }

  /** Zurück auf die Ausgangsstellung — das Brett übernimmt den Nutzerzug selbst, es MUSS neu gebunden werden. */
  retry(): void {
    const m = this.current();
    if (!m) return;
    this.boardFen.set(m.fenBefore);
    this.lastMove.set(undefined);
    this.phase.set('ask');
  }

  showSolution(): void {
    const m = this.current();
    if (!m) return;
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
    this.missedHere = false;
    this.tried.set('');
    this.foundSan.set('');
    this.foundBest.set(false);
    this.lastMove.set(undefined);
    this.boardFen.set(m?.fenBefore ?? '');
    this.phase.set(m ? 'ask' : 'done');
  }
}
