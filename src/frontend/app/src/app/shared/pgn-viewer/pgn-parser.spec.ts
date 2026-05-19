import { parsePgnText, ParsedGame, START_FEN } from './pgn-parser';

const SINGLE_GAME = `[Event "Test"]
[White "Kasparov"]
[Black "Karpov"]
[Result "1-0"]

1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 1-0`;

const MULTI_GAME = `[Event "Game 1"]
[White "Player A"]
[Black "Player B"]
[Result "1-0"]

1. e4 e5 1-0

[Event "Game 2"]
[White "Player C"]
[Black "Player D"]
[Result "0-1"]

1. d4 d5 2. c4 e6 0-1`;

describe('parsePgnText', () => {
  it('should parse a single game', () => {
    const games = parsePgnText(SINGLE_GAME);
    expect(games.length).toBe(1);
    expect(games[0].headers['White']).toBe('Kasparov');
    expect(games[0].headers['Black']).toBe('Karpov');
    expect(games[0].moves.length).toBe(6);
  });

  it('should precompute FEN positions', () => {
    const games = parsePgnText(SINGLE_GAME);
    expect(games[0].fens.length).toBe(7);
    expect(games[0].fens[0]).toBe(START_FEN);
  });

  it('should parse multiple games', () => {
    const games = parsePgnText(MULTI_GAME);
    expect(games.length).toBe(2);
    expect(games[0].headers['White']).toBe('Player A');
    expect(games[1].headers['White']).toBe('Player C');
    expect(games[0].moves.length).toBe(2);
    expect(games[1].moves.length).toBe(4);
  });

  it('should handle empty string', () => {
    expect(parsePgnText('').length).toBe(0);
  });

  it('should skip invalid PGN', () => {
    expect(parsePgnText('not valid pgn %%%').length).toBe(0);
  });

  it('should skip invalid games in mixed input', () => {
    const mixed = SINGLE_GAME + '\n\n[Event "Bad"]\n\n1. Zz9 ???';
    const games = parsePgnText(mixed);
    expect(games.length).toBe(1);
  });
});

describe('START_FEN', () => {
  it('should be the standard starting position', () => {
    expect(START_FEN).toBe('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1');
  });
});
