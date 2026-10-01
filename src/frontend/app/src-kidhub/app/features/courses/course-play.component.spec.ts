import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { CoursePlayComponent, nextUnsolved } from './course-play.component';
import { KidsApiService, KidsCourseLine } from '../../core/kids-api.service';

describe('nextUnsolved (Kinderkurs)', () => {
  const lines = [{ id: 10 }, { id: 11 }, { id: 12 }];

  it('nimmt die erste ungeloeste ab der Stelle', () => {
    expect(nextUnsolved(lines, new Set(), 0)).toBe(0);
    expect(nextUnsolved(lines, new Set([10]), 0)).toBe(1);
    expect(nextUnsolved(lines, new Set([11]), 1)).toBe(2);
  });

  it('faengt am Ende von vorn an', () => {
    expect(nextUnsolved(lines, new Set([12]), 2)).toBe(0);
    expect(nextUnsolved(lines, new Set([10, 12]), 3)).toBe(1);
  });

  it('alles geloest → -1', () => {
    expect(nextUnsolved(lines, new Set([10, 11, 12]), 0)).toBe(-1);
    expect(nextUnsolved([], new Set(), 0)).toBe(-1);
  });
});

/** Codereview 2026-09-29, F7-011: im Fehlerfall stand nur ein Satz da — ohne Knopf, mit dem das Kind weiterkommt. */
describe('CoursePlayComponent — Ladefehler', () => {
  const line: KidsCourseLine = {
    id: 1, bookTitle: 'Matt in einem Zug', fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8', startPly: 0,
    title: null, chapter: null, comment: null, moveComments: null, altMoves: null,
  };

  it('Fehlerkachel mit „Nochmal", das die Linien des Kurses neu holt', () => {
    localStorage.removeItem('rh-kids-progress-v1');
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['coursePuzzles']);
    api.coursePuzzles.and.returnValues(throwError(() => new Error('500')), of([line]));
    TestBed.configureTestingModule({
      imports: [CoursePlayComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: KidsApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ bookId: '7' })) } },
      ],
    });
    const f = TestBed.createComponent(CoursePlayComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const again = el.querySelector<HTMLButtonElement>('kid-error button.again');
    expect(again).withContext('Knopf „Nochmal"').not.toBeNull();

    again!.click();
    f.detectChanges();

    expect(api.coursePuzzles).toHaveBeenCalledTimes(2);
    expect(api.coursePuzzles.calls.mostRecent().args[0]).toBe(7);
    expect(el.querySelector('kid-error')).toBeNull();
    expect(f.componentInstance.current()?.id).toBe(1);
  });
});
