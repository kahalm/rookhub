import { longMoveLabel, moveKey, parseMoveInput } from './similar-move.util';

describe('similar-move.util', () => {
  // Italiener (Vier-Springer-artig), Weiß am Zug: Springer auf c3 und f3, Rochade möglich —
  // genug Stoff für Disambiguierung (`Ncd5`), Rochade und Läufer-vs-b-Linie.
  const italian = 'r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/2N2N2/PPPP1PPP/R1BQK2R w KQkq - 6 5';

  it('resolves SAN on the anchor position to from/to', () => {
    const r = parseMoveInput(italian, 'Nd5');
    expect(r.invalid).toBeFalse();
    expect(r.move).toEqual({ from: 'c3', to: 'd5' });
    expect(r.san).toBe('Nd5');
  });

  it('reads a differently disambiguated SAN as the SAME move (Ncd5 = Nd5)', () => {
    const a = parseMoveInput(italian, 'Nd5');
    const b = parseMoveInput(italian, 'Ncd5');
    expect(b.move).toEqual(a.move!);
    expect(moveKey(b.move)).toBe(moveKey(a.move));
    expect(b.san).toBe('Nd5');            // kanonisch, nicht wie getippt
  });

  it('accepts what a human types: captures, check marks, 0-0, lower-case piece letters', () => {
    expect(parseMoveInput(italian, 'Nxe5').move).toEqual({ from: 'f3', to: 'e5' });
    expect(parseMoveInput(italian, 'Ng5+').move).toEqual({ from: 'f3', to: 'g5' });
    expect(parseMoveInput(italian, ' Nd5 ').move).toEqual({ from: 'c3', to: 'd5' });
    expect(parseMoveInput(italian, 'O-O').move).toEqual({ from: 'e1', to: 'g1' });
    expect(parseMoveInput(italian, '0-0').move).toEqual({ from: 'e1', to: 'g1' });
    expect(parseMoveInput(italian, 'nd5').move).toEqual({ from: 'c3', to: 'd5' });
    expect(parseMoveInput(italian, 'd3').move).toEqual({ from: 'd2', to: 'd3' });
  });

  it('keeps the b-file/bishop ambiguity working in both readings', () => {
    // 'b4' ist der Bauernzug, 'be2'/'Be2' der Läuferzug — beides muss durchkommen.
    expect(parseMoveInput(italian, 'b4').move).toEqual({ from: 'b2', to: 'b4' });
    expect(parseMoveInput(italian, 'be2').move).toEqual({ from: 'c4', to: 'e2' });
  });

  it('carries the promotion piece over', () => {
    const promo = '8/P7/8/8/8/8/8/K6k w - - 0 1';
    expect(parseMoveInput(promo, 'a8=Q').move).toEqual({ from: 'a7', to: 'a8', promotion: 'q' });
    expect(parseMoveInput(promo, 'a8=N').move).toEqual({ from: 'a7', to: 'a8', promotion: 'n' });
    // Umwandlung ohne '=' — genau die Schreibweise, die im August still danebenging.
    expect(parseMoveInput(promo, 'a8Q').move).toEqual({ from: 'a7', to: 'a8', promotion: 'q' });
    expect(moveKey(parseMoveInput(promo, 'a8=Q').move)).not.toBe(moveKey(parseMoveInput(promo, 'a8=N').move));
  });

  it('reports an illegal move as invalid instead of silently returning nothing', () => {
    const r = parseMoveInput(italian, 'Nb6');   // kein Springer kann nach b6
    expect(r.move).toBeNull();
    expect(r.san).toBe('');
    expect(r.invalid).toBeTrue();
    expect(parseMoveInput(italian, 'zzz').invalid).toBeTrue();
    expect(parseMoveInput(italian, 'N').invalid).toBeTrue();       // Tippen unterwegs
  });

  it('an empty field is not an error', () => {
    for (const t of ['', '   ']) {
      const r = parseMoveInput(italian, t);
      expect(r.move).toBeNull();
      expect(r.invalid).toBeFalse();
    }
  });

  it('a position chess.js refuses (pattern diagram without kings) counts as "not legal here"', () => {
    const r = parseMoveInput('8/8/4p3/8/8/8/8/8 w - - 0 1', 'e4');
    expect(r.move).toBeNull();
    expect(r.invalid).toBeTrue();
  });

  it('the same SAN means a different move on a different anchor', () => {
    const afterNg5 = 'r1bqk2r/pppp1ppp/2n2n2/2b1p1N1/2B1P3/2N5/PPPP1PPP/R1BQK2R b KQkq - 7 5';
    expect(parseMoveInput(italian, 'Nd4').move).toEqual({ from: 'f3', to: 'd4' });     // Weiß
    expect(parseMoveInput(afterNg5, 'Nd4').move).toEqual({ from: 'c6', to: 'd4' });    // Schwarz
  });

  it('moveKey separates from, to and promotion', () => {
    expect(moveKey(null)).toBe('');
    expect(moveKey({ from: 'c3', to: 'd5' })).toBe('c3d5');
    expect(moveKey({ from: 'c3', to: 'd5' })).not.toBe(moveKey({ from: 'f3', to: 'd5' }));
    expect(moveKey({ from: 'a7', to: 'a8', promotion: 'q' })).toBe('a7a8q');
  });

  it('longMoveLabel keeps the piece letter and names the departure square', () => {
    expect(longMoveLabel('Nd5', 'f3', 'd5')).toBe('Nf3-d5');
    expect(longMoveLabel('Nd5', 'c3', 'd5')).toBe('Nc3-d5');
    expect(longMoveLabel('exd5', 'e4', 'd5')).toBe('e4-d5');   // Bauer: kein Buchstabe
    expect(longMoveLabel('O-O', 'e1', 'g1')).toBe('e1-g1');    // Rochade: auch keiner
    expect(longMoveLabel('Nd5', '', '')).toBe('Nd5');          // ohne Felder bleibt der SAN
  });
});
