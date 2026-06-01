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

describe('annotated PGN', () => {
  const ANNOTATED_PGN = `[Event "Repertoire"]
[White "Nimzo-Indian"]
[Black "Guide"]
[FEN "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"]
[Result "*"]

{[%tqu "En","find the move","","","d2d4","",10]} 1. d4 Nf6 2. c4 e6 3. Nc3 {Comment.} Bb4 4. Qc2 O-O 5. e4 ({A)} 5.a3 Bxc3+ 6.Qxc3) ({B)} 5.Nf3) c5 6. e5 (6.a3 Bxc3+ 7.bxc3) Ne8 *`;

  it('should parse PGN with RAV variations', () => {
    const games = parsePgnText(ANNOTATED_PGN);
    expect(games.length).toBe(1);
    // Main line: 1.d4 Nf6 2.c4 e6 3.Nc3 Bb4 4.Qc2 O-O 5.e4 c5 6.e5 Ne8 = 12 half-moves
    expect(games[0].moves.length).toBe(12);
    expect(games[0].moves[0].san).toBe('d4');
    expect(games[0].moves[11].san).toBe('Ne8');
  });

  it('should strip NAG symbols', () => {
    const pgn = `[Event "Test"]
[Result "*"]

1. e4 e5 2. Nf3 $1 Nc6 $14 *`;
    const games = parsePgnText(pgn);
    expect(games.length).toBe(1);
    expect(games[0].moves.length).toBe(4);
  });

  it('should handle nested variations', () => {
    const pgn = `[Event "Test"]
[Result "*"]

1. e4 (1. d4 d5 (1...Nf6 2. c4)) e5 2. Nf3 *`;
    const games = parsePgnText(pgn);
    expect(games.length).toBe(1);
    expect(games[0].moves.length).toBe(3);
    expect(games[0].moves[0].san).toBe('e4');
    expect(games[0].moves[1].san).toBe('e5');
    expect(games[0].moves[2].san).toBe('Nf3');
  });
});

describe('comments', () => {
  it('should extract comments and associate with moves', () => {
    const pgn = `[Event "Test"]
[Result "*"]

1. e4 {Best move} e5 {Solid reply} 2. Nf3 *`;
    const games = parsePgnText(pgn);
    expect(games.length).toBe(1);
    expect(games[0].comments[0]).toBe('Best move');
    expect(games[0].comments[1]).toBe('Solid reply');
    expect(games[0].comments[2]).toBeUndefined();
  });

  it('should strip Chessbase annotations from comments', () => {
    const pgn = `[Event "Test"]
[Result "*"]

1. e4 {[%csl Ge4]A strong move.} e5 {[%cal Re5e4]} *`;
    const games = parsePgnText(pgn);
    expect(games[0].comments[0]).toBe('A strong move.');
    expect(games[0].comments[1]).toBeUndefined(); // only annotation, no text
  });

  it('should handle comment before first move', () => {
    const pgn = `[Event "Test"]
[Result "*"]

{Starting comment} 1. e4 e5 *`;
    const games = parsePgnText(pgn);
    expect(games[0].comments[-1]).toBe('Starting comment');
  });

  it('should return empty comments for PGN without comments', () => {
    const games = parsePgnText(SINGLE_GAME);
    expect(Object.keys(games[0].comments).length).toBe(0);
  });
});

describe('START_FEN', () => {
  it('should be the standard starting position', () => {
    expect(START_FEN).toBe('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1');
  });
});

describe('parsePgnText input limits', () => {
  it('caps the number of parsed games (no unbounded work)', () => {
    const game = '[Event "G"]\n\n1. e4 e5 *';
    const many = Array.from({ length: 600 }, () => game).join('\n\n');
    const games = parsePgnText(many);
    expect(games.length).toBe(500); // MAX_GAMES
  });

  it('skips a single pathologically large game instead of freezing', () => {
    const huge = '[Event "X"]\n\n{' + 'a'.repeat(200_001) + '} 1. e4 *';
    const games = parsePgnText(huge);
    expect(games.length).toBe(0);
  });
});
