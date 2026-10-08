import { blackOf, boardFromPlacement, castlingOf, movePiece, placePiece, removePiece, composeFen, formatGames, formatShare, materialProblem, parseFenInput, placementOf, positionProblem, START_PLACEMENT } from './position-setup';

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

  it('Figurenzahl: drei Springer (gesetzt statt gezogen), neun Bauern; Umwandlung erlaubt Überzählige je fehlendem Bauern', () => {
    const p = (fen: string) => positionProblem(boardFromPlacement(fen)!, 'w');
    // Prod-Befund 08.10.: Sf3 gesetzt, Sg1 nicht entfernt
    expect(p('rnbqkbnr/pp3ppp/4p3/2pp4/4P3/2P2N2/PP1P1PPP/RNBQKBNR'))
      .toBe('Weiß hat 3 Springer — in der Grundstellung sind es 2; Figur wegnehmen oder ziehen statt setzen.');
    expect(p('rnbqkbnr/pp3ppp/4p3/2pp4/4P3/2P2N2/PP1P1PPP/RNBQKB1R')).toBeNull();
    expect(p('4k3/8/8/8/P7/8/PPPPPPPP/4K3')).toContain('Weiß hat 9 Bauern');
    expect(p('4k3/pppppppp/p7/8/8/8/8/4K3')).toContain('Schwarz hat 9 Bauern');
    // Dame + ein fehlender Bauer = zwei Damen erlaubt, zwei Extra-Damen nicht
    expect(p('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPP1/RNBQKBNQ')).toBeNull();
    expect(p('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPP1/RQBQKBNQ')).toContain('Weiß hat 3 Damen');
    expect(p('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPP1/RQBQKBNQ')).toContain('es fehlt nur 1 Bauer');
    expect(materialProblem(boardFromPlacement('4k3/8/8/8/8/8/8/QQQQK3')!)).toBeNull();   // keine Bauern: bis zu 8 Extras
  });

  it('Ziehen/Setzen/Entfernen: reine Funktionen, Ziel wird ersetzt, Ausgangsfeld = Abbruch', () => {
    const b = boardFromPlacement(START_PLACEMENT)!;
    const moved = movePiece(b, 52, 36);   // e2 → e4
    expect(placementOf(moved)).toBe('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR');
    expect(placementOf(b)).toBe(START_PLACEMENT);   // Eingabe bleibt unverändert
    const takes = movePiece(b, 59, 11);  // Dd1 auf d7 ersetzt den Bauern
    expect(takes[11]).toBe('Q');
    expect(takes[59]).toBe('');
    expect(placementOf(movePiece(b, 52, 52))).toBe(START_PLACEMENT);   // zurück aufs Ausgangsfeld
    expect(placementOf(movePiece(b, 36, 20))).toBe(START_PLACEMENT);   // leeres Ausgangsfeld
    expect(removePiece(b, 0)[0]).toBe('');
    const placed = placePiece(b, 27, 'n');
    expect(placed[27]).toBe('n');
    // zweiter König derselben Farbe ersetzt den ersten
    const king = placePiece(b, 36, 'K');
    expect(king[36]).toBe('K');
    expect(king[60]).toBe('');
    expect(king.filter(p => p === 'K').length).toBe(1);
    expect(blackOf('Q')).toBe('q');
    expect(blackOf('q')).toBe('q');
    expect(blackOf('x')).toBe('x');
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
