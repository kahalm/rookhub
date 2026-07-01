import { parseMoveShapes } from './move-shapes.util';

describe('parseMoveShapes', () => {
  it('parst Pfeile (mit dest) und Feld-Markierungen (ohne dest) je Ply', () => {
    const json = JSON.stringify({
      '0': [{ o: 'd8', d: 'g8', b: 'green' }, { o: 'g8', b: 'red' }],
      '7': [{ o: 'g6', d: 'f5', b: 'red' }, { o: 'g6', d: 'h5', b: 'red' }],
    });
    const map = parseMoveShapes(json);
    expect(map[0]).toEqual([
      { orig: 'd8', dest: 'g8', brush: 'green' },
      { orig: 'g8', brush: 'red' },
    ] as any);
    expect(map[7].length).toBe(2);
    expect(map[7][0]).toEqual({ orig: 'g6', dest: 'f5', brush: 'red' } as any);
  });

  it('liefert bei null/leer/ungültig eine leere Map (wirft nie)', () => {
    expect(parseMoveShapes(null)).toEqual({});
    expect(parseMoveShapes(undefined)).toEqual({});
    expect(parseMoveShapes('')).toEqual({});
    expect(parseMoveShapes('{kaputt')).toEqual({});
  });

  it('nimmt default-Brush green, wenn keiner angegeben ist', () => {
    const map = parseMoveShapes(JSON.stringify({ '-1': [{ o: 'e4' }] }));
    expect(map[-1]).toEqual([{ orig: 'e4', brush: 'green' } as any]);
  });
});
