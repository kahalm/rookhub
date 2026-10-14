import { EndlessHistoryComponent, durationText } from './endless-history.component';
import { registerFormatLocaleData } from '../../core/locale-data';

// Die Elo-Delta-Methoden hängen nicht von HttpClient/Router/Locale ab → direkte Instanziierung reicht.
function makeComponent(): EndlessHistoryComponent {
  return new EndlessHistoryComponent({} as any, {} as any, { current: 'en' } as any);
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
    const de = new EndlessHistoryComponent({} as any, {} as any, { current: 'de' } as any);
    expect(de.formatDate(ts)).toContain('.');
    expect(de.formatDate(ts)).not.toContain('/');
  });

  it('returns - for an invalid timestamp', () => {
    const en = new EndlessHistoryComponent({} as any, {} as any, { current: 'en' } as any);
    expect(en.formatDate(NaN)).toBe('-');
  });
});

describe('EndlessHistoryComponent toRow precomputes display fields', () => {
  it('computes config/mistakes/elo text once instead of parsing JSON per CD cycle', () => {
    const c = new EndlessHistoryComponent({} as any, {} as any, { current: 'en' } as any);
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
    const c = new EndlessHistoryComponent({} as any, {} as any, { current: 'en' } as any);
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
    const de = new EndlessHistoryComponent({} as any, {} as any, { current: 'de' } as any);
    const en = new EndlessHistoryComponent({} as any, {} as any, { current: 'en' } as any);
    expect(de.formatDate(ts)).toBe('10.10.26, 00:20');
    expect(en.formatDate(ts).replace(/\s/g, ' ')).toBe('10/10/26, 12:20 AM');
  });

  it('Sprache ohne Locale-Daten fällt auf en zurück statt zu werfen', () => {
    const fr = new EndlessHistoryComponent({} as any, {} as any, { current: 'fr' } as any);
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
