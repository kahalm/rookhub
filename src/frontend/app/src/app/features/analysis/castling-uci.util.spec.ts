import { normalizeCastlingUci } from './castling-uci.util';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
// Weiß am Zug, beide Seiten dürfen kurz rochieren.
const READY_TO_CASTLE = 'rnbqk2r/pppp1ppp/5n2/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4';
// Turm auf e1, König auf g1 (schon rochiert): e1h1 ist hier ein legaler TURM-Zug.
const ROOK_ON_E1 = '4k3/8/8/8/8/8/8/4R1K1 w - - 0 1';

describe('normalizeCastlingUci', () => {
  it('rewrites king-takes-rook castling to the standard two-square form', () => {
    expect(normalizeCastlingUci(READY_TO_CASTLE, ['e1h1'])).toEqual(['e1g1']);
    expect(normalizeCastlingUci(READY_TO_CASTLE, ['e1g1'])).toEqual(['e1g1']);   // schon normal
  });

  it('rewrites black castling later in the variation', () => {
    // 1. O-O (als e1h1) O-O (als e8h8)
    expect(normalizeCastlingUci(READY_TO_CASTLE, ['e1h1', 'e8h8'])).toEqual(['e1g1', 'e8g8']);
  });

  it('leaves a genuine rook move e1h1 untouched (no blind string replacement)', () => {
    expect(normalizeCastlingUci(ROOK_ON_E1, ['e1h1'])).toEqual(['e1h1']);
  });

  it('passes through variations without castling unchanged (identity, no copy)', () => {
    const moves = ['e2e4', 'e7e5', 'g1f3'];
    expect(normalizeCastlingUci(START, moves)).toBe(moves);
  });

  it('keeps the remainder unchanged when the line stops being replayable', () => {
    // 2. Zug illegal → ab da Rohform, aber der erste Zug ist korrekt normalisiert.
    expect(normalizeCastlingUci(READY_TO_CASTLE, ['e1h1', 'a1a1', 'e8h8'])).toEqual(['e1g1', 'a1a1', 'e8h8']);
  });

  it('survives an invalid fen and an empty line', () => {
    expect(normalizeCastlingUci('not a fen', ['e1h1'])).toEqual(['e1h1']);
    expect(normalizeCastlingUci(START, [])).toEqual([]);
  });

  it('normalises queenside castling too', () => {
    const fen = 'r3kbnr/pppqpppp/2np4/8/3P4/2N1B3/PPPQPPPP/R3KBNR w KQkq - 6 5';
    expect(normalizeCastlingUci(fen, ['e1a1'])).toEqual(['e1c1']);
  });
});
