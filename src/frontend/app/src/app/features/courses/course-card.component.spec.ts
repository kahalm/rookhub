import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
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
