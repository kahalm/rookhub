import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService, TranslationObject } from '@ngx-translate/core';
import { WOODPECKER_STATS_DEBOUNCE_MS, WoodpeckerStatsPanelComponent } from './woodpecker-stats-panel.component';
import { todayYmd, WoodpeckerPlan, WoodpeckerStats, WoodpeckerStatsCycle } from './woodpecker.service';

/** Ziffern allein — vergleicht Zahlen unabhängig von Sprache, Leer- und Trennzeichen („46 %“ / „46%“, „2,2“ / „2.2“). */
const digits = (s: string | null | undefined) => (s ?? '').replace(/\D+/g, ' ').trim();

describe('WoodpeckerStatsPanelComponent (0.957.0, Übersicht 2026-11-18)', () => {
  let fixture: ComponentFixture<WoodpeckerStatsPanelComponent>;
  let http: HttpTestingController;
  let el: HTMLElement;

  const DAY = 86_400_000;
  const iso = (offsetDays: number) => new Date(Date.now() + offsetDays * DAY).toISOString();
  /** Mittag in n Tagen (Ortszeit): von heute bis dorthin sind es — unabhängig von der Uhrzeit des Testlaufs — genau
   *  n + 1 Kalendertage (heute und der Fristtag zählen mit). */
  const noonIn = (n: number) => { const d = new Date(); return new Date(d.getFullYear(), d.getMonth(), d.getDate() + n, 12, 0, 0).toISOString(); };
  /** Frist heute kurz vor Mitternacht: heute ist der letzte Tag. */
  const endOfToday = () => { const d = new Date(); return new Date(d.getFullYear(), d.getMonth(), d.getDate(), 23, 59, 59, 999).toISOString(); };
  const DAILY = { count: '{{today}} of {{target}}', how: '{{open}}+{{today}}:{{days}}', howLastDay: '{{open}}+{{today}} last day', reached: 'goal reached' };
  /** Zyklus 2 läuft seit gestern, Frist in 12,5 Tagen (→ „noch 13 Tage“). Der Plan trägt bewusst ALTE Zahlen im
   *  laufenden Zyklus (solved/timeSeconds 0): gezeigt wird der Stand aus `/stats`. */
  const plan = (attempted: number, over: Partial<WoodpeckerPlan> = {}): WoodpeckerPlan => ({
    id: 3, name: 'WP', createdAt: '', completedAt: null, books: [], setSize: 388, availableCount: 400,
    currentCycle: 2, maxCycles: 7, status: 'solving', nextCycleAt: null,
    cycles: [
      { number: 1, targetDays: 28, startedAt: iso(-30), deadline: iso(-2), finishedAt: iso(-2), attempted: 388, solved: 337, timeSeconds: 51379 },
      { number: 2, targetDays: 14, startedAt: iso(-1), deadline: iso(12.5), finishedAt: null, attempted, solved: 0, timeSeconds: 0 },
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

  const cells = (row: Element | null | undefined) => Array.from(row?.children ?? []).map(c => c.textContent!.trim());
  /** Leerraum aus dem Template (Zeilenumbrüche zwischen Beschriftung, Zahl und Klammer) zu einem Leerzeichen. */
  const squash = (t: string) => t.replace(/\s+/g, ' ');

  it('lädt sofort, zeigt erst den Ladebalken, dann ganz oben die Übersicht: Zeit und Quote bisher gegen dieselben Aufgaben in Zyklus 1', () => {
    make(plan(73), { woodpecker: { overview: { now: 'so far', correct: 'correct' }, panel: { colTime: 'time', colCycle: 'C{{k}}' } } });
    const req = expectStats();
    expect(req.request.params.has('utcOffsetMinutes')).toBeTrue();
    expect(el.querySelector('mat-progress-bar')).not.toBeNull();
    expect(el.querySelector('.wps-top')).toBeNull();
    req.flush(stats2());
    fixture.detectChanges();
    expect(el.querySelector('.wps')!.firstElementChild?.classList).toContain('wps-top');   // die Übersicht steht zuoberst
    const table = el.querySelector('.wps-top table.wps-overview')!;
    expect(cells(table.querySelector('thead tr'))).toEqual(['', 'so far', 'C1']);
    expect(cells(table.querySelector('.wps-ov-time'))).toEqual(['time', '1:25:06', '3:06:30']);
    const rate = cells(table.querySelector('.wps-ov-rate'));
    expect(rate[0]).toBe('correct');
    expect(rate.slice(1).map(digits)).toEqual(['75', '95']);     // Prozentpunkte, nie 0,75
    expect(el.querySelector('.wps-ov-note')?.textContent).toContain('woodpecker.overview.sameNote');
    expect(el.querySelector('.wps-ov-partial')).toBeNull();      // 73 gemeinsam = 73 bearbeitet: nichts zu erklären
  });

  it('Übersicht „je Tag“: das Tagesziel mit den Zahlen aus /stats — „heute n von Soll“ und woraus das Soll entsteht', () => {
    const p = plan(70);                       // Plan: 70, /stats: 73
    p.cycles[1].deadline = noonIn(12);        // 13 Kalendertage inkl. heute
    make(p, { woodpecker: { overview: { todayLabel: 'today' }, daily: DAILY } });
    expectStats().flush(stats2({ days: [{ date: '2020-01-01', attempted: 61, solved: 45, timeSeconds: 4000 }, { date: todayYmd(), attempted: 12, solved: 10, timeSeconds: 1106 }] }));
    fixture.detectChanges();
    const day = el.querySelector('.wps-ov-day')!;
    // 388 − 73 = 315 offen; Soll = ⌈(315 + 12 heute) / 13⌉ = 26 — fest über den Tag, nicht mehr ⌈offen / Resttage⌉
    expect(cells(day.querySelector('.wps-ov-goal-row')).map(squash)).toEqual(['today:12 of 26', '(315+12:13)']);
    expect(day.classList).toContain('rh-dots');
    expect(day.children.length).toBe(1);   // Ziel + Herleitung sind EIN Kind: kein Trennpunkt dazwischen
    expect(day.querySelector('.wps-ov-goal-row')!.firstElementChild?.tagName.toLowerCase()).toBe('app-woodpecker-daily-goal');   // derselbe Baustein wie Kachel und Löser-Kopf
    expect(day.querySelector('.wdg-check')).toBeNull();          // nicht erreicht: kein Haken
    expect(day.querySelector('.wdg-done')).toBeNull();
  });

  it('Tagesziel erreicht: grüner Haken und „Tagesziel erreicht“ stehen auch im Fenster', () => {
    const p = plan(73);
    p.cycles[1].deadline = noonIn(12);
    make(p, { woodpecker: { overview: { todayLabel: 'today' }, daily: DAILY } });
    expectStats().flush(stats2({ days: [{ date: '2020-01-01', attempted: 43, solved: 30, timeSeconds: 3000 }, { date: todayYmd(), attempted: 30, solved: 25, timeSeconds: 2106 }] }));
    fixture.detectChanges();
    const goal = el.querySelector('.wps-ov-day app-woodpecker-daily-goal')!;
    expect(goal.querySelector('.wdg-done mat-icon.wdg-check')?.textContent?.trim()).toBe('check_circle');
    expect(goal.querySelector('.wdg-reached')?.textContent).toContain('goal reached');
    expect(goal.querySelector('.wdg-count')?.textContent?.trim()).toBe('30 of 27');   // ⌈(315 + 30) / 13⌉ = 27
    expect(el.querySelector('.wps-ov-how')?.textContent?.trim()).toBe('(315+30:13)');
  });

  it('heute noch nichts bearbeitet: „heute 0“ (letzter Tageseintrag ist älter); am letzten Tag ist alles Offene das Soll', () => {
    const p = plan(73);
    p.cycles[1].deadline = endOfToday();
    make(p, { woodpecker: { overview: { todayLabel: 'today' }, daily: DAILY } });
    expectStats().flush(stats2());   // Tage 9./10.11.2026
    fixture.detectChanges();
    expect(cells(el.querySelector('.wps-ov-day .wps-ov-goal-row')).map(squash)).toEqual(['today:0 of 315', '(315+0 last day)']);
  });

  it('Frist vorbei: „Frist überschritten“ + offen statt einer Soll-Zahl', () => {
    const p = plan(73);
    p.cycles[1].deadline = iso(-1);
    make(p);
    expectStats().flush(stats2());
    fixture.detectChanges();
    const day = el.querySelector('.wps-ov-day')!;
    expect(cells(day).map(squash)).toEqual(['woodpecker.overdue', 'woodpecker.stats.open', 'woodpecker.overview.todayLabel: 0']);
    expect(day.querySelector('.wps-late')).not.toBeNull();
    expect(day.querySelector('app-woodpecker-daily-goal')).toBeNull();   // nach der Frist kein Tagesziel
  });

  it('mehrere frühere Zyklen: jüngster zuerst; kleinere Schnittmenge wird mit den Jetzt-Werten auf diesen Aufgaben ausgewiesen', () => {
    const p = plan(40, { currentCycle: 3, setSize: 100 });
    p.cycles = [p.cycles[0], { ...p.cycles[0], number: 2, targetDays: 14 }, { ...p.cycles[1], number: 3, targetDays: 7 }];
    make(p, { woodpecker: { overview: { partial: 'C{{k}} only {{n}}: {{time}} {{pct}}' }, panel: { colCycle: 'C{{k}}', olderShare: 'C{{k}} {{n}} {{pct}}' } } });
    const over = (cycle: number, count: number, secondsNow: number, secondsThen: number, now: number, then: number) => ({
      cycle, count, secondsNow, secondsThen, solvedNow: 0, solvedThen: 0, hintedNow: 0, hintedThen: 0,
      ratePctNow: now, ratePctThen: then, timeRatio: secondsNow / secondsThen, speedFactor: secondsThen / secondsNow });
    expectStats().flush(stats2({ currentCycle: 3, setSize: 100, changes: null, forecast: null, cycles: [
      row({ sameAsNow: over(1, 40, 2000, 5200, 75, 65) }),
      row({ number: 2, targetDays: 14, sameAsNow: over(2, 25, 1100, 1800, 72, 88) }),
      row({ number: 3, targetDays: 7, finishedAt: null, attempted: 40, solved: 30, solvedClean: 30, timeSeconds: 2000, ratePct: 75, avgSeconds: 50 }),
    ] }));
    fixture.detectChanges();
    const table = el.querySelector('table.wps-overview')!;
    expect(cells(table.querySelector('thead tr')).slice(1)).toEqual(['woodpecker.overview.now', 'C2', 'C1']);
    expect(cells(table.querySelector('.wps-ov-time')).slice(1)).toEqual(['33:20', '30:00', '1:26:40']);
    expect(cells(table.querySelector('.wps-ov-rate')).slice(1).map(digits)).toEqual(['75', '88', '65']);
    const partial = Array.from(el.querySelectorAll('.wps-ov-partial')).map(x => x.textContent!.trim());
    expect(partial.length).toBe(1);                                // nur Zyklus 2 (25 von 40)
    expect(partial[0].replace(/\s*%/, '%')).toBe('C2 only 25: 18:20 72%');
    // Abschnitt „Dieselben Aufgaben“: ältere Zyklen nur noch mit Anteil der Zeit (die Zeit selbst steht oben)
    expect(digits(el.querySelector('.wps-older')?.textContent)).toBe('1 40 38');
  });

  it('Abschnitt „Dieser Zyklus“ (vorher in der Kontext-Karte): Zählung ohne Quote, Tipps, Ø; Hochrechnung ohne „nötig je Tag“', () => {
    make(plan(73), { woodpecker: { stats: { solvedCount: '{{solved}}/{{done}}', avg: 'avg {{time}}', clean: 'clean {{n}}', hinted: 'hint {{n}}' } } });
    expectStats().flush(stats2({ cycles: [stats2().cycles[0], { ...stats2().cycles[1], solvedClean: 50, hinted: 6 }] }));
    fixture.detectChanges();
    const sec = el.querySelector('.wps-cycle')!;
    const rows = Array.from(sec.querySelectorAll('.wps-line'));
    expect(cells(rows[0])).toEqual(['55/73', 'clean 50', 'hint 6', 'avg 1:10']);   // 5106 s / 73
    expect(cells(rows[1])).toEqual(['woodpecker.stats.remaining', 'woodpecker.stats.pace', 'woodpecker.stats.etaOnTrack']);
    expect(sec.textContent).not.toContain('%');
    expect(el.querySelector('.wps-top')!.compareDocumentPosition(sec) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('Abschnitt „Dieselben Aufgaben“: gemeinsame Zahl, Anteil der Zeit, Faktor und gelöst jetzt/damals — Zeiten und Quoten nur oben', () => {
    make(plan(73), { woodpecker: { panel: { sameCount: '{{n}} like C{{k}}', share: 'share {{pct}}', faster: 'x{{x}}', solvedNowThen: '{{now}}/{{then}}/{{n}}' } } });
    expectStats().flush(stats2());
    fixture.detectChanges();
    const same = el.querySelector('.wps-same')!;
    expect(same.querySelector('.wps-strong')?.textContent?.trim()).toBe('73 like C1');
    expect(digits(same.querySelector('.wps-share')?.textContent)).toBe('46');        // Prozentpunkte, nie 0,46
    expect(same.querySelector('.wps-share')?.classList).toContain('wps-ok');          // ≤ halbe Zeit
    expect(digits(same.querySelector('.wps-factor')?.textContent)).toBe('2 2');      // 2,2× / 2.2×
    expect(same.querySelector('.wps-solved-change')?.textContent?.trim()).toBe('55/69/73');
    expect(same.textContent).not.toContain('1:25:06');
    expect(same.textContent).not.toContain('3:06:30');
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
    expect(hist[0].children.length).toBe(7);   // Spalte „dieselben wie jetzt“ steht seit 2026-11-18 oben in der Übersicht
    expect(el.querySelector('.wps-days')).toBeNull();
    const links = Array.from(el.querySelectorAll('.wps-trouble a')).map(a => a.getAttribute('href'));
    expect(links).toEqual(['/puzzles/book/35000', '/puzzles/book/34885']);
    expect(el.querySelector('.wps-missed .wps-label')?.textContent).toContain('woodpecker.panel.puzzleFallback');
    expect(el.textContent).not.toContain('/8/8/');   // nie eine FEN
  });

  it('Zyklus 1: kein Vergleich und kein Soll — Übersicht nur mit „Bisher“, Tempo und heute; dazu je Tag (jüngste zuerst) und Zeitstufen', () => {
    const c1 = { ...plan(0).cycles[0], startedAt: iso(-3), deadline: iso(25), finishedAt: null };
    make(plan(5, { currentCycle: 1, cycles: [c1] }));
    expectStats().flush(stats2({
      currentCycle: 1, cycles: [row({ finishedAt: null, attempted: 73, solved: 55, timeSeconds: 5106 })], changes: null, forecast: null,
      hints: { tracked: 0, untracked: 73, solvedNoHint: 0, solvedHint1: 0, solvedHint2: 0, solvedHint3: 0, missedNoHint: 0, missedWithHint: 0 },
      alwaysMissedCount: 0, alwaysMissed: [],
    }));
    fixture.detectChanges();
    const table = el.querySelector('table.wps-overview')!;
    expect(cells(table.querySelector('thead tr'))).toEqual(['', 'woodpecker.overview.now']);   // keine früheren Zyklen
    expect(cells(table.querySelector('.wps-ov-time'))[1]).toBe('1:25:06');
    expect(el.querySelector('.wps-ov-note')).toBeNull();
    expect(cells(el.querySelector('.wps-ov-day')).map(squash)).toEqual(['woodpecker.stats.pace', 'woodpecker.overview.todayLabel: 0']);
    expect(el.querySelector('.wps-ov-day app-woodpecker-daily-goal')).toBeNull();   // Zyklus 1: kein Soll
    expect(el.querySelector('.wps-cycle')?.textContent).toContain('woodpecker.stats.projection');
    expect(el.querySelector('.wps-same')).toBeNull();
    expect(el.querySelector('.wps-forecast')).toBeNull();
    expect(el.querySelector('.wps-history')).toBeNull();
    const days = Array.from(el.querySelectorAll('.wps-days tbody tr'));
    expect(days.length).toBe(2);
    expect(digits(days[0].children[1].textContent)).toBe('63');
    expect(el.querySelector('.wps-hint-table')).toBeNull();   // keine Tipp-Daten → keine Tipp-Tabelle
    expect(el.querySelector('.wps-buckets-line')).not.toBeNull();
  });

  it('ohne Versuch im Zyklus: Leerzustand statt Nullen — die Übersicht nennt trotzdem das Tagesziel („heute 0 von Soll“)', () => {
    const p0 = plan(0);
    p0.cycles[1].deadline = noonIn(12);
    make(p0, { woodpecker: { overview: { todayLabel: 'today' }, daily: DAILY } });
    expectStats().flush(stats2({ days: [], changes: null, forecast: null,
      cycles: [row({}), row({ number: 2, attempted: 0, solved: 0, solvedClean: 0, timeSeconds: 0, ratePct: null, avgSeconds: null })] }));
    fixture.detectChanges();
    expect(el.querySelector('.wps-empty')).not.toBeNull();
    expect(el.querySelector('.wps-sec')).toBeNull();
    const table = el.querySelector('table.wps-overview')!;
    expect(cells(table.querySelector('.wps-ov-time'))[1]).toBe('–');   // keine vorgetäuschte 0:00
    expect(cells(table.querySelector('.wps-ov-rate'))[1]).toBe('–');
    expect(cells(el.querySelector('.wps-ov-day .wps-ov-goal-row')).map(squash)).toEqual(['today:0 of 30', '(388+0:13)']);   // ⌈388 / 13⌉
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
