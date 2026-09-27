import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { Observable, of, throwError } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { CourseTranslateMenuComponent } from './course-translate-menu.component';
import { CourseService, CourseTranslationJob, CourseTranslations } from './course.service';

const BOOK = 275;

function job(over: Partial<CourseTranslationJob> = {}): CourseTranslationJob {
  return {
    id: 7, bookId: BOOK, language: 'de', status: 'running', linesTotal: 332, linesDone: 0, linesFailed: 0,
    automatic: false, requestedByMe: true, createdAt: '2026-09-27T12:00:00Z', ...over,
  };
}

function overview(over: Partial<CourseTranslations> = {}): CourseTranslations {
  return {
    sourceLanguage: 'en', languages: [], jobs: [], quietUntil: null, myOpenJob: null,
    available: true, canRequest: true, ...over,
  };
}

/** Das Untermenü im ⋮ des Kurs-Lösers — dieselben Regeln wie der Kasten auf der Kursseite. */
describe('CourseTranslateMenuComponent', () => {
  function setup(answer: CourseTranslations | 'error', post?: () => Observable<HttpResponse<CourseTranslationJob>>) {
    const courses = {
      getTranslations: jasmine.createSpy('getTranslations').and.callFake(() =>
        answer === 'error' ? throwError(() => new HttpErrorResponse({ status: 500 })) : of(answer)),
      requestTranslation: jasmine.createSpy('requestTranslation').and.callFake(
        post ?? (() => of(new HttpResponse({ status: 202, body: job({ status: 'queued' }) })))),
    };
    const snackbar = { success: jasmine.createSpy('success'), warn: jasmine.createSpy('warn') };
    TestBed.configureTestingModule({
      imports: [CourseTranslateMenuComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: CourseService, useValue: courses },
        { provide: SnackbarService, useValue: snackbar },
      ],
    });
    const fixture = TestBed.createComponent(CourseTranslateMenuComponent);
    fixture.componentRef.setInput('bookId', BOOK);
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, courses, snackbar };
  }

  it('holt den Stand erst beim Aufklappen', () => {
    const { c, courses } = setup(overview());
    expect(courses.getTranslations).not.toHaveBeenCalled();
    c.load();
    expect(courses.getTranslations).toHaveBeenCalledWith(BOOK);
    expect(c.options().map(o => o.code)).not.toContain('en');   // die Quelle wird nicht angeboten
    expect(c.options().map(o => o.code)).toContain('de');
  });

  it('eine Sprache anfordern: Auftrag, Meldung, neu laden', () => {
    const { c, courses, snackbar } = setup(overview());
    c.load();
    c.request('fr');
    expect(courses.requestTranslation).toHaveBeenCalledWith(BOOK, 'fr');
    expect(snackbar.success).toHaveBeenCalled();
    expect(courses.getTranslations).toHaveBeenCalledTimes(2);
  });

  it('Absage mit Grund landet als Warnung', () => {
    const { c, snackbar } = setup(overview(), () => throwError(() =>
      new HttpErrorResponse({ status: 409, error: { reason: 'user-limit' } })));
    const tr = TestBed.inject(TranslateService);
    tr.setTranslation('en', { courses: { translations: { reason: { 'user-limit': 'Only one per person' } } } });
    tr.use('en');
    c.load();
    c.request('fr');
    expect(snackbar.warn).toHaveBeenCalledWith('Only one per person');
  });

  it('eigener offener Auftrag: keine Sprachen, dafür der Hinweis — und der laufende steht oben', () => {
    const { c } = setup(overview({
      canRequest: false, jobs: [job()],
      myOpenJob: { jobId: 7, bookId: BOOK, bookName: 'Kurs', language: 'de', status: 'running' },
    }));
    c.load();
    expect(c.openJobs().length).toBe(1);
    expect(c.limitText()).toBe('courses.translations.limitHere');
    expect(c.options().map(o => o.code)).not.toContain('de');   // für de läuft schon einer
  });

  it('Fehler beim Laden: „nicht verfügbar" statt ewig „lädt"', () => {
    const { c } = setup('error');
    c.load();
    expect(c.failed()).toBeTrue();
  });
});
