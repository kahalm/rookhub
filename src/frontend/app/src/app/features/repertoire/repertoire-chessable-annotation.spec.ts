import { parsePgnText } from '../../shared/pgn-viewer/pgn-parser';
import { lineKeyFromSans } from './repertoire-line-key.util';

/**
 * 0.885.0: „Prep: …"-Repertoires tragen je Chessable-Linie Kopfzeilen (`ChessableLastTrained`, `ChessableLevel`, `ChessableUrl`),
 * einen Anfangskommentar und Fehler-Kommentare vor einzelnen Zügen (Server `ChessableLineHistory.Annotate`). Der Trainer muss
 * dieselben Züge und denselben Linien-Schlüssel sehen wie ohne — sonst ginge der Trainingsstand verloren.
 */
describe('Chessable-Beschriftung im Repertoire-PGN', () => {
  const plain = '[Event "x"]\n[White "Spanisch"]\n[Black "Kapitel"]\n[ChessableOid "987"]\n\n'
    + '1. e4 e5 {Hauptlinie} 2. Nf3 (2. Bc4 Nf6) 2... Nc6 3. Bb5 $1 a6 *\n';
  const annotated = '[Event "x"]\n[White "Spanisch"]\n[Black "Kapitel"]\n[ChessableOid "987"]\n'
    + '[ChessableLastTrained "2026.11.05"]\n[ChessableLevel "7"]\n[ChessableUrl "https://www.chessable.com/variation/987/"]\n\n'
    + '{Chessable: zuletzt trainiert 05.11.2026, Stufe 7 — https://www.chessable.com/variation/987/} 1. e4 e5 {Hauptlinie} '
    + '{Chessable: hier 3× falsch (zuletzt 2.Nc3)} 2. Nf3 (2. Bc4 Nf6) 2... Nc6 {Chessable: hier 1× falsch} 3. Bb5 $1 a6 *\n';

  it('dieselben Züge und derselbe Linien-Schlüssel; die Kopfzeilen sind lesbar', () => {
    const [a] = parsePgnText(plain);
    const [b] = parsePgnText(annotated);
    expect(b.moves.map(m => m.san)).toEqual(a.moves.map(m => m.san));
    expect(lineKeyFromSans(b.moves.map(m => m.san))).toBe(lineKeyFromSans(a.moves.map(m => m.san)));
    expect(b.headers['ChessableUrl']).toBe('https://www.chessable.com/variation/987/');
    expect(b.headers['ChessableLevel']).toBe('7');
  });
});
