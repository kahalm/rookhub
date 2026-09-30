import { DEFAULT_TREE_FILTER, TREE_SPEEDS, effectiveTreeFilter, normalizeTreeFilter, toggleSpeed } from './tree-filter';

describe('tree-filter', () => {
  it('Tempo-Kürzel wie am Server (LeagueOnlineSync.Speeds)', () => {
    expect(TREE_SPEEDS.map(s => s.key)).toEqual(['bullet', 'blitz', 'rapid', 'classical', 'correspondence']);
  });

  it('normalisiert Gespeichertes und Unsinn', () => {
    expect(normalizeTreeFilter(null)).toEqual(DEFAULT_TREE_FILTER);
    expect(normalizeTreeFilter('x')).toEqual(DEFAULT_TREE_FILTER);
    expect(normalizeTreeFilter({ source: 'both', speeds: ['rapid', 'blitz', 'hyper'], years: 5, withUnsure: true }))
      .toEqual({ source: 'both', speeds: ['blitz', 'rapid'], years: 5, withUnsure: true });
    expect(normalizeTreeFilter({ source: 'alles', speeds: 'blitz', years: 4, withUnsure: 'ja' }))
      .toEqual({ source: 'board', speeds: [], years: null, withUnsure: false });
    // Gespeichert vor 0.612.0 (onlySure): unsichere bleiben draussen, die neue Vorgabe.
    expect(normalizeTreeFilter({ source: 'both', onlySure: false }).withUnsure).toBeFalse();
  });

  it('wirksame Quelle hängt an den Partienzahlen', () => {
    const both = { ...DEFAULT_TREE_FILTER, source: 'both' as const };
    expect(effectiveTreeFilter(both, 10, 0).source).toBe('board');           // keine Online-Partien
    expect(effectiveTreeFilter(DEFAULT_TREE_FILTER, 0, 5).source).toBe('online');   // keine Brettpartien
    expect(effectiveTreeFilter(both, 0, 5).source).toBe('both');
    expect(effectiveTreeFilter(DEFAULT_TREE_FILTER, 10, 5).source).toBe('board');
  });

  it('Tempo an/ab; alle einzeln = alle', () => {
    expect(toggleSpeed([], 'rapid')).toEqual(['rapid']);
    expect(toggleSpeed(['rapid'], 'blitz')).toEqual(['blitz', 'rapid']);
    expect(toggleSpeed(['blitz', 'rapid'], 'blitz')).toEqual(['rapid']);
    expect(toggleSpeed(['bullet', 'blitz', 'rapid', 'classical'], 'correspondence')).toEqual([]);
  });
});
