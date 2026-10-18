import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { CourseProgressSplitComponent, splitPercent } from './course-progress-split.component';
import { CourseScopeStats } from './course.service';

/** Kursfortschritt dreigeteilt (Wunsch 2026-10-15). */
describe('CourseProgressSplitComponent', () => {
  const stats = (o: Partial<CourseScopeStats>): CourseScopeStats => ({
    solvedCount: 0, total: 1000, progressPercent: 0, totalSeconds: 0, attemptedCount: 0,
    firstTryCorrect: 0, accuracyPercent: 0, ...o,
  });

  it('rechnet Abschnittsbreiten in Prozent, gekappt auf den Gesamtwert', () => {
    expect(splitPercent(30, 1000)).toBe(3);
    expect(splitPercent(1, 3)).toBe(33.3);
    expect(splitPercent(5, 0)).toBe(0);
    expect(splitPercent(undefined, 10)).toBe(0);
    expect(splitPercent(20, 10)).toBe(100);
  });

  it('zeigt drei Zahlen und drei Leistenabschnitte', () => {
    TestBed.configureTestingModule({ imports: [CourseProgressSplitComponent], providers: [provideTranslateService({ fallbackLang: 'en' })] });
    const f = TestBed.createComponent(CourseProgressSplitComponent);
    f.componentRef.setInput('stats', stats({ solvedCount: 30, cleanCount: 20, helpedCount: 10, gaveUpCount: 5 }));
    f.detectChanges();
    const el: HTMLElement = f.nativeElement;
    expect(el.querySelector('.lg.clean')!.textContent).toContain('20');
    expect(el.querySelector('.lg.helped')!.textContent).toContain('10');
    expect(el.querySelector('.lg.gaveup')!.textContent).toContain('5');
    expect((el.querySelector('.seg.clean') as HTMLElement).style.width).toBe('2%');
    expect((el.querySelector('.seg.helped') as HTMLElement).style.width).toBe('1%');
    expect((el.querySelector('.seg.gaveup') as HTMLElement).style.width).toBe('0.5%');
  });

  it('d4-rk-progress-bulb-light-red: „mit Tipp“ trägt --rh-hint, nicht die Warn-/Fehlerfarbe', () => {
    TestBed.configureTestingModule({ imports: [CourseProgressSplitComponent], providers: [provideTranslateService({ fallbackLang: 'en' })] });
    const f = TestBed.createComponent(CourseProgressSplitComponent);
    f.componentRef.setInput('stats', stats({ solvedCount: 30, cleanCount: 20, helpedCount: 10, gaveUpCount: 5 }));
    f.detectChanges();
    const el: HTMLElement = f.nativeElement;
    document.body.appendChild(el);
    try {
      const probe = document.createElement('span');
      probe.style.color = 'var(--rh-hint)';
      document.body.appendChild(probe);
      const hint = getComputedStyle(probe).color;
      probe.remove();
      expect(getComputedStyle(el.querySelector('.lg.helped mat-icon')!).color).toBe(hint);
      expect(getComputedStyle(el.querySelector('.seg.helped')!).backgroundColor).toBe(hint);
      expect(hint).not.toBe(getComputedStyle(el.querySelector('.lg.gaveup mat-icon')!).color);
    } finally {
      el.remove();
    }
  });

  it('ältere Antwort ohne Aufschlüsselung: alles Gelöste zählt als ohne Hilfe', () => {
    TestBed.configureTestingModule({ imports: [CourseProgressSplitComponent], providers: [provideTranslateService({ fallbackLang: 'en' })] });
    const f = TestBed.createComponent(CourseProgressSplitComponent);
    f.componentRef.setInput('stats', stats({ solvedCount: 7 }));
    f.detectChanges();
    expect(f.componentInstance.clean()).toBe(7);
  });
});
