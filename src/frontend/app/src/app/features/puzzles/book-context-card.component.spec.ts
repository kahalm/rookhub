import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { BookContextCardComponent, WOODPECKER_STATS_OPEN_KEY } from './book-context-card.component';

describe('BookContextCardComponent (Zerlegung Z18)', () => {
  const providers = () => [provideTranslateService({ fallbackLang: 'en' }), provideRouter([]), provideHttpClient(), provideHttpClientTesting()];
  /** Woodpecker-Statistik: Vorgabe zugeklappt (10.11.2026); Tests, die die Zeilen sehen wollen, klappen über den Gerätespeicher auf. */
  const setStatsOpen = (open: boolean) => { try { localStorage.setItem(WOODPECKER_STATS_OPEN_KEY, JSON.stringify(open)); } catch { /* gesperrt */ } };
  beforeEach(() => { try { localStorage.removeItem(WOODPECKER_STATS_OPEN_KEY); } catch { /* gesperrt */ } });
  afterEach(() => { try { localStorage.removeItem(WOODPECKER_STATS_OPEN_KEY); } catch { /* gesperrt */ } });

  function make(inputs: Record<string, unknown>) {
    TestBed.configureTestingModule({
      imports: [BookContextCardComponent],
      providers: providers(),
    });
    const fixture = TestBed.createComponent(BookContextCardComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, c: fixture.componentInstance };
  }

  it('rendert mit Puzzle die Kontext-Karte mit Titel und Buchname', () => {
    const { el } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', titleDisplay: 'Linie 1', bookName: 'Mein Buch' });
    expect(el.querySelector('.context-card .meta-title')?.textContent).toContain('Linie 1');
    expect(el.querySelector('.context-card .book-name')?.textContent).toContain('Mein Buch');
  });

  it('ohne Puzzle: nur im Kurs/Wochenpost und nicht beim Laden die Karte „kein Puzzle“', () => {
    expect(make({ hasPuzzle: false, state: 'LOADING', inCourse: true }).el.querySelector('.context-card')).toBeNull();
    TestBed.resetTestingModule();
    const { el, c } = make({ hasPuzzle: false, state: 'COURSE_DONE', inCourse: true, courseCompleted: true, courseModeKind: 'random' });
    expect(el.querySelector('.context-card .ctx-done')).not.toBeNull();
    let restarted = 0;
    c.restartCourse.subscribe(() => restarted++);
    (el.querySelector('.ctx-nav button[mat-flat-button]') as HTMLButtonElement).click();
    expect(restarted).toBe(1);
  });

  it('Rückpfeil und ⓘ melden sich als Ausgaben; Tagespuzzle-Chip und Partie-Link aus den Eingaben', () => {
    const { el, c } = make({
      hasPuzzle: true, state: 'AWAITING_USER_MOVE', inCourse: true, courseTotal: 5,
      sourceGame: { kind: 'club', id: 9, ply: 12 },
    });
    let back = 0, stats = 0;
    c.back.subscribe(() => back++);
    c.statsToggled.subscribe(() => stats++);
    (el.querySelector('.ctx-back') as HTMLButtonElement).click();
    (el.querySelector('.ctx-stats-btn') as HTMLButtonElement).click();
    expect([back, stats]).toEqual([1, 1]);
    expect(c.sourceGameLink()).toEqual(['/club-games', 9]);
    expect(c.day()).toBeNull();
    TestBed.resetTestingModule();
    const daily = make({ hasPuzzle: true, state: 'SOLVED', isDaily: true, dailyDate: '20260714' });
    expect(daily.c.day()?.toISOString()).toBe('2026-07-14T00:00:00.000Z');
    expect(daily.el.querySelector('.daily-chip-row')).not.toBeNull();
  });
  // 0.882.0: Woodpecker läuft im Kurs-Löser — der Plan-Kopf des alten Lösers steht in der Kontext-Karte.
  const wpPlan = (over: Record<string, unknown> = {}): any => ({
    id: 3, name: '100 Endspiele, die Sie kennen müssen — Band zwei', setSize: 10, availableCount: 287,
    currentCycle: 2, maxCycles: 7, status: 'solving', cycles: [], ...over,
  });

  it('Woodpecker: Plan-Chip, Zyklus · Fortschritt · Resttage, Fortschrittsbalken; Rückpfeil zur Übersicht', () => {
    const { el, c } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true,
      woodpeckerPlan: wpPlan(), woodpeckerAttempted: 4, woodpeckerPercent: 40, woodpeckerDaysLeft: 3 });
    expect(el.querySelector('.wp-chip')?.textContent).toContain('100 Endspiele');
    const parts = Array.from(el.querySelectorAll('.wp-meta > span')).map(x => x.textContent!.trim());
    expect(parts).toEqual(['woodpecker.cycleOf', 'woodpecker.cycleProgress', 'woodpecker.daysLeft']);
    expect(el.querySelector('mat-progress-bar')).not.toBeNull();
    let back = 0;
    c.back.subscribe(() => back++);
    (el.querySelector('.ctx-back') as HTMLButtonElement).click();
    expect(back).toBe(1);
  });

  it('Woodpecker Zyklus 1 (d3-rk-wp-loeser-im-satz-beschriftung): spricht vom wachsenden Satz, ohne Balken', () => {
    const { el } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true,
      woodpeckerPlan: wpPlan({ currentCycle: 1, setSize: 12 }), woodpeckerRepeatHint: true });
    expect(el.querySelector('.wp-meta')?.textContent).toContain('woodpecker.setGrowing');
    expect(el.querySelector('mat-progress-bar')).toBeNull();
    expect(el.querySelector('.wp-repeat')?.textContent).toContain('woodpecker.repeatHint');
  });

  // 0.956.0: Statistik des laufenden Zyklus (gelöst, mit Tipp, offen, Ø, Hochrechnung, Vorzyklus).
  const DAY = 86_400_000;
  const wpCycle = (over: Record<string, unknown>) => ({
    number: 1, targetDays: 28, startedAt: new Date(Date.now() - 3 * DAY).toISOString(),
    deadline: new Date(Date.now() + 25 * DAY).toISOString(), finishedAt: null,
    attempted: 0, solved: 0, timeSeconds: 0, hinted: 0, solvedWithHint: 0, ...over,
  });

  it('Woodpecker-Statistik Zyklus 2+: gelöst/bearbeitet mit Quote, ohne/mit Tipp, offen, Ø; Hochrechnung; Vorzyklus', () => {
    const plan = wpPlan({ setSize: 100, cycles: [
      wpCycle({ attempted: 100, solved: 75, timeSeconds: 9000 }),
      wpCycle({ number: 2, targetDays: 14, deadline: new Date(Date.now() + 10 * DAY).toISOString(),
        attempted: 40, solved: 30, timeSeconds: 2400, hinted: 6, solvedWithHint: 5 }),
    ] });
    setStatsOpen(true);
    const { el } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true, woodpeckerPlan: plan });
    const rows = Array.from(el.querySelectorAll('.wp-stats .wp-stats-row'));
    const kinds = (row: Element) => Array.from(row.children).map(x => x.textContent!.trim());
    expect(kinds(rows[0])).toEqual(['woodpecker.stats.solvedOf', 'woodpecker.stats.clean', 'woodpecker.stats.hinted',
      'woodpecker.stats.open', 'woodpecker.stats.avg']);
    expect(rows[0].classList).toContain('rh-dots');
    expect(kinds(rows[1])).toEqual(['woodpecker.stats.remaining', 'woodpecker.stats.needed', 'woodpecker.stats.pace',
      'woodpecker.stats.etaOnTrack']);   // 40 in 4 Kalendertagen = 10/Tag → 60 offen in 6 Tagen, Frist in 10
    expect(rows[2].textContent).toContain('woodpecker.stats.prev');
  });

  it('Woodpecker-Statistik: Werte gehen formatiert in die Übersetzung (Prozent über formatPct, Zeit als Uhr)', () => {
    const plan = wpPlan({ setSize: 100, cycles: [
      wpCycle({ attempted: 100, solved: 75, timeSeconds: 9000 }),
      wpCycle({ number: 2, attempted: 40, solved: 30, timeSeconds: 2400 }),
    ] });
    setStatsOpen(true);
    TestBed.configureTestingModule({
      imports: [BookContextCardComponent],
      providers: providers(),
    });
    const t = TestBed.inject(TranslateService);
    t.setTranslation('en', { woodpecker: { stats: { solvedOf: '{{solved}}/{{done}} ({{pct}})', avg: 'avg {{time}}', prev: 'C{{n}} {{pct}} {{time}}' } } });
    t.use('en');
    const fixture = TestBed.createComponent(BookContextCardComponent);
    for (const [k, v] of Object.entries({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true, woodpeckerPlan: plan }))
      fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.wp-solved')?.textContent?.trim()).toBe('30/40 (75%)');   // Prozentpunkte, nie 0,75
    expect(el.querySelector('.wp-avg')?.textContent?.trim()).toBe('avg 1:00');
    expect(el.querySelector('.wp-prev')?.textContent?.trim()).toBe('C1 75% 1:30');
    expect(el.querySelector('.wp-hinted')).toBeNull();   // ohne Tipp-Versuche keine Tipp-Angaben
  });

  it('Woodpecker-Statistik Zyklus 1: Kennzahlen ohne „offen“, dazu ≈ Satz bis zur Frist; ohne Versuch nichts', () => {
    setStatsOpen(true);
    const one = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true,
      woodpeckerPlan: wpPlan({ currentCycle: 1, setSize: 40, cycles: [wpCycle({ attempted: 40, solved: 30, timeSeconds: 1200 })] }) });
    expect(one.el.querySelector('.wp-open')).toBeNull();
    expect(one.el.querySelector('.wp-forecast')?.textContent).toContain('woodpecker.stats.projection');
    TestBed.resetTestingModule();
    const empty = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true,
      woodpeckerPlan: wpPlan({ currentCycle: 1, setSize: 0, cycles: [wpCycle({})] }) });
    expect(empty.el.querySelector('.wp-stats-row')).toBeNull();
  });

  it('Woodpecker-Statistik Zyklus 2 ohne Versuch, Frist vorbei: „Frist überschritten“ statt nötig/Tag, kein Tempo', () => {
    const plan = wpPlan({ setSize: 20, cycles: [
      wpCycle({ attempted: 20, solved: 10, timeSeconds: 100 }),
      wpCycle({ number: 2, startedAt: new Date(Date.now() - 3 * DAY).toISOString(), deadline: new Date(Date.now() - DAY).toISOString() }),
    ] });
    setStatsOpen(true);
    const { el } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true, woodpeckerPlan: plan });
    const forecast = el.querySelector('.wp-forecast')!;
    expect(Array.from(forecast.children).map(x => x.textContent!.trim())).toEqual(['woodpecker.overdue']);
    expect(el.querySelector('.wp-solved')).toBeNull();
  });

  it('Woodpecker-Statistik ist standardmäßig zugeklappt: eigener Knopf „anzeigen“, kein Abruf; Aufklappen merkt es je Gerät und lädt /stats', () => {
    const plan = wpPlan({ setSize: 100, cycles: [
      wpCycle({ attempted: 100, solved: 75, timeSeconds: 9000 }),
      wpCycle({ number: 2, attempted: 40, solved: 30, timeSeconds: 2400 }),
    ] });
    const { el, fixture } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true, woodpeckerPlan: plan });
    const http = TestBed.inject(HttpTestingController);
    // Knopf in eigener Zeile unter der Zyklus-Zeile (0.958.1), nicht mehr als Textlink in der .rh-dots-Zeile
    expect(el.querySelector('.wp-meta .wp-stats-toggle')).toBeNull();
    const btn = el.querySelector('.wp-stats-actions > button.wp-stats-toggle[mat-stroked-button]') as HTMLButtonElement;
    expect(btn).not.toBeNull();
    expect(btn.getAttribute('aria-expanded')).toBe('false');
    expect(btn.getAttribute('aria-controls')).toBe('wp-stats-block');
    expect(btn.textContent).toContain('woodpecker.statsShow');   // ohne geladene Sprache = Schlüssel
    expect(btn.querySelector('mat-icon')?.textContent?.trim()).toBe('expand_more');
    expect(el.querySelector('.wp-stats')).toBeNull();
    expect(el.querySelector('.wp-stats-hide-end')).toBeNull();   // unterer Knopf nur offen
    expect(el.querySelector('app-woodpecker-stats-panel')).toBeNull();
    http.expectNone(r => r.url.includes('/stats'));

    btn.click();
    fixture.detectChanges();
    expect(btn.getAttribute('aria-expanded')).toBe('true');
    expect(btn.textContent).toContain('woodpecker.statsHide');
    expect(btn.querySelector('mat-icon')?.textContent?.trim()).toBe('expand_less');
    expect(localStorage.getItem(WOODPECKER_STATS_OPEN_KEY)).toBe('true');
    expect(el.querySelector('.wp-stats .wp-solved')).not.toBeNull();   // die drei kompakten Zeilen zuerst …
    expect(el.querySelector('.wp-stats app-woodpecker-stats-panel')).not.toBeNull();   // … darunter das Ausführliche
    const end = el.querySelector('#wp-stats-block > .wp-stats-end > button.wp-stats-hide-end') as HTMLButtonElement;
    expect(end).not.toBeNull();   // … und ganz unten der zweite Knopf
    expect(end.textContent).toContain('woodpecker.statsHide');
    http.expectOne(r => r.url === '/api/woodpecker/3/stats');

    btn.click();
    fixture.detectChanges();
    expect(el.querySelector('.wp-stats')).toBeNull();
    expect(el.querySelector('.wp-stats-hide-end')).toBeNull();
    expect(btn.textContent).toContain('woodpecker.statsShow');
    expect(localStorage.getItem(WOODPECKER_STATS_OPEN_KEY)).toBe('false');
  });

  it('Woodpecker-Statistik: gemerkt offen startet aufgeklappt; Zuklappen über den unteren Knopf fokussiert den oberen und holt ihn in den Blick', async () => {
    setStatsOpen(true);
    const plan = wpPlan({ setSize: 100, cycles: [
      wpCycle({ attempted: 100, solved: 75, timeSeconds: 9000 }),
      wpCycle({ number: 2, attempted: 40, solved: 30, timeSeconds: 2400 }),
    ] });
    const { el, fixture } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true, woodpeckerPlan: plan });
    document.body.appendChild(el);   // fokussierbar nur im Dokument
    try {
      const top = el.querySelector('.wp-stats-toggle') as HTMLButtonElement;
      expect(top.getAttribute('aria-expanded')).toBe('true');
      const scrolled = spyOn(top, 'scrollIntoView');
      (el.querySelector('.wp-stats-hide-end') as HTMLButtonElement).click();
      fixture.detectChanges();
      await fixture.whenStable();
      expect(el.querySelector('.wp-stats')).toBeNull();
      expect(el.querySelector('.wp-stats-hide-end')).toBeNull();
      expect(top.getAttribute('aria-expanded')).toBe('false');
      expect(document.activeElement).toBe(top);
      expect(scrolled).toHaveBeenCalledWith({ block: 'nearest' });
      expect(localStorage.getItem(WOODPECKER_STATS_OPEN_KEY)).toBe('false');
    } finally {
      el.remove();
    }
  });

  it('Woodpecker ohne Aufgabe: Abschluss bzw. „gibt es nicht (mehr)“ (c1-wp-loeser-404-verbindung), je mit „Zur Übersicht“', () => {
    const done = make({ hasPuzzle: false, state: 'COURSE_DONE', inWoodpecker: true, woodpeckerDoneText: 'Zyklus 2 geschafft' });
    expect(done.el.querySelector('.wp-done')?.textContent).toContain('Zyklus 2 geschafft');
    let over = 0;
    done.c.woodpeckerOverview.subscribe(() => over++);
    (done.el.querySelector('.wp-overview') as HTMLButtonElement).click();
    expect(over).toBe(1);
    TestBed.resetTestingModule();
    const missing = make({ hasPuzzle: false, state: 'COURSE_DONE', inWoodpecker: true, woodpeckerMissing: true });
    expect(missing.el.querySelector('.wp-missing')?.textContent).toContain('woodpecker.notFound');
    expect(missing.el.querySelector('.wp-done')).toBeNull();
    expect(missing.el.querySelector('.wp-overview')).not.toBeNull();
  });
});
