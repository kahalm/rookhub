import { nearestMaiaElo, readAnalysisParams } from './analysis-route';

describe('readAnalysisParams', () => {
  const read = (p: Record<string, string>, valid = true) => readAnalysisParams({ get: k => p[k] ?? null }, () => valid);

  it('übernimmt gültige Werte', () => {
    const r = read({ fen: 'F', orientation: 'black', from: '/puzzles/1', engine: 'eei_a', depth: '30', lines: '5',
      moves: 'e2e4, e7e5  g1f3', pgn: '1. e4', history: '7', blind: '1', maia: '2143', game: '/club-games/236' });
    expect(r).toEqual({ fen: 'F', orientation: 'black', returnTo: '/puzzles/1', engine: 'eei_a', depth: 30, lines: 5,
      moves: ['e2e4', 'e7e5', 'g1f3'], pgn: '1. e4', historyId: 7, blind: true, maia: 2200, gamePage: '/club-games/236' });
  });

  it('verwirft ungültige Werte', () => {
    const r = read({ fen: 'F', orientation: 'red', from: 'https://x', engine: 'a b', depth: '31', lines: '6', pgn: '  ',
      history: '0', maia: 'x', game: 'https://evil' }, false);
    expect(r).toEqual({ fen: null, orientation: null, returnTo: null, engine: null, depth: null, lines: null, moves: [],
      pgn: null, historyId: null, blind: false, maia: null, gamePage: null });
    expect(read({ from: '//evil' }).returnTo).toBeNull();
    expect(read({ game: '//evil' }).gamePage).toBeNull();
    expect(read({ game: '/\\evil' }).gamePage).toBeNull();
  });

  it('Maia-Stufe (0.892.0): nächste der 200er-Stufen, außerhalb geklemmt, Gleichstand nach oben', () => {
    expect(nearestMaiaElo(1800)).toBe(1800);
    expect(nearestMaiaElo(1899)).toBe(1800);
    expect(nearestMaiaElo(1900)).toBe(2000);
    expect(nearestMaiaElo(300)).toBe(600);
    expect(nearestMaiaElo(2850)).toBe(2600);
    expect(read({ maia: '0' }).maia).toBeNull();
  });
});
