import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { CourseListComponent } from './course-list.component';
import { KidsApiService } from '../../core/kids-api.service';
import { KidsProgressStore } from '../../core/kids-progress.store';
import { contrast, parseColor } from '@rh/testing/contrast';

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

  /** UI-Sweep 2026-10-10 (k-courses-empty): vorher nur eine graue Zeile, der Titel stand neben der Mitte. */
  it('ohne Kurse: Karte mit Eule und zwei Wegen zum Spielen, der Titel genau mittig', async () => {
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['courses']);
    api.courses.and.returnValue(of([]));
    TestBed.configureTestingModule({
      imports: [CourseListComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' }), { provide: KidsApiService, useValue: api }],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', { kids: { back: 'Start', courses: {
      title: 'Kurse', soon: 'Hier kommen bald Kurse!', playLevels: 'Stufen spielen', playEndless: 'Endlos spielen' } } });
    await translate.use('de');

    const f = TestBed.createComponent(CourseListComponent);
    (f.nativeElement as HTMLElement).style.width = '820px';
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;

    const card = el.querySelector('.empty')!;
    expect(card.querySelector('.owl')?.textContent).toContain('🦉');
    expect(card.querySelector('h2')?.textContent).toContain('Hier kommen bald Kurse!');
    const links = Array.from(card.querySelectorAll<HTMLAnchorElement>('a.way'));
    expect(links.map(a => a.getAttribute('href'))).toEqual(['/levels', '/endless']);
    expect(links.map(a => a.textContent!.trim())).toEqual(['Stufen spielen', 'Endlos spielen']);
    for (const a of links) {
      const s = getComputedStyle(a);
      expect(contrast(parseColor(s.color), parseColor(s.backgroundColor))).withContext(a.textContent!).toBeGreaterThanOrEqual(4.5);
    }

    const head = el.querySelector('.head')!.getBoundingClientRect();
    const title = el.querySelector('.head h1')!.getBoundingClientRect();
    expect(Math.abs((title.left + title.right) / 2 - (head.left + head.right) / 2)).toBeLessThan(1.5);
  });
});
