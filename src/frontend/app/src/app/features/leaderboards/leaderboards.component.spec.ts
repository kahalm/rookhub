import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { Subject, of, throwError } from 'rxjs';
import { LeaderboardsComponent } from './leaderboards.component';
import { LeaderboardService, Leaderboards } from './leaderboard.service';

function data(period: string): Leaderboards {
  return {
    period,
    puzzles: [{ name: 'anna', count: 3, rank: 1, isMe: false }, { name: 'ben', count: 1, rank: 2, isMe: true }],
    endlessRuns: [{ name: 'anna', count: 2, rank: 1, isMe: false }],
    courseLines: [],
    dailyPuzzles: [{ name: 'anna', count: 4, rank: 1, isMe: false }],
  };
}

describe('LeaderboardsComponent', () => {
  it('loads the default period (weekly) on init', () => {
    const svc: any = { get: jasmine.createSpy('get').and.callFake((p: string) => of(data(p))) };
    const c = new LeaderboardsComponent(svc);
    c.ngOnInit();

    expect(svc.get).toHaveBeenCalledWith('weekly');
    expect(c.loading).toBeFalse();
    expect(c.rows('puzzles').length).toBe(2);
    expect(c.rows('puzzles')[0].name).toBe('anna');
    expect(c.rows('courseLines').length).toBe(0);
  });

  it('switching period refetches; same period does not', () => {
    const svc: any = { get: jasmine.createSpy('get').and.callFake((p: string) => of(data(p))) };
    const c = new LeaderboardsComponent(svc);
    c.ngOnInit();
    expect(svc.get).toHaveBeenCalledTimes(1);

    c.onPeriod('monthly');
    expect(c.period).toBe('monthly');
    expect(svc.get).toHaveBeenCalledTimes(2);
    expect(svc.get).toHaveBeenCalledWith('monthly');

    c.onPeriod('monthly');               // identische Periode → kein erneuter Call
    expect(svc.get).toHaveBeenCalledTimes(2);
  });

  it('sets error flag when the request fails', () => {
    const svc: any = { get: () => throwError(() => new Error('boom')) };
    const c = new LeaderboardsComponent(svc);
    c.ngOnInit();

    expect(c.error).toBeTrue();
    expect(c.loading).toBeFalse();
    expect(c.rows('puzzles').length).toBe(0);
  });

  // W5 F5-004: „Monat" (langsam) → „Gesamt" — die spaete Monatsliste darf nicht unter „Gesamt" stehen.
  it('Zeitraumwechsel: die spaete Antwort des vorigen Zeitraums landet nicht beim neuen', () => {
    const subjects: Record<string, Subject<Leaderboards>> = { weekly: new Subject(), monthly: new Subject(), alltime: new Subject() };
    const svc: any = { get: jasmine.createSpy('get').and.callFake((p: string) => subjects[p]) };
    const c = new LeaderboardsComponent(svc);
    c.ngOnInit();
    c.onPeriod('monthly');
    c.onPeriod('alltime');
    expect(subjects['weekly'].observed).toBeFalse();
    expect(subjects['monthly'].observed).toBeFalse();
    subjects['alltime'].next(data('alltime'));
    subjects['monthly'].next(data('monthly'));
    expect(c.data?.period).toBe('alltime');
    expect(c.loading).toBeFalse();
  });

  /** UI-Sweep a1: vier Karten in einer Zeile am PC (Karma 1400 px), eigene Zeile mit 12 px Polster rechts. */
  it('a1-rk-bestenlisten-raster / -eigene-zeile: 4 Karten nebeneinander, rechtes Polster der Zeile 12 px', async () => {
    TestBed.configureTestingModule({
      imports: [LeaderboardsComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: LeaderboardService, useValue: { get: (p: string) => of(data(p)) } }],
    });
    const fixture = TestBed.createComponent(LeaderboardsComponent);
    (fixture.nativeElement as HTMLElement).style.display = 'block';
    (fixture.nativeElement as HTMLElement).style.width = '1100px';
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const cards = [...el.querySelectorAll('.lb-grid > *')] as HTMLElement[];
    expect(cards.length).toBe(4);
    const tops = new Set(cards.map(c => Math.round(c.getBoundingClientRect().top)));
    expect(tops.size).withContext('alle vier Karten in einer Zeile').toBe(1);
    const me = el.querySelector('.lb-row.lb-me') as HTMLElement;
    expect(getComputedStyle(me).paddingRight).toBe('12px');

    const css = (((LeaderboardsComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, ' ');
    expect(css).toMatch(/@media \(min-width: ?600px\) ?\{ ?\.lb-grid ?\{ ?grid-template-columns: ?repeat\(2/);
  });
});
