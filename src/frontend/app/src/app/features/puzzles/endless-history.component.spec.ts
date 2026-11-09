import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { Injector, runInInjectionContext } from '@angular/core';
import { EndlessHistoryComponent, durationText } from './endless-history.component';
import { registerFormatLocaleData } from '../../core/locale-data';

/** Direkt gebaut (ohne TestBed); Snackbar/Übersetzung kommen per inject() aus einem Mini-Injector. */
function makeHistory(http: any, router: any, locale: any): EndlessHistoryComponent {
  const injector = Injector.create({ providers: [
    { provide: SnackbarService, useValue: { warn: () => {} } },
    { provide: TranslateService, useValue: { instant: (k: string) => k } },
  ] });
  return runInInjectionContext(injector, () => new EndlessHistoryComponent(http, router, locale));
}

// Die Elo-Delta-Methoden hängen nicht von HttpClient/Router/Locale ab → direkte Instanziierung reicht.
function makeComponent(): EndlessHistoryComponent {
  return makeHistory({} as any, {} as any, { current: 'en' } as any);
}

function session(maxRating: number, configJson: string): any {
  return { id: 1, timestamp: 0, totalSolved: 0, maxRating, durationSeconds: 0, configJson, mistakeAtRatings: '', isArchived: false };
}

describe('EndlessHistoryComponent Elo-Delta', () => {
  const c = makeComponent();

  it('zeigt positiven Aufstieg mit + an', () => {
    const s = session(1840, JSON.stringify({ startElo: 1500 }));
    expect(c.eloDelta(s)).toBe(340);
    expect(c.formatEloDelta(s)).toBe('+340');
    expect(c.eloDeltaClass(s)).toBe('elo-pos');
  });

  it('zeigt Verlust mit Minus an', () => {
    const s = session(1400, JSON.stringify({ startElo: 1500 }));
    expect(c.eloDelta(s)).toBe(-100);
    expect(c.formatEloDelta(s)).toBe('−100');
    expect(c.eloDeltaClass(s)).toBe('elo-neg');
  });

  it('zeigt ±0 ohne Farbe wenn unverändert', () => {
    const s = session(1500, JSON.stringify({ startElo: 1500 }));
    expect(c.formatEloDelta(s)).toBe('±0');
    expect(c.eloDeltaClass(s)).toBe('');
  });

  it('fällt bei fehlender/ungültiger Config auf - zurück', () => {
    expect(c.formatEloDelta(session(1500, 'nicht-json'))).toBe('-');
    expect(c.formatEloDelta(session(1500, JSON.stringify({})))).toBe('-');
    expect(c.eloDeltaClass(session(1500, 'nicht-json'))).toBe('');
  });
});

describe('EndlessHistoryComponent formatDate uses the active locale', () => {
  const ts = Date.UTC(2021, 2, 5, 12, 0); // 2021-03-05
  beforeAll(() => registerFormatLocaleData());

  it('formats with the locale from LocaleService (de uses dotted date, not US slashes)', () => {
    const de = makeHistory({} as any, {} as any, { current: 'de' } as any);
    expect(de.formatDate(ts)).toContain('.');
    expect(de.formatDate(ts)).not.toContain('/');
  });

  it('returns - for an invalid timestamp', () => {
    const en = makeHistory({} as any, {} as any, { current: 'en' } as any);
    expect(en.formatDate(NaN)).toBe('-');
  });
});

describe('EndlessHistoryComponent toRow precomputes display fields', () => {
  it('computes config/mistakes/elo text once instead of parsing JSON per CD cycle', () => {
    const c = makeHistory({} as any, {} as any, { current: 'en' } as any);
    const dto = {
      id: 1, timestamp: Date.UTC(2021, 2, 5, 12, 0), totalSolved: 5, maxRating: 1840,
      durationSeconds: 90, configJson: JSON.stringify({ startElo: 1500, fasttrackThreshold1: 3 }),
      mistakeAtRatings: '1600,1700', isArchived: false,
    };
    const row = (c as any).toRow(dto);
    // UI-Sweep z1 (z1-rk-endless-history-config): nur das Start-Rating, Schwellen getrennt (Tooltip).
    expect(row.configText).toBe('1500');
    expect(row.t1).toBe(3);
    expect(row.t2).toBeNull();
    expect(row.mistakesText).toBe('1600, 1700');
    expect(row.eloText).toBe('+340');   // 1840 - 1500
    expect(row.eloClass).toBe('elo-pos');
    expect(row.dateText).not.toBe('-');
    expect(row.dur).toEqual({ key: 'common.durationMinSec', m: 1, s: 30 });
  });
});

/** UI-Sweep b1 (b1-rk-endless-verlauf-dauer): Dauer über Übersetzungsschlüssel statt fest „21m 0s“. */
describe('EndlessHistoryComponent Dauer in der App-Sprache', () => {
  it('wählt Minuten+Sekunden bzw. nur Sekunden, ohne Wert null', () => {
    expect(durationText(1260)).toEqual({ key: 'common.durationMinSec', m: 21, s: 0 });
    expect(durationText(42)).toEqual({ key: 'common.durationSec', m: 0, s: 42 });
    expect(durationText(0)).toBeNull();
  });
});

