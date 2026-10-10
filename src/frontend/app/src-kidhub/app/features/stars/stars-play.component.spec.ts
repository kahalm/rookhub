import { TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { BehaviorSubject } from 'rxjs';
import { Key } from 'chessground/types';
import { StarsPlayComponent } from './stars-play.component';
import { KidsStarsStore } from '../../core/kids-stars.store';
import { STARS_PER_STAGE, StarPuzzle, squareIndex, squareName, starFen } from '../../core/kids-stars';
import { WRONG_HOLD_MS } from '../../shared/kids-puzzle.component';

describe('StarsPlayComponent', () => {
  const KEY = 'rh-kids-stars-v1';
  let params: BehaviorSubject<any>;

  beforeEach(() => {
    localStorage.removeItem(KEY);
    params = new BehaviorSubject(convertToParamMap({ stage: '1' }));
    TestBed.configureTestingModule({
      imports: [StarsPlayComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    });
  });
  afterEach(() => localStorage.removeItem(KEY));

  /** Turm a1, Sterne a3 und c3 — eindeutig a3, dann c3. */
  const fixed: StarPuzzle = { piece: 'R', start: 0, stars: [16, 18], solution: [16, 18], unique: true };
  const create = () => {
    const f = TestBed.createComponent(StarsPlayComponent);
    const c = f.componentInstance;
    c.puzzle.set(fixed);
    (c as any).route.set([...fixed.solution]);
    (c as any).eaten.set([]);
    c.fen.set(starFen('R', 0));
    f.detectChanges();
    return { f, c };
  };
  const move = (c: StarsPlayComponent, orig: string, dest: string) => c.onMove({ orig: orig as Key, dest: dest as Key });

  /** UI-Sweep 2026-10-10 (k-stars-progress, k-task-pos): grosse Fortschritts-Sterne, „Aufgabe x von 6" ueber der Aufgabe. */
  it('Fortschritt: grosse Sterne (gefressen gold, offen grau) und der Aufgaben-Balken in der rechten Spalte', () => {
    const { f, c } = create();
    const el = f.nativeElement as HTMLElement;
    const stars = () => Array.from(el.querySelectorAll<HTMLElement>('.left span'));
    expect(stars().map(s => s.textContent!.trim())).toEqual(['☆', '☆']);
    const size = parseFloat(getComputedStyle(el.querySelector('.left')!).fontSize);
    expect(size).toBeGreaterThanOrEqual(28);
    expect(size).toBeLessThanOrEqual(32);
    expect(el.querySelector('.left')!.getAttribute('role')).toBe('img');

    move(c, 'a1', 'a3');
    f.detectChanges();
    expect(stars().map(s => s.textContent!.trim())).toEqual(['★', '☆']);
    expect(getComputedStyle(stars()[0]).color).toBe('rgb(245, 180, 0)');
    expect(getComputedStyle(stars()[1]).color).not.toBe('rgb(245, 180, 0)');

    // Aufgabe 1 von 6: nicht mehr in der Kopfzeile, sondern ueber der Aufgabe — sechs Abschnitte, der erste gefuellt.
    expect(el.querySelector('.head .round')).toBeNull();
    const rounds = el.querySelector('.task-slot .rounds')!;
    expect(rounds.querySelector('.round')!.textContent).toContain('kids.stars.round');
    const segs = Array.from(rounds.querySelectorAll('.segments span'));
    expect(segs.length).toBe(STARS_PER_STAGE);
    expect(segs.map(x => x.classList.contains('on'))).toEqual([true, false, false, false, false, false]);
    expect(rounds.compareDocumentPosition(el.querySelector('.task')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('zeigt die Sterne und frisst sie in der richtigen Reihenfolge', () => {
    const { c } = create();
    expect(c.shapes().filter(s => s.customSvg).map(s => s.orig)).toEqual(['a3', 'c3'] as Key[]);
    expect(c.dests().get('a1' as Key)).toContain('a3' as Key);
    expect(c.dests().get('a1' as Key)).not.toContain('a4' as Key);

    move(c, 'a1', 'a3');
    expect(c.status()).toBe('good');
    expect(c.starsLeft()).toEqual([squareIndex('c3')]);
    move(c, 'a3', 'c3');
    expect(c.status()).toBe('solved');
    expect(c.interactive()).toBeFalse();
  });

  it('ein Zug ohne Stern bleibt kurz stehen und wird zurückgenommen', fakeAsync(() => {
    const { c } = create();
    move(c, 'a1', 'a2');
    expect(c.status()).toBe('empty');
    expect(c.holding()).toBeTrue();
    expect(c.fen()).toBe(starFen('R', squareIndex('a2')));
    tick(WRONG_HOLD_MS);
    expect(c.holding()).toBeFalse();
    expect(c.fen()).toBe(starFen('R', 0));
    expect(c.starsLeft().length).toBe(2);
  }));

  it('ein Stern, nach dem nicht mehr alle zu holen sind, ist eine Sackgasse', fakeAsync(() => {
    const { c } = create();
    c.puzzle.set({ piece: 'R', start: 0, stars: [2, 16, 18], solution: [16, 18, 2], unique: true });
    (c as any).route.set([16, 18, 2]);
    move(c, 'a1', 'c1');
    expect(c.status()).toBe('deadEnd');
    tick(WRONG_HOLD_MS);
    expect(c.fen()).toBe(starFen('R', 0));
  }));

  it('die Figur ist immer ausgewählt — nur das Zielfeld tippen', () => {
    const { c } = create();
    expect(c.selectedSquare()).toBe('a1' as Key);
    move(c, 'a1', 'a3');
    expect(c.selectedSquare()).toBe('a3' as Key);
  });

  it('mehrere Wege: ein anderer Stern zählt, solange es noch aufgeht', fakeAsync(() => {
    const { c } = create();
    // Turm a1, Sterne a3, c3, c1: a3→c3→c1 und c1→c3→a3 gehen beide.
    c.puzzle.set({ piece: 'R', start: 0, stars: [2, 16, 18], solution: [16, 18, 2], unique: false });
    (c as any).route.set([16, 18, 2]);
    move(c, 'a1', 'c1');
    expect(c.status()).toBe('good');
    expect(c.starsLeft()).toEqual([18, 16]);
    // Hier wäre a1 ein leeres Feld — weiter c3, dann a3.
    move(c, 'c1', 'c3');
    move(c, 'c3', 'a3');
    expect(c.status()).toBe('solved');
  }));

  it('Tipp: erst leuchtet der nächste Stern, dann zeigt ein Pfeil den Zug', () => {
    const { c } = create();
    c.showHint();
    expect(c.shapes().find(s => s.brush === 'yellow')?.orig).toBe('a3' as Key);
    c.showHint();
    const arrow = c.shapes().find(s => s.brush === 'green');
    expect([arrow?.orig, arrow?.dest]).toEqual(['a1', 'a3'] as Key[]);
  });

  it('nach allen Aufgaben ist die Stufe geschafft und die nächste offen', () => {
    const { c } = create();
    for (let r = 0; r < STARS_PER_STAGE; r++) {
      if (r > 0) expect(c.puzzle()!.stars.length).toBe([2, 2, 3, 3, 4, 4][r]);
      const p = c.puzzle()!;
      let from = p.start;
      for (const s of p.solution) {
        move(c, squareName(from), squareName(s));
        from = s;
      }
      c.next();
    }
    expect(c.complete()).toBeTrue();
    expect(c.nextStage()).toBe(2);
    expect(TestBed.inject(KidsStarsStore).isOpen(2)).toBeTrue();
  });

  it('eine gesperrte Stufe lässt sich nicht spielen', () => {
    params.next(convertToParamMap({ stage: '3' }));
    const f = TestBed.createComponent(StarsPlayComponent);
    f.detectChanges();
    expect(f.componentInstance.locked()).toBeTrue();
  });
});

describe('StarsPlayComponent – freier Modus', () => {
  const FREE = 'rh-kids-stars-free';
  beforeEach(() => {
    localStorage.removeItem(FREE);
    TestBed.configureTestingModule({
      imports: [StarsPlayComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { data: { free: true } }, paramMap: new BehaviorSubject(convertToParamMap({})) } },
      ],
    });
  });
  afterEach(() => localStorage.removeItem(FREE));

  it('Figur und Sternzahl frei wählen, Aufgaben ohne Ende, kein Stufen-Fortschritt', () => {
    const f = TestBed.createComponent(StarsPlayComponent);
    f.detectChanges();
    const c = f.componentInstance;
    expect(c.free()).toBeTrue();
    expect(c.puzzle()?.piece).toBe('R');
    expect(c.puzzle()?.stars.length).toBe(3);

    c.chooseFree('N', 5);
    expect(c.puzzle()?.piece).toBe('N');
    expect(c.puzzle()?.stars.length).toBe(5);
    expect(JSON.parse(localStorage.getItem(FREE)!)).toEqual({ piece: 'N', count: 5 });

    for (let r = 0; r < 8; r++) {
      const p = c.puzzle()!;
      let from = p.start;
      for (const s of p.solution) {
        c.onMove({ orig: squareName(from) as Key, dest: squareName(s) as Key });
        from = s;
      }
      c.next();
    }
    expect(c.freeSolved()).toBe(8);
    expect(c.complete()).toBeFalse();
    expect(c.puzzle()?.stars.length).toBe(5);
    expect(TestBed.inject(KidsStarsStore).done()).toBe(0);
  });

  it('Zahlenfeld hinter der 8: bis 63, beim Läufer bis 31', () => {
    const f = TestBed.createComponent(StarsPlayComponent);
    f.detectChanges();
    const c = f.componentInstance;
    const input = document.createElement('input');
    input.value = '99';
    c.typedCount({ target: input } as unknown as Event);
    expect(c.freeCount()).toBe(63);
    expect(input.value).toBe('63');
    c.chooseFree('B', 63);
    expect(c.freeCount()).toBe(31);
  });

  // UI-Sweep 2026-10-10 (k-free-controls, zweiter Vorschlag): Figur als Umschalter, Sternzahl als Zähler − / +.
  it('Zähler − / +: zeigt die neue Zahl sofort, würfelt erst nach der Pause, bleibt in den Grenzen', fakeAsync(() => {
    const f = TestBed.createComponent(StarsPlayComponent);
    f.detectChanges();
    const c = f.componentInstance;
    c.chooseFree('R', 3);
    const steps = f.nativeElement.querySelectorAll('.stepper .step') as NodeListOf<HTMLButtonElement>;
    steps[1].click(); steps[1].click();
    f.detectChanges();
    expect(c.shownCount()).toBe(5);
    expect(c.freeCount()).toBe(3);                       // noch keine neue Aufgabe
    expect(f.nativeElement.querySelector('.stepper .count').textContent).toContain('5');
    tick(StarsPlayComponent.ApplyDelayMs);
    expect(c.freeCount()).toBe(5);
    expect(c.puzzle()?.stars.length).toBe(5);
    c.chooseFree('R', 2);
    c.stepCount(-1);
    expect(c.shownCount()).toBe(2);                      // nie unter 2
    c.chooseFree('B', 31);
    c.stepCount(1);
    expect(c.shownCount()).toBe(31);                     // Läufer höchstens 31
    expect(f.nativeElement.querySelectorAll('.seg .seg-btn').length).toBe(4);
    discardPeriodicTasks();
  }));

  it('gedrückt halten zählt weiter, der Klick danach zählt nicht doppelt', fakeAsync(() => {
    const f = TestBed.createComponent(StarsPlayComponent);
    f.detectChanges();
    const c = f.componentInstance;
    c.chooseFree('N', 3);
    c.holdStep(1, { button: 0 } as PointerEvent);
    tick(StarsPlayComponent.HoldDelayMs + 2 * StarsPlayComponent.RepeatMs);
    c.releaseStep();
    const held = c.shownCount();
    expect(held).toBe(6);
    c.clickStep(1);
    expect(c.shownCount()).toBe(held);
    tick(StarsPlayComponent.ApplyDelayMs);
    expect(c.freeCount()).toBe(6);
    discardPeriodicTasks();
  }));

  it('merkt sich die letzte Wahl', () => {
    localStorage.setItem(FREE, JSON.stringify({ piece: 'Q', count: 7 }));
    const f = TestBed.createComponent(StarsPlayComponent);
    f.detectChanges();
    expect(f.componentInstance.puzzle()?.piece).toBe('Q');
    expect(f.componentInstance.puzzle()?.stars.length).toBe(7);
  });
});
