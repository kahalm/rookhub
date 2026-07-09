import { parsedGameToPgn } from './repertoire-line-pgn.util';
import { parsePgnText } from '../../shared/pgn-viewer/pgn-parser';

describe('parsedGameToPgn', () => {
  it('round-trips moves + comments through the PGN parser', () => {
    const src = '[Event "x"]\n[White "?"]\n[Black "Najdorf"]\n\n1. e4 c5 2. Nf3 {develops} d6 *\n';
    const game = parsePgnText(src)[0];

    const pgn = parsedGameToPgn(game, { title: 'Sicilian Najdorf' });
    expect(pgn).toContain('[Event "Sicilian Najdorf"]');
    expect(pgn).toContain('[Black "Najdorf"]');

    const reparsed = parsePgnText(pgn)[0];
    expect(reparsed.moves.map(m => m.san)).toEqual(['e4', 'c5', 'Nf3', 'd6']);
    // Kommentar am Nf3-Zug (Index 2) bleibt erhalten.
    expect(reparsed.comments[2]).toContain('develops');
  });

  it('emits a FEN header + correct move numbering for a mid-game start', () => {
    // Start bei Schwarz am Zug, Vollzug 5.
    const fen = 'r1bqkbnr/pp1ppppp/2n5/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 2 5';
    const src = `[Event "x"]\n[White "?"]\n[Black "line"]\n[SetUp "1"]\n[FEN "${fen}"]\n\n5... d6 6. d4 cxd4 *\n`;
    const game = parsePgnText(src)[0];

    const pgn = parsedGameToPgn(game);
    expect(pgn).toContain('[FEN "' + fen + '"]');
    expect(pgn).toContain('5...');
    expect(pgn).toContain('6.');

    const reparsed = parsePgnText(pgn)[0];
    expect(reparsed.moves.map(m => m.san)).toEqual(['d6', 'd4', 'cxd4']);
  });

  it('escapes quotes in header values', () => {
    const game = parsePgnText('[Event "x"]\n[White "?"]\n[Black "?"]\n\n1. e4 *\n')[0];
    const pgn = parsedGameToPgn(game, { title: 'the "sharp" line' });
    expect(pgn).toContain('[Event "the \\"sharp\\" line"]');
  });
});
