import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { Key } from 'chessground/types';
import { KidsPuzzleComponent, REPLY_DELAY_MS, SETUP_DELAY_MS, WRONG_HOLD_MS } from './kids-puzzle.component';
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
    // Die Eule sagt gleich, wer dran ist — ein eigener „Pass auf"-Satz war zu kurz zum Lesen.
    const bubble = () => (f.nativeElement as HTMLElement).querySelector('.bubble p')!.textContent!.trim();
    expect(bubble()).toBe('kids.feedback.yourTurnWhite');

    tick(SETUP_DELAY_MS);
    f.detectChanges();
    expect(c.status()).toBe('yourTurn');
    expect(bubble()).toBe('kids.feedback.yourTurnWhite');
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
    tick(WRONG_HOLD_MS);
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

  it('ein falscher Zug bleibt zwei Sekunden stehen, dann springt das Brett zurueck', fakeAsync(() => {
    const f = create(mate1);
    const c = f.componentInstance;
    tick(SETUP_DELAY_MS);
    const before = c.fen();

    c.onMove(move('b8', 'b1'));
    expect(c.status()).toBe('wrong');
    expect(c.fen().split(' ')[0]).toBe('8/8/8/8/8/6p1/r6k/1R3K2');   // der Zug steht noch da
    expect(c.lastMove()).toEqual(['b8' as Key, 'b1' as Key]);
    expect(c.shapes()).toEqual([{ orig: 'b1' as Key, brush: 'red' }]);
    expect(c.interactive()).toBeFalse();                             // so lange gesperrt

    tick(WRONG_HOLD_MS - 1);
    expect(c.fen()).not.toBe(before);
    tick(1);
    expect(c.fen()).toBe(before);
    expect(c.lastMove()).toEqual(['g4' as Key, 'g3' as Key]);        // wieder der Stellungszug
    expect(c.shapes()).toEqual([]);
    expect(c.interactive()).toBeTrue();
    expect(c.status()).toBe('wrong');                                // die Eule sagt es weiter
  }));

  it('eine neue Aufgabe waehrend des Stehenbleibens bricht es ab', fakeAsync(() => {
    const f = create(mate1);
    const c = f.componentInstance;
    tick(SETUP_DELAY_MS);
    c.onMove(move('b8', 'b1'));
    f.componentRef.setInput('task', fork);
    f.detectChanges();
    expect(c.holding()).toBeFalse();
    tick(WRONG_HOLD_MS + SETUP_DELAY_MS);
    expect(c.status()).toBe('yourTurn');
  }));

  function key(k: string, target: EventTarget = document.body, init: KeyboardEventInit = {}): KeyboardEvent {
    const e = new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(e);
    return e;
  }

  it('Leertaste = Weiter, aber erst wenn geloest', fakeAsync(() => {
    const f = create(mate1);
    const c = f.componentInstance;
    let next = 0;
    c.next.subscribe(() => next++);
    tick(SETUP_DELAY_MS);

    key(' ');
    expect(next).toBe(0);

    c.onMove(move('b8', 'h8'));
    const e = key(' ');
    expect(next).toBe(1);
    expect(e.defaultPrevented).toBeTrue();          // die Seite scrollt nicht
    key(' ', document.body, { repeat: true });
    expect(next).toBe(1);                           // gehaltene Taste zaehlt nicht
    key('Enter');
    expect(next).toBe(2);
  }));

  it('Leertaste in einem Feld oder auf einem Knopf loest kein zweites Weiter aus', fakeAsync(() => {
    const f = create(mate1);
    const c = f.componentInstance;
    let next = 0;
    c.next.subscribe(() => next++);
    tick(SETUP_DELAY_MS);
    c.onMove(move('b8', 'h8'));

    for (const tag of ['select', 'input', 'button', 'a']) {
      const el = document.createElement(tag);
      document.body.appendChild(el);
      key(' ', el);
      el.remove();
    }
    expect(next).toBe(0);
  }));

  it('am Weiter-Knopf steht die Taste dabei', fakeAsync(() => {
    const f = create(mate1);
    tick(SETUP_DELAY_MS);
    f.componentInstance.onMove(move('b8', 'h8'));
    f.detectChanges();
    expect(f.nativeElement.querySelector('button.next kbd.key')).not.toBeNull();
  }));
});
