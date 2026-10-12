import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, RouterLink } from '@angular/router';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { OverlayContainer } from '@angular/cdk/overlay';
import { MatMenuTrigger } from '@angular/material/menu';
import { CourseCardComponent } from './course-card.component';

describe('CourseCardComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [CourseCardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CourseCardComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });
});

/**
 * (!) am Kurs: ein veralteter Kurs, den der „Aktualisieren"-Knopf NICHT beheben kann, muss AM KURS
 * sichtbar sein — im Banner stand nur eine anonyme Zahl, man sah nie, welcher Kurs gemeint ist.
 */
describe('CourseCardComponent Showstopper-Markierung', () => {
  let lastFixture: { detectChanges(): void } | null = null;
  async function render(needsReimport: boolean): Promise<HTMLElement> {
    await TestBed.configureTestingModule({
      imports: [CourseCardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CourseCardComponent);
    fixture.componentInstance.course = {
      bookId: 1, displayName: 'Kurs', puzzleCount: 10, solvedCount: 0, progressPercent: 0,
      needsReimport,
    } as never;
    fixture.detectChanges();
    lastFixture = fixture;
    return fixture.nativeElement as HTMLElement;
  }

  it('zeigt den Chip „Neu holen" nur bei einem Kurs, der sich hier nicht aktualisieren lässt', async () => {
    expect((await render(true)).querySelector('.stale-chip')).not.toBeNull();
    TestBed.resetTestingModule();
    expect((await render(false)).querySelector('.stale-chip')).toBeNull();
  });

  // UI-Sweep 2026-10-11 (r2-rp-course-warn): ein Tipp klappt die Erklärung auf (am Handy gibt es keinen Tooltip).
  it('ein Tipp auf den Chip klappt die Erklärung auf', async () => {
    const el = await render(true);
    expect(el.querySelector('.stale-hint')).toBeNull();
    (el.querySelector('.stale-chip') as HTMLButtonElement).click();
    lastFixture!.detectChanges();
    expect(el.querySelector('.stale-hint')).not.toBeNull();
  });

  // UI-Sweep z2 (z2-rk-course-card-chip-titel): der Chip steht in der Herkunftszeile, nicht neben dem Titel.
  it('setzt den „Neu holen"-Chip unter den Titel (Titel behält die volle Breite)', async () => {
    const el = await render(true);
    expect(el.querySelector('.title-row .stale-chip')).toBeNull();
    expect(el.querySelector('.origin-line .stale-chip')).not.toBeNull();
  });
});

/** UI-Sweep v1 (v1-rk-falsche-mehrzahl-puzzles): „1 Puzzle" statt „1 Puzzles" (DESIGN 5.13, Schlüsselpaar …/…One). */
describe('CourseCardComponent Puzzle-Anzahl', () => {
  afterEach(() => TestBed.resetTestingModule());
  async function meta(puzzleCount: number): Promise<string> {
    await TestBed.configureTestingModule({
      imports: [CourseCardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'de' })],
    }).compileComponents();
    const t = TestBed.inject(TranslateService);
    t.setTranslation('de', { courses: { puzzleCount: '{{count}} Puzzles', puzzleCountOne: '{{count}} Puzzle' } });
    t.use('de');
    const fixture = TestBed.createComponent(CourseCardComponent);
    fixture.componentInstance.course = { bookId: 1, displayName: 'Kurs', puzzleCount, solvedCount: 0, progressPercent: 0 } as never;
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('.card-meta')?.textContent ?? '';
  }

  it('Einzahl bei genau einem Puzzle, sonst Mehrzahl', async () => {
    const one = await meta(1);
    expect(one).toContain('1 Puzzle');
    expect(one).not.toContain('1 Puzzles');
    TestBed.resetTestingModule();
    expect(await meta(7)).toContain('7 Puzzles');
  });
});

