import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { CourseListComponent } from './course-list.component';
import { KidsApiService } from '../../core/kids-api.service';

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
