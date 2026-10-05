import { distinctClassifiers, filterByClassifiers, hasUnclassified, NO_CLASSIFIER, seasonOf } from './classifier.util';

describe('classifier.util', () => {
  const games = [
    { id: 1, classifier1: 'chess.com', classifier2: 'Blitz' },
    { id: 2, classifier1: 'chess.com', classifier2: 'Rapid' },
    { id: 3, classifier1: 'Lichess', classifier2: 'Blitz' },
    { id: 4, classifier1: 'Landesliga', classifier2: '2026/27' },
    { id: 5, classifier1: null, classifier2: null },
  ];

  it('listet jeden Wert einmal, alphabetisch', () => {
    expect(distinctClassifiers(games, 1)).toEqual(['chess.com', 'Landesliga', 'Lichess']);
    expect(distinctClassifiers(games, 2)).toEqual(['2026/27', 'Blitz', 'Rapid']);
  });

  it('erkennt Partien ohne Wert', () => {
    expect(hasUnclassified(games, 1)).toBeTrue();
    expect(hasUnclassified(games.slice(0, 4), 1)).toBeFalse();
  });

  it('ohne Filter bleibt alles, beide Filter schneiden sich', () => {
    expect(filterByClassifiers(games, '', '').length).toBe(5);
    expect(filterByClassifiers(games, 'chess.com', '').map(g => g.id)).toEqual([1, 2]);
    expect(filterByClassifiers(games, 'chess.com', 'Blitz').map(g => g.id)).toEqual([1]);
    expect(filterByClassifiers(games, '', 'Blitz').map(g => g.id)).toEqual([1, 3]);
  });

  it('„ohne Angabe" findet nur die ungesetzten', () => {
    expect(filterByClassifiers(games, NO_CLASSIFIER, '').map(g => g.id)).toEqual([5]);
  });

  it('schlägt den Jahrgang aus dem Datum vor: ab August die neue Saison', () => {
    expect(seasonOf('2026-10-05')).toBe('2026/27');
    expect(seasonOf('2027-03-01')).toBe('2026/27');
    expect(seasonOf('2026-07-31')).toBe('2025/26');
    expect(seasonOf('2099-09-01')).toBe('2099/00');
    expect(seasonOf('')).toBeNull();
    expect(seasonOf(null)).toBeNull();
  });
});