/**
 * Kapitel-Primäraktion: der Play-Knopf startet im zuletzt genutzten Kursmodus
 * (die Alternativen liegen im ⋮-Menü der Zeile).
 */
describe('CourseCardComponent Kapitel-Modus', () => {
  function make(lastMode: string | null): CourseCardComponent {
    const c = new CourseCardComponent();
    c.course = { bookId: 1, lastMode } as never;
    return c;
  }

  it('nimmt den letzten Modus des Kurses, sonst sequenziell', () => {
    expect(make('random').chapterMode).toBe('random');
    expect(make('sequential').chapterMode).toBe('sequential');
    expect(make(null).chapterMode).toBe('sequential');
    expect(make('quatsch').chapterMode).toBe('sequential');
  });

  it('der Tooltip nennt den Modus', () => {
    expect(make('random').startChapterTooltip).toBe('courses.startChapterRandom');
    expect(make(null).startChapterTooltip).toBe('courses.startChapterSequential');
  });
});

/**
 * Kalkulationsbuch (Book.IsCalculation): dort gibt es keine Lösung, also auch kein
 * sequenziell/zufällig — die Karte führt in den Kalkulations-Modus und lässt die
 * Kapitel-Modi weg (die Kapitel sind dort nur Sprungliste IM Modus).
 */
