import { TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { Observable, of, throwError } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { CourseLanguageService } from './course-language.service';
import { CourseTranslationsComponent, isOpenJob, jobPercent } from './course-translations.component';
import { CourseService, CourseTranslationJob, CourseTranslations } from './course.service';

const BOOK = 58;

function job(over: Partial<CourseTranslationJob> = {}): CourseTranslationJob {
  return {
    id: 1, bookId: BOOK, language: 'fr', status: 'queued', linesTotal: 100, linesDone: 0, linesFailed: 0,
    automatic: false, requestedByMe: false, queuePosition: 3, createdAt: '2026-09-26T10:00:00Z', ...over,
  };
}

function overview(over: Partial<CourseTranslations> = {}): CourseTranslations {
  return {
    sourceLanguage: 'en',
    languages: [
      { language: 'de', linesTranslated: 1234, linesTotal: 1881 },
      { language: 'hr', linesTranslated: 1881, linesTotal: 1881 },   // vollständig
    ],
    jobs: [], quietUntil: null, myOpenJob: null, available: true, canRequest: true,
    ...over,
  };
}

interface Setup {
  loggedIn?: boolean;
  isAdmin?: boolean;
  /** Antworten auf GET …/translations der Reihe nach (letzte wiederholt sich). */
  answers?: Array<CourseTranslations | 'e404'>;
  post?: () => Observable<HttpResponse<CourseTranslationJob>>;
  del?: () => Observable<void>;
}

function setup(opts: Setup = {}) {
  const answers = opts.answers ?? [overview()];
  let call = 0;
  const courses = {
    getTranslations: jasmine.createSpy('getTranslations').and.callFake(() => {
      const a = answers[Math.min(call++, answers.length - 1)];
      return a === 'e404' ? throwError(() => new HttpErrorResponse({ status: 404 })) : of(a);
    }),
    requestTranslation: jasmine.createSpy('requestTranslation').and.callFake(
      opts.post ?? (() => of(new HttpResponse({ status: 202, body: job() })))),
    withdrawTranslation: jasmine.createSpy('withdrawTranslation').and.callFake(opts.del ?? (() => of(undefined))),
  };
  const snackbar = {
    success: jasmine.createSpy('success'), warn: jasmine.createSpy('warn'),
    quick: jasmine.createSpy('quick'), info: jasmine.createSpy('info'),
  };
  TestBed.configureTestingModule({
    imports: [CourseTranslationsComponent],
    providers: [
      provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      { provide: CourseService, useValue: courses },
      { provide: AuthService, useValue: { isLoggedIn: opts.loggedIn ?? true, isAdmin: opts.isAdmin ?? false } },
      { provide: SnackbarService, useValue: snackbar },
    ],
  });
  const fixture = TestBed.createComponent(CourseTranslationsComponent);
  fixture.componentRef.setInput('bookId', BOOK);
  fixture.detectChanges();
  return { fixture, c: fixture.componentInstance, courses, snackbar, el: fixture.nativeElement as HTMLElement };
}

describe('CourseTranslationsComponent', () => {
  it('zeigt vorhandene Sprachen mit Fortschritt und meldet die Sprachen an die Sprachwahl', () => {
    const { c } = setup();
    expect(c.languageRows().map(r => [r.code, r.done, r.total])).toEqual([['de', 1234, 1881], ['hr', 1881, 1881]]);
    expect(TestBed.inject(CourseLanguageService).languages({ bookId: BOOK })).toEqual(['en', 'de', 'hr']);
  });

  it('„Übersetzen in …": alle 25 Sprachen ohne Quelle, ohne vollständige und ohne offene', () => {
    const { c } = setup({ answers: [overview({ jobs: [job({ language: 'fr' })] })] });
    const codes = c.requestOptions().map(o => o.code);
    expect(codes.length).toBe(25 - 3);
    expect(codes).not.toContain('en');   // Quelle
    expect(codes).not.toContain('hr');   // schon vollständig
    expect(codes).not.toContain('fr');   // Auftrag offen
    expect(codes).toContain('de');       // halb übersetzt → weiter anforderbar
    expect(codes[0]).toBe('de');         // Reihenfolge der Sprachauswahl
  });

  it('Limit: Knopf aus, Hinweis mit Link auf den Kurs, der gerade läuft', () => {
    const { el, c } = setup({ answers: [overview({
      canRequest: false,
      myOpenJob: { jobId: 9, bookId: 77, bookName: 'Anderer Kurs', language: 'de', status: 'running' },
    })] });
    const btn = el.querySelector('.tr-request-btn') as HTMLButtonElement;
    expect(btn.disabled).toBeTrue();
    const link = el.querySelector('.tr-limit a') as HTMLAnchorElement;
    expect(link.textContent).toContain('Anderer Kurs');
    expect(link.getAttribute('href')).toBe('/courses/77');
    expect(c.overview()?.myOpenJob?.bookId).toBe(77);
  });

  it('Limit im SELBEN Kurs: Hinweis ohne Link', () => {
    const { el } = setup({ answers: [overview({
      canRequest: false,
      jobs: [job({ requestedByMe: true })],
      myOpenJob: { jobId: 1, bookId: BOOK, bookName: 'Dieser', language: 'fr', status: 'queued' },
    })] });
    expect(el.querySelector('.tr-limit a')).toBeNull();
    expect(el.querySelector('.tr-limit')?.textContent).toContain('courses.translations.limitHere');
  });

  it('Anfordern: POST mit der Sprache, Meldung und neu laden', () => {
    const { c, courses, snackbar } = setup();
    c.request('fr');
    expect(courses.requestTranslation).toHaveBeenCalledWith(BOOK, 'fr');
    expect(snackbar.success).toHaveBeenCalledWith('courses.translations.requested', jasmine.any(Object));
    expect(courses.getTranslations).toHaveBeenCalledTimes(2);
  });

  it('Anfordern: 200 heißt „wartet schon, du bist dabei"', () => {
    const { c, snackbar } = setup({ post: () => of(new HttpResponse({ status: 200, body: job() })) });
    c.request('fr');
    expect(snackbar.success).toHaveBeenCalledWith('courses.translations.joined', jasmine.any(Object));
  });

  it('Anfordern: eine Absage nennt ihren Grund (409 user-limit) und lädt den Stand neu', () => {
    const translate = () => TestBed.inject(TranslateService);
    const { c, courses, snackbar } = setup({
      post: () => throwError(() => new HttpErrorResponse({ status: 409, error: { reason: 'user-limit' } })),
    });
    translate().setTranslation('en', { courses: { translations: { reason: { 'user-limit': 'Schon einer offen' } } } });
    translate().use('en');
    c.request('fr');
    expect(snackbar.warn).toHaveBeenCalledWith('Schon einer offen');
    expect(courses.getTranslations).toHaveBeenCalledTimes(2);
  });

  it('Zurückziehen: nur der eigene wartende Auftrag (Admin: jeder offene)', () => {
    const mine = job({ id: 5, requestedByMe: true, status: 'queued' });
    const mineRunning = job({ id: 6, requestedByMe: true, status: 'running', queuePosition: null });
    const foreign = job({ id: 7, requestedByMe: false });
    const { c, courses } = setup({ answers: [overview({ jobs: [mineRunning, mine, foreign] })] });
    expect(c.canWithdraw(mine)).toBeTrue();
    expect(c.canWithdraw(mineRunning)).toBeFalse();
    expect(c.canWithdraw(foreign)).toBeFalse();

    c.withdraw(mine);
    expect(courses.withdrawTranslation).toHaveBeenCalledWith(BOOK, 5);
    expect(courses.getTranslations).toHaveBeenCalledTimes(2);

    TestBed.resetTestingModule();
    const admin = setup({ isAdmin: true, answers: [overview({ jobs: [foreign, mineRunning] })] });
    expect(admin.c.canWithdraw(foreign)).toBeTrue();
    expect(admin.c.canWithdraw(mineRunning)).toBeTrue();
  });

  it('fragt alle 15 s nach, solange ein Auftrag offen ist — und hört danach auf', fakeAsync(() => {
    const { courses } = setup({ answers: [
      overview({ jobs: [job({ status: 'running', queuePosition: null, linesDone: 42 })] }),
      overview({ jobs: [job({ status: 'done' })] }),
    ] });
    expect(courses.getTranslations).toHaveBeenCalledTimes(1);
    tick(CourseTranslationsComponent.PollMs - 1);
    expect(courses.getTranslations).toHaveBeenCalledTimes(1);
    tick(1);
    expect(courses.getTranslations).toHaveBeenCalledTimes(2);   // nachgefragt
    tick(CourseTranslationsComponent.PollMs * 3);
    expect(courses.getTranslations).toHaveBeenCalledTimes(2);   // nichts mehr offen → Ruhe
    discardPeriodicTasks();
  }));

  it('ohne offenen Auftrag wird gar nicht erst nachgefragt', fakeAsync(() => {
    const { courses } = setup();
    tick(CourseTranslationsComponent.PollMs * 2);
    expect(courses.getTranslations).toHaveBeenCalledTimes(1);
  }));

  it('ohne Anmeldung nur lesend, ohne Modell ein Hinweis statt Knopf', () => {
    const anon = setup({ loggedIn: false });
    expect(anon.el.querySelector('.tr-request-btn')).toBeNull();
    expect(anon.el.textContent).toContain('courses.translations.loginHint');

    TestBed.resetTestingModule();
    const off = setup({ answers: [overview({ available: false, canRequest: false })] });
    expect(off.el.querySelector('.tr-request-btn')).toBeNull();
    expect(off.el.textContent).toContain('courses.translations.unavailable');
  });

  it('404 (kein Zugang) blendet den Kasten aus', () => {
    const { el, c } = setup({ answers: ['e404'] });
    expect(c.overview()).toBeNull();
    expect(el.querySelector('.tr-card')).toBeNull();
  });

  it('Status: wartet mit Platz, läuft mit Prozent, pausiert in der Sperrzeit', () => {
    const { c } = setup();
    const t = TestBed.inject(TranslateService);
    t.setTranslation('en', { courses: { translations: { status: {
      queued: 'wartet · Platz {{pos}}', running: 'läuft · {{percent}} %', paused: 'pausiert bis {{time}}',
      waiting: 'wartet',
    } } } });
    t.use('en');
    expect(c.statusText(job({ queuePosition: 3 }))).toBe('wartet · Platz 3');
    expect(c.statusText(job({ queuePosition: null }))).toBe('wartet');
    expect(c.statusText(job({ status: 'running', linesDone: 40, linesFailed: 2 }))).toBe('läuft · 42 %');
    c.overview.set(overview({ quietUntil: '2026-09-25T12:00:00Z' }));
    expect(c.statusText(job())).toMatch(/^pausiert bis .+/);
  });

  it('jobPercent / isOpenJob', () => {
    expect(jobPercent({ linesTotal: 0, linesDone: 0, linesFailed: 0 })).toBe(0);
    expect(jobPercent({ linesTotal: 3, linesDone: 1, linesFailed: 0 })).toBe(33);
    expect(jobPercent({ linesTotal: 10, linesDone: 12, linesFailed: 0 })).toBe(100);
    expect(isOpenJob({ status: 'queued' })).toBeTrue();
    expect(isOpenJob({ status: 'running' })).toBeTrue();
    expect(isOpenJob({ status: 'done' })).toBeFalse();
  });
});
