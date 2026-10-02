import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { Observable, TimeoutError, of, throwError } from 'rxjs';
import { PublicSlugComponent } from './public-slug.component';
import { CourseService, PublicSlugChapterTarget, PublicSlugTarget } from './course.service';

interface Nav { commands: unknown[]; queryParams?: Record<string, unknown> }

/** Ausgang einer Auflösung: Ziel, HTTP-Status des Fehlers (0 = offline) oder ein Timeout ohne HTTP-Antwort. */
type Outcome<T> = T | number | 'timeout';

function answer<T>(res: Outcome<T> | undefined): Observable<T> {
  if (res === 'timeout') return throwError(() => new TimeoutError());
  if (res === undefined) return answer<T>(404);
  if (typeof res === 'number') {
    return throwError(() => new HttpErrorResponse({ status: res, url: '/api/courses/by-slug/x' }));
  }
  return of(res);
}

/**
 * Der Slug-Auflöser ist reine Weiterleitungs-Logik — geprüft wird ausschließlich, WOHIN er
 * springt (und ob er überhaupt fragt). Attrappen statt echter Dienste; die Komponente selbst
 * wird zusätzlich einmal über TestBed erzeugt (AOT + DI).
 */
function run(
  params: { slug?: string; chapter?: string },
  resolve: { book?: Outcome<PublicSlugTarget>; chapter?: Outcome<PublicSlugChapterTarget> } = {},
) {
  const navigations: Nav[] = [];
  const asked: { slug?: string; chapter?: string } = {};
  let calls = 0;

  const courses = {
    resolvePublicSlug: (slug: string) => {
      asked.slug = slug;
      calls++;
      return answer(resolve.book);
    },
    resolvePublicSlugChapter: (slug: string, chapter: string) => {
      asked.slug = slug;
      asked.chapter = chapter;
      calls++;
      return answer(resolve.chapter);
    },
  };
  const route = {
    snapshot: {
      paramMap: { get: (k: string) => (params as Record<string, string | undefined>)[k] ?? null },
    },
  };
  const router = {
    navigate: (commands: unknown[], extras?: { queryParams?: Record<string, unknown> }) => {
      navigations.push({ commands, queryParams: extras?.queryParams });
      return Promise.resolve(true);
    },
  };

  TestBed.configureTestingModule({
    providers: [
      { provide: ActivatedRoute, useValue: route },
      { provide: Router, useValue: router },
      { provide: CourseService, useValue: courses },
    ],
  });
  const component = TestBed.runInInjectionContext(() => new PublicSlugComponent());
  component.ngOnInit();
  return { component, navigations, asked, resolve, calls: () => calls };
}

describe('PublicSlugComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [PublicSlugComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PublicSlugComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('rendert die Hinweisseite, wenn der Alias unbekannt ist', async () => {
    await TestBed.configureTestingModule({
      imports: [PublicSlugComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: (k: string) => (k === 'slug' ? 'profil' : null) } } } },
        { provide: CourseService, useValue: { resolvePublicSlug: () => answer(404) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PublicSlugComponent);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('app-not-found h1')?.textContent).toContain('app.notFound.title');
  });

  it('rendert offline den Ladefehler mit „Wiederholen“, NICHT „Seite nicht gefunden“', async () => {
    await TestBed.configureTestingModule({
      imports: [PublicSlugComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: (k: string) => (k === 'slug' ? 'mate1' : null) } } } },
        { provide: CourseService, useValue: { resolvePublicSlug: () => answer(0) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PublicSlugComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('app-not-found')).toBeNull();
    expect(el.querySelector('app-load-error [role="alert"]')?.textContent).toContain('common.loadFailed');
    expect(el.querySelector('app-load-error button')?.textContent).toContain('common.retry');
  });
});