describe('CourseCardComponent Kalkulationsbuch', () => {
  /** Ziele aller routerLink-Knöpfe/Links der Karte (routerLink an einem <button> rendert kein href). */
  async function render(isCalculation: boolean): Promise<{ targets: string[]; html: string }> {
    await TestBed.configureTestingModule({
      imports: [CourseCardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CourseCardComponent);
    fixture.componentInstance.course = {
      bookId: 42, displayName: 'Rechnen üben', puzzleCount: 12, solvedCount: 3,
      progressPercent: 25, lastMode: null, isOwned: true, isPinned: false, isCalculation,
    } as never;
    fixture.detectChanges();
    const targets = fixture.debugElement.queryAll(By.directive(RouterLink))
      .map(d => d.injector.get(RouterLink).urlTree?.toString() ?? '')
      .filter(Boolean);
    return { targets, html: (fixture.nativeElement as HTMLElement).innerHTML };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('führt in den Kalkulations-Modus statt in den Solver', async () => {
    const { targets, html } = await render(true);
    expect(targets).toContain('/courses/42/calc');
    expect(targets).not.toContain('/courses/42/sequential');
    expect(targets).not.toContain('/courses/42/random');
    expect(html).toContain('courses.calculate');       // Beschriftung des Knopfes
    expect(html).not.toContain('chapters-block');      // keine Kapitel-Modi
  });

  it('normale Kurse behalten sequenziell/zufällig + Kapitel', async () => {
    const { targets, html } = await render(false);
    expect(targets).toContain('/courses/42/sequential');
    expect(targets).toContain('/courses/42/random');
    expect(targets).not.toContain('/courses/42/calc');
    expect(html).toContain('chapters-block');
  });

  /** Text des geöffneten ⋮-Menüs der Karte (mat-menu rendert erst beim Öffnen, im CDK-Overlay). */
  async function menuText(isCalculation: boolean): Promise<string> {
    await TestBed.configureTestingModule({
      imports: [CourseCardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CourseCardComponent);
    fixture.componentInstance.course = {
      bookId: 42, displayName: 'Rechnen üben', puzzleCount: 12, solvedCount: 3,
      progressPercent: 25, lastMode: null, isOwned: true, isPinned: false, isCalculation,
    } as never;
    fixture.detectChanges();
    const trigger = fixture.debugElement.query(By.directive(MatMenuTrigger)).injector.get(MatMenuTrigger);
    trigger.openMenu();
    fixture.detectChanges();
    await fixture.whenStable();
    return TestBed.inject(OverlayContainer).getContainerElement().textContent ?? '';
  }

  it('bietet beim Kalkulationsbuch weder PGN-Download noch „In Repertoire umwandeln" an', async () => {
    // Beide holen das Roh-PGN aller Wochen (samt Lösung) — der Server sperrt sie dort (404).
    const text = await menuText(true);
    expect(text).toContain('courses.detail.open');            // Menü ist wirklich offen
    expect(text).not.toContain('courses.downloadPgnTooltip');
    expect(text).not.toContain('courses.convertToRepertoireTooltip');
  });

  it('normale Kurse behalten PGN-Download und Umwandeln', async () => {
    const text = await menuText(false);
    expect(text).toContain('courses.downloadPgnTooltip');
    expect(text).toContain('courses.convertToRepertoireTooltip');
  });
});

/**
 * Punkte der Selbstbewertung: eine nackte Summe ist ohne die Zahl der Stellungen nicht lesbar —
 * die Karte nennt deshalb IMMER auch das erreichbare Maximum.
 */
describe('CourseCardComponent Kalkulations-Punkte', () => {
  function make(course: Record<string, unknown>): CourseCardComponent {
    const c = new CourseCardComponent();
    c.course = course as never;
    return c;
  }

  it('nennt die Punkte mit ihrem Maximum', () => {
    expect(make({ isCalculation: true, calcPoints: 14, calcMaxPoints: 24 }).calcScore).toBe('14 / 24');
  });

  it('rechnet das Maximum aus den Stellungen, wenn der Server keins liefert (4 je Stellung)', () => {
    expect(make({ isCalculation: true, calcPoints: 3, puzzleCount: 6 }).calcScore).toBe('3 / 24');
  });

  it('zeigt nichts bei einem normalen Kurs — dort gibt es nichts zu bewerten', () => {
    expect(make({ isCalculation: false, calcPoints: 5, puzzleCount: 3 }).calcScore).toBe('');
    expect(make({ isCalculation: true, calcPoints: null, puzzleCount: 3 }).calcScore).toBe('');
  });

  it('unterscheidet „bewertet, aber 0 Punkte" von „kein Kalkulationsbuch"', () => {
    expect(make({ isCalculation: true, calcPoints: 0, puzzleCount: 2 }).calcScore).toBe('0 / 8');
  });
});

/**
 * Kurs-Übersetzung (0.549.0): die Kapitelliste der Karte zeigt die ÜBERSETZUNG (`label`), sonst das
 * Original — geroutet wird weiter über den Index.
 */
describe('CourseCardComponent Kapitelnamen (label ?? name)', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('zeigt das Label, fällt aufs Original und auf „ohne Kapitel" zurück', async () => {
    await TestBed.configureTestingModule({
      imports: [CourseCardComponent],
      providers: [provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(CourseCardComponent);
    fixture.componentInstance.course = {
      bookId: 42, displayName: 'K', puzzleCount: 5, solvedCount: 0, progressPercent: 0, lastMode: null,
      isOwned: false, isPinned: false,
    } as never;
    fixture.componentInstance.expanded = true;
    fixture.componentInstance.chapters = [
      { index: 0, name: "King's Indian", label: 'Königsindisch', puzzleCount: 3, solvedCount: 0, progressPercent: 0, infoCount: 0 },
      { index: 1, name: 'Endgames', label: null, puzzleCount: 2, solvedCount: 0, progressPercent: 0, infoCount: 0 },
      { index: 2, name: null, puzzleCount: 1, solvedCount: 0, progressPercent: 0, infoCount: 0 },
    ];
    fixture.detectChanges();
    const names = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.chapter-name'))
      .map(e => e.textContent?.trim());
    expect(names).toEqual(['Königsindisch', 'Endgames', 'courses.noChapter']);
    const c = fixture.componentInstance;
    expect(c.chapterName({ index: 0, name: 'A', label: 'B', puzzleCount: 0, solvedCount: 0, progressPercent: 0, infoCount: 0 })).toBe('B');
  });
});

/** UI-Sweep 2026-10-11 (r2-rp-taktik-chip, r2-rp-empty-course): Themen-Chips nur für gesetzte Themen,
 *  Kalkulationsbücher als „Kalkulation"; Kurse ohne Quiz-Linie bekommen einen Leerzustand. */
describe('CourseCardComponent Themen-Chip + Leerzustand', () => {
  function make(course: Record<string, unknown>): CourseCardComponent {
    const c = new CourseCardComponent();
    c.course = { puzzleCount: 5, themes: ['tactics'], ...course } as never;
    return c;
  }

  it('die Vorgabe „nur Taktik" bekommt keinen Chip, gesetzte Themen schon', () => {
    expect(make({}).themeChipKeys).toEqual([]);
    expect(make({ themes: [] }).themeChipKeys).toEqual([]);
    expect(make({ themes: ['opening'] }).themeChipKeys).toEqual(['trainingGoals.theme.opening']);
    expect(make({ themes: ['endgame', 'tactics'] }).themeChipKeys)
      .toEqual(['trainingGoals.theme.endgame', 'trainingGoals.theme.tactics']);
  });

  it('Kalkulationsbücher heißen „Kalkulation"', () => {
    expect(make({ isCalculation: true, themes: ['opening'] }).themeChipKeys).toEqual(['courses.kindCalculation']);
  });

  it('erkennt Kurse ohne Quiz-Linie und ob es Partien zum Durchsehen gibt', () => {
    expect(make({}).isEmptyQuiz).toBeFalse();
    expect(make({ puzzleCount: 0 }).isEmptyQuiz).toBeTrue();
    expect(make({ puzzleCount: 0, isCalculation: true }).isEmptyQuiz).toBeFalse();
    expect(make({ puzzleCount: 0, infoLineCount: 3 }).hasInfoLines).toBeTrue();
    expect(make({ puzzleCount: 0 }).hasInfoLines).toBeFalse();
  });
});

/** UI-Sweep 2026-10-11 (d2-rk-course-card-aktionen): Aktionszeile sitzt in jeder Karte unten bündig (DESIGN 5.2). */
describe('CourseCardComponent Aktionszeile unten', () => {
  it('Inhalt wächst, die Aktionszeile schiebt sich nach unten', async () => {
    await TestBed.configureTestingModule({
      imports: [CourseCardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(CourseCardComponent);
    fixture.componentInstance.course = { bookId: 1, displayName: 'Kurs', puzzleCount: 10, solvedCount: 0, progressPercent: 0 } as never;
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(getComputedStyle(el.querySelector('mat-card-content')!).flexGrow).toBe('1');
    const css = ((CourseCardComponent as any).ɵcmp.styles as string[]).join('\n').replace(/\s+/g, ' ');
    expect(css).toMatch(/\.action-row[^{]*\{[^}]*margin-top: auto/);
  });
});

/** UI-Sweep z1 (z1-rk-course-card-buendig): einzeiliger Titel, Fortschritt unten im Inhalt. */
describe('CourseCardComponent bündig', () => {
  it('kürzt den Titel einzeilig mit „…" (voller Titel im title) und schiebt den Fortschritt nach unten', async () => {
    await TestBed.configureTestingModule({
      imports: [CourseCardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(CourseCardComponent);
    const long = 'Capablanca – Chess Fundamentals, ein sehr langer Titel über mehrere Zeilen';
    fixture.componentInstance.course = { bookId: 1, displayName: long, puzzleCount: 10, solvedCount: 2, progressPercent: 20 } as never;
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const title = el.querySelector('.card-title') as HTMLElement;
    expect(title.getAttribute('title')).toBe(long);
    const cs = getComputedStyle(title);
    expect(cs.whiteSpace).toBe('nowrap');
    expect(cs.textOverflow).toBe('ellipsis');
    expect(getComputedStyle(el.querySelector('mat-card-content') as HTMLElement).flexDirection).toBe('column');
    el.remove();
  });
});
