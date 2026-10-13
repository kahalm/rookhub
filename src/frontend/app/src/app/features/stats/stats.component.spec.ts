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
  it('nimmt den Kacheln den Karten-Außenabstand (z1-rk-stats-kacheln)', () => {
    expect(css).toMatch(/\.cards[^{]* mat-card[^{]*\{ ?margin: 0/);
  });
  it('gibt jedem Karteninhalt unter einem Titel denselben Abstand (z1-rk-stats-titelabstand)', () => {
    expect(css).toMatch(/mat-card-header[^{]*\+ ?mat-card-content[^{]*\{ ?padding-top: 8px/);
    expect(css).not.toMatch(/\.bands[^{]*\{[^}]*padding-top/);
  });
});
