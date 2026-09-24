import { Chess } from 'chess.js';
import {
  attackersOf, capturedValue, exchangeGain, inCheck, isPieceHanging, isPromotion, legalCapturersOf, pieceValue, sacrificedPiece,
  uciOf,
} from './move-tactics.util';

// Alle Stellungen sind LITERALE, mit chess.js 1.4 nachgespielt (Zugfolge steht jeweils dabei) — keine
// Stellung wird im Test erst erzeugt, sonst prüfte der Test den Zuggenerator statt der Regel.

/** 1.e4 e6 2.d4 d5 3.Sc3 Sf6 4.e5 Sfd7 5.Sf3 Le7 6.Ld3 0-0 — und 7.Lxh7+ (Griechisches Geschenk). */
const GREEK_BEFORE = 'rnbq1rk1/pppnbppp/4p3/3pP3/3P4/2NB1N2/PPP2PPP/R1BQK2R w KQ - 5 7';
const GREEK_AFTER = 'rnbq1rk1/pppnbppB/4p3/3pP3/3P4/2N2N2/PPP2PPP/R1BQK2R b KQ - 0 7';
/** Dasselbe gespiegelt mit Schwarz am Zug: 1.d4 d5 2.e3 Sf6 3.Ld3 e6 4.Se2 Ld6 5.0-0 — und 5…Lxh2+. */
const GREEK_BLACK_BEFORE = 'rnbqk2r/ppp2ppp/3bpn2/3p4/3P4/3BP3/PPP1NPPP/RNBQ1RK1 b kq - 3 5';
const GREEK_BLACK_AFTER = 'rnbqk2r/ppp2ppp/4pn2/3p4/3P4/3BP3/PPP1NPPb/RNBQ1RK1 w kq - 0 6';
/** Spanisch, Abtauschvariante: 1.e4 e5 2.Sf3 Sc6 3.Lb5 a6 — und 4.Lxc6. */
const RUY_BEFORE = 'r1bqkbnr/1ppp1ppp/p1n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 4';
const RUY_AFTER = 'r1bqkbnr/1ppp1ppp/p1B5/4p3/4P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 0 4';
/** Legall: 1.e4 e5 2.Sf3 Sc6 3.Lc4 d6 4.Sc3 Lg4 5.h3 Lh5 — und 6.Sxe5 (die Dame auf d1 bleibt stehen). */
const LEGALL_BEFORE = 'r2qkbnr/ppp2ppp/2np4/4p2b/2B1P3/2N2N1P/PPPP1PP1/R1BQK2R w KQkq - 1 6';
const LEGALL_AFTER = 'r2qkbnr/ppp2ppp/2np4/4N2b/2B1P3/2N4P/PPPP1PP1/R1BQK2R b KQkq - 0 6';

