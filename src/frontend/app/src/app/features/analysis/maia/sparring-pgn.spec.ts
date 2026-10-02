import { buildSparringPgn, SparringGame } from './sparring-pgn';

/** Literale Erwartungen: die GANZE Zeichenkette — genau so geht sie an `POST /api/games/import`. */
describe('buildSparringPgn', () => {
  const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  /** Zweispringerspiel nach 4.O-O — Schwarz am Zug. */
  const BLACK_TO_MOVE = 'r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQ1RK1 b kq - 5 4';
  const game = (over: Partial<SparringGame>): SparringGame => ({
    startFen: START, sans: [], userColor: 'white', userName: 'anna', elo: 1600,
    date: new Date(2026, 9, 2, 23, 30), ...over,
  });

  it('start position, user White: no FEN header, result *', () => {
    expect(buildSparringPgn(game({ sans: ['e4', 'e5', 'Nf3'] }))).toBe(
      '[Event "Sparring vs Maia"]\n[Site "RookHub"]\n[Date "2026.10.02"]\n[Round "-"]\n'
      + '[White "anna"]\n[Black "Maia 1600"]\n[Result "*"]\n\n1. e4 e5 2. Nf3 *');
  });

  it('from a FEN with Black to move: SetUp/FEN in the header, move text in the compact form „4... Bc5"', () => {
    expect(buildSparringPgn(game({ startFen: BLACK_TO_MOVE, sans: ['Bc5', 'c3', 'd6'], elo: 2000 }))).toBe(
      '[Event "Sparring vs Maia"]\n[Site "RookHub"]\n[Date "2026.10.02"]\n[Round "-"]\n'
      + '[White "anna"]\n[Black "Maia 2000"]\n[Result "*"]\n[SetUp "1"]\n'
      + '[FEN "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQ1RK1 b kq - 5 4"]\n\n4... Bc5 5. c3 d6 *');
  });

  it('user Black: names swapped', () => {
    expect(buildSparringPgn(game({ sans: ['e4', 'c5'], userColor: 'black', userName: 'bert', elo: 1200 }))).toBe(
      '[Event "Sparring vs Maia"]\n[Site "RookHub"]\n[Date "2026.10.02"]\n[Round "-"]\n'
      + '[White "Maia 1200"]\n[Black "bert"]\n[Result "*"]\n\n1. e4 c5 *');
  });

  it('checkmate: the side to move has lost (Scholar\'s mate 1-0, Fool\'s mate 0-1)', () => {
    expect(buildSparringPgn(game({ sans: ['e4', 'e5', 'Bc4', 'Nc6', 'Qh5', 'Nf6', 'Qxf7#'] }))).toBe(
      '[Event "Sparring vs Maia"]\n[Site "RookHub"]\n[Date "2026.10.02"]\n[Round "-"]\n'
      + '[White "anna"]\n[Black "Maia 1600"]\n[Result "1-0"]\n\n1. e4 e5 2. Bc4 Nc6 3. Qh5 Nf6 4. Qxf7# 1-0');
    expect(buildSparringPgn(game({ sans: ['f3', 'e5', 'g4', 'Qh4#'], userColor: 'black' }))).toBe(
      '[Event "Sparring vs Maia"]\n[Site "RookHub"]\n[Date "2026.10.02"]\n[Round "-"]\n'
      + '[White "Maia 1600"]\n[Black "anna"]\n[Result "0-1"]\n\n1. f3 e5 2. g4 Qh4# 0-1');
  });

  it('stalemate: 1/2-1/2', () => {
    expect(buildSparringPgn(game({ startFen: '7k/4Q3/6K1/8/8/8/8/8 w - - 0 1', sans: ['Qf7'] }))).toBe(
      '[Event "Sparring vs Maia"]\n[Site "RookHub"]\n[Date "2026.10.02"]\n[Round "-"]\n'
      + '[White "anna"]\n[Black "Maia 1600"]\n[Result "1/2-1/2"]\n[SetUp "1"]\n'
      + '[FEN "7k/4Q3/6K1/8/8/8/8/8 w - - 0 1"]\n\n1. Qf7 1/2-1/2');
  });

  it('an illegal move, an unreadable position or no moves → null', () => {
    expect(buildSparringPgn(game({ sans: ['e4', 'e5', 'Ke3'] }))).toBeNull();
    expect(buildSparringPgn(game({ startFen: 'kein FEN', sans: ['e4'] }))).toBeNull();
    expect(buildSparringPgn(game({ sans: [] }))).toBeNull();
  });

  it('no name → „Player"; characters that would break the header fall away; the date is the LOCAL one, padded', () => {
    expect(buildSparringPgn(game({ sans: ['d4'], userName: '  ', date: new Date(2026, 0, 5, 0, 10) }))).toBe(
      '[Event "Sparring vs Maia"]\n[Site "RookHub"]\n[Date "2026.01.05"]\n[Round "-"]\n'
      + '[White "Player"]\n[Black "Maia 1600"]\n[Result "*"]\n\n1. d4 *');
    expect(buildSparringPgn(game({ sans: ['d4'], userName: 'a"n]n\\a' }))).toContain('[White "anna"]');
  });
});
