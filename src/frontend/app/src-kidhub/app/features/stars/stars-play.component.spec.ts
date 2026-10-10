import { TestBed, fakeAsync, tick } from '@angular/core/testing';
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
  const fixed: StarPuzzle = { piece: 'R', start: 0, stars: [16, 18], solution: [16, 18] };
  const create = () => {
    const f = TestBed.createComponent(StarsPlayComponent);
    const c = f.componentInstance;
    c.puzzle.set(fixed);
    c.fen.set(starFen('R', 0));
    f.detectChanges();
    return { f, c };
  };
  const move = (c: StarsPlayComponent, orig: string, dest: string) => c.onMove({ orig: orig as Key, dest: dest as Key });

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
    c.puzzle.set({ piece: 'R', start: 0, stars: [2, 16, 18], solution: [16, 18, 2] });
    move(c, 'a1', 'c1');
    expect(c.status()).toBe('deadEnd');
    tick(WRONG_HOLD_MS);
    expect(c.fen()).toBe(starFen('R', 0));
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

  it('merkt sich die letzte Wahl', () => {
    localStorage.setItem(FREE, JSON.stringify({ piece: 'Q', count: 7 }));
    const f = TestBed.createComponent(StarsPlayComponent);
    f.detectChanges();
    expect(f.componentInstance.puzzle()?.piece).toBe('Q');
    expect(f.componentInstance.puzzle()?.stars.length).toBe(7);
  });
});