describe('move-tactics.util', () => {
  it('Figurenwerte: Bauer 1, Leichtfiguren 3, Turm 5, Dame 9, König unendlich', () => {
    expect(pieceValue('p')).toBe(1);
    expect(pieceValue('n')).toBe(3);
    expect(pieceValue('b')).toBe(3);
    expect(pieceValue('r')).toBe(5);
    expect(pieceValue('q')).toBe(9);
    expect(pieceValue('k')).toBe(Infinity);
  });

  it('attackersOf: Angreifer einer Farbe samt Figurenart, der König zählt mit', () => {
    const after = new Chess(GREEK_AFTER);
    expect(attackersOf(after, 'h7', 'b')).toEqual([{ square: 'g8', type: 'k' }]);
    expect(attackersOf(after, 'h7', 'w')).toEqual([]);
  });

  describe('isPieceHanging (nach freechess board.ts)', () => {
    it('Griechisches Geschenk: Lxh7+ — nur der König greift an, niemand deckt → hängt', () => {
      expect(isPieceHanging(GREEK_BEFORE, GREEK_AFTER, 'h7')).toBeTrue();
    });

    it('Abtausch Lxc6: vorher stand dort eine gleichwertige gegnerische Figur → hängt nicht, obwohl b7/d7 angreifen', () => {
      expect(isPieceHanging(RUY_BEFORE, RUY_AFTER, 'c6')).toBeFalse();
    });

    it('der König darf eine gedeckte Dame nicht schlagen: Ke8 + De7 gegen den Lc4 → nur die Dame greift legal an → hängt nicht', () => {
      // 1.e4 e5 2.Lc4 De7 3.Dh5 Sc6 4.Dxf7+: f7 greifen Ke8 und De7 an, gedeckt nur vom Lc4. Mit
      // Pseudo-Angriffen wäre das eine Überzahl (2 gegen 1) — legal ist Kxf7 nicht, und Dxf7 Lxf7 ist ein
      // Damentausch, kein Opfer.
      expect(isPieceHanging(
        'r1b1kbnr/ppppqppp/2n5/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4',
        'r1b1kbnr/ppppqQpp/2n5/4p3/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 0 4', 'f7')).toBeFalse();
    });

    it('Abzugsschach: der Bauer e6 greift f5 an, darf aber nicht schlagen, weil der Td1 Schach gibt → hängt nicht', () => {
      // Sd4–f5+ öffnet die d-Linie; Schwarz muss das Schach beheben, exf5 tut das nicht.
      const before = '3k4/8/4p3/8/3N4/8/8/3R2K1 w - - 0 1';
      const after = '3k4/8/4p3/5N2/8/8/8/3R2K1 b - - 1 1';
      expect(legalCapturersOf(new Chess(after), 'f5')).toEqual([]);
      expect(attackersOf(new Chess(after), 'f5', 'b')).toEqual([{ square: 'e6', type: 'p' }]);
      expect(isPieceHanging(before, after, 'f5')).toBeFalse();
      expect(sacrificedPiece(before, after, 'd4f5')).toBeNull();
    });

    it('gefesselter Angreifer: der Ld7 greift c6 an, ist aber durch den Td1 an den Kd8 gefesselt → hängt nicht', () => {
      // Sb4–c6+: der Läufer dürfte schlagen, stünde er nicht in der d-Linie zwischen Turm und König.
      const before = '3k4/3b4/8/8/1N6/8/8/3R2K1 w - - 0 1';
      const after = '3k4/3b4/2N5/8/8/8/8/3R2K1 b - - 1 1';
      expect(attackersOf(new Chess(after), 'c6', 'b')).toEqual([{ square: 'd7', type: 'b' }]);
      expect(legalCapturersOf(new Chess(after), 'c6')).toEqual([]);
      expect(isPieceHanging(before, after, 'c6')).toBeFalse();
    });

    it('Schäfermatt Dxf7: der König allein gegen eine gedeckte Dame → hängt nicht', () => {
      // 1.e4 e5 2.Lc4 Sc6 3.Dh5 Sf6 4.Dxf7#: ein Angreifer (König), ein Verteidiger (Lc4) — keine Überzahl.
      expect(isPieceHanging(
        'r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4',
        'r1bqkb1r/pppp1Qpp/2n2n2/4p3/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 0 4', 'f7')).toBeFalse();
    });

    it('mehr Angreifer als Verteidiger, aber ein Bauer deckt → hängt nicht (der Bauer wäre das Opfer)', () => {
      // Se5 greifen Sc6 und De7 an (keiner billiger als der Springer), gedeckt vom Bauern d4.
      expect(isPieceHanging(
        'r1b1kbnr/ppppqppp/2n5/8/3P4/5N2/PPP2PPP/RNBQKB1R w KQkq - 0 5',
        'r1b1kbnr/ppppqppp/2n5/4N3/3P4/8/PPP2PPP/RNBQKB1R b KQkq - 1 5', 'e5')).toBeFalse();
    });

    it('mehr Angreifer, aber die Figur ist billiger als jeder Angreifer und ein billigerer Verteidiger steht bereit → hängt nicht', () => {
      // Sd5: Td8 und Ta5 greifen an (Wert 5), der Lc4 (Wert 3) deckt — Txd5 Lxd5 verlöre die Qualität.
      expect(isPieceHanging('3r3k/8/8/r7/2B5/2N5/8/6K1 w - - 0 1', '3r3k/8/8/r2N4/2B5/8/8/6K1 b - - 1 1', 'd5'))
        .toBeFalse();
    });

    it('Angreifer mit kleinerem Wert: der Springer greift die Dame an → hängt, auch ohne Überzahl', () => {
      // 1.e4 Sf6 2.Dh5.
      expect(isPieceHanging(
        'rnbqkb1r/pppppppp/5n2/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 1 2',
        'rnbqkb1r/pppppppp/5n2/7Q/4P3/8/PPPP1PPP/RNB1KBNR b KQkq - 2 2', 'h5')).toBeTrue();
    });

    it('Turm-Ausnahme: Txf6 gegen ein Feld, das genau EINE Leichtfigur deckt → hängt nicht', () => {
      expect(isPieceHanging('7k/4b3/5n2/8/8/8/6PP/5RK1 w - - 0 1', '7k/4b3/5R2/8/8/8/6PP/6K1 b - - 0 1', 'f6'))
        .toBeFalse();
    });

    it('dasselbe mit dem Bauern g7 als einzigem Verteidiger → hängt (Qualitätsopfer)', () => {
      expect(isPieceHanging('7k/6p1/5n2/8/8/8/6PP/5RK1 w - - 0 1', '7k/6p1/5R2/8/8/8/6PP/6K1 b - - 0 1', 'f6'))
        .toBeTrue();
    });

    it('leeres Feld und unlesbare Stellung: hängt nicht, statt zu werfen', () => {
      expect(isPieceHanging(GREEK_BEFORE, GREEK_AFTER, 'd3')).toBeFalse();
      expect(isPieceHanging('kaputt', GREEK_AFTER, 'h7')).toBeFalse();
      expect(isPieceHanging(GREEK_BEFORE, '8/8/8/8/8/8/8/8 w - - 0 1', 'h7')).toBeFalse();
    });
  });

  describe('sacrificedPiece', () => {
    it('Griechisches Geschenk: der Läufer auf h7 ist das Opfer', () => {
      expect(sacrificedPiece(GREEK_BEFORE, GREEK_AFTER, 'd3h7')).toEqual({ square: 'h7', piece: 'b' });
    });

    it('Schwarz am Zug: 5…Lxh2+ opfert den Läufer auf h2', () => {
      expect(sacrificedPiece(GREEK_BLACK_BEFORE, GREEK_BLACK_AFTER, 'd6h2')).toEqual({ square: 'h2', piece: 'b' });
    });

    it('Abtausch Lxc6 ist kein Opfer', () => {
      expect(sacrificedPiece(RUY_BEFORE, RUY_AFTER, 'b5c6')).toBeNull();
    });

    it('das teuerste hängende Stück zählt — auch eines, das nicht gezogen hat (Legall: die Dame auf d1)', () => {
      // Der Springer auf e5 hängt ebenfalls (d6, Sc6), die Dame ist mehr wert.
      expect(isPieceHanging(LEGALL_BEFORE, LEGALL_AFTER, 'e5')).toBeTrue();
      expect(sacrificedPiece(LEGALL_BEFORE, LEGALL_AFTER, 'f3e5')).toEqual({ square: 'd1', piece: 'q' });
    });

    it('Qualitätsopfer Txf6 gegen den Bauern g7 ist ein Opfer (Turm 5 > Springer 3)', () => {
      expect(sacrificedPiece('7k/6p1/5n2/8/8/8/6PP/5RK1 w - - 0 1', '7k/6p1/5R2/8/8/8/6PP/6K1 b - - 0 1', 'f1f6'))
        .toEqual({ square: 'f6', piece: 'r' });
    });

    it('eine hängende Figur, die nicht mehr wert ist als die geschlagene, ist kein Opfer (Sxd8 lässt Lf5 stehen)', () => {
      const before = '3q3k/1N6/4p3/5B2/8/8/8/6K1 w - - 0 1';
      const after = '3N3k/8/4p3/5B2/8/8/8/6K1 b - - 0 1';
      expect(isPieceHanging(before, after, 'f5')).toBeTrue();
      expect(sacrificedPiece(before, after, 'b7d8')).toBeNull();
    });

    // Gemeldet 2026-09-24 (Prod-Partie MYXN3hXqz1X2hm7Cx6V47Q, chess.com: kein Brilliant): 22.Lxb7 gabelt Ta8 und
    // Lc8, Schwarz schlägt 22…gxf4 den Läufer. Der Ta8 „hängt" (Läufer 3 < Turm 5), ist aber von der Db8 gedeckt:
    // nach Lxa8 Dxa8 kostet er die Qualität (2) — weniger als der Läufer, den Schwarz gerade genommen hat.
    it('22…gxf4: ein gedeckter Turm, der nur die Qualität kostet, ist kein Opfer für einen geschlagenen Läufer', () => {
      const before = 'rqb4r/1B2k2p/4pp2/3pn1p1/5B2/2P5/PR2QPPP/1R4K1 b - - 0 22';
      const after = 'rqb4r/1B2k2p/4pp2/3pn3/5p2/2P5/PR2QPPP/1R4K1 w - - 0 23';
      expect(isPieceHanging(before, after, 'a8')).toBeTrue();
      expect(sacrificedPiece(before, after, 'g5f4')).toBeNull();
    });

    // Dieselbe Partie, 23.Lxd5: der Läufer „hängt" gegen exd5, aber der Zug öffnet die b-Linie — nach exd5 nimmt
    // Tb2 die Db8. Ein Köder, kein Opfer (freechess' Schlag-Simulation; bis 0.518.0 hier Brilliant).
    it('23.Lxd5: ein Stück, dessen Schlagen die eigene Dame kostet, ist kein Opfer', () => {
      const before = 'rqb4r/1B2k2p/4pp2/3pn3/5p2/2P5/PR2QPPP/1R4K1 w - - 0 23';
      const after = 'rqb4r/4k2p/4pp2/3Bn3/5p2/2P5/PR2QPPP/1R4K1 b - - 0 23';
      expect(isPieceHanging(before, after, 'd5')).toBeTrue();
      expect(sacrificedPiece(before, after, 'b7d5')).toBeNull();
    });

    it('ein Leichtfiguren-„Opfer", dessen Schlagen Matt in einem Zug erlaubt, ist eine Mattdrohung, kein Opfer', () => {
      // Sb5-d4 lässt den Springer vor dem Td8 stehen; Txd4 verlässt die Grundreihe → Te8#.
      const before = '3r2k1/5ppp/8/1N6/8/8/5PPP/4R1K1 w - - 0 1';
      const after = '3r2k1/5ppp/8/8/3N4/8/5PPP/4R1K1 b - - 1 1';
      expect(isPieceHanging(before, after, 'd4')).toBeTrue();
      expect(sacrificedPiece(before, after, 'b5d4')).toBeNull();
      // Ohne den Te1 gibt es kein Matt — dann ist es ein (schlechtes) Opfer; ob es gut ist, sagt die Engine.
      expect(sacrificedPiece('3r2k1/5ppp/8/1N6/8/8/5PPP/6K1 w - - 0 1', '3r2k1/5ppp/8/8/3N4/8/5PPP/6K1 b - - 1 1', 'b5d4'))
        .toEqual({ square: 'd4', piece: 'n' });
    });

    it('ohne Schlagen ist schon die Qualität ein Opfer: gedeckter Turm, den ein Läufer angreift', () => {
      // Kc1-b1 lässt den Td1 stehen, den der Lf3 angreift; gedeckt nur von der Dd2 → Lxd1 Dxd1 kostet 2 > 0.
      const before = '6k1/8/8/8/8/5b2/3Q4/2KR4 w - - 0 1';
      const after = '6k1/8/8/8/8/5b2/3Q4/1K1R4 b - - 1 1';
      expect(sacrificedPiece(before, after, 'c1b1')).toEqual({ square: 'd1', piece: 'r' });
    });

    it('unlesbare Stellung → kein Opfer, statt zu werfen', () => {
      expect(sacrificedPiece('kaputt', GREEK_AFTER, 'd3h7')).toBeNull();
      expect(sacrificedPiece(GREEK_BEFORE, '8/8/8/8/8/8/8/8 w - - 0 1', 'd3h7')).toBeNull();
    });
  });

  describe('exchangeGain (Abtausch auf einem Feld, ohne Röntgen)', () => {
    it('ungedeckt: der volle Wert', () => {
      expect(exchangeGain(5, [3], [])).toBe(5);
    });
    it('gedeckt, billigerer Angreifer: die Differenz', () => {
      expect(exchangeGain(5, [3], [9])).toBe(2);
      expect(exchangeGain(9, [3], [Infinity])).toBe(6);   // Legall: Lxd1 Kxd1
    });
    it('gedeckt, gleich teurer oder teurerer Angreifer: nichts', () => {
      expect(exchangeGain(3, [3], [3])).toBe(0);
      expect(exchangeGain(3, [5], [1])).toBe(0);
    });
    it('der König schlägt nur, was niemand mehr deckt', () => {
      expect(exchangeGain(3, [Infinity], [])).toBe(3);
      expect(exchangeGain(3, [Infinity], [1])).toBe(0);
    });
    it('Überzahl: jede Seite hört auf, sobald weiterschlagen Material kostet', () => {
      // Turm gegen Turm + Dame, nur von der Dame gedeckt: nach Txt schlägt die Dame NICHT zurück (Dxd kostete sie
      // die Dame) — der Angreifer gewinnt den Turm, nicht mehr.
      expect(exchangeGain(5, [5, 9], [9])).toBe(5);
      // Leichtfigur gegen zwei Leichtfiguren, von einer gedeckt: Sxs, Sxs, Sxs — am Ende eine Figur mehr.
      expect(exchangeGain(3, [3, 3], [3])).toBe(3);
    });
    it('ohne Angreifer: nichts', () => {
      expect(exchangeGain(9, [], [1])).toBe(0);
    });
  });

  describe('capturedValue', () => {
    it('Wert der geschlagenen Figur auf dem Zielfeld', () => {
      expect(capturedValue(RUY_BEFORE, 'b5c6')).toBe(3);
      expect(capturedValue(GREEK_BEFORE, 'd3h7')).toBe(1);
    });

    it('en passant: der Bauer steht NICHT auf dem Zielfeld und zählt trotzdem', () => {
      // 1.e4 a6 2.e5 d5 — 3.exd6 e.p.
      expect(capturedValue('rnbqkbnr/1pp1pppp/p7/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3', 'e5d6')).toBe(1);
    });

    it('ohne Schlagen 0, unlesbar null', () => {
      expect(capturedValue('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', 'e2e4')).toBe(0);
      expect(capturedValue('kaputt', 'e2e4')).toBeNull();
    });
  });

  it('isPromotion erkennt die Umwandlung am fünften Zeichen', () => {
    expect(isPromotion('e7e8q')).toBeTrue();
    expect(isPromotion('a2a1N')).toBeTrue();
    expect(isPromotion('e2e4')).toBeFalse();
    expect(isPromotion('')).toBeFalse();
  });

  it('inCheck: steht die Seite am Zug im Schach? Unlesbar = nein', () => {
    expect(inCheck('4k3/8/8/8/1b1p4/8/8/1N2K3 w - - 0 1')).toBeTrue();
    expect(inCheck('r1bqkb1r/pppp1Qpp/2n2n2/4p3/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 0 4')).toBeTrue();   // Schäfermatt
    expect(inCheck('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1')).toBeFalse();
    expect(inCheck('8/8/8/8/8/8/8/8 w - - 0 1')).toBeFalse();
  });

  it('uciOf: von + nach + Umwandlung, wie der Server den Partiezug schreibt', () => {
    expect(uciOf({ from: 'e2', to: 'e4' })).toBe('e2e4');
    expect(uciOf({ from: 'e7', to: 'e8', promotion: 'q' })).toBe('e7e8q');
  });
});
