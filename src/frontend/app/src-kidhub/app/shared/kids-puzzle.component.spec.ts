import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { Key } from 'chessground/types';
import { KidsPuzzleComponent, REPLY_DELAY_MS, SETUP_DELAY_MS } from './kids-puzzle.component';
import { KidsTask } from '../core/kids-solver';

describe('KidsPuzzleComponent', () => {
  const mate1: KidsTask = { fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: ['g4g3', 'b8h8'], startPly: 0 };
  const fork: KidsTask = { fen: '8/8/2k5/8/8/r2NK1P1/5P2/8 b - - 1 63', moves: ['a3a2', 'd3b4', 'c6d6', 'b4a2'], startPly: 0 };

  function create(task: KidsTask) {
    TestBed.configureTestingModule({
      imports: [KidsPuzzleComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(KidsPuzzleComponent);
    fixture.componentRef.setInput('task', task);
    fixture.detectChanges();
    return fixture;
  }

  const move = (from: string, to: string) => ({ orig: from as Key, dest: to as Key });

  it('zeigt erst den Stellungszug, dann ist das Kind dran', fakeAsync(() => {
    const f = create(mate1);
    const c = f.componentInstance;
    expect(c.status()).toBe('watch');
    expect(c.interactive()).toBeFalse();
    expect(c.orientation()).toBe('white');

    tick(SETUP_DELAY_MS);
    expect(c.status()).toBe('yourTurn');
    expect(c.lastMove()).toEqual(['g4' as Key, 'g3' as Key]);
    expect(c.dests().get('b8' as Key)).toContain('h8' as Key);
  }));

  it('Fehler und Tipps zaehlen, geloest meldet sie', fakeAsync(() => {
    const f = create(mate1);
    const c = f.componentInstance;
    const solved: number[] = [];
    c.solved.subscribe(e => solved.push(e.mistakes));
    tick(SETUP_DELAY_MS);

    const destsBefore = c.dests();
    c.onMove(move('b8', 'b1'));
    expect(c.status()).toBe('wrong');
    // Neues Objekt, damit das Brett den falschen Zug optisch zuruecknimmt.
    expect(c.dests()).not.toBe(destsBefore);

    c.showHint();
    expect(c.shapes()).toEqual([{ orig: 'b8' as Key, brush: 'yellow' }]);
    c.showHint();
    expect(c.shapes()).toEqual([{ orig: 'b8' as Key, dest: 'h8' as Key, brush: 'green' }]);

    c.onMove(move('b8', 'h8'));
    expect(c.status()).toBe('solved');
    expect(solved).toEqual([3]);
    expect(c.interactive()).toBeFalse();
  }));

  it('zwei Zuege: nach dem richtigen ersten antwortet der Gegner', fakeAsync(() => {
    const f = create(fork);
    const c = f.componentInstance;
    tick(SETUP_DELAY_MS);

    c.onMove(move('d3', 'b4'));
    expect(c.status()).toBe('good');
    tick(REPLY_DELAY_MS);
    expect(c.lastMove()).toEqual(['c6' as Key, 'd6' as Key]);
    expect(c.interactive()).toBeTrue();

    c.onMove(move('b4', 'a2'));
    expect(c.status()).toBe('solved');
  }));

  it('zeigt nach dem Loesen den Kurs-Kommentar zum letzten Zug', fakeAsync(() => {
    const f = create(mate1);
    f.componentRef.setInput('moveComments', { 1: 'Das Grundreihenmatt!' });
    f.detectChanges();
    tick(SETUP_DELAY_MS);
    f.componentInstance.onMove(move('b8', 'h8'));
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Das Grundreihenmatt!');
  }));

  it('eine neue Aufgabe setzt alles zurueck', fakeAsync(() => {
    const f = create(mate1);
    const c = f.componentInstance;
    tick(SETUP_DELAY_MS);
    c.onMove(move('b8', 'h8'));
    expect(c.status()).toBe('solved');

    f.componentRef.setInput('task', fork);
    f.detectChanges();
    expect(c.status()).toBe('watch');
    expect(c.orientation()).toBe('white');
    tick(SETUP_DELAY_MS);
    expect(c.status()).toBe('yourTurn');
  }));
});
