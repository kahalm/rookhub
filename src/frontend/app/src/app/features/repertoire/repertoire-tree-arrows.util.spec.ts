import { ARROW_MAX, ARROW_MIN, ARROW_UNIFORM, canonSan, sameArrows, treeArrows } from './repertoire-tree-arrows.util';
import { ExplorerPosition } from './repertoire-explorer.service';

const pop = (moves: [string, number][]): ExplorerPosition => ({
  status: 'ok', retryAfterSeconds: null, source: 'online', database: 'lichess', total: 0, white: 0, draws: 0, black: 0,
  opening: null, eco: null,
  moves: moves.map(([san, games]) => ({ uci: '', san, games, white: 0, draws: 0, black: 0, averageRating: null, opening: null, eco: null })),
} as unknown as ExplorerPosition);

const MOVES = [{ san: 'e4', from: 'e2', to: 'e4' }, { san: 'd4', from: 'd2', to: 'd4' }, { san: 'Nf3', from: 'g1', to: 'f3' }];

describe('repertoire-tree-arrows.util', () => {
  it('Breite nach Lichess-Häufigkeit: häufigster Zug am dicksten, ohne Partien am dünnsten, dicker Pfeil zuletzt', () => {
    const a = treeArrows(MOVES, pop([['e4', 1000], ['d4', 250], ['c4', 5000]]));
    const by = new Map(a.map(x => [x.from + x.to, x.lineWidth!]));
    expect(by.get('e2e4')).toBe(ARROW_MAX);
    expect(by.get('g1f3')).toBe(ARROW_MIN);
    expect(by.get('d2d4')!).toBeGreaterThan(ARROW_MIN);
    expect(by.get('d2d4')!).toBeLessThan(ARROW_MAX);
    expect(a[a.length - 1].to).toBe('e4');
    expect(a.every(x => x.brush === 'green')).toBeTrue();
  });

  it('ohne Explorer-Daten (oder Fehler) alle gleich breit', () => {
    expect(treeArrows(MOVES, null).map(x => x.lineWidth)).toEqual([ARROW_UNIFORM, ARROW_UNIFORM, ARROW_UNIFORM]);
    expect(treeArrows(MOVES, { ...pop([['e4', 9]]), status: 'rateLimited' }).every(x => x.lineWidth === ARROW_UNIFORM)).toBeTrue();
  });

  it('SAN-Schreibweisen gleich behandeln (Schach, Rochade mit 0)', () => {
    expect(canonSan('Qxf7#')).toBe('Qxf7');
    expect(canonSan('0-0-0')).toBe('O-O-O');
    const a = treeArrows([{ san: 'O-O', from: 'e1', to: 'g1' }, { san: 'Bb5+', from: 'f1', to: 'b5' }], pop([['O-O', 10], ['Bb5', 40]]));
    expect(a.find(x => x.to === 'b5')!.lineWidth).toBe(ARROW_MAX);
  });

  it('eigene Züge: Breite nach Zahl der Repertoire-Linien, Lichess zählt dann nicht', () => {
    const own = [{ san: 'e4', from: 'e2', to: 'e4', count: 40 }, { san: 'd4', from: 'd2', to: 'd4', count: 10 },
      { san: 'Nf3', from: 'g1', to: 'f3', count: 0 }];
    const a = treeArrows(own, pop([['d4', 99999], ['e4', 1]]), true);
    const by = new Map(a.map(x => [x.to, x.lineWidth!]));
    expect(by.get('e4')).toBe(ARROW_MAX);
    expect(by.get('f3')).toBe(ARROW_MIN);
    expect(by.get('d4')!).toBeGreaterThan(ARROW_MIN);
    expect(by.get('d4')!).toBeLessThan(ARROW_MAX);
    // Gegnerzug (ownMove false): wie bisher nach Lichess, die Linienzahl zählt nicht
    expect(new Map(treeArrows(own, pop([['d4', 99999], ['e4', 1]])).map(x => [x.to, x.lineWidth!])).get('d4')).toBe(ARROW_MAX);
  });

  it('sameArrows vergleicht Inhalt', () => {
    expect(sameArrows(treeArrows(MOVES, null), treeArrows(MOVES, null))).toBeTrue();
    expect(sameArrows(treeArrows(MOVES, null), treeArrows(MOVES, pop([['e4', 1]])))).toBeFalse();
  });
});
