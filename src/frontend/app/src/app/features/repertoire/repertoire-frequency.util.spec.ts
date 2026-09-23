import { MAX_STORED_POSITIONS, StoredFrequencies, mergeFrequencies, readRepertoireFrequencies, sameSelection, saveRepertoireFrequencies } from './repertoire-frequency.util';
import { DEFAULT_EXPLORER_SETTINGS } from './repertoire-explorer.service';

function freq(positions: Record<string, number>): StoredFrequencies {
  return { savedAt: '2026-09-23T10:00:00Z', source: 'local', database: 'masters', ratings: [], speeds: [], complete: true, positions };
}

describe('repertoire-frequency.util', () => {
  afterEach(() => localStorage.removeItem('rookhub_rep_freq_9'));

  it('round trip, rounded to four digits', () => {
    expect(saveRepertoireFrequencies(9, freq({ a: 0.123456789, b: 1 }))).toBeTrue();
    const f = readRepertoireFrequencies(9)!;
    expect(f.positions).toEqual({ a: 0.1235, b: 1 });
    expect(f.source).toBe('local');
  });

  it('keeps only the most frequent positions of a huge repertoire', () => {
    const many: Record<string, number> = {};
    for (let i = 0; i < MAX_STORED_POSITIONS + 50; i++) many['p' + i] = (i + 1) / 100000;
    saveRepertoireFrequencies(9, freq(many));
    const kept = readRepertoireFrequencies(9)!.positions;
    expect(Object.keys(kept).length).toBe(MAX_STORED_POSITIONS);
    expect(kept['p0']).toBeUndefined();   // der seltenste fällt raus
  });

  it('nothing stored or junk → null', () => {
    expect(readRepertoireFrequencies(9)).toBeNull();
    localStorage.setItem('rookhub_rep_freq_9', '{kaputt');
    expect(readRepertoireFrequencies(9)).toBeNull();
    localStorage.setItem('rookhub_rep_freq_9', '{"savedAt":1}');
    expect(readRepertoireFrequencies(9)).toBeNull();
  });

  it('merges into the same selection, starts over for another one', () => {
    const local = { ...DEFAULT_EXPLORER_SETTINGS, source: 'local' as const, database: 'masters' as const };
    const base = freq({ a: 0.5 });
    expect(sameSelection(base, local)).toBeTrue();
    expect(mergeFrequencies(base, local, { b: 0.1 }).positions).toEqual({ a: 0.5, b: 0.1 });

    const online = { ...local, source: 'online' as const };
    expect(sameSelection(base, online)).toBeFalse();
    const fresh = mergeFrequencies(base, online, { b: 0.1 });
    expect(fresh.positions).toEqual({ b: 0.1 });
    expect(fresh.source).toBe('online');

    // Lichess: Elo und Tempo gehören zur Auswahl, bei Meistern nicht.
    const lichess = { ...local, database: 'lichess' as const, ratings: [1800], speeds: ['blitz'] };
    const lichessSet = mergeFrequencies(null, lichess, {});
    expect(sameSelection(lichessSet, { ...lichess, ratings: [2000] })).toBeFalse();
    expect(sameSelection(base, { ...local, ratings: [2500] })).toBeTrue();
  });
});
