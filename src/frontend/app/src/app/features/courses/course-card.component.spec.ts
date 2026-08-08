import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, RouterLink } from '@angular/router';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
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
