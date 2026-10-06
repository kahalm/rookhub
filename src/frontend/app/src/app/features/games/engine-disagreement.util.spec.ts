import { classesDisagree, engineDisagreements, moveNumberLabel, stepDisagreement } from './engine-disagreement.util';
import { GameReview, MoveClass, ReviewedMove } from './game-review.util';

function review(bases: (MoveClass | null)[]): GameReview {
  const counts = {} as GameReview['white']['counts'];
  return {
    series: [], curve: [],
    moves: bases.map((b, i) => b === null ? null : ({ ply: i, white: i % 2 === 0, cls: b, base: b } as ReviewedMove)),
    white: { accuracy: null, counts }, black: { accuracy: null, counts },
  };
}

describe('engine-disagreement.util', () => {
  it('ignoriert Abweichungen an der Bandgrenze', () => {
    expect(classesDisagree('good', 'inaccuracy')).toBeFalse();
    expect(classesDisagree('best', 'good')).toBeFalse();
    expect(classesDisagree('best', 'inaccuracy')).toBeFalse();
    expect(classesDisagree('mistake', 'inaccuracy')).toBeFalse();
    expect(classesDisagree('blunder', 'mistake')).toBeFalse();
  });

  it('meldet Fehler gegen höchstens gut und einseitige grobe Fehler', () => {
    expect(classesDisagree('mistake', 'best')).toBeTrue();
    expect(classesDisagree('good', 'mistake')).toBeTrue();
    expect(classesDisagree('excellent', 'blunder')).toBeTrue();
    expect(classesDisagree('blunder', 'inaccuracy')).toBeTrue();
    expect(classesDisagree('inaccuracy', 'blunder')).toBeTrue();
  });

  it('lässt Klassen ohne Rang (Buch) aus', () => {
    expect(classesDisagree('book', 'blunder')).toBeFalse();
  });

  it('sammelt die uneinigen Züge und überspringt fehlende Bewertungen', () => {
    const a = review(['best', 'mistake', null, 'blunder', 'good']);
    const b = review(['good', 'best', 'blunder', 'inaccuracy', 'inaccuracy']);
    expect(engineDisagreements(a, b)).toEqual([
      { ply: 1, primary: 'mistake', alt: 'best' },
      { ply: 3, primary: 'blunder', alt: 'inaccuracy' },
    ]);
  });

  it('springt vor und zurück mit Umlauf', () => {
    const list = [{ ply: 4, primary: 'best' as MoveClass, alt: 'mistake' as MoveClass },
                  { ply: 9, primary: 'best' as MoveClass, alt: 'mistake' as MoveClass }];
    expect(stepDisagreement(list, -1, 1)).toBe(4);
    expect(stepDisagreement(list, 4, 1)).toBe(9);
    expect(stepDisagreement(list, 9, 1)).toBe(4);
    expect(stepDisagreement(list, 9, -1)).toBe(4);
    expect(stepDisagreement(list, 4, -1)).toBe(9);
    expect(stepDisagreement([], 0, 1)).toBeNull();
  });

  it('nummeriert aus der Stellung vor dem Zug', () => {
    expect(moveNumberLabel('r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3', 4)).toBe('3.');
    expect(moveNumberLabel('8/8/8/8/8/8/8/K6k b - - 0 17', 0)).toBe('17...');
    expect(moveNumberLabel(undefined, 5)).toBe('3...');
  });
});
