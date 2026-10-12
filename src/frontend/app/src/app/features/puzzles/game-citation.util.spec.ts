import { isFillerEndComment, isGameCitationComment } from './game-citation.util';

describe('isGameCitationComment', () => {
  it('accepts pure game citations ending in a year', () => {
    expect(isGameCitationComment('Bayer - Kuenitz, Wiesbaden, 2015.')).toBeTrue();
    expect(isGameCitationComment('Wan Yunguo - Bjel, Russia teams rapid 2018.')).toBeTrue();
    expect(isGameCitationComment('Maedler-Stahl, Magdeburg 1964.')).toBeTrue();           // Dash ohne Leerzeichen
    expect(isGameCitationComment('Dominguez Perez-Perunovic, World Blitz Championship, Berlin 2015.')).toBeTrue();
    expect(isGameCitationComment('Kuijf-Böhm, Wijk aan Zee 1983.')).toBeTrue();           // Umlaut im Namen
    expect(isGameCitationComment('  Knorre-Fritz, Leipzig 1877.  ')).toBeTrue();          // getrimmt
  });

  it('accepts single-name study/composer citations', () => {
    expect(isGameCitationComment('Kubbel (1916).')).toBeTrue();
    expect(isGameCitationComment('Troitzky, 1914.')).toBeTrue();
    expect(isGameCitationComment('This exercise was based on a study, composed by Troitzky in 1914.')).toBeTrue();
  });

  it('accepts a short outcome + citation (still just "which game")', () => {
    expect(isGameCitationComment('Black resigned in Blalock-Francisco, Evora 2008.')).toBeTrue();
    expect(isGameCitationComment('Black won in Prihodko-Kivosheev, Uljanovsk 2007.')).toBeTrue();
  });

  it('accepts pure result / outcome phrases', () => {
    expect(isGameCitationComment('White wins.')).toBeTrue();
    expect(isGameCitationComment('Black wins.')).toBeTrue();
    expect(isGameCitationComment('White resigns.')).toBeTrue();
    expect(isGameCitationComment('Stalemate!')).toBeTrue();
    expect(isGameCitationComment('It is game over.')).toBeTrue();
    expect(isGameCitationComment('And White wins.')).toBeTrue();
  });

  it('accepts a result phrase followed by a source citation', () => {
    expect(isGameCitationComment('White wins. Rinck (1928).')).toBeTrue();
  });

  it('rejects instructional comments (even when they end in a year or contain dashes)', () => {
    // enthält Satzzeichen . ! ? bzw. Zugnotation → kein reines Zitat
    expect(isGameCitationComment('The double attack!')).toBeFalse();
    expect(isGameCitationComment('The pinned pawn is a poor defender!')).toBeFalse();
    expect(isGameCitationComment('The second passed pawn decides the game! Note that 6.h7! also wins. Simoni 1947 .')).toBeFalse();
    // Angabe + Erklärung: die Erklärung folgt NACH dem Zitat → weiterhin pausieren
    expect(isGameCitationComment('Bawart - Stuhlik, Graz, 1999. 1...Qf5+ allows the king to escape with 2.Ke7 .')).toBeFalse();
    // Zugnotation enthält Punkt → ausgeschlossen
    expect(isGameCitationComment('1...Rg5+ 2.Kf6 wins, as played in 2012')).toBeFalse();
  });

  it('rejects empty / missing', () => {
    expect(isGameCitationComment('')).toBeFalse();
    expect(isGameCitationComment(null)).toBeFalse();
    expect(isGameCitationComment(undefined)).toBeFalse();
    expect(isGameCitationComment('   ')).toBeFalse();
  });
});

/** Inhaltsleere Abschluss-Floskeln → Solver rückt automatisch weiter (Wunsch 2026-10-12). */
describe('isFillerEndComment', () => {
  it('erkennt Zeichen ohne Buchstaben, Ausgang, Lob, Punkte und Verweise', () => {
    for (const c of ['✔', '1-0', '0-1 .', '½–½', '=', '+- ✔', 'and White won.', 'Resigns.', 'Draw agreed.',
      'Checkmate!', 'Black is mated.', 'etc.', "and it's all over.", 'CORRECT! Well done.', 'Great job!',
      '(1 point)', 'Another 1 point for this variation.', '2 points', 'Go to the lesson',
      '____________ Full Game', 'Continued in the next variation.', 'See the relevant Theme.']) {
      expect(isFillerEndComment(c)).withContext(c).toBeTrue();
    }
  });

  it('erkennt Mattbild-Namen, Themen-Labels, Materialgewinn und kurze Bewertungen (zweite Runde)', () => {
    for (const c of ['Greco\'s mate', 'Railroad into Triangle mate', 'Theme: domination.', '(1 point) Theme: mate.',
      'White wins a piece.', 'You won the queen.', 'Winning the queen.', 'The queen is trapped.', 'Black is a piece up.',
      'White is clearly winning.', 'Black has no problems.', 'With a decisive advantage.', 'With equality.', 'Zugzwang.',
      'Winning.', 'Black holds.']) {
      expect(isFillerEndComment(c)).withContext(c).toBeTrue();
    }
  });

  it('lässt lehrreiche Kommentare und Zugnotation stehen', () => {
    for (const c of ['Castle quickly!', 'Not 2.Qxe5? Rxe5', 'Mate after 2.Qh7+', 'The knight helps the queen.',
      'White has to watch out for the queenside pawns, which are dangerous.', '', null]) {
      expect(isFillerEndComment(c)).withContext(String(c)).toBeFalse();
    }
  });
});
