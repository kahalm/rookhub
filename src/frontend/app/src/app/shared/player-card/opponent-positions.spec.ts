import { Chess } from 'chess.js';
import { OpponentPositions, opponentMovesText, opponentNote, positionKey } from './opponent-positions';

/** Stellungen einer Zugfolge ab der Grundstellung. */
function fensOf(sans: string[]): string[] {
  const c = new Chess();
  return [c.fen(), ...sans.map(s => { c.move(s); return c.fen(); })];
}

describe('opponent-positions (Partien des Gegners je Stellung, 0.892.0)', () => {
  const sans = ['e4', 'c5', 'Nf3', 'd6', 'd4'];
  const fens = fensOf(sans);
  const positions: OpponentPositions = {
    [positionKey(fens[0])]: { n: 14, y: 2024, m: {} },
    [positionKey(fens[1])]: { n: 14, y: 2024, m: { c5: 9, e6: 3, e5: 1, c6: 1 } },
    [positionKey(fens[2])]: { n: 9, y: 2023, m: {} },
    [positionKey(fens[3])]: { n: 6, y: null, m: { d6: 6 } },
    // nach 2…d6 hat er die Stellung nie erreicht (er spielte immer 2…Nc6) → ab 2…d6 keine Partien mehr
  };

  it('Schlüssel = die ersten drei FEN-Felder (wie RepertoireReach.Key)', () => {
    expect(positionKey('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1')).toBe('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq');
  });

  it('in seinen Partien: erreicht, Jahr und seine Züge (häufigste zuerst, höchstens 3)', () => {
    const n = opponentNote(fens, sans, 1, positions, 'de');
    expect(n).toEqual({ kind: 'in', reached: 14, lastYear: 2024, moves: [{ san: 'c5', n: 9 }, { san: 'e6', n: 3 }, jasmine.any(Object) as any] });
    expect(opponentMovesText(n && n.kind === 'in' ? n.moves.slice(0, 2) : [], fens, sans, 1, 'de')).toBe('1…c5 9×, 1…e6 3×');
    expect(opponentNote(fens, sans, 3, positions)).toEqual({ kind: 'in', reached: 6, lastYear: null, moves: [{ san: 'd6', n: 6 }] });
  });

  it('Umschlag am richtigen Zug: ab dem ersten Halbzug ohne Partien bleibt die Meldung bis zum Ende stehen', () => {
    expect(opponentNote(fens, sans, 4, positions, 'de')).toEqual({ kind: 'left', leftPly: 3, move: '2…d6' });
    expect(opponentNote(fens, sans, 5, positions, 'de')).toEqual({ kind: 'left', leftPly: 3, move: '2…d6' });
    // Figurenbuchstaben nach Sprache
    const knight = { ...positions };
    delete knight[positionKey(fens[2])];
    expect(opponentNote(fens, sans, 4, knight, 'de')).toEqual({ kind: 'left', leftPly: 1, move: '1…c5' });
  });

  it('nie erreicht / ohne Daten', () => {
    expect(opponentNote(fens, sans, 2, { [positionKey(fens[1])]: { n: 1, y: null, m: {} } })).toEqual({ kind: 'never' });
    expect(opponentNote(fens, sans, 2, null)).toBeNull();
    expect(opponentNote([], [], 0, positions)).toBeNull();
  });

  it('Zugangabe in deutscher Notation (Sf3)', () => {
    const p2 = { ...positions };
    delete p2[positionKey(fens[3])];
    expect(opponentNote(fens, sans, 3, p2, 'de')).toEqual({ kind: 'left', leftPly: 2, move: '2.Sf3' });
    expect(opponentNote(fens, sans, 3, p2, 'en')).toEqual({ kind: 'left', leftPly: 2, move: '2.Nf3' });
  });
});
