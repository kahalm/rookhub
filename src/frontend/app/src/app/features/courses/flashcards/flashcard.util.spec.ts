import { buildFlashcard, buildRepertoireFlashcards, extractLastShapes, formatNotation, parseCalCsl } from './flashcard.util';
import { parsePgnText } from '../../../shared/pgn-viewer/pgn-parser';
import { lineKeyFromSans } from '../../repertoire/repertoire-line-key.util';
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
  it('KURS: vorn die AUSGANGSSTELLUNG (Aufgabe), hinten die Lösung', () => {
    // startPly -1: die FEN selbst ist die Aufgabe — vorn unverändert, hinten alle Lösungszüge.
    const card = buildFlashcard(puzzle())!;
    expect(card.frontFen).toBe(START);
    expect(card.notation).toBe('1.e4 e5 2.Nf3');

    // startPly 0: moves[0] ist Setup — vorn die Stellung NACH 1.e4, hinten die Lösung ab Schwarz.
    const setup = buildFlashcard(puzzle({ startPly: 0 }))!;
    expect(setup.frontFen).toContain('4P3');           // e4 steht schon
    expect(setup.frontFen).toContain(' b ');           // Schwarz (der Löser) am Zug
    expect(setup.notation).toBe('1…e5 2.Nf3');
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

  it('KURS-Vorderseite zeigt nur Kontext-Markierungen — nie Lösungs-Pfeile späterer Züge', () => {
    const card = buildFlashcard(puzzle({
      moveShapes: JSON.stringify({
        '-1': [{ o: 'd4' }],                              // Einleitung = Kontext, darf drauf
        '2': [{ o: 'f3', d: 'e5', b: 'green' }],          // Lösungs-Pfeil → würde verraten
      }),
    }))!;
    expect(card.shapes.length).toBe(1);
    expect(card.shapes[0]).toEqual(jasmine.objectContaining({ orig: 'd4' }));
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

describe('buildRepertoireFlashcards', () => {
  const PGN = `[Event "Kurs"]
[White "Vorstoßvariante Hauptlinie"]
[Black "Kapitel 4"]
[Result "*"]

1. e4 e6 2. d4 d5 3. e5 {Der Vorstoß.} c5 {[%cal Gc5d4,Rd4c5][%csl Ge5] Der Hebel — Druck gegen d4.} *

[Event "Kurs"]
[White "Nebenlinie"]
[Black "Kapitel 4"]
[Result "*"]

1. e4 e6 2. Qe2 *`;

  function build() {
    const raws = PGN.split(/\n\n(?=\[Event )/);
    const games = raws.map(r => parsePgnText(r)[0]).filter(g => !!g && g.moves.length > 0);
    return buildRepertoireFlashcards(games as never, raws, lineKeyFromSans);
  }

  it('REPERTOIRE (umgekehrt): vorn die ENDSTELLUNG mit den Markern, hinten Linie + Abschluss', () => {
    const [a, b] = build();
    // Endstellung nach 3…c5 — Weiß am Zug, Bauernkette e5/d4 gegen d5/c5.
    expect(a.card.frontFen).toBe('rnbqkbnr/pp3ppp/4p3/2ppP3/3P4/8/PPP2PPP/RNBQKBNR w KQkq - 0 4');
    expect(a.card.notation).toBe('1.e4 e6 2.d4 d5 3.e5 c5');
    expect(a.card.closing).toBe('Der Hebel — Druck gegen d4.');   // Marker sind raus
    expect(a.card.shapes.length).toBe(3);               // 2 Pfeile + 1 Feld
    expect(a.card.shapes[0]).toEqual(jasmine.objectContaining({ orig: 'c5', dest: 'd4', brush: 'green' }));
    expect(a.card.shapes[2]).toEqual(jasmine.objectContaining({ orig: 'e5', brush: 'green' }));
    // Ausrichtung = Seite des letzten Zugs (…c5 = Schwarz — ein Schwarz-Repertoire).
    expect(a.card.orientation).toBe('black');
    expect(b.card.orientation).toBe('white');           // endet mit 2.De2 (Weiß)
    expect(b.card.shapes.length).toBe(0);
  });

  it('Kopf = White-Header, Kapitel = Black-Header, lineKey passt zur Linienliste', () => {
    const [a] = build();
    expect(a.card.heading).toBe('Vorstoßvariante Hauptlinie');
    expect(a.card.chapter).toBe('Kapitel 4');
    expect(a.lineKey).toBe(lineKeyFromSans(['e4', 'e6', 'd4', 'd5', 'e5', 'c5']));
  });
});

describe('parseCalCsl / extractLastShapes', () => {
  it('parst Pfeile und Felder mit Farb-Präfixen', () => {
    const shapes = parseCalCsl('{[%cal Gc8h3,Yd1d8][%csl Rb5]}');
    expect(shapes).toEqual([
      jasmine.objectContaining({ orig: 'c8', dest: 'h3', brush: 'green' }),
      jasmine.objectContaining({ orig: 'd1', dest: 'd8', brush: 'yellow' }),
      jasmine.objectContaining({ orig: 'b5', brush: 'red' }),
    ] as never);
  });

  it('nimmt den LETZTEN Kommentar-Block mit Markern (Endstellungs-Annotation)', () => {
    const raw = '1. e4 {[%cal Ga1a2]} e5 2. Nf3 {[%csl Gf3]} *';
    const shapes = extractLastShapes(raw);
    expect(shapes.length).toBe(1);
    expect(shapes[0]).toEqual(jasmine.objectContaining({ orig: 'f3' }));
  });
});
