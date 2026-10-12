import { EndlessHistoryComponent } from './endless-history.component';

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
