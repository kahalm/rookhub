import { classifyStandardFirstMove } from './puzzle-hints.util';

describe('classifyStandardFirstMove', () => {
  it('erkennt einen ruhigen Zug', () => {
    // Startstellung; moves[0]=e2e4 (Setup), moves[1]=e7e5 (Löserzug, ruhig)
    const h = classifyStandardFirstMove(
      'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', 'e2e4 e7e5');
    expect(h).toEqual({ type: 'quiet', pieceType: 'p', san: 'e5' });
  });

  it('erkennt einen Schlagzug', () => {
    // weiße Bauer auf e4, schwarzer auf d5; Setup Nb1-c3, dann d5xe4
    const h = classifyStandardFirstMove(
      'rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2', 'b1c3 d5e4');
    expect(h?.type).toBe('capture');
    expect(h?.pieceType).toBe('p');
    expect(h?.san).toContain('x');
  });

  it('erkennt ein Schach (Vorrang vor Schlag)', () => {
    // Setup Ke8-d8 (schwarz), dann Rf1-f8+ (weiß) — Schach
    const h = classifyStandardFirstMove('4k3/8/8/8/8/8/8/4KR2 b - - 0 1', 'e8d8 f1f8');
    expect(h?.type).toBe('check');
    expect(h?.pieceType).toBe('r');
    expect(h?.san).toContain('+');
  });

  it('liefert null bei zu kurzer Zugliste', () => {
    expect(classifyStandardFirstMove('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', 'e2e4')).toBeNull();
  });
});
