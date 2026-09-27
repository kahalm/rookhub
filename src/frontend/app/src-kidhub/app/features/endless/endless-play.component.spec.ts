import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { EndlessPlayComponent } from './endless-play.component';
import { KidsApiService, KidsEndlessPuzzle } from '../../core/kids-api.service';
import { KidsEndlessStore } from '../../core/kids-endless.store';
import { WRONG_HOLD_MS } from '../../shared/kids-puzzle.component';

describe('EndlessPlayComponent', () => {
  const KEY = 'rh-kids-endless-v1';
  let api: jasmine.SpyObj<KidsApiService>;
  let nextId: number;

  /** Antwortet je Fenster mit einem Puzzle in dessen Mitte. */
  function answer(windows: { minRating: number; maxRating: number }[]): KidsEndlessPuzzle[] {
    return windows.map(w => ({ id: nextId++, fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8', rating: (w.minRating + w.maxRating) / 2 }));
  }

  beforeEach(() => {
    localStorage.removeItem(KEY);
    nextId = 1;
    api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['endlessBatch']);
    api.endlessBatch.and.callFake((w: { minRating: number; maxRating: number }[]) => of(answer(w)));
    TestBed.configureTestingModule({
      imports: [EndlessPlayComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' }), { provide: KidsApiService, useValue: api }],
    });
  });
  afterEach(() => localStorage.removeItem(KEY));

  const create = () => TestBed.createComponent(EndlessPlayComponent).componentInstance;

  it('holt den ersten Block entlang der Grundkurve und beginnt bei 700', () => {
    const c = create();
    const [windows, exclude] = api.endlessBatch.calls.first().args;
    expect(windows.length).toBe(20);
    expect(windows[0]).toEqual({ minRating: 680, maxRating: 720 });
    expect(windows[5]).toEqual({ minRating: 780, maxRating: 820 });
    expect(exclude).toEqual([]);
    expect(c.current()?.rating).toBe(700);
    expect(c.lives()).toBe(3);
  });

  it('hoechstens ein Herz je Aufgabe, die Aufgabe wird trotzdem fertig geloest', () => {
    const c = create();
    c.onMistake();
    c.onMistake();                       // derselbe Fehler zaehlt nicht doppelt
    expect(c.lives()).toBe(2);
    c.onSolved();
    expect(c.solved()).toBe(1);
    c.onNext();
    expect(c.current()?.rating).toBe(720);
  });

  it('drei Herzen weg: nach der Pause vorbei, Lauf mit erstem Fehler und bestem sauberen Rating gemerkt', fakeAsync(() => {
    const c = create();
    c.onSolved(); c.onNext();            // 700 sauber
    c.onSolved(); c.onNext();            // 720 sauber
    c.onMistake(); c.onSolved(); c.onNext();   // 740 mit Fehler
    c.onMistake(); c.onSolved(); c.onNext();   // 760
    c.onMistake();                             // 780: letztes Herz
    expect(c.over()).toBeFalse();
    tick(WRONG_HOLD_MS);
    expect(c.over()).toBeTrue();
    expect(c.solved()).toBe(4);
    const run = TestBed.inject(KidsEndlessStore).runs()[0];
    expect(run).toEqual(jasmine.objectContaining({ solved: 4, maxRating: 720, firstMistakeRating: 740 }));
    expect(c.newBest()).toBeTrue();
  }));

  it('laedt rechtzeitig nach — ab der naechsten Stelle der Kurve, ohne schon Gespieltes', () => {
    const c = create();
    for (let i = 0; i < 15; i++) { c.onSolved(); c.onNext(); }
    expect(api.endlessBatch.calls.count()).toBe(2);
    const [windows, exclude] = api.endlessBatch.calls.mostRecent().args;
    expect(windows[0]).toEqual({ minRating: 1080, maxRating: 1120 });   // Puzzle 20
    expect(exclude.length).toBe(20);
    expect(c.current()?.rating).toBe(1000);
  });

  it('der naechste Lauf ist nach einem guten steiler', fakeAsync(() => {
    TestBed.inject(KidsEndlessStore).record({ at: 1, solved: 30, maxRating: 1600, firstMistakeRating: 1100 });
    create();
    const [windows] = api.endlessBatch.calls.mostRecent().args;
    expect(windows[5]).toEqual({ minRating: 880, maxRating: 920 });    // 900 statt 800
  }));

  it('vorbei: Leertaste startet neu', fakeAsync(() => {
    const c = create();
    c.onMistake(); c.onSolved(); c.onNext();
    c.onMistake(); c.onSolved(); c.onNext();
    c.onMistake();
    tick(WRONG_HOLD_MS);
    const calls = api.endlessBatch.calls.count();
    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true }));
    expect(c.over()).toBeFalse();
    expect(c.lives()).toBe(3);
    expect(api.endlessBatch.calls.count()).toBe(calls + 1);
  }));

  it('schon der erste Block leer: Fehlermeldung statt Spielende', () => {
    api.endlessBatch.and.returnValue(of([]));
    const c = create();
    expect(c.over()).toBeFalse();
    expect(c.failed()).toBeTrue();
  });

  it('spaeter keine Aufgaben mehr: der Lauf endet als geschafft', () => {
    let first = true;
    api.endlessBatch.and.callFake((w: { minRating: number; maxRating: number }[]) => {
      const res = first ? answer(w).slice(0, 2) : [];
      first = false;
      return of(res);
    });
    const c = create();
    c.onSolved(); c.onNext();
    c.onSolved(); c.onNext();
    expect(c.over()).toBeTrue();
    expect(c.solved()).toBe(2);
  });
});
