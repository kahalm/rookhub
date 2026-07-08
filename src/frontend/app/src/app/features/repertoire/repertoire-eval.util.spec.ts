import { parseWhiteEval } from './repertoire-eval.util';

describe('repertoire-eval.util parseWhiteEval', () => {
  it('parses a positive centipawn value', () => {
    expect(parseWhiteEval('1.5')).toEqual({ cp: 150, mateFor: null });
  });
  it('parses a negative centipawn value', () => {
    expect(parseWhiteEval('-0.8')).toEqual({ cp: -80, mateFor: null });
  });
  it('parses mate for white (#N → 100000 − N, mateFor w)', () => {
    expect(parseWhiteEval('#3')).toEqual({ cp: 100000 - 3, mateFor: 'w' });
  });
  it('parses mate for black (#-N → −100000 + N, mateFor b)', () => {
    expect(parseWhiteEval('#-2')).toEqual({ cp: -100000 + 2, mateFor: 'b' });
  });
  it('empty string → 0 cp, no mate', () => {
    expect(parseWhiteEval('')).toEqual({ cp: 0, mateFor: null });
  });
  it('unparsable string → 0 cp, no mate', () => {
    expect(parseWhiteEval('abc')).toEqual({ cp: 0, mateFor: null });
  });
});
