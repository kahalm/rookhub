import { EvalPoint, badMoveVerdict, evalDrop, matchingBefore, pawnsOf } from './sparring-check';

/** Literale Werte — die Regeln entscheiden, ob dem Nutzer ein Zug als „nicht gut" gemeldet wird. */
describe('sparring-check', () => {
  const cp = (depth: number, score: number): EvalPoint =>
    ({ depth, score, scoreType: 'cp', evalText: (score > 0 ? '+' : '') + (score / 100).toFixed(2) });
  const mate = (depth: number, n: number): EvalPoint => ({ depth, score: n, scoreType: 'mate', evalText: '#' + n });

  it('pawnsOf: Centibauern → Bauern, Matt in n = ±(1000 − |n|)', () => {
    expect(pawnsOf({ score: 35, scoreType: 'cp' })).toBe(0.35);
    expect(pawnsOf({ score: -120, scoreType: 'cp' })).toBe(-1.2);
    expect(pawnsOf({ score: 3, scoreType: 'mate' })).toBe(997);
    expect(pawnsOf({ score: -2, scoreType: 'mate' })).toBe(-998);
  });

  it('matchingBefore: größte Tiefe ≤ Nachher-Tiefe, sonst die kleinste, leer → null', () => {
    const track = [cp(8, 10), cp(12, 20), cp(16, 30)];
    expect(matchingBefore(track, 14)!.depth).toBe(12);
    expect(matchingBefore(track, 16)!.depth).toBe(16);
    expect(matchingBefore(track, 6)!.depth).toBe(8);
    expect(matchingBefore([cp(16, 30), cp(8, 10)], 20)!.depth).toBe(16);   // Reihenfolge der Spur egal
    expect(matchingBefore([], 14)).toBeNull();
  });

  it('evalDrop: aus Sicht des Ziehenden, positiv = schlechter', () => {
    expect(evalDrop(cp(14, 40), cp(14, -30), 'white')).toBe(0.7);
    expect(evalDrop(cp(14, 40), cp(14, -30), 'black')).toBe(-0.7);   // für Schwarz ein Gewinn
    expect(evalDrop(cp(14, -50), cp(14, 10), 'black')).toBe(0.6);
  });

  it('badMoveVerdict: ab 0,2 Bauern eine Warnung, darunter nicht; Matt verpasst immer; ohne Vorher-Wert nie', () => {
    expect(badMoveVerdict([cp(14, 40)], cp(14, 21), 'white')).toBeNull();          // 0,19
    expect(badMoveVerdict([cp(14, 40)], cp(14, 20), 'white')).toEqual({ drop: 0.2, before: cp(14, 40), after: cp(14, 20) });
    expect(badMoveVerdict([cp(14, -50)], cp(14, -30), 'black')).toEqual({ drop: 0.2, before: cp(14, -50), after: cp(14, -30) });
    expect(badMoveVerdict([cp(14, -50)], cp(14, -31), 'black')).toBeNull();        // 0,19 für Schwarz
    const missed = badMoveVerdict([mate(12, 3)], cp(14, 150), 'white');
    expect(missed!.drop).toBe(995.5);
    expect(missed!.before).toEqual(mate(12, 3));
    expect(badMoveVerdict([], cp(14, -300), 'white')).toBeNull();
  });

  it('badMoveVerdict vergleicht bei passender Tiefe, nicht mit dem jüngsten Wert', () => {
    // Vorher: flach +0,40, tief +0,10. Nachher bei Tiefe 14: −0,05 → verglichen mit Tiefe 12 (+0,10) = 0,15 → keine Warnung.
    expect(badMoveVerdict([cp(8, 40), cp(12, 10), cp(18, 60)], cp(14, -5), 'white')).toBeNull();
    // Mit der flachen Tiefe 8 (+0,40) wäre es eine gewesen.
    expect(badMoveVerdict([cp(8, 40)], cp(14, -5), 'white')!.drop).toBe(0.45);
  });

  it('eigene Schwelle', () => {
    expect(badMoveVerdict([cp(14, 40)], cp(14, 0), 'white', 0.5)).toBeNull();
    expect(badMoveVerdict([cp(14, 40)], cp(14, -10), 'white', 0.5)!.drop).toBe(0.5);
  });
});
