import { buildRepertoireGraph, cardsForColor, normFen, normSan, sideToMove } from './repertoire-tree.util';

const START = '[FEN "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"]\n\n';

describe('repertoire-tree.util', () => {
  it('normFen drops the move counters', () => {
    expect(normFen('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1'))
      .toBe('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3');
  });

  // Chessable schreibt Umwandlungen ohne „=" (`c8Q`) und gelegentlich Rochaden mit Nullen.
  // normSan vereinheitlicht auf die chess.js-Schreibweise — sonst gilt im Trainer ein korrekter
  // Umwandlungszug als falsch, und der Linien-Hash driftet gegen den Server-Spiegel
  // (ChessableTrainedLineService.CanonicalSan).
  it('normSan canonicalises promotions and castling like chess.js', () => {
    expect(normSan('c8Q')).toBe('c8=Q');
    expect(normSan('c8q')).toBe('c8=Q');
    expect(normSan('c8=q')).toBe('c8=Q');
    expect(normSan('bxc8N+')).toBe('bxc8=N');
    expect(normSan('b1Q+')).toBe('b1=Q');
    expect(normSan('0-0')).toBe('O-O');
    expect(normSan('0-0-0')).toBe('O-O-O');
  });

  it('normSan leaves ordinary moves alone (idempotent on chess.js output)', () => {
    for (const san of ['e4', 'Nf3', 'Qxd5', 'O-O', 'O-O-O', 'exd5', 'c8=Q', 'R1a3']) {
      expect(normSan(san)).toBe(san.replace(/[+#!?]+$/g, ''));
    }
    expect(normSan('Nf3!')).toBe('Nf3');
    expect(normSan('Qh7#')).toBe('Qh7');
  });

  it('builds cards for the trained side and extracts [%alt] tolerated moves', () => {
    const pgn = START + '1. e4 e6 {[%alt c5 e5]} 2. d4 d5 *';
    const g = buildRepertoireGraph(pgn);
    expect(g.guessedColor).toBe('b');   // nur Schwarz trägt [%alt]

    const cards = cardsForColor(g, 'b');
    // Schwarz ist nach 1.e4 und nach 2.d4 am Zug → 2 Karten.
    expect(cards.length).toBe(2);
    const first = cards.find(c => sideToMove(c.fenBefore) === 'b' && c.expected === 'e6')!;
    expect(first).toBeTruthy();
    expect(first.accepted.sort()).toEqual(['c5', 'e5']);
  });

  it('records variation moves as alternatives at the same position', () => {
    const pgn = START + '1. e4 e6 2. d4 d5 (2... c5 3. e5) *';
    const g = buildRepertoireGraph(pgn);
    const cards = cardsForColor(g, 'b');
    const move2 = cards.find(c => c.expected === 'd5')!;
    expect(move2).toBeTruthy();
    expect(move2.accepted).toContain('c5');
  });

  it('merges transpositions into a single card', () => {
    const pgn = START + '1. e4 e6 2. d4 d5 *\n\n' + START + '1. e4 e6 2. d4 d5 *';
    const g = buildRepertoireGraph(pgn);
    const cards = cardsForColor(g, 'b');
    // Trotz zweier identischer Linien: je Stellung genau eine Karte (e6, d5).
    expect(cards.length).toBe(2);
  });

  it('does not crash on an illegal/garbled variation and still parses the mainline', () => {
    const pgn = START + '1. e4 e6 (1... Zz9 2. ??) 2. d4 d5 *';
    const g = buildRepertoireGraph(pgn);
    const cards = cardsForColor(g, 'b');
    expect(cards.some(c => c.expected === 'e6')).toBeTrue();
    expect(cards.some(c => c.expected === 'd5')).toBeTrue();
  });

  // Regression (Codereview 2026-08-07): der Graph speicherte den ROHEN PGN-Token. Chessable
  // notiert Züge teils lang/überbestimmt (`Ng1f3`, `e2e4`); der Trainer vergleicht dagegen den
  // SAN, den chess.js aus dem Brett-Zug des Nutzers bildet (`Nf3`) — der korrekte Zug galt damit
  // als falsch. Erwartung: Haupt- UND [%alt]-Züge liegen in chess.js-Schreibweise im Graph.
  it('canonicalises long/overdetermined notation to the chess.js SAN', () => {
    const pgn = START + '1. e2e4 e7e5 2. Ng1f3 Nb8c6 *';
    const g = buildRepertoireGraph(pgn);
    const white = cardsForColor(g, 'w').map(c => c.expected).sort();
    const black = cardsForColor(g, 'b').map(c => c.expected).sort();
    expect(white).toEqual(['Nf3', 'e4']);
    expect(black).toEqual(['Nc6', 'e5']);
  });

  it('canonicalises [%alt] tolerances against the board, not just textually', () => {
    // Der geduldete Zug steht lang notiert im Kommentar (`Ng8f6`) — die Toleranz muss trotzdem
    // gegen den chess.js-SAN `Nf6` greifen, den der Trainer aus dem Brettzug bildet.
    const pgn = START + '1. e4 e5 2. Nf3 Nc6 {[%alt Ng8f6 d7d6]} *';
    const g = buildRepertoireGraph(pgn);
    const card = cardsForColor(g, 'b').find(c => c.expected === 'Nc6')!;
    expect(card).toBeTruthy();
    expect(card.accepted.sort()).toEqual(['Nf6', 'd6']);
  });

  it('keeps an unparsable [%alt] token as plain text instead of dropping it', () => {
    const pgn = START + '1. e4 e5 {[%alt Zz9]} *';
    const g = buildRepertoireGraph(pgn);
    const card = cardsForColor(g, 'b').find(c => c.expected === 'e5')!;
    expect(card.accepted).toEqual(['Zz9']);
  });

  it('white repertoire: cards are White moves', () => {
    const pgn = START + '1. e4 {[%alt d4 c4]} e5 2. Nf3 Nc6 *';
    const g = buildRepertoireGraph(pgn);
    expect(g.guessedColor).toBe('w');
    const cards = cardsForColor(g, 'w');
    expect(cards.some(c => c.expected === 'e4' && c.accepted.sort().join() === 'c4,d4')).toBeTrue();
    expect(cards.some(c => c.expected === 'Nf3')).toBeTrue();
  });
});
