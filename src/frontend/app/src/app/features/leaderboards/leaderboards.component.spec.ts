import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
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
  /** UI-Sweep d3 (d3-rk-bestenlisten-leer-ohne-weg): alles leer → eine Leerkarte mit Zeitraum-Satz und „Puzzles lösen“. */
  it('d3-rk-bestenlisten-leer-ohne-weg: eine Leerkarte mit Grund und einem Knopf; einzelne leere Karte nennt den Zeitraum', () => {
    const empty = (p: string): Leaderboards => ({ period: p, puzzles: [], endlessRuns: [], courseLines: [], dailyPuzzles: [] });
    let next: (p: string) => Leaderboards = empty;
    TestBed.configureTestingModule({
      imports: [LeaderboardsComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }), provideRouter([]),
        { provide: LeaderboardService, useValue: { get: (p: string) => of(next(p)) } }],
    });
    const fixture = TestBed.createComponent(LeaderboardsComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance.allEmpty).toBeTrue();
    expect(el.querySelectorAll('.lb-card').length).toBe(0);
    const card = el.querySelector('.lb-empty-all') as HTMLElement;
    expect(card.textContent).toContain('leaderboards.emptyAll.weekly');
    const links = card.querySelectorAll('a[mat-flat-button]');
    expect(links.length).toBe(1);
    expect(links[0].getAttribute('href')).toBe('/puzzles');

    next = data;
    fixture.componentInstance.onPeriod('monthly');
    fixture.detectChanges();
    expect(el.querySelector('.lb-empty-all')).toBeNull();
    expect(el.querySelectorAll('.lb-card').length).toBe(4);
    expect(el.querySelector('.lb-card .empty-state')?.textContent).toContain('leaderboards.emptyIn.monthly');
  });

  /** UI-Sweep b1 (b1-rk-bestenlisten-namen-laptop): am Laptop (1 366 px) breiter, Einheit in vier schmalen Spalten aus. */
  it('b1-rk-bestenlisten-namen-laptop: Seite ab 1 280 px bis 1 400 px breit, Einheit 1 100–1 399 px ausgeblendet', () => {
    const css = (((LeaderboardsComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, ' ');
    expect(css).toMatch(/@media \(min-width: ?1280px\) ?\{ ?\.lb-page ?\{ ?max-width: min\(1400px, 96vw\)/);
    expect(css).not.toMatch(/@media \(min-width: ?1400px\) ?\{ ?\.lb-page/);
    expect(css).toMatch(/@media \(min-width: ?1100px\) and \(max-width: ?1399\.98px\) ?\{ ?\.lb-count small ?\{ ?display: none/);
  });

  it('d5-rk-bestenlisten-zeitraum-hoehe: am Handy ist jedes Zeitraum-Segment 44 px hoch', () => {
    const css = (((LeaderboardsComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, ' ').replace(/%NS%/g, '');
    const at = css.indexOf('@media (max-width: 599.98px)');
    expect(at).toBeGreaterThan(-1);
    const media = css.slice(at, css.indexOf('} }', at) + 3);
    expect(media).toMatch(/\.lb-period ?\{ ?--mat-button-toggle-height: 44px/);
    expect(media).toMatch(/\.lb-period \.mat-button-toggle-label-content ?\{ ?line-height: 44px/);
  });
});
