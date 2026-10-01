import { TestBed, fakeAsync, flush, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { LevelPlayComponent } from './level-play.component';
import { KidsApiService, KidsLevelDetail } from '../../core/kids-api.service';
import { KidsProgressStore } from '../../core/kids-progress.store';

describe('LevelPlayComponent', () => {
  const KEY = 'rh-kids-progress-v1';
  const detail = (level: number): KidsLevelDetail => ({
    level, theme: 'mate1',
    puzzles: [
      { id: 1, fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8' },
      { id: 2, fen: '6k1/8/6K1/8/8/8/1R6/R7 b - - 0 1', moves: 'g8h8 a1a8' },
    ],
  });
  let params: BehaviorSubject<any>;
  let api: jasmine.SpyObj<KidsApiService>;

  beforeEach(() => {
    localStorage.removeItem(KEY);
    params = new BehaviorSubject(convertToParamMap({ level: '1' }));
    api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['level', 'levels']);
    api.level.and.callFake((n: number) => of(detail(n)));
    api.levels.and.returnValue(of([{ level: 1, theme: 'mate1', puzzleCount: 2 }, { level: 2, theme: 'promote', puzzleCount: 2 }]));
    TestBed.configureTestingModule({
      imports: [LevelPlayComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: KidsApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    });
  });
  afterEach(() => localStorage.removeItem(KEY));

  it('spielt die Aufgaben nacheinander und vergibt am Ende Sterne', () => {
    const f = TestBed.createComponent(LevelPlayComponent);
    f.detectChanges();
    const c = f.componentInstance;
    expect(c.index()).toBe(0);
    expect(c.task()?.moves).toEqual(['g4g3', 'b8h8']);

    c.onSolved(0);
    c.onNext();
    expect(c.index()).toBe(1);
    c.onSolved(1);
    c.onNext();

    expect(c.finished()).toBe(3);
    expect(c.nextLevel()).toBe(2);
    expect(TestBed.inject(KidsProgressStore).isUnlocked(2)).toBeTrue();
  });

  /** Codereview 2026-09-29, F7-011: im Fehlerfall stand nur ein Satz da — ohne Knopf, mit dem das Kind weiterkommt. */
  it('Ladefehler: Fehlerkachel mit „Nochmal", das die Stufe neu holt', () => {
    api.level.and.returnValues(throwError(() => new Error('500')), of(detail(1)));
    const f = TestBed.createComponent(LevelPlayComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const again = el.querySelector<HTMLButtonElement>('kid-error button.again');
    expect(again).withContext('Knopf „Nochmal"').not.toBeNull();

    again!.click();
    f.detectChanges();

    expect(api.level).toHaveBeenCalledTimes(2);
    expect(api.level.calls.mostRecent().args[0]).toBe(1);
    expect(el.querySelector('kid-error')).toBeNull();
    expect(f.componentInstance.task()?.moves).toEqual(['g4g3', 'b8h8']);
  });

  /** F7-011: scheiterte nur der Zusatzabruf der Leiter, fehlte am Ende still „Naechste Stufe" (samt Leertaste). */
  it('scheitert die Leiter beim Oeffnen, holt der Abschluss der Stufe sie nach', () => {
    api.levels.and.returnValues(
      throwError(() => new Error('429')),
      of([{ level: 1, theme: 'mate1', puzzleCount: 2 }, { level: 2, theme: 'promote', puzzleCount: 2 }]),
    );
    const f = TestBed.createComponent(LevelPlayComponent);
    f.detectChanges();
    const c = f.componentInstance;
    expect(c.allLevels()).toEqual([]);

    c.onSolved(0); c.onNext(); c.onSolved(0); c.onNext();
    f.detectChanges();

    expect(api.levels).toHaveBeenCalledTimes(2);
    expect(c.nextLevel()).toBe(2);
    expect((f.nativeElement as HTMLElement).querySelector('.buttons a.btn.primary')).not.toBeNull();
  });

  it('macht einen angefangenen Durchgang bei der naechsten Aufgabe weiter', () => {
    TestBed.inject(KidsProgressStore).recordSolved(1, 0);
    const f = TestBed.createComponent(LevelPlayComponent);
    f.detectChanges();
    expect(f.componentInstance.index()).toBe(1);
  });

  it('eine gesperrte Stufe laedt gar nicht erst', () => {
    params.next(convertToParamMap({ level: '2' }));
    const f = TestBed.createComponent(LevelPlayComponent);
    f.detectChanges();
    expect(f.componentInstance.locked()).toBeTrue();
    expect(api.level).not.toHaveBeenCalled();
  });

  it('„Nochmal" beginnt den Durchgang von vorn, die Sterne bleiben', () => {
    const f = TestBed.createComponent(LevelPlayComponent);
    f.detectChanges();
    const c = f.componentInstance;
    c.onSolved(0); c.onNext(); c.onSolved(0); c.onNext();
    c.again();
    expect(c.index()).toBe(0);
    expect(c.finished()).toBeNull();
    expect(TestBed.inject(KidsProgressStore).level(1).stars).toBe(3);
  });

  /** Gemeldet 2026-09-27 (Screenshot): der erste gruene Punkt war ein grosses Oval — er trug die
   *  Klasse „done", und die Regeln des Abschlussbilds (30px Innenabstand) galten auch fuer ihn. */
  it('ein geloester Punkt bleibt ein kleiner Kreis', () => {
    const f = TestBed.createComponent(LevelPlayComponent);
    f.detectChanges();
    f.componentInstance.onSolved(0);
    f.componentInstance.onNext();
    f.detectChanges();
    const dots = f.nativeElement.querySelectorAll('.dots li') as NodeListOf<HTMLElement>;
    expect(dots.length).toBe(2);
    expect(dots[0].classList).toContain('solved');
    expect(dots[0].offsetHeight).toBe(18);
    expect(dots[0].offsetWidth).toBe(18);
  });

  it('Punkte in der Titelzeile, Aufgabentext neben dem Brett', () => {
    const f = TestBed.createComponent(LevelPlayComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('header.head .dots')).not.toBeNull();
    expect(el.querySelector('kid-puzzle .task-slot .task')).not.toBeNull();
  });

  it('Stufe geschafft: Leertaste fuehrt zur naechsten Stufe', () => {
    const f = TestBed.createComponent(LevelPlayComponent);
    f.detectChanges();
    const c = f.componentInstance;
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const space = () => document.body.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true }));

    space();
    expect(navigate).not.toHaveBeenCalled();         // mitten in der Stufe tut die Leertaste hier nichts

    c.onSolved(0); c.onNext(); c.onSolved(0); c.onNext();
    f.detectChanges();
    space();
    expect(navigate).toHaveBeenCalledWith(['/levels', 2]);
  });
});