describe('PublicSlugComponent /{slug}', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('springt bei einem normalen Kurs weiterhin in den Solver (Zufallsmodus)', () => {
    const { navigations } = run({ slug: 'mate1' }, { book: { bookId: 5, isCalculation: false } });
    expect(navigations[0].commands).toEqual(['/courses', 5, 'random']);
    expect(navigations[0].queryParams).toEqual({ visualmode: 0 });
  });

  it('springt bei einem KALKULATIONSBUCH in den Kalkulations-Modus', () => {
    // Ohne diese Verzweigung liefe der Link ins Leere: die Stellungen eines Kalkulationsbuchs
    // sind Info-Linien und aus allen Solver-Pools ausgeschlossen — der Solver meldete sofort
    // „abgeschlossen".
    const { navigations } = run({ slug: 'noel' }, { book: { bookId: 9, isCalculation: true } });
    expect(navigations[0].commands).toEqual(['/courses', 9, 'calc']);
    expect(navigations[0].queryParams).toBeUndefined();
  });

  it('behandelt eine Antwort ohne isCalculation als normalen Kurs', () => {
    const { navigations } = run({ slug: 'mate1' }, { book: { bookId: 5 } });
    expect(navigations[0].commands).toEqual(['/courses', 5, 'random']);
  });

  // UX-026: vorher still aufs Dashboard (Gäste: Anmeldemaske) — niemand erkannte den veralteten oder vertippten Link.
  it('zeigt bei unbekannten Aliassen (404) „Seite nicht gefunden“ und bleibt auf der Adresse', () => {
    const { component, navigations } = run({ slug: 'gibtsnicht' }, { book: 404 });
    expect(navigations).toEqual([]);
    expect(component.notFound()).toBeTrue();
    expect(component.loadFailed()).toBeFalse();
  });

  // Nacharbeit UX-026: offline, 502/503 im nächtlichen Deploy oder ein Timeout sagen nichts über den Link —
  // „Link veraltet oder vertippt“ widerspräche dem Verbindungsbanner und ließe einen gültigen Kurslink tot wirken.
  for (const [what, outcome] of [['offline (Status 0)', 0], ['503 im Deploy', 503], ['502', 502], ['Timeout', 'timeout']] as const) {
    it(`meldet bei ${what} einen Ladefehler statt „Seite nicht gefunden“`, () => {
      const { component, navigations } = run({ slug: 'mate1' }, { book: outcome });
      expect(component.notFound()).toBeFalse();
      expect(component.loadFailed()).toBeTrue();
      expect(navigations).toEqual([]);
    });
  }

  it('„Wiederholen“ fragt erneut und springt, sobald der Server wieder antwortet', () => {
    const { component, navigations, resolve, calls } = run({ slug: 'mate1' }, { book: 503 });
    resolve.book = { bookId: 5, isCalculation: false };
    component.retry();
    expect(calls()).toBe(2);
    expect(component.loadFailed()).toBeFalse();
    expect(navigations[0].commands).toEqual(['/courses', 5, 'random']);
  });

  it('zeigt bei einem leeren Slug die Hinweisseite, ohne zu fragen', () => {
    const { component, navigations, asked } = run({ slug: '  ' });
    expect(navigations).toEqual([]);
    expect(component.notFound()).toBeTrue();
    expect(asked.slug).toBeUndefined();
  });

  it('lässt die Hinweisseite weg, solange die Auflösung klappt', () => {
    const { component } = run({ slug: 'mate1' }, { book: { bookId: 5, isCalculation: false } });
    expect(component.notFound()).toBeFalse();
  });
});

describe('PublicSlugComponent /{slug}/{kapitel}', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('fragt Slug UND Kapitelnamen an — der zweite Teil IST der Kapitelname', () => {
    const { asked } = run({ slug: 'noel', chapter: 'KW46' },
      { chapter: { bookId: 9, isCalculation: true, chapter: 'KW46', chapterIndex: null } });
    expect(asked).toEqual({ slug: 'noel', chapter: 'KW46' });
  });

  it('gibt das Kapitel beim Kalkulationsbuch als Filter mit', () => {
    const { navigations } = run({ slug: 'noel', chapter: 'kw46' },
      { chapter: { bookId: 9, isCalculation: true, chapter: 'KW46', chapterIndex: null } });
    expect(navigations[0].commands).toEqual(['/courses', 9, 'calc']);
    // Weitergereicht wird die Schreibweise des SERVERS, nicht die aus der URL.
    expect(navigations[0].queryParams).toEqual({ chapter: 'KW46' });
  });

  it('nutzt beim Solver-Kurs den SOLVER-Kapitelindex', () => {
    const { navigations } = run({ slug: 'mate1', chapter: 'Kapitel 2' },
      { chapter: { bookId: 5, isCalculation: false, chapter: 'Kapitel 2', chapterIndex: 1 } });
    expect(navigations[0].commands).toEqual(['/courses', 5, 'chapter', 1, 'random']);
    expect(navigations[0].queryParams).toEqual({ visualmode: 0 });
  });

  it('nimmt den Index 0 ernst (nicht als „kein Index" missverstehen)', () => {
    const { navigations } = run({ slug: 'mate1', chapter: 'Erstes' },
      { chapter: { bookId: 5, isCalculation: false, chapter: 'Erstes', chapterIndex: 0 } });
    expect(navigations[0].commands).toEqual(['/courses', 5, 'chapter', 0, 'random']);
  });

  it('fällt ohne Solver-Index auf das ganze Buch zurück', () => {
    // Reines Info-/Stellungs-Kapitel: im Solver gibt es dafür keinen Einstieg — die Kapitel-Route
    // wäre leer, statt „abgeschlossen" lieber das ganze Buch.
    const { navigations } = run({ slug: 'mate1', chapter: 'Nur Info' },
      { chapter: { bookId: 5, isCalculation: false, chapter: 'Nur Info', chapterIndex: null } });
    expect(navigations[0].commands).toEqual(['/courses', 5, 'random']);
  });

  it('zeigt bei einem unbekannten (z. B. umbenannten) Kapitel „Seite nicht gefunden“ statt des Dashboards', () => {
    const { component, navigations } = run({ slug: 'noel', chapter: 'KW99' }, { chapter: 404 });
    expect(navigations).toEqual([]);
    expect(component.notFound()).toBeTrue();
  });

  it('meldet beim Kapitel-Link offline einen Ladefehler, nicht „veraltet“', () => {
    const { component } = run({ slug: 'noel', chapter: 'KW46' }, { chapter: 0 });
    expect(component.notFound()).toBeFalse();
    expect(component.loadFailed()).toBeTrue();
  });
});
