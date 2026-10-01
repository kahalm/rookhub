import { parseKeyboardMove, pieceLettersFor } from './keyboard-move.util';

/** Codereview F2-004: getippte Züge — literale Stellungen, literale Erwartungen. */
describe('parseKeyboardMove', () => {
  const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  // Weiß: Kg1, Sb1, Sf3, Ta1; Schwarz: Ke8, Bauer d5; e4-Bauer kann d5 schlagen. Beide Springer erreichen d2.
  const TWO_KNIGHTS = '4k3/8/8/3p4/4P3/5N2/8/RN4K1 w - - 0 1';
  const PROMO = '8/P6k/8/8/8/8/7K/8 w - - 0 1';
  const CASTLE = 'r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1';

  it('liest englische SAN und Koordinaten', () => {
    expect(parseKeyboardMove(START, 'Nf3')).toEqual({ orig: 'g1', dest: 'f3', needsPromotion: false } as any);
    expect(parseKeyboardMove(START, 'e4')).toEqual({ orig: 'e2', dest: 'e4', needsPromotion: false } as any);
    expect(parseKeyboardMove(START, 'g1f3')).toEqual({ orig: 'g1', dest: 'f3', needsPromotion: false } as any);
    expect(parseKeyboardMove(START, ' g1-f3 ')).toEqual({ orig: 'g1', dest: 'f3', needsPromotion: false } as any);
    expect(parseKeyboardMove(START, 'Ng1-f3')).toEqual({ orig: 'g1', dest: 'f3', needsPromotion: false } as any);
  });

  it('versteht deutsche/kroatische und ungarische Figurenbuchstaben je Sprache', () => {
    expect(parseKeyboardMove(START, 'Sf3', 'de')?.orig).toBe('g1');
    expect(parseKeyboardMove(START, 'Sf3', 'hr')?.orig).toBe('g1');
    expect(parseKeyboardMove(START, 'Hf3', 'hu')?.orig).toBe('g1');
    expect(parseKeyboardMove(START, 'Sf3', 'en')).toBeNull();   // englisch gibt es kein S
    // Ungarisch heißt B der Turm: in CASTLE zieht „Bb1" den Turm a1, nicht einen Läufer.
    expect(parseKeyboardMove(CASTLE, 'Bb1', 'hu')).toEqual({ orig: 'a1', dest: 'b1', needsPromotion: false } as any);
    expect(parseKeyboardMove(CASTLE, 'Bb1', 'en')).toBeNull();  // englisch: kein Läufer da
    expect(pieceLettersFor('de-AT')['D']).toBe('Q');
    expect(pieceLettersFor('fr')['D']).toBeUndefined();
  });

  it('Schlagzeichen und Schachzeichen sind egal', () => {
    expect(parseKeyboardMove(TWO_KNIGHTS, 'exd5')).toEqual({ orig: 'e4', dest: 'd5', needsPromotion: false } as any);
    expect(parseKeyboardMove(TWO_KNIGHTS, 'ed5')).toEqual({ orig: 'e4', dest: 'd5', needsPromotion: false } as any);
    expect(parseKeyboardMove(TWO_KNIGHTS, 'e4xd5+')).toEqual({ orig: 'e4', dest: 'd5', needsPromotion: false } as any);
  });

  it('mehrdeutig ist null — mit Zusatz oder Koordinaten eindeutig', () => {
    expect(parseKeyboardMove(TWO_KNIGHTS, 'Nd2')).toBeNull();
    expect(parseKeyboardMove(TWO_KNIGHTS, 'Nbd2')).toEqual({ orig: 'b1', dest: 'd2', needsPromotion: false } as any);
    expect(parseKeyboardMove(TWO_KNIGHTS, 'Sfd2', 'de')).toEqual({ orig: 'f3', dest: 'd2', needsPromotion: false } as any);
    expect(parseKeyboardMove(TWO_KNIGHTS, 'f3d2')).toEqual({ orig: 'f3', dest: 'd2', needsPromotion: false } as any);
  });

  it('Rochade in allen üblichen Schreibweisen', () => {
    expect(parseKeyboardMove(CASTLE, 'O-O')).toEqual({ orig: 'e1', dest: 'g1', needsPromotion: false } as any);
    expect(parseKeyboardMove(CASTLE, '0-0')).toEqual({ orig: 'e1', dest: 'g1', needsPromotion: false } as any);
    expect(parseKeyboardMove(CASTLE, '0-0-0')).toEqual({ orig: 'e1', dest: 'c1', needsPromotion: false } as any);
    expect(parseKeyboardMove(CASTLE, 'e1g1')).toEqual({ orig: 'e1', dest: 'g1', needsPromotion: false } as any);
  });

  it('Umwandlung: mit Figur fertig, ohne Figur fragt die Auswahl', () => {
    expect(parseKeyboardMove(PROMO, 'a8=Q')).toEqual({ orig: 'a7', dest: 'a8', promotion: 'q', needsPromotion: false });
    expect(parseKeyboardMove(PROMO, 'a8N')).toEqual({ orig: 'a7', dest: 'a8', promotion: 'n', needsPromotion: false });
    expect(parseKeyboardMove(PROMO, 'a8=S', 'de')).toEqual({ orig: 'a7', dest: 'a8', promotion: 'n', needsPromotion: false });
    expect(parseKeyboardMove(PROMO, 'a7a8r')).toEqual({ orig: 'a7', dest: 'a8', promotion: 'r', needsPromotion: false });
    expect(parseKeyboardMove(PROMO, 'a8')).toEqual({ orig: 'a7', dest: 'a8', needsPromotion: true } as any);
    expect(parseKeyboardMove(PROMO, 'a7a8')).toEqual({ orig: 'a7', dest: 'a8', needsPromotion: true } as any);
    expect(parseKeyboardMove(PROMO, 'a8=K')).toBeNull();
  });

  it('illegal, unlesbar oder leer ist null — auch der Null-Zug', () => {
    expect(parseKeyboardMove(START, 'e5')).toBeNull();
    expect(parseKeyboardMove(START, 'e2e5')).toBeNull();
    expect(parseKeyboardMove(START, 'e2e4q')).toBeNull();      // keine Umwandlung
    expect(parseKeyboardMove(START, 'Bg1f3')).toBeNull();      // Figur passt nicht zum Zug
    expect(parseKeyboardMove(START, '--')).toBeNull();
    expect(parseKeyboardMove(START, 'Xf3')).toBeNull();
    expect(parseKeyboardMove(START, '   ')).toBeNull();
    expect(parseKeyboardMove('kein fen', 'e4')).toBeNull();
    expect(parseKeyboardMove('8/8/8/8/8/8/8/8 w - - 0 1', 'e4')).toBeNull();   // Info-Diagramm ohne Könige
  });
});
