import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { CourseStatsPanelComponent } from './course-stats-panel.component';

describe('CourseStatsPanelComponent (Zerlegung Z18)', () => {
  function make(inputs: Record<string, unknown>): HTMLElement {
    TestBed.configureTestingModule({
      imports: [CourseStatsPanelComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(CourseStatsPanelComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('ohne Statistik vom Server: Balken und „x/y (n %)“', () => {
    const el = make({ solved: 3, total: 10, percent: 30 });
    expect(el.querySelector('.ctx-stats')?.textContent?.trim()).toBe('3/10 (30%)');
    expect(el.querySelector('.cs-scope')).toBeNull();
  });

  it('mit Buch- und Kapitelstatistik: zwei Abschnitte, Kapitelname, Zeit und Restzeit', () => {
    const stats = { total: 10, solvedCount: 4, attemptedCount: 5, accuracyPercent: 80, totalSeconds: 600 };
    const el = make({ bookStats: stats, chapterStats: stats, chapterLabel: 'Kapitel 2' });
    expect(el.querySelectorAll('.cs-scope').length).toBe(2);
    expect(el.querySelector('.cs-chapter .cs-label')?.textContent).toContain('Kapitel 2');
    expect(el.querySelector('.cs-time')?.textContent).toContain('10:00');
    expect(el.querySelector('.cs-eta')).not.toBeNull();
  });
});
