import { boardFromPlacement, castlingOf, composeFen, formatGames, formatShare, parseFenInput, placementOf, positionProblem, START_PLACEMENT } from './position-setup';

describe('position-setup', () => {
  it('Platzierung hin und zurück', () => {
    const b = boardFromPlacement(START_PLACEMENT)!;
    expect(b.length).toBe(64);
    expect(b[0]).toBe('r');
    expect(b[60]).toBe('K');
    expect(placementOf(b)).toBe(START_PLACEMENT);
    expect(boardFromPlacement('8/8/8')).toBeNull();
    expect(boardFromPlacement('9/8/8/8/8/8/8/8')).toBeNull();
    expect(boardFromPlacement('rnbqkbnrr/8/8/8/8/8/8/8')).toBeNull();
  });

  it('Rochaderechte aus der Stellung, en passant „-"', () => {
    const b = boardFromPlacement('r3k2r/8/8/8/8/8/8/R3K1R1')!;
    expect(castlingOf(b)).toBe('Qkq');
    expect(composeFen(b, 'b')).toBe('r3k2r/8/8/8/8/8/8/R3K1R1 b Qkq - 0 1');
    expect(castlingOf(boardFromPlacement('4k3/8/8/8/8/8/8/4K3')!)).toBe('-');
  });

  it('FEN-Eingabe: nur die Platzierung ist Pflicht', () => {
    expect(parseFenInput('4k3/8/8/8/8/8/8/4K3')?.side).toBe('w');
    expect(parseFenInput('  4k3/8/8/8/8/8/8/4K3 b - - 0 1 ')?.side).toBe('b');
    expect(parseFenInput('')).toBeNull();
    expect(parseFenInput('x')).toBeNull();
  });

  it('Gültigkeit: Könige, Bauern auf der Grundreihe, Schach der Seite, die nicht am Zug ist', () => {
    const p = (fen: string, side: 'w' | 'b' = 'w') => positionProblem(boardFromPlacement(fen)!, side);
    expect(p(START_PLACEMENT)).toBeNull();
    expect(p('8/8/8/8/8/8/8/4K3')).toContain('König');
    expect(p('4k3/8/8/8/8/8/8/3KK3')).toContain('König');
    expect(p('4k2p/8/8/8/8/8/8/4K3')).toContain('Reihe');
    expect(p('4k3/8/8/8/8/8/8/4R1K1', 'w')).toContain('Schwarz steht im Schach');
    expect(p('4k3/8/8/8/8/8/8/4R1K1', 'b')).toBeNull();
    expect(p('4k3/4r3/8/8/8/8/8/4K3', 'b')).toContain('Weiß steht im Schach');
  });

  it('Partien und Anteil deutsch gerundet', () => {
    expect(formatGames(2773801)).toBe('≈ 2,8 Mio. Partien');
    expect(formatGames(73905)).toBe('≈ 74.000 Partien');
    expect(formatGames(1234)).toBe('≈ 1.200 Partien');
    expect(formatGames(640)).toBe('≈ 640 Partien');
    expect(formatGames(0.4)).toBe('≈ 1 Partie');
    expect(formatShare(0.894)).toBe('89,4 %');
    expect(formatShare(0.0004)).toBe('< 0,1 %');
    expect(formatShare(0)).toBe('0,0 %');
  });
});
