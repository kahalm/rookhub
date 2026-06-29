import { fakeAsync, tick } from '@angular/core/testing';
import { of } from 'rxjs';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';
import { RepCard } from './repertoire-tree.util';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -';

function card(expected: string, accepted: string[] = []): RepCard {
  return { cardKey: START, fenBefore: START, expected, accepted };
}

function make(): RepertoireTrainerComponent {
  const route: any = { snapshot: { paramMap: { get: () => '1' } } };
  const training: any = { review: () => of({ cardKey: START }) };
  const prefs: any = {};
  const translate: any = {};
  const cdr: any = { markForCheck: () => {} };
  const stockfish: any = { init: () => Promise.resolve(), getEval: () => Promise.resolve('+0.0') };
  const c = new RepertoireTrainerComponent(route, training, prefs, translate, cdr, stockfish);
  c.fen = START + ' 0 1';
  c.queue = [card('e4', ['d4']), card('Nf3')];
  c.index = 0;
  c.phase = 'PLAYING';
  return c;
}

describe('RepertoireTrainerComponent auto-advance', () => {
  it('correct move auto-advances without a button', fakeAsync(() => {
    const c = make();
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect(c.outcome).toBe('correct');
    expect(c.phase).toBe('FEEDBACK');
    expect(c.index).toBe(0);   // noch nicht weiter
    tick(700);
    expect(c.index).toBe(1);   // automatisch weiter, kein Klick nötig
    expect(c.phase).toBe('PLAYING');
  }));

  it('tolerated move auto-advances after the longer delay', fakeAsync(() => {
    const c = make();
    c.onMove({ orig: 'd2' as any, dest: 'd4' as any });
    expect(c.outcome).toBe('tolerated');
    expect(c.phase).toBe('FEEDBACK');
    tick(700);
    expect(c.index).toBe(0);   // bei geduldet länger sichtbar
    tick(1100);
    expect(c.index).toBe(1);
  }));

  it('tapping skips the wait and continues immediately', fakeAsync(() => {
    const c = make();
    c.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    c.onPlayClick();
    expect(c.index).toBe(1);   // sofort weiter
    expect(c.phase).toBe('PLAYING');
    tick(2000);                // kein doppeltes Weiterschalten
    expect(c.index).toBe(1);
  }));

  it('wrong move keeps manual continue (no auto-advance)', fakeAsync(() => {
    const c = make();
    c.onMove({ orig: 'g1' as any, dest: 'f3' as any });
    expect(c.outcome).toBe('wrong');
    expect(c.phase).toBe('FEEDBACK');
    expect(c.wrongRevealed).toBeFalse();    // erst nach „Lösung zeigen" enthüllt
    expect(c.wrong).toBe(0);                 // wrong-Zähler erst beim Show
    tick(3000);
    expect(c.index).toBe(0);   // bleibt stehen bis „Weiter"
    expect(c.phase).toBe('FEEDBACK');
    c.onPlayClick();           // Klick darf bei „falsch" NICHT überspringen
    expect(c.index).toBe(0);
    c.next();
    expect(c.index).toBe(1);
  }));

  it('mouseslip after wrong move: no penalty, return to PLAYING', () => {
    const c = make();
    c.onMove({ orig: 'g1' as any, dest: 'f3' as any });
    expect(c.outcome).toBe('wrong');
    const queueLenBefore = c.queue.length;
    c.mouseslip();
    expect(c.phase).toBe('PLAYING');
    expect(c.wrong).toBe(0);
    expect(c.queue.length).toBe(queueLenBefore);   // KEIN Re-Queue der Karte
  });

  it('showSolution after wrong move: counts as wrong + reveal + re-queue', () => {
    const c = make();
    c.onMove({ orig: 'g1' as any, dest: 'f3' as any });
    const queueLenBefore = c.queue.length;
    c.showSolution();
    expect(c.wrongRevealed).toBeTrue();
    expect(c.wrong).toBe(1);
    expect(c.queue.length).toBe(queueLenBefore + 1);
  });
});
