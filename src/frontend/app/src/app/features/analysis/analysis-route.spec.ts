import { readAnalysisParams } from './analysis-route';

describe('readAnalysisParams', () => {
  const read = (p: Record<string, string>, valid = true) => readAnalysisParams({ get: k => p[k] ?? null }, () => valid);

  it('übernimmt gültige Werte', () => {
    const r = read({ fen: 'F', orientation: 'black', from: '/puzzles/1', engine: 'eei_a', depth: '30', lines: '5',
      moves: 'e2e4, e7e5  g1f3', pgn: '1. e4', history: '7' });
    expect(r).toEqual({ fen: 'F', orientation: 'black', returnTo: '/puzzles/1', engine: 'eei_a', depth: 30, lines: 5,
      moves: ['e2e4', 'e7e5', 'g1f3'], pgn: '1. e4', historyId: 7 });
  });

  it('verwirft ungültige Werte', () => {
    const r = read({ fen: 'F', orientation: 'red', from: 'https://x', engine: 'a b', depth: '31', lines: '6', pgn: '  ',
      history: '0' }, false);
    expect(r).toEqual({ fen: null, orientation: null, returnTo: null, engine: null, depth: null, lines: null, moves: [],
      pgn: null, historyId: null });
    expect(read({ from: '//evil' }).returnTo).toBeNull();
  });
});
