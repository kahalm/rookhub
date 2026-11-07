import { parsePgnText } from '../../shared/pgn-viewer/pgn-parser';
import { ChessableMisses, chessableOid, chessableUrl, missAt, moveLabelAt, plyOfMid } from './repertoire-trainer-misses.util';

describe('Chessable-Fehler im Repertoire-Trainer (0.886.0)', () => {
  const [line] = parsePgnText('[Event "x"]\n[Black "K"]\n[ChessableOid "555"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 *\n');
  const [blackStart] = parsePgnText('[Event "x"]\n[FEN "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1"]\n[ChessableOid "556"]\n\n1... c5 2. Nf3 d6 *\n');

  it('oid und Link nur numerisch', () => {
    expect(chessableOid(line)).toBe('555');
    expect(chessableUrl('555')).toBe('https://www.chessable.com/variation/555/');
    expect(chessableOid(parsePgnText('[Event "x"]\n[ChessableOid "x1"]\n\n1. e4 *\n')[0])).toBeNull();
  });

  it('mid ↔ Halbzug je Farbe und Startseite', () => {
    expect(plyOfMid(0, 'w', false)).toBe(0);
    expect(plyOfMid(1, 'w', false)).toBe(2);
    expect(plyOfMid(1, 'b', false)).toBe(3);
    expect(plyOfMid(0, 'b', true)).toBe(0);
    expect(moveLabelAt(line, 2, 'Bc4')).toBe('2.Bc4');
    expect(moveLabelAt(blackStart, 0, 'e5')).toBe('1…e5');
  });

  it('summiert über Durchläufe am richtigen Zug, nur eigene Farbe, mit RookHub-Fehlern der Linie', () => {
    const misses: ChessableMisses = { '555': [
      { mid: 1, mode: 'w', count: 2, last: 'Bc4' }, { mid: 1, mode: null, count: 1, last: 'Nc3' },
      { mid: 1, mode: 'b', count: 5, last: 'Nf6' }, { mid: 2, mode: 'w', count: 9, last: 'd4' }] };
    expect(missAt(line, 2, 'w', misses, 2)).toEqual({ chessable: 3, last: '2.Nc3', rookhub: 2 });
    expect(missAt(line, 0, 'w', misses, 0)).toBeNull();               // keine Daten → nichts anzeigen
    expect(missAt(line, 0, 'w', misses, 1)).toEqual({ chessable: 0, last: null, rookhub: 1 });
    expect(missAt(line, 2, 'w', null, 0)).toBeNull();
  });
});
