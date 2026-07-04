import { parseAltMoves } from './alt-moves.util';

describe('parseAltMoves', () => {
  it('parses a ply→uci-list map', () => {
    const m = parseAltMoves('{"2":["c2d3"],"4":["d2d1r","d2d1q"]}');
    expect(m[2]).toEqual(['c2d3']);
    expect(m[4]).toEqual(['d2d1r', 'd2d1q']);
  });

  it('returns {} for null/undefined/empty', () => {
    expect(parseAltMoves(null)).toEqual({});
    expect(parseAltMoves(undefined)).toEqual({});
    expect(parseAltMoves('')).toEqual({});
  });

  it('returns {} for malformed JSON', () => {
    expect(parseAltMoves('{not json')).toEqual({});
  });

  it('skips non-array values, non-integer keys and too-short UCIs', () => {
    const m = parseAltMoves('{"1":"c2d3","x":["a1a2"],"3":["", "e2e4"]}');
    expect(m[1]).toBeUndefined();      // value not an array
    expect(m['x' as unknown as number]).toBeUndefined();
    expect(m[3]).toEqual(['e2e4']);    // empty string dropped
  });
});
