import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { BookContextCardComponent } from './book-context-card.component';

describe('BookContextCardComponent (Zerlegung Z18)', () => {
  const providers = () => [provideTranslateService({ fallbackLang: 'en' }), provideRouter([]), provideHttpClient(), provideHttpClientTesting()];

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

  // Wunsch 2026-11-23: einspaltig (< 900 px) stehen Aufgabe · Kapitel · Buch direkt unter dem Brett, der Fortschritt
  // danach — per CSS order auf .wp-progress (ein Template). Hier: genau die drei Fortschrittszeilen tragen die Klasse.
  it('Woodpecker: nur Plan-Chip, Zyklus-Zeile und „Statistik“ sind .wp-progress — Titel, Kapitel, Buch nicht', () => {
    const { el } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true, woodpeckerPlan: wpPlan(),
      titleDisplay: 'Exercise 373', chapterDisplay: '5.3) Intermediate Exercises I', bookName: 'The Woodpecker Method' });
    const text = el.querySelector('.ctx-text')!;
    const tagged = Array.from(text.querySelectorAll(':scope > .wp-progress'));
    expect(tagged.length).toBe(3);
    expect(tagged[0].querySelector('.wp-chip')).not.toBeNull();
    expect(tagged[1].classList).toContain('wp-meta');
    expect(tagged[2].classList).toContain('wp-stats-actions');
    for (const sel of ['.meta-title', '.meta-chapter', '.book-name']) {
      expect(text.querySelector(`:scope > ${sel}`)?.classList.contains('wp-progress')).toBeFalse();
    }
  });

  it('Woodpecker Zyklus 1 (d3-rk-wp-loeser-im-satz-beschriftung): spricht vom wachsenden Satz, ohne Balken', () => {
    const { el } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true,
      woodpeckerPlan: wpPlan({ currentCycle: 1, setSize: 12 }), woodpeckerRepeatHint: true });
    expect(el.querySelector('.wp-meta')?.textContent).toContain('woodpecker.setGrowing');
    expect(el.querySelector('mat-progress-bar')).toBeNull();
    expect(el.querySelector('.wp-repeat')?.textContent).toContain('woodpecker.repeatHint');
  });

  // 2026-11-18: die Statistik ist ein Fenster über dem Löser (vorher aufklappbarer Block in der Karte, 0.956.0–0.958.1).
  it('Woodpecker: Knopf „Statistik“ in eigener Zeile meldet den Wunsch an den Löser — die Karte zeigt und lädt selbst keine Statistik', () => {
    const plan = wpPlan({ setSize: 100, cycles: [
      { number: 1, attempted: 100, solved: 75, timeSeconds: 9000 }, { number: 2, attempted: 40, solved: 30, timeSeconds: 2400 },
    ] });
    const { el, c, fixture } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true, woodpeckerPlan: plan });
    const http = TestBed.inject(HttpTestingController);
    expect(el.querySelector('.wp-meta button')).toBeNull();   // nicht als Textlink in der .rh-dots-Zeile (0.958.1)
    const btn = el.querySelector('.wp-stats-actions > button.wp-stats-open[mat-stroked-button]') as HTMLButtonElement;
    expect(btn).not.toBeNull();
    expect(btn.textContent).toContain('woodpecker.statistics');   // ohne geladene Sprache = Schlüssel
    expect(btn.getAttribute('aria-haspopup')).toBe('dialog');
    expect(btn.hasAttribute('aria-expanded')).toBeFalse();        // kein Auf-/Zuklappen mehr
    let asked = 0;
    c.woodpeckerStats.subscribe(() => asked++);
    btn.click();
    fixture.detectChanges();
    expect(asked).toBe(1);
    expect(el.querySelector('app-woodpecker-stats-panel')).toBeNull();
    expect(el.querySelector('.wp-stats')).toBeNull();
    http.expectNone(r => r.url.includes('/stats'));
  });

  // 0.993.0, Wunsch 18.11.: „auch ohne Statistik visualisieren, wenn ich heute das notwendige Minimum erledigt habe
  // (… als auch wenn ich drinnen bin)“ — das Tagesziel steht im Plan-Kopf und läuft mit jeder gemeldeten Aufgabe mit.
  describe('Woodpecker-Tagesziel im Plan-Kopf', () => {
    const noonIn = (n: number) => { const d = new Date(); return new Date(d.getFullYear(), d.getMonth(), d.getDate() + n, 12, 0, 0).toISOString(); };
    /** Satz 388, Frist übermorgen Mittag = 3 Kalendertage inkl. heute. */
    const running = (attempted: number, todayAttempted: number | undefined, over: Record<string, unknown> = {}) => wpPlan({
      setSize: 388, todayAttempted,
      cycles: [
        { number: 1, attempted: 388, solved: 337, timeSeconds: 9000, startedAt: '', deadline: '', finishedAt: 'x' },
        { number: 2, attempted, solved: attempted, timeSeconds: 60, startedAt: new Date().toISOString(), deadline: noonIn(2), finishedAt: null },
      ],
      ...over,
    });
    const goal = (el: HTMLElement) => Array.from(el.querySelectorAll('.wp-meta app-woodpecker-daily-goal .wdg > *')).map(x => x.textContent!.trim());
    const inputs = (plan: unknown) => ({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true, woodpeckerPlan: plan });

    it('offen: „Heute: 23 von 90“ am Ende der Zyklus-Zeile (dahinter nur der Zeitplan), ohne Haken', () => {
      const { el } = make(inputs(running(141, 23)));   // 247 offen; Soll = ⌈(247 + 23) / 3⌉ = 90
      expect(goal(el)).toEqual(['woodpecker.overview.todayLabel:', 'woodpecker.daily.count']);
      // letzte Angaben der Zyklus-Zeile: Tagesziel, dahinter (seit 0.994.0) der Zeitplan
      expect(Array.from(el.querySelector('.wp-meta')!.children).slice(3).map(x => x.tagName.toLowerCase()))
        .toEqual(['app-woodpecker-daily-goal', 'app-woodpecker-schedule']);
      expect(el.querySelector('.wp-meta .wdg-check')).toBeNull();
    });

    it('erreicht: grüner Haken + „Tagesziel erreicht“', () => {
      const { el } = make(inputs(running(208, 90)));   // 180 offen; Soll = ⌈270 / 3⌉ = 90
      expect(goal(el)).toEqual(['check_circle', 'woodpecker.daily.reached', 'woodpecker.daily.count']);
      expect(el.querySelector('.wp-meta .wdg-done .wdg-check')?.getAttribute('aria-hidden')).toBe('true');
    });

    it('läuft mit: der Plan aus der Antwort auf die gemeldete Aufgabe schaltet von offen auf erreicht', () => {
      const { el, fixture } = make(inputs(running(207, 89)));
      expect(el.querySelector('.wp-meta .wdg-check')).toBeNull();
      fixture.componentRef.setInput('woodpeckerPlan', running(208, 90));
      fixture.detectChanges();
      expect(el.querySelector('.wp-meta .wdg-done .wdg-check')).not.toBeNull();
    });

    it('kein Element ohne Ziel: Feld fehlt (älterer Server), Frist vorbei — sonst stünde ein Trennpunkt ohne Angabe', () => {
      const old = make(inputs(running(141, undefined)));
      expect(old.el.querySelector('app-woodpecker-daily-goal')).toBeNull();
      // drei feste Angaben + der Zeitplan (braucht `todayAttempted` nicht) — kein leeres Element fürs Tagesziel
      expect(Array.from(old.el.querySelector('.wp-meta')!.children).map(x => x.tagName.toLowerCase()))
        .toEqual(['span', 'span', 'span', 'app-woodpecker-schedule']);
      TestBed.resetTestingModule();
      const late = running(141, 23);
      late.cycles[1].deadline = new Date(Date.now() - 86_400_000).toISOString();
      expect(make(inputs(late)).el.querySelector('app-woodpecker-daily-goal')).toBeNull();
    });

    it('Zyklus 1 (kein Soll): nur „Heute: n“', () => {
      const p = wpPlan({ currentCycle: 1, setSize: 12, todayAttempted: 5, cycles: [
        { number: 1, attempted: 12, solved: 10, timeSeconds: 600, startedAt: new Date().toISOString(), deadline: noonIn(20), finishedAt: null },
      ] });
      const { el } = make(inputs(p));
      expect(goal(el)).toEqual(['woodpecker.overview.todayLabel:', '5']);
    });

    it('außerhalb von Woodpecker nie', () => {
      const { el } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inCourse: true, woodpeckerPlan: running(208, 90) });
      expect(el.querySelector('app-woodpecker-daily-goal')).toBeNull();
    });
  });

  // 0.994.0, Wunsch 18.11.: „neben x pro Tag für fertig auch noch ein Checkmark, wenn ich % der Gesamtpuzzles passend
  // zum aktuellen Tag hab“ — Zyklus 2 über 14 Kalendertage, heute Tag 8; Satz 270 → Soll ⌈270 × 8 / 14⌉ = 155.
  describe('Woodpecker-Zeitplan im Plan-Kopf', () => {
    const midnightIn = (n: number) => { const d = new Date(); return new Date(d.getFullYear(), d.getMonth(), d.getDate() + n, 0, 0, 0).toISOString(); };
    const running = (attempted: number, over: Record<string, unknown> = {}) => wpPlan({
      setSize: 270,
      cycles: [
        { number: 1, attempted: 270, solved: 240, timeSeconds: 9000, startedAt: '', deadline: '', finishedAt: 'x' },
        { number: 2, attempted, solved: attempted, timeSeconds: 60, startedAt: midnightIn(-7), deadline: midnightIn(7), finishedAt: null },
      ],
      ...over,
    });
    const sched = (el: HTMLElement) => Array.from(el.querySelectorAll('.wp-meta .wsc > :not(.wsc-ok), .wp-meta .wsc-ok > *')).map(x => x.textContent!.trim());
    const inputs = (plan: unknown) => ({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inWoodpecker: true, woodpeckerPlan: plan });

    it('dahinter: „Zeitplan: noch …“ als letzte Angabe der Zyklus-Zeile, ohne Haken', () => {
      const { el } = make(inputs(running(143)));
      expect(sched(el)).toEqual(['woodpecker.schedule.label:', 'woodpecker.schedule.left']);   // ohne geladene Sprache = Schlüssel
      expect(el.querySelector('.wp-meta')!.lastElementChild?.tagName.toLowerCase()).toBe('app-woodpecker-schedule');
      expect(el.querySelector('.wp-meta .wsc-check')).toBeNull();
    });

    it('im Zeitplan: grüner Haken + „Im Zeitplan“ + Soll-Anteil (57 %)', () => {
      const { el } = make(inputs(running(155)));
      const parts = sched(el);
      expect(parts.slice(0, 2)).toEqual(['check_circle', 'woodpecker.schedule.onTrack']);
      expect(parts[2].replace(/\D+/g, '')).toBe('57');
      expect(el.querySelector('.wp-meta .wsc-done .wsc-check')?.getAttribute('aria-hidden')).toBe('true');
    });

    it('läuft mit: der Plan aus der Antwort auf die gemeldete Aufgabe schaltet auf „Im Zeitplan“', () => {
      const { el, fixture } = make(inputs(running(154)));
      expect(el.querySelector('.wp-meta .wsc-check')).toBeNull();
      fixture.componentRef.setInput('woodpeckerPlan', running(155));
      fixture.detectChanges();
      expect(el.querySelector('.wp-meta .wsc-done .wsc-check')).not.toBeNull();
    });

    it('kein Element ohne Zeitplan: Zyklus 1 — sonst stünde ein Trennpunkt ohne Angabe', () => {
      const p = wpPlan({ currentCycle: 1, setSize: 12, cycles: [
        { number: 1, attempted: 12, solved: 10, timeSeconds: 600, startedAt: midnightIn(-3), deadline: midnightIn(25), finishedAt: null },
      ] });
      const { el } = make(inputs(p));
      expect(el.querySelector('app-woodpecker-schedule')).toBeNull();
      expect(el.querySelector('.wp-meta')!.children.length).toBe(3);
    });

    it('außerhalb von Woodpecker nie', () => {
      const { el } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inCourse: true, woodpeckerPlan: running(155) });
      expect(el.querySelector('app-woodpecker-schedule')).toBeNull();
    });
  });

  it('ohne Woodpecker kein Statistik-Knopf', () => {
    const { el } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', inCourse: true });
    expect(el.querySelector('.wp-stats-open')).toBeNull();
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

  // 0.999.5: Sprachwahl, ⓘ und Kurswechsel stehen als EINE Gruppe .ctx-tools im Kopf; ist die Karte schmal, rutscht die
  // Gruppe rechtsbündig in eine eigene Zeile, statt den Titel auf ~90 px zusammenzudrücken (Kurs-Löser 0.999.3).
  describe('Kopf-Werkzeuge (.ctx-tools)', () => {
    const toolInputs = {
      hasPuzzle: true, state: 'AWAITING_USER_MOVE', inCourse: true, courseTotal: 5,
      titleDisplay: 'PATTERN 40 (#181) Deflecting the King’s Rook Protection with the Bishop',
      chapterDisplay: 'Pattern 40', bookName: '100 Tactical Patterns You Must Know',
      langEnabled: true, languages: ['en', 'de'], langValue: 'en',
      linkedCourseBookId: 12, linkedCourseName: '100 Tactical Patterns You Must Know — Workbook',
    };

    let box: HTMLElement | null = null;
    afterEach(() => { box?.remove(); box = null; });

    /** Rendert die Karte in einem Kasten fester Breite (der Host selbst ist display: contents). */
    function layout(width: number) {
      const r = make(toolInputs);
      box = document.createElement('div');
      box.style.width = `${width}px`;
      document.body.appendChild(box);
      box.appendChild(r.el);
      r.fixture.detectChanges();
      const text = r.el.querySelector('.ctx-head > .ctx-text') as HTMLElement;
      const tools = r.el.querySelector('.ctx-head > .ctx-tools') as HTMLElement;
      const head = r.el.querySelector('.ctx-head') as HTMLElement;
      return { text: text.getBoundingClientRect(), tools: tools.getBoundingClientRect(), head: head.getBoundingClientRect() };
    }

    it('fasst Sprachwahl, ⓘ und Kurswechsel in einer Gruppe neben dem Text', () => {
      const { el } = make(toolInputs);
      const tools = el.querySelector('.ctx-head > .ctx-tools')!;
      expect(tools.querySelector('app-course-lang-picker.ctx-lang')).not.toBeNull();
      expect(tools.querySelector('.ctx-stats-btn')).not.toBeNull();
      expect(tools.querySelector('button.ctx-switch')).not.toBeNull();
    });

    it('ohne Werkzeuge keine leere Gruppe', () => {
      const { el } = make({ hasPuzzle: true, state: 'AWAITING_USER_MOVE', titleDisplay: 'Linie 1' });
      expect(el.querySelector('.ctx-tools')).toBeNull();
    });

    it('schmale Karte (360 px): Gruppe in eigener Zeile, rechtsbündig, der Text behält die Breite', () => {
      const { text, tools, head } = layout(360);
      expect(tools.top).toBeGreaterThanOrEqual(text.bottom - 1);
      expect(Math.abs(tools.right - head.right)).toBeLessThan(2);
      expect(text.width).toBeGreaterThan(200);
    });

    it('breite Karte (900 px): alles in einer Zeile', () => {
      const { text, tools } = layout(900);
      expect(Math.abs(tools.top - text.top)).toBeLessThan(2);
    });
  });
});