/** Codereview 2026-09-29, A10-003: eine Schulklasse hinter EINER NAT-Adresse lief in die Drossel, und das Kind sah
 *  beim Stufenstart sofort das Fehlerbild. Hier mit dem ECHTEN KidsApiService — der holt ein 429 einmal nach. */
describe('LevelPlayComponent — Drossel (429)', () => {
  const KEY = 'rh-kids-progress-v1';
  beforeEach(() => {
    localStorage.removeItem(KEY);
    TestBed.configureTestingModule({
      imports: [LevelPlayComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ level: '1' })) } },
      ],
    });
  });
  afterEach(() => localStorage.removeItem(KEY));

  it('holt die Stufe nach einem 429 einmal nach, statt das Fehlerbild zu zeigen', fakeAsync(() => {
    const http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(LevelPlayComponent);
    f.detectChanges();
    const c = f.componentInstance;

    http.expectOne('/api/kids/levels').flush([{ level: 1, theme: 'mate1', puzzleCount: 1 }]);
    http.expectOne('/api/kids/levels/1').flush(null, {
      status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '2' },
    });
    f.detectChanges();
    expect(c.failed()).toBeFalse();
    expect((f.nativeElement as HTMLElement).textContent).toContain('kids.loading');

    tick(2000);
    http.expectOne('/api/kids/levels/1').flush({
      level: 1, theme: 'mate1', puzzles: [{ id: 1, fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8' }],
    });
    f.detectChanges();
    expect(c.failed()).toBeFalse();
    expect(c.detail()?.level).toBe(1);
    expect((f.nativeElement as HTMLElement).textContent).not.toContain('kids.loadError');
    http.verify();
    f.destroy();
    flush();
  }));
});
