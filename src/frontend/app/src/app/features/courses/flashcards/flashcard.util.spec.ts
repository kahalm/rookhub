import { buildFlashcard, formatNotation } from './flashcard.util';
import { BookPuzzleDto } from '../../puzzles/puzzle.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

function puzzle(over: Partial<BookPuzzleDto> = {}): BookPuzzleDto {
  return {
    id: 1, lineId: 'b.pgn:1', bookFileName: 'b.pgn', round: '1',
    fen: START, moves: 'e2e4 e7e5 g1f3', startPly: -1,
    ...over,
  } as BookPuzzleDto;
}

describe('buildFlashcard', () => {
  it('vorn die ENDSTELLUNG (Züge durchgespielt), hinten die Notation', () => {
    const card = buildFlashcard(puzzle())!;
    // Endstellung nach 1.e4 e5 2.Sf3 — Springer auf f3, Schwarz am Zug.
    expect(card.frontFen).toBe('rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2');
    expect(card.notation).toBe('1.e4 e5 2.Nf3');
  });

  it('Abschlussbeschreibung: Kommentar des LETZTEN Zugs schlägt den Linien-Kommentar', () => {
    const withLast = buildFlashcard(puzzle({
      comment: 'Linien-Kommentar',
      moveComments: { '2': 'Springer entwickelt — Druck auf e5.' },
    }))!;
    expect(withLast.closing).toBe('Springer entwickelt — Druck auf e5.');

    const withoutLast = buildFlashcard(puzzle({ comment: 'Linien-Kommentar' }))!;
    expect(withoutLast.closing).toBe('Linien-Kommentar');
  });

  it('Pfeile: die Shapes des letzten Halbzugs landen auf der Vorderseite', () => {
    const card = buildFlashcard(puzzle({
      moveShapes: JSON.stringify({
        '0': [{ o: 'a1', d: 'a8', b: 'red' }],
        '2': [{ o: 'f3', d: 'e5', b: 'green' }, { o: 'd4' }],
      }),
    }))!;
    expect(card.shapes.length).toBe(2);
    expect(card.shapes[0]).toEqual(jasmine.objectContaining({ orig: 'f3', dest: 'e5' }));
    expect(card.shapes[1]).toEqual(jasmine.objectContaining({ orig: 'd4' }));   // Feld-Markierung
  });

  it('Ausrichtung = Seite des Trainierenden (startPly -1 → am Zug; 0 → nach dem Setup-Zug)', () => {
    expect(buildFlashcard(puzzle({ startPly: -1 }))!.orientation).toBe('white');
    expect(buildFlashcard(puzzle({ startPly: 0 }))!.orientation).toBe('black');
  });

  it('Stellungs-/Info-Linie ohne Züge: die Stellung selbst, Einleitungs-Shapes, keine Notation', () => {
    const card = buildFlashcard(puzzle({
      moves: '', comment: 'Merkbild',
      fen: '8/8/8/4k3/8/8/4K3/8 b - - 0 1',
      moveShapes: JSON.stringify({ '-1': [{ o: 'e5' }] }),
    }))!;
    expect(card.frontFen).toBe('8/8/8/4k3/8/8/4K3/8 b - - 0 1');
    expect(card.orientation).toBe('black');
    expect(card.notation).toBe('');
    expect(card.closing).toBe('Merkbild');
    expect(card.shapes.length).toBe(1);
  });

  it('ILLEGALE Muster-FEN (ohne Könige) crasht nicht — Karte mit der Stellung selbst', () => {
    const card = buildFlashcard(puzzle({ fen: '8/8/8/3q4/8/8/8/8 w - - 0 1', moves: '' }))!;
    expect(card.frontFen).toBe('8/8/8/3q4/8/8/8/8 w - - 0 1');
  });

  it('kaputte Zugliste → keine Karte statt einer falschen', () => {
    expect(buildFlashcard(puzzle({ moves: 'e2e4 zz99' }))).toBeNull();
  });

  it('Kopfzeile: Titel, sonst #Runde', () => {
    expect(buildFlashcard(puzzle({ title: 'Tarrasch #1' }))!.heading).toBe('Tarrasch #1');
    expect(buildFlashcard(puzzle({ round: '0042' }))!.heading).toBe('#0042');
  });
});

describe('formatNotation', () => {
  it('nummeriert ab Startzug, Schwarz-Start mit „…"', () => {
    expect(formatNotation(['e4', 'e5', 'Nf3'], true, 1)).toBe('1.e4 e5 2.Nf3');
    expect(formatNotation(['e5', 'Nf3', 'Nc6'], false, 12)).toBe('12…e5 13.Nf3 Nc6');
    expect(formatNotation([], true, 1)).toBe('');
  });
});
