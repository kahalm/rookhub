import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { CourseListComponent } from './course-list.component';
import { KidsApiService } from '../../core/kids-api.service';
import { KidsProgressStore } from '../../core/kids-progress.store';

describe('CourseListComponent', () => {
  it('fragt die Kurse in der Sprache der Seite (Kindertitel je Sprache) und zeigt ihre Titel', async () => {
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['courses']);
    api.courses.and.returnValue(of([{ bookId: 339, title: 'Matt in einem Zug', description: null, puzzleCount: 498 }]));
    TestBed.configureTestingModule({
      imports: [CourseListComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' }), { provide: KidsApiService, useValue: api }],
    });
    await TestBed.inject(TranslateService).use('de');

    const f = TestBed.createComponent(CourseListComponent);
    f.detectChanges();

    expect(api.courses).toHaveBeenCalledWith('de');
    expect((f.nativeElement as HTMLElement).textContent).toContain('Matt in einem Zug');
  });

  /** Codereview 2026-09-29, F7-014: der Stand behaelt die Ids geloeschter Linien — die Liste zeigte „10 von 8 geschafft". */
  it('zaehlt nie mehr geloeste Linien, als der Kurs hat (verwaiste Ids)', async () => {
    localStorage.removeItem('rh-kids-progress-v1');
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['courses']);
    api.courses.and.returnValue(of([
      { bookId: 339, title: 'Matt in einem Zug', description: null, puzzleCount: 8 },
      { bookId: 340, title: 'Gabeln', description: null, puzzleCount: 5 },
    ]));
    TestBed.configureTestingModule({
      imports: [CourseListComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' }), { provide: KidsApiService, useValue: api }],
    });
    const progress = TestBed.inject(KidsProgressStore);
    for (let id = 1; id <= 10; id++) progress.recordCourseSolved(339, id);   // zwei davon hat der Admin geloescht
    progress.recordCourseSolved(340, 99);
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { kids: { courses: { progress: '{{done}} of {{total}} done' } } });
    await translate.use('en');

    const f = TestBed.createComponent(CourseListComponent);
    f.detectChanges();

    const metas = [...(f.nativeElement as HTMLElement).querySelectorAll('.meta')].map(m => m.textContent!.trim());
    expect(metas).toEqual(['8 of 8 done', '1 of 5 done']);
    localStorage.removeItem('rh-kids-progress-v1');
  });

  /** Codereview 2026-09-29, F7-011: ein Fehler setzte nur „laedt nicht mehr" — da stand „Noch keine Kurse für Kinder". */
  it('Ladefehler: Fehlerkachel statt „Noch keine Kurse", „Nochmal" holt die Kurse neu', () => {
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['courses']);
    api.courses.and.returnValues(
      throwError(() => new Error('500')),
      of([{ bookId: 339, title: 'Matt in einem Zug', description: null, puzzleCount: 498 }]),
    );
    TestBed.configureTestingModule({
      imports: [CourseListComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' }), { provide: KidsApiService, useValue: api }],
    });
    const f = TestBed.createComponent(CourseListComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.textContent).not.toContain('kids.courses.empty');
    const again = el.querySelector<HTMLButtonElement>('kid-error button.again');
    expect(again).withContext('Knopf „Nochmal"').not.toBeNull();

    again!.click();
    f.detectChanges();

    expect(api.courses).toHaveBeenCalledTimes(2);
    expect(el.querySelector('kid-error')).toBeNull();
    expect(el.textContent).toContain('Matt in einem Zug');
  });
});