describe('EndlessHistoryComponent Start-Rating ohne Wert (z1-rk-endless-history-config)', () => {
  it('zeigt „-“ statt „?“, wenn das Start-Rating fehlt', () => {
    const c = makeHistory({} as any, {} as any, { current: 'en' } as any);
    expect(c.formatConfig(JSON.stringify({ fasttrackThreshold1: 3 }))).toBe('-');
    expect(c.formatConfig('kaputt')).toBe('-');
    expect(c.formatConfig(JSON.stringify({ startElo: 1200 }))).toBe('1200');
  });
});

/** Kompilierte Komponenten-Styles (ohne die Seite samt Diensten aufzubauen). */
function css(): string {
  return ((EndlessHistoryComponent as any).ɵcmp.styles ?? []).join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
}

describe('EndlessHistoryComponent UI-Sweep a1', () => {
  beforeAll(() => registerFormatLocaleData());
  const ts = new Date(2026, 9, 10, 0, 20).getTime();

  it('a1-rk-endless-verlauf-tabelle-datum: knappes Locale-Format short statt langem Eigenformat', () => {
    const de = makeHistory({} as any, {} as any, { current: 'de' } as any);
    const en = makeHistory({} as any, {} as any, { current: 'en' } as any);
    expect(de.formatDate(ts)).toBe('10.10.26, 00:20');
    expect(en.formatDate(ts).replace(/\s/g, ' ')).toBe('10/10/26, 12:20 AM');
  });

  it('Sprache ohne Locale-Daten fällt auf en zurück statt zu werfen', () => {
    const fr = makeHistory({} as any, {} as any, { current: 'fr' } as any);
    expect(fr.formatDate(ts).replace(/\s/g, ' ')).toBe('10/10/26, 12:20 AM');
  });

  it('a1-rk-endless-verlauf-tabelle-datum: Zellen einzeilig, Tabelle scrollt im eigenen Kasten', () => {
    expect(css()).toMatch(/\.history-table[^{]*th[^{]*,\.history-table[^{]*td[^{]*\{white-space:nowrap/);
    expect(css()).toMatch(/\.desktop-only[^{]*\{overflow-x:auto/);
  });

  it('a1-rk-endless-verlauf-leer-kontrast: Leersatz im Sekundärtext-Token, Symbol 45 %', () => {
    expect(css()).toMatch(/\.empty-state[^{]*p[^{]*\{color:var\(--mat-sys-on-surface-variant\)/);
    expect(css()).toContain('currentColor45%');
  });

  it('a1-rk-endless-verlauf-handykarte: Beschriftung 0,75 rem in 65 %, Handy-Raster dreispaltig', () => {
    expect(css()).toMatch(/\.session-stat-label[^{]*\{font-size:0?\.75rem;color:color-mix\(insrgb,currentColor65%,transparent\)/);
    expect(css()).toMatch(/@media\(max-width:768px\)\{.*\.session-card-stats[^{]*\{grid-template-columns:repeat\(3,1fr\)/);
  });
});

/** Delta-Review 2026-11-09, F1-004: Ladefehler sah aus wie „Noch keine Läufe“, Archivieren scheiterte still. */
describe('EndlessHistoryComponent Fehlerpfade (F1-004)', () => {
  async function setup() {
    const snackbar = { warn: jasmine.createSpy('warn'), info: jasmine.createSpy('info') };
    await TestBed.configureTestingModule({
      imports: [EndlessHistoryComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: SnackbarService, useValue: snackbar },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(EndlessHistoryComponent);
    fixture.detectChanges();
    return { fixture, http: TestBed.inject(HttpTestingController), snackbar, el: fixture.nativeElement as HTMLElement };
  }
  const page = (items: any[]) => ({ items, totalCount: items.length, page: 1, pageSize: 20 });

  it('Ladefehler → app-load-error statt Leerzustand; Wiederholen lädt neu', async () => {
    const { fixture, http, el } = await setup();
    http.expectOne(r => r.url === '/api/endless/history').flush('x', { status: 500, statusText: 'err' });
    fixture.detectChanges();
    expect(el.querySelector('app-load-error')).not.toBeNull();
    expect(el.textContent).not.toContain('endless.history.noSessions');
    (el.querySelector('app-load-error button') as HTMLButtonElement).click();
    http.expectOne(r => r.url === '/api/endless/history').flush(page([]));
    fixture.detectChanges();
    expect(el.querySelector('app-load-error')).toBeNull();
    expect(el.textContent).toContain('endless.history.noSessions');
  });

  it('Archivieren scheitert → Warnung', async () => {
    const { fixture, http, snackbar } = await setup();
    const row = { id: 4, timestamp: 0, totalSolved: 1, maxRating: 900, durationSeconds: 5, configJson: '{}', mistakeAtRatings: '', isArchived: false };
    http.expectOne(r => r.url === '/api/endless/history').flush(page([row]));
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.selection.select(c.sessions[0]);
    c.archiveSelected(true);
    http.expectOne('/api/endless/archive').flush('x', { status: 500, statusText: 'err' });
    expect(snackbar.warn).toHaveBeenCalledWith('endless.history.archiveFailed');
  });
});
