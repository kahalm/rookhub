import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { PublicSlugComponent } from './public-slug.component';
import { CourseService, PublicSlugChapterTarget, PublicSlugTarget } from './course.service';

interface Nav { commands: unknown[]; queryParams?: Record<string, unknown> }

/**
 * Der Slug-Auflöser ist reine Weiterleitungs-Logik — geprüft wird ausschließlich, WOHIN er
 * springt (und ob er überhaupt fragt). Attrappen statt echter Dienste; die Komponente selbst
 * wird zusätzlich einmal über TestBed erzeugt (AOT + DI).
 */
function run(
  params: { slug?: string; chapter?: string },
  resolve: { book?: PublicSlugTarget | 'error'; chapter?: PublicSlugChapterTarget | 'error' } = {},
) {
  const navigations: Nav[] = [];
  const asked: { slug?: string; chapter?: string } = {};

  const courses = {
    resolvePublicSlug: (slug: string) => {
      asked.slug = slug;
      const res = resolve.book;
      return res && res !== 'error' ? of(res) : throwError(() => new Error('404'));
    },
    resolvePublicSlugChapter: (slug: string, chapter: string) => {
      asked.slug = slug;
      asked.chapter = chapter;
      const res = resolve.chapter;
      return res && res !== 'error' ? of(res) : throwError(() => new Error('404'));
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
  return { component, navigations, asked };
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

  it('schickt unbekannte Aliasse aufs Dashboard', () => {
    const { navigations } = run({ slug: 'gibtsnicht' }, { book: 'error' });
    expect(navigations[0].commands).toEqual(['/dashboard']);
  });

  it('schickt einen leeren Slug aufs Dashboard, ohne zu fragen', () => {
    const { navigations, asked } = run({ slug: '  ' });
    expect(navigations[0].commands).toEqual(['/dashboard']);
    expect(asked.slug).toBeUndefined();
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

  it('schickt ein unbekanntes Kapitel aufs Dashboard', () => {
    const { navigations } = run({ slug: 'noel', chapter: 'KW99' }, { chapter: 'error' });
    expect(navigations[0].commands).toEqual(['/dashboard']);
  });
});
