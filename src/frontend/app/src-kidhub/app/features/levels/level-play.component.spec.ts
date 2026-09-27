import { TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { BehaviorSubject, of } from 'rxjs';
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
});
