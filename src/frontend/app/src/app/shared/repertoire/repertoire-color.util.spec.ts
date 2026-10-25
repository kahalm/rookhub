import {
  autoChapterColors, readChapterColorOverrides, resolveChapterColors, rootSideOf,
  setChapterColorOverride, sideOfLastMove,
} from './repertoire-color.util';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('repertoire-color.util', () => {
  it('rootSideOf reads the side to move', () => {
    expect(rootSideOf(START)).toBe('w');
    expect(rootSideOf('8/8/8/8/8/8/8/8 b - - 0 1')).toBe('b');
  });

  it('sideOfLastMove: parity from the root side', () => {
    expect(sideOfLastMove(START, 0)).toBeNull();
    expect(sideOfLastMove(START, 1)).toBe('w');   // e4
    expect(sideOfLastMove(START, 2)).toBe('b');   // e4 e5
    expect(sideOfLastMove(START, 3)).toBe('w');   // e4 e5 Nf3
    expect(sideOfLastMove(START, 4)).toBe('b');   // e4 e6 d4 d5
  });

  it('autoChapterColors: majority of last-move sides per chapter', () => {
    const auto = autoChapterColors([
      { chapter: 'White', side: 'w', rootSide: 'w' },
      { chapter: 'White', side: 'b', rootSide: 'w' },   // 2:1 → Weiß
      { chapter: 'White', side: 'w', rootSide: 'w' },
      { chapter: 'Black', side: 'b', rootSide: 'w' },
      { chapter: 'Black', side: 'b', rootSide: 'w' },
    ]);
    expect(auto.get('White')).toBe('w');
    expect(auto.get('Black')).toBe('b');
  });

  it('autoChapterColors: tie falls back to the opposite of the root side', () => {
    const auto = autoChapterColors([
      { chapter: 'Caro', side: 'w', rootSide: 'w' },
      { chapter: 'Caro', side: 'b', rootSide: 'w' },   // 1:1 → Fallback = Gegenseite von Weiß = Schwarz
    ]);
    expect(auto.get('Caro')).toBe('b');
  });

  describe('overrides (localStorage)', () => {
    afterEach(() => localStorage.removeItem('rookhub_rep_train_chaptercolor_42'));

    it('reads back a written override, ignoring garbage values', () => {
      setChapterColorOverride(42, 'French', 'b');
      localStorage.setItem('rookhub_rep_train_chaptercolor_42',
        JSON.stringify({ French: 'b', Bogus: 'x' }));
      const ovr = readChapterColorOverrides(42);
      expect(ovr['French']).toBe('b');
      expect(ovr['Bogus']).toBeUndefined();
    });

    it('resolveChapterColors: overrides win over auto-detection', () => {
      const auto = new Map<string, 'w' | 'b'>([['Caro', 'b'], ['Sicilian', 'w']]);
      setChapterColorOverride(42, 'Caro', 'w');
      const resolved = resolveChapterColors(42, auto);
      expect(resolved.get('Caro')).toBe('w');      // Override
      expect(resolved.get('Sicilian')).toBe('w');  // unverändert aus Auto
    });

    it('returns {} on missing / corrupt storage', () => {
      expect(readChapterColorOverrides(999)).toEqual({});
      localStorage.setItem('rookhub_rep_train_chaptercolor_42', '{not json');
      expect(readChapterColorOverrides(42)).toEqual({});
    });
  });
});
