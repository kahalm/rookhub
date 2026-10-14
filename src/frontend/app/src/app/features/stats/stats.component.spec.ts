import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { Subject, of } from 'rxjs';
import { StatsComponent } from './stats.component';

describe('StatsComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [StatsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(StatsComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('laesst die Rating-Baender IM Container scrollen statt die Seite zu verbreitern', async () => {
    // Regression (Hochformat, gemeldet 2026-09-05): 200er-Baender ueber die ganze Rating-Spanne
    // ergeben 14–18 Saeulen. Ohne eigenes overflow-x wuchs die SEITE um rund zwei Bildschirme nach
    // rechts — und weil CDK-Overlays gegen das Dokument rechnen, landeten die Untermenues des
    // Hamburger-Menues ausserhalb des Sichtfelds. Der Test misst genau das: schmaler Host,
    // viele Baender, Dokument darf NICHT breiter werden.
    await TestBed.configureTestingModule({
      imports: [StatsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(StatsComponent);
    const host = fixture.nativeElement as HTMLElement;
    // Hochkant-Handy nachstellen (iPhone-Breite), damit die Messung nicht vom Karma-Fenster abhaengt.
    host.style.width = '390px';
    host.style.overflowX = 'hidden';   // wie ein Viewport: was rauslaeuft, wuerde hier scrollen

    // ERST rendern (dabei laeuft ngOnInit und setzt `loading` selbst auf true; die HTTP-Aufrufe
    // bleiben im Testing-Backend offen), DANN den Zustand setzen — umgekehrt ueberschreibt
    // ngOnInit die Testdaten und es rendert nur der Spinner.
    fixture.detectChanges();
    (fixture.componentInstance as unknown as { loading: boolean }).loading = false;
    fixture.componentInstance.ratingBands = Array.from({ length: 18 }, (_, i) => ({
      from: 400 + i * 200, to: 599 + i * 200, attempts: 120, solved: 1234,
    }));
    fixture.detectChanges();

    const bands = host.querySelector('.bands') as HTMLElement | null;
    expect(bands).withContext('Baender-Streifen gerendert').toBeTruthy();
    // Der Streifen selbst ist breiter als der Platz — genau deshalb MUSS er scrollen koennen …
    expect(bands!.scrollWidth).toBeGreaterThan(bands!.clientWidth);
    expect(getComputedStyle(bands!).overflowX).toBe('auto');
    // … und darf die Karte/den Container NICHT aufblaehen.
    const container = host.querySelector('.stats-container') as HTMLElement;
    expect(container.scrollWidth).toBeLessThanOrEqual(container.clientWidth + 1);
  });

  it('zeigt in BEIDEN Modi die Zahlen der jeweils gewaehlten Statistik (`current`)', async () => {
    // Die fuenf Kacheln lasen bis 0.499.5 fuenf einzelne Getter, jeder mit derselben
    // Modus-Weiche. Jetzt ist es EIN `current` — der Test haelt fest, dass das Umschalten
    // wirklich die andere Quelle zeigt und nicht etwa beide Modi dieselben Zahlen bekommen.
    await TestBed.configureTestingModule({
      imports: [StatsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();

    TestBed.inject(TranslateService).use('en');   // Prozent englisch „75%“ (e1-rk-percent-space-en)
    const fixture = TestBed.createComponent(StatsComponent);
    const c = fixture.componentInstance;
    fixture.detectChanges();
    (c as unknown as { loading: boolean }).loading = false;

    c.stats = {
      totalAttempts: 40, solved: 30, accuracy: 75, currentStreak: 3, bestStreak: 9, puzzleElo: 1620,
    };
    c.courseStats = {
      totalAttempts: 7, solved: 5, accuracy: 71.4, currentStreak: 1, bestStreak: 2,
    };

    c.mode = 'standard';
    fixture.detectChanges();
    expect(c.current).toBe(c.stats);
    let values = Array.from(fixture.nativeElement.querySelectorAll('.cards .stat .val')).map(e => (e as HTMLElement).textContent!.trim());
    // Im Standard-Modus steht die Elo-Kachel VOR den fuenf gemeinsamen.
    expect(values).toEqual(['1620', '30', '40', '75%', '3', '9']);

    c.mode = 'course';
    fixture.detectChanges();
    expect(c.current).toBe(c.courseStats);
    values = Array.from(fixture.nativeElement.querySelectorAll('.cards .stat .val')).map(e => (e as HTMLElement).textContent!.trim());
    expect(values).toEqual(['5', '7', '71.4%', '1', '2']);   // keine Elo-Kachel

    // Deutsch mit geschütztem Leerzeichen und Komma (DESIGN 5.13)
    TestBed.inject(TranslateService).use('de');
    fixture.detectChanges();
    values = Array.from(fixture.nativeElement.querySelectorAll('.cards .stat .val')).map(e => (e as HTMLElement).textContent!.trim());
    expect(values[2]).toBe('71,4\u00a0%');
  });

  // W5 F5-017: scheitert das Laden, zeigten die Karten Nullen und „–" wie bei einem neuen Konto.
  it('Ladefehler → Fehlerhinweis statt Null-Karten', async () => {
    await TestBed.configureTestingModule({
      imports: [StatsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(StatsComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    // forkJoin bricht die uebrigen Anfragen ab, sobald die erste scheitert.
    for (const r of http.match(() => true)) if (!r.cancelled) r.flush('x', { status: 500, statusText: 'err' });
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(fixture.componentInstance.loadError).toBeTrue();
    expect(el.querySelector('app-load-error')).not.toBeNull();
    expect(el.querySelector('.cards')).toBeNull();
  });

  // W5 F5-004: „Standard" (langsam) → „Kurs" — die spaete Standard-Antwort darf die Kurs-Tabelle nicht ueberschreiben.
  it('Moduswechsel: die spaete Antwort des vorigen Modus landet nicht unter dem neuen', () => {
    const std = { stats: new Subject<any>(), history: new Subject<any>(), elo: new Subject<any>(), breakdown: new Subject<any>() };
    const emptyBreakdown = { themes: [], ratingBands: [], activity: [] };
    const puzzles = {
      getStats: () => std.stats, getHistory: () => std.history, getEloHistory: () => std.elo, getBreakdown: () => std.breakdown,
      getCourseStats: () => of({ totalAttempts: 7, solved: 5, accuracy: 71.4, currentStreak: 1, bestStreak: 2 }),
      getCourseHistory: () => of([{ bookRating: 1500, solved: true, timeSeconds: 12, attemptedAt: '2026-09-30', bookPuzzleId: 77 }]),
      getCourseBreakdown: () => of(emptyBreakdown),
    };
    const c = new StatsComponent(puzzles as any, { visualization: 0 } as any);
    c.ngOnInit();
    c.mode = 'course';
    c.onModeChange();
    expect(std.stats.observed).toBeFalse();

    const late: [Subject<any>, unknown][] = [
      [std.stats, { totalAttempts: 40, solved: 30, accuracy: 75, currentStreak: 3, bestStreak: 9, puzzleElo: 1620 }],
      [std.history, [{ puzzleRating: 1800, solved: false, timeSpentSeconds: 30, attemptedAt: '2026-09-29', eloChange: -8, puzzleId: 5 }]],
      [std.elo, []],
      [std.breakdown, emptyBreakdown],
    ];
    for (const [s$, v] of late) { s$.next(v); s$.complete(); }

    expect(c.recentRows.map(r => r.link)).toEqual([['/puzzles/book', 77]]);
    expect(c.stats).toBeNull();
    expect(c.loading).toBeFalse();
  });

  it('scrollt die Heatmap nach rechts (heute sichtbar) und rendert die Handy-Liste der letzten Puzzles', async () => {
    // UI-Sweep 2026-10-11 (r2-rp-stats-mobile): am Handy lag der einzige aktive Tag (heute, rechts)
    // ausserhalb des Sichtbereichs; die Tabelle war abgeschnitten.
    await TestBed.configureTestingModule({
      imports: [StatsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(StatsComponent);
    const host = fixture.nativeElement as HTMLElement;
    host.style.width = '390px';
    fixture.detectChanges();
    const c = fixture.componentInstance as unknown as { loading: boolean; heatmap: unknown[][]; recentRows: unknown[] };
    c.loading = false;
    c.heatmap = Array.from({ length: 27 }, (_, w) =>
      Array.from({ length: 7 }, (_, d) => ({ date: `w${w}d${d}`, count: 0, level: 0 })));
    c.recentRows = [{ rating: 1490, solved: true, time: 64, attemptedAt: '2026-10-11T00:20:00Z', eloChange: 28, link: ['/puzzles', 1] }];
    fixture.detectChanges();
    await new Promise(r => requestAnimationFrame(() => r(null)));

    const hm = host.querySelector('.heatmap') as HTMLElement;
    expect(hm.scrollWidth).toBeGreaterThan(hm.clientWidth);
    expect(hm.scrollLeft).toBeGreaterThan(0);
    const item = host.querySelector('.recent-list a.recent-item') as HTMLAnchorElement;
    expect(item).toBeTruthy();
    expect(item.getAttribute('href')).toBe('/puzzles/1');
    expect(item.textContent).toContain('+28');
    // z2 (z2-rk-stats-letzte-liste): die Zahl ist als Puzzle-Wertung beschriftet, ein Pfeil zeigt die antippbare Zeile.
    expect(item.textContent).toContain('stats.puzzleRating');
    expect(item.querySelector('mat-icon.ri-go')?.textContent?.trim()).toBe('chevron_right');
  });
});

/** UI-Sweep z1: Handy-Regeln der Statistik greifen (Reihenfolge/Spezifität). */
describe('StatsComponent Layout (z1)', () => {
  const css = ((StatsComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/\s+/g, ' ');
  it('setzt die Handy-Breiten der Themenzeile NACH den Grundregeln (z1-rk-stats-themenbalken)', () => {
    const base = css.search(/\.theme-val[^{]*\{ ?width: 120px/);
    // z2 (z2-rk-stats-themenname): Name 128 px, Zahl nur so breit wie nötig.
    const phone = css.search(/\.theme-val[^{]*\{ ?width: auto; min-width: 64px/);
    expect(base).toBeGreaterThan(-1);
    expect(phone).toBeGreaterThan(base);
    expect(css.search(/\.theme-name[^{]*\{ ?width: 128px/)).toBeGreaterThan(css.search(/\.theme-name[^{]*\{ ?width: 150px/));
  });
  it('legt ab 1280 px „Nach Thema" und „Schwächste Themen" nebeneinander (z2-rk-stats-gross-einspaltig)', () => {
    const wide = css.slice(css.indexOf('@media (min-width: 1280px)'));
    expect(wide).toMatch(/\.theme-pair\.pair[^{]*\{ ?display: grid; grid-template-columns: 3fr 2fr/);
  });
  // UI-Sweep z3 (z3-rk-stats-seitenbreite): Seitenbreite wie die übrigen Inhaltsseiten, kein eigener 1400-px-Wert.
  it('nutzt die gemeinsame Seitenbreite (z3-rk-stats-seitenbreite)', () => {
    expect(css).toMatch(/\.stats-container[^{]*\{ ?max-width: min\(var\(--(%NS%)?page-max-width\), 96vw\)/);
    expect(css).not.toContain('1400px');
  });
  // UI-Sweep z3 (z3-rk-stats-heatmap-gross): ab 1280 px wachsen die Raster-Zellen mit.
  it('vergrößert die Aktivitäts-Zellen am großen Bildschirm (z3-rk-stats-heatmap-gross)', () => {
    const wide = css.slice(css.lastIndexOf('@media (min-width: 1280px)'));
    expect(wide).toMatch(/\.hm-cell[^{]*\{ ?width: 17px; height: 17px/);
  });
  // UI-Sweep u1 (u1-rk-stats-2spalten): ab 2400 px breiter und zweispaltig, Themen-Paar rechts neben dem Elo-Verlauf.
  it('wird ab 2400 px breit und zweispaltig (u1-rk-stats-2spalten)', () => {
    const at = css.indexOf('@media (min-width: 2400px)');
    expect(at).toBeGreaterThan(-1);
    const wide = css.slice(at);
    expect(wide).toMatch(/\.stats-container[^{]*\{ ?max-width: min\(var\(--(%NS%)?page-max-width-wide, 1880px\), 96vw\); display: grid; grid-template-columns: minmax\(0, 1fr\) minmax\(0, 1fr\); grid-auto-flow: row dense/);
    expect(wide).toMatch(/> ?\.bands-card[^{]*\{ ?grid-column: 1/);
    expect(wide).toMatch(/> ?\.theme-pair[^{]*\{ ?grid-column: 2; grid-row: span 3/);
    expect(css.slice(0, at)).not.toContain('page-max-width-wide');
  });
  // alles-voll-2: Umschalter nicht über die volle Breite gezogen; ohne Themen-Paar keine leere rechte Spalte.
  it('streckt ab 2400 px den Umschalter nicht und nutzt ohne Themen-Paar die volle Breite', () => {
    const wide = css.slice(css.indexOf('@media (min-width: 2400px)'));
    expect(wide).toMatch(/> ?\.mode-toggle[^{]*\{ ?justify-self: start/);
    expect(wide).toMatch(/:not\(:has\(> ?\.theme-pair[^)]*\)\) ?> ?\*[^{]*\{ ?grid-column: 1 ?\/ ?-1/);
  });
  it('nimmt den Kacheln den Karten-Außenabstand (z1-rk-stats-kacheln)', () => {
    expect(css).toMatch(/\.cards[^{]* mat-card[^{]*\{ ?margin: 0/);
  });
  it('gibt jedem Karteninhalt unter einem Titel denselben Abstand (z1-rk-stats-titelabstand)', () => {
    expect(css).toMatch(/mat-card-header[^{]*\+ ?mat-card-content[^{]*\{ ?padding-top: 8px/);
    expect(css).not.toMatch(/\.bands[^{]*\{[^}]*padding-top/);
  });
});

/** UI-Sweep a1: Leerzustand ohne Versuche und Hinweis nach dem automatischen Umschalten auf „Alle“. */
describe('StatsComponent a1 (Leerzustand, Level-Hinweis)', () => {
  function render() {
    TestBed.configureTestingModule({
      imports: [StatsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(StatsComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance as StatsComponent & { loading: boolean };
    c.loading = false;
    return { fixture, c, el: fixture.nativeElement as HTMLElement };
  }

  it('a1-rk-statistik-leer: ohne Versuch EINE Leerkarte mit „Puzzles lösen“ statt Verlauf, Level-Elo und Letzte Puzzles', () => {
    const { fixture, c, el } = render();
    c.stats = { totalAttempts: 0, solved: 0, accuracy: 0, currentStreak: 0, bestStreak: 0, puzzleElo: 1500 } as never;
    c.perLevel = [{ level: 0, elo: 1500 }, { level: 1, elo: 1400 }];
    fixture.detectChanges();
    expect(el.querySelector('.stats-empty a[href="/puzzles"]')).not.toBeNull();
    expect(el.querySelector('.chart-card')).toBeNull();
    expect(el.querySelector('.perlevel-card')).toBeNull();
    expect(el.querySelector('.recent-card')).toBeNull();

    c.stats = { totalAttempts: 3, solved: 2, accuracy: 66, currentStreak: 1, bestStreak: 2, puzzleElo: 1510 } as never;
    fixture.detectChanges();
    expect(el.querySelector('.stats-empty')).toBeNull();
    expect(el.querySelector('.chart-card')).not.toBeNull();
    expect(el.querySelector('.recent-card')).not.toBeNull();
  });

  it('a1-rk-statistik-level-hinweis: nach dem Ausweichen auf „Alle“ sagt der Hinweis, warum', () => {
    const { fixture, c, el } = render();
    c.stats = { totalAttempts: 3, solved: 2, accuracy: 66, currentStreak: 1, bestStreak: 2, puzzleElo: 1510 } as never;
    c.level = -1;
    c.levelHint = { level: 1, other: 0, count: 14 };
    fixture.detectChanges();
    expect(el.querySelector('.level-hint')!.textContent).toContain('stats.levelEmptySwitched');
    c.level = 1;
    fixture.detectChanges();
    expect(el.querySelector('.level-hint')!.textContent).toContain('stats.levelEmpty');
    expect(el.querySelector('.level-hint')!.textContent).not.toContain('stats.levelEmptySwitched');
  });
});

/** UI-Sweep b1: Kurs-Modus (Leerkarte, Aufgabentitel), Seitenrand, Aktivitäts-Raster im Breitbild. */
describe('StatsComponent b1', () => {
  function render() {
    TestBed.configureTestingModule({
      imports: [StatsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(StatsComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance as StatsComponent & { loading: boolean };
    c.loading = false;
    return { fixture, c, el: fixture.nativeElement as HTMLElement };
  }
  const css = ((StatsComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/\s+/g, ' ');

  it('b1-rk-stats-kurse-leer: Kurs-Modus ohne Versuch — EINE zentrierte Leerkarte mit „Zu den Kursen“, keine „Letzte Puzzles“', () => {
    const { fixture, c, el } = render();
    c.mode = 'course';
    c.courseStats = { totalAttempts: 0, solved: 0, accuracy: 0, currentStreak: 0, bestStreak: 0 } as never;
    c.recentRows = [];
    fixture.detectChanges();
    const cards = el.querySelectorAll('.stats-empty');
    expect(cards.length).toBe(1);
    expect(cards[0].querySelector('a[href="/courses"]')?.textContent?.trim()).toBe('stats.courseEmptyAction');
    expect(cards[0].textContent).toContain('stats.courseEmpty');
    expect(el.querySelector('.recent-card')).toBeNull();
    expect(getComputedStyle(cards[0].querySelector('mat-card-content')!).alignItems).toBe('center');
  });

  it('b1-rk-stats-kurse-wertung-leer: Kurs-Zeilen nennen die Aufgabe statt „Puzzle –“, Tabelle mit Spalte „Aufgabe“', () => {
    const history = new Subject<any>();
    const puzzles = {
      getCourseStats: () => of({ totalAttempts: 2, solved: 1, accuracy: 50, currentStreak: 0, bestStreak: 1 }),
      getCourseHistory: () => history,
      getCourseBreakdown: () => of({ themes: [], ratingBands: [], activity: [] }),
    };
    const c = new StatsComponent(puzzles as any, { visualization: 0 } as any);
    c.mode = 'course';
    c.load();
    history.next([
      { bookPuzzleId: 1, title: 'Exercise 245', solved: true, timeSeconds: 12, attemptedAt: '2026-10-11T00:20:00Z' },
      { bookPuzzleId: 2, title: '  ', solved: false, timeSeconds: 30, attemptedAt: '2026-10-11T00:19:00Z' },
    ]);
    history.complete();
    expect(c.recentRows.map(r => r.label)).toEqual(['Exercise 245', null]);
    expect(c.cols).toEqual(['date', 'task', 'result', 'time', 'open']);

    const { fixture, c: v, el } = render();
    v.mode = 'course';
    v.courseStats = { totalAttempts: 2, solved: 1, accuracy: 50, currentStreak: 0, bestStreak: 1 } as never;
    v.cols = c.cols;
    v.recentRows = c.recentRows;
    fixture.detectChanges();
    const items = Array.from(el.querySelectorAll('.recent-list .ri-title')).map(i => i.textContent!);
    expect(items[0]).toContain('Exercise 245');
    expect(items[1]).toContain('stats.courseTask');
    expect(items.join(' ')).not.toContain('stats.puzzleRating');
    expect(el.querySelector('.recent-table th')?.parentElement?.textContent).toContain('stats.task');
  });

  it('b1-rk-stats-kurse-wertung-leer: Wertungsspalte nur, wenn ein Kurs-Versuch eine hat', () => {
    const puzzles = {
      getCourseStats: () => of({ totalAttempts: 1, solved: 1, accuracy: 100, currentStreak: 1, bestStreak: 1 }),
      getCourseHistory: () => of([{ bookPuzzleId: 1, title: 'A', bookRating: 7, solved: true, timeSeconds: 5, attemptedAt: '2026-10-11' }]),
      getCourseBreakdown: () => of({ themes: [], ratingBands: [], activity: [] }),
    };
    const c = new StatsComponent(puzzles as any, { visualization: 0 } as any);
    c.mode = 'course';
    c.load();
    expect(c.cols).toEqual(['date', 'task', 'rating', 'result', 'time', 'open']);
  });

  it('b1-rk-statistik-rand-12: Seitenpolster 16 px wie die übrigen Seiten', () => {
    const { el } = render();
    expect(getComputedStyle(el.querySelector('.stats-container')!).paddingLeft).toBe('16px');
    expect(css).not.toMatch(/\.stats-container[^{]*\{[^}]*padding: 0 12px/);
  });

  it('b1-rk-stats-heatmap-ultrawide: ab 2400 px 28-px-Zellen (Raster füllt die 1 880-px-Karte), darunter unverändert', () => {
    const wide = css.slice(css.indexOf('@media (min-width: 2400px)'));
    expect(wide).toMatch(/\.hm-cell[^{]*\{ ?width: 28px; height: 28px/);
    expect(wide).toMatch(/\.heatmap[^{]*, ?\.hm-col[^{]*\{ ?gap: 6px/);
    const { fixture, c, el } = render();
    c.stats = { totalAttempts: 3, solved: 2, accuracy: 66, currentStreak: 1, bestStreak: 2, puzzleElo: 1510 } as never;
    c.heatmap = [[{ date: 'd', count: 1, level: 1 }]];
    fixture.detectChanges();
    expect(getComputedStyle(el.querySelector('.hm-cell')!).width).toBe('17px');   // Karma 1 400 px
  });
});
