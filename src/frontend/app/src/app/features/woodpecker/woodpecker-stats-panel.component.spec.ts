import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService, TranslationObject } from '@ngx-translate/core';
import { WOODPECKER_STATS_DEBOUNCE_MS, WoodpeckerStatsPanelComponent } from './woodpecker-stats-panel.component';
import { WoodpeckerPlan, WoodpeckerStats, WoodpeckerStatsCycle } from './woodpecker.service';

/** Ziffern allein — vergleicht Zahlen unabhängig von Sprache, Leer- und Trennzeichen („46 %“ / „46%“, „2,2“ / „2.2“). */
const digits = (s: string | null | undefined) => (s ?? '').replace(/\D+/g, ' ').trim();

describe('WoodpeckerStatsPanelComponent (0.957.0)', () => {
  let fixture: ComponentFixture<WoodpeckerStatsPanelComponent>;
  let http: HttpTestingController;
  let el: HTMLElement;

  const plan = (attempted: number, over: Partial<WoodpeckerPlan> = {}): WoodpeckerPlan => ({
    id: 3, name: 'WP', createdAt: '', completedAt: null, books: [], setSize: 388, availableCount: 400,
    currentCycle: 2, maxCycles: 7, status: 'solving', nextCycleAt: null,
    cycles: [
      { number: 1, targetDays: 28, startedAt: '', deadline: '', finishedAt: '', attempted: 388, solved: 337, timeSeconds: 51379 },
      { number: 2, targetDays: 14, startedAt: '', deadline: '', finishedAt: null, attempted, solved: 0, timeSeconds: 0 },
    ],
    ...over,
  });

  const row = (over: Partial<WoodpeckerStatsCycle>): WoodpeckerStatsCycle => ({
    number: 1, targetDays: 28, startedAt: '2026-10-09T22:00:00Z', finishedAt: '2026-11-06T22:00:00Z', attempted: 388,
    solved: 337, solvedClean: 337, hinted: 0, timeSeconds: 51379, ratePct: 86.9, avgSeconds: 132.4, daysUsed: 28,
    hintsTrackedCount: 0, sameAsNow: null, ...over,
  });

  /** Die echten Prod-Zahlen von Plan 1 am 10.11.2026 (73 gemeinsame Aufgaben). */
  const stats2 = (over: Partial<WoodpeckerStats> = {}): WoodpeckerStats => ({
    planId: 3, currentCycle: 2, setSize: 388,
    cycles: [
      row({ sameAsNow: { cycle: 1, count: 73, secondsNow: 5106, secondsThen: 11190, solvedNow: 55, solvedThen: 69,
        hintedNow: 0, hintedThen: 0, ratePctNow: 75.34, ratePctThen: 94.52, timeRatio: 0.4563, speedFactor: 2.1915 } }),
      row({ number: 2, targetDays: 14, finishedAt: null, attempted: 73, solved: 55, solvedClean: 55, timeSeconds: 5106,
        ratePct: 75.34, avgSeconds: 69.9, daysUsed: 0.9, hintsTrackedCount: 2 }),
    ],
    changes: { cycle: 1, improved: 2, worsened: 16, bothSolved: 53, bothMissed: 2, faster: 52, slower: 21, sameTime: 0,
      muchFaster: 30, muchSlower: 5, savedSeconds: 6500, lostSeconds: 416,
      bucketsNow: { fast: 30, medium: 35, slow: 8 }, bucketsThen: { fast: 10, medium: 40, slow: 23 } },
    forecast: { basisCycle: 1, sample: 73, timeRatio: 0.4563, basisTotalSeconds: 51379, spentSeconds: 5106, openCount: 315,
      remainingSeconds: 18338, projectedTotalSeconds: 23444, goalRatio: 0.5, goalReached: true },
    days: [{ date: '2026-11-09', attempted: 10, solved: 8, timeSeconds: 700 }, { date: '2026-11-10', attempted: 63, solved: 47, timeSeconds: 4406 }],
    hintsTrackedSince: '2026-11-10T17:34:00Z',
    hints: { tracked: 2, untracked: 71, solvedNoHint: 1, solvedHint1: 1, solvedHint2: 0, solvedHint3: 0, missedNoHint: 0, missedWithHint: 0 },
    hintChanges: null,
    timeBuckets: { fast: 30, medium: 35, slow: 8 },
    slowest: [{ bookPuzzleId: 34885, bookId: 7, label: 'Exercise 26', secondsNow: 355, secondsPrev: 130, solvedNow: false, cycles: 2 }],
    alwaysMissedCount: 2,
    alwaysMissed: [{ bookPuzzleId: 35000, bookId: 7, label: '', secondsNow: 90, secondsPrev: 100, solvedNow: false, cycles: 2 }],
    ...over,
  });

  function make(p: WoodpeckerPlan, translations?: TranslationObject) {
    TestBed.configureTestingModule({
      imports: [WoodpeckerStatsPanelComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    });
    const t = TestBed.inject(TranslateService);
    if (translations) t.setTranslation('en', translations);
    t.use('en');   // Sprache im Spec selbst festlegen (Prozent-/Zahlschreibweise)
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(WoodpeckerStatsPanelComponent);
    fixture.componentRef.setInput('plan', p);
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  }

  const expectStats = () => http.expectOne(r => r.url === '/api/woodpecker/3/stats');

  afterEach(() => http.verify());

  it('lädt sofort, zeigt erst den Ladebalken, dann Vergleich auf denselben Aufgaben mit Anteil und Faktor', () => {
    make(plan(73), { woodpecker: { panel: {
      sameCount: '{{n}} like C{{k}}', nowTime: 'now {{time}}', thenTime: 'then {{time}}', share: 'share {{pct}}',
      faster: 'x{{x}}', rateNowThen: '{{now}}|{{then}}',
    } } });
    const req = expectStats();
    expect(req.request.params.has('utcOffsetMinutes')).toBeTrue();
    expect(el.querySelector('mat-progress-bar')).not.toBeNull();
    req.flush(stats2());
    fixture.detectChanges();
    const same = el.querySelector('.wps-same')!;
    expect(same.querySelector('.wps-strong')?.textContent?.trim()).toBe('73 like C1');
    expect(same.querySelector('.wps-now')?.textContent?.trim()).toBe('now 1:25:06');
    expect(same.querySelector('.wps-then')?.textContent?.trim()).toBe('then 3:06:30');
    expect(digits(same.querySelector('.wps-share')?.textContent)).toBe('46');        // Prozentpunkte, nie 0,46
    expect(same.querySelector('.wps-share')?.classList).toContain('wps-ok');          // ≤ halbe Zeit
    expect(digits(same.querySelector('.wps-factor')?.textContent)).toBe('2 2');      // 2,2× / 2.2×
    expect(digits(same.querySelector('.wps-rate')?.textContent)).toBe('75 95');
  });

  it('zeigt Wechsel, Zeitstufen, Hochrechnung mit Ziel, Tipps mit Stichtag, Verlauf und Sorgenkinder — keine Tageszeilen ab Zyklus 2', () => {
    make(plan(73));
    expectStats().flush(stats2());
    fixture.detectChanges();
    expect(el.querySelector('.wps-changes')?.textContent).toContain('woodpecker.panel.worsened');
    expect(el.querySelectorAll('.wps-buckets tbody tr').length).toBe(3);
    expect(el.querySelector('.wps-forecast')?.textContent).toContain('woodpecker.panel.goalReached');
    expect(el.querySelector('.wps-hints')?.textContent).toContain('woodpecker.panel.hintsSince');
    expect(el.querySelector('.wps-hints')?.textContent).toContain('woodpecker.panel.hintCompareLater');
    const hist = Array.from(el.querySelectorAll('.wps-history tbody tr'));
    expect(hist.length).toBe(2);
    // „Ohne Tipp“ nur, wenn ALLE Versuche des Zyklus Tipp-Daten haben — sonst „–“ statt einer vorgetäuschten Zahl.
    expect(hist[0].children[3].textContent?.trim()).toBe('–');
    expect(hist[0].children[7].textContent?.trim()).toBe('3:06:30');   // dieselben Aufgaben wie jetzt
    expect(el.querySelector('.wps-days')).toBeNull();
    const links = Array.from(el.querySelectorAll('.wps-trouble a')).map(a => a.getAttribute('href'));
    expect(links).toEqual(['/puzzles/book/35000', '/puzzles/book/34885']);
    expect(el.querySelector('.wps-missed .wps-label')?.textContent).toContain('woodpecker.panel.puzzleFallback');
    expect(el.textContent).not.toContain('/8/8/');   // nie eine FEN
  });

  it('Zyklus 1: kein Vergleich, stattdessen je Tag (jüngste zuerst) und Zeitstufen', () => {
    make(plan(5, { currentCycle: 1, cycles: [plan(0).cycles[0]] }));
    expectStats().flush(stats2({
      currentCycle: 1, cycles: [row({ finishedAt: null, attempted: 73 })], changes: null, forecast: null,
      hints: { tracked: 0, untracked: 73, solvedNoHint: 0, solvedHint1: 0, solvedHint2: 0, solvedHint3: 0, missedNoHint: 0, missedWithHint: 0 },
      alwaysMissedCount: 0, alwaysMissed: [],
    }));
    fixture.detectChanges();
    expect(el.querySelector('.wps-same')).toBeNull();
    expect(el.querySelector('.wps-forecast')).toBeNull();
    expect(el.querySelector('.wps-history')).toBeNull();
    const days = Array.from(el.querySelectorAll('.wps-days tbody tr'));
    expect(days.length).toBe(2);
    expect(digits(days[0].children[1].textContent)).toBe('63');
    expect(el.querySelector('.wps-hint-table')).toBeNull();   // keine Tipp-Daten → keine Tipp-Tabelle
    expect(el.querySelector('.wps-buckets-line')).not.toBeNull();
  });

  it('ohne Versuch im Zyklus: Leerzustand statt Nullen', () => {
    make(plan(0));
    expectStats().flush(stats2({ cycles: [row({}), row({ number: 2, attempted: 0, ratePct: null, avgSeconds: null })], changes: null, forecast: null }));
    fixture.detectChanges();
    expect(el.querySelector('.wps-empty')).not.toBeNull();
    expect(el.querySelector('.wps-sec')).toBeNull();
  });

  it('Fehler: app-load-error mit Wiederholen, der Strom lebt danach weiter', () => {
    make(plan(73));
    expectStats().flush('x', { status: 500, statusText: 'err' });
    fixture.detectChanges();
    expect(el.querySelector('app-load-error')).not.toBeNull();
    (el.querySelector('app-load-error button') as HTMLButtonElement).click();
    expectStats().flush(stats2());
    fixture.detectChanges();
    expect(el.querySelector('app-load-error')).toBeNull();
    expect(el.querySelector('.wps-same')).not.toBeNull();
  });

  it('lädt nach einer gemeldeten Aufgabe neu — entprellt, ein Abruf für mehrere Meldungen', fakeAsync(() => {
    make(plan(73));
    expectStats().flush(stats2());
    fixture.componentRef.setInput('plan', plan(74));
    fixture.detectChanges();
    tick(WOODPECKER_STATS_DEBOUNCE_MS / 2);
    fixture.componentRef.setInput('plan', plan(75));
    fixture.detectChanges();
    http.expectNone(r => r.url === '/api/woodpecker/3/stats');
    tick(WOODPECKER_STATS_DEBOUNCE_MS);
    expectStats().flush(stats2());
    // Gleicher Stand (z. B. neues Planobjekt nach „Weiter“) lädt nicht neu.
    fixture.componentRef.setInput('plan', plan(75));
    fixture.detectChanges();
    tick(WOODPECKER_STATS_DEBOUNCE_MS * 2);
    http.expectNone(r => r.url === '/api/woodpecker/3/stats');
  }));

  it('am Handy (390 px) läuft die Seite nicht waagerecht über — Tabellen scrollen im eigenen Kasten', () => {
    make(plan(73));
    expectStats().flush(stats2());
    fixture.detectChanges();
    el.style.display = 'block';
    el.style.width = '390px';
    document.body.appendChild(el);
    const wps = el.querySelector('.wps') as HTMLElement;
    expect(wps.scrollWidth).toBeLessThanOrEqual(wps.clientWidth + 1);
    el.remove();
  });
});
