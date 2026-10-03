import { DEFAULT_FACE, clampFace, faceBox } from './face';

describe('face (Kreis ums Gesicht)', () => {
  // Dieselben LITERALEN Werte wie in tests/RookHub.Api.Tests/ClubFaceTests.cs (ClubFace.Normalize) — die Regel steht auf
  // beiden Seiten, und keiner der Tests importiert die Gegenseite.
  it('clampFace holt den Kreis ins Bild', () => {
    expect(clampFace({ x: 0.5, y: 0.4, r: 0.28 }, 900, 1200)).toEqual({ x: 0.5, y: 0.4, r: 0.28 });          // passt: bleibt
    expect(clampFace({ x: 0.05, y: 0.02, r: 0.3 }, 900, 1200)).toEqual({ x: 0.3, y: 0.225, r: 0.3 });        // links oben hinein
    expect(clampFace({ x: 0.99, y: 0.99, r: 0.9 }, 1200, 800)).toEqual({ x: 0.6667, y: 0.5, r: 0.5 });       // zu groß
    expect(clampFace({ x: 0.5, y: 0.5, r: 0.001 }, 1000, 1000)).toEqual({ x: 0.5, y: 0.5, r: 0.05 });        // zu klein
    expect(clampFace({ x: 0, y: 0, r: 0.5 }, 1200, 800)).toEqual({ x: 0.3333, y: 0.5, r: 0.5 });
  });

  it('ohne Zahlen oder ohne Bild gibt es keinen Kreis', () => {
    expect(clampFace({ x: NaN, y: 0.5, r: 0.2 }, 900, 1200)).toBeNull();
    expect(clampFace({ x: 0.5, y: Infinity, r: 0.2 }, 900, 1200)).toBeNull();
    expect(clampFace({ x: 0.5, y: 0.5, r: NaN }, 900, 1200)).toBeNull();
    expect(clampFace({ x: 0.5, y: 0.5, r: 0.2 }, 0, 1200)).toBeNull();
  });

  it('der Kreis, mit dem ein Bild beginnt, passt in Hoch- und Querformat', () => {
    expect(clampFace(DEFAULT_FACE, 900, 1200)).toEqual(DEFAULT_FACE);
    expect(clampFace(DEFAULT_FACE, 1200, 800)).toEqual(DEFAULT_FACE);
  });

  it('faceBox: der Kasten des Kreises in Prozent der Bildfläche — in Pixeln ein Quadrat', () => {
    const box = faceBox({ x: 0.5, y: 0.4, r: 0.28 }, 300, 400);                          // Radius 84 px
    expect([box.left, box.top, box.width, box.height].map(v => Math.round(v * 100) / 100)).toEqual([22, 19, 56, 42]);
    expect(Math.round(box.width * 300 / 100)).toBe(Math.round(box.height * 400 / 100));  // 168 px breit und hoch
  });
});
