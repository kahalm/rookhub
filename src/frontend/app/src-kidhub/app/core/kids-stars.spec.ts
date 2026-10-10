import {
  STAR_STAGES, STAR_SVG, generateStarPuzzle, reachable, reachableStars, seededRng, solveStars, squareIndex, squareName,
  starFen,
} from './kids-stars';

describe('kids-stars', () => {
  const sq = (names: string[]) => names.map(squareIndex);

  it('Feldnamen hin und zurück', () => {
    expect(squareName(0)).toBe('a1');
    expect(squareName(28)).toBe('e4');
    expect(squareName(63)).toBe('h8');
    expect(squareIndex('e4')).toBe(28);
    expect(squareIndex('z9')).toBe(-1);
  });

  it('ein Stern hält Turm, Läufer und Dame auf — sie dürfen auf ihn, nicht darüber', () => {
    const rook = reachable('R', squareIndex('a1'), new Set(sq(['a3']))).map(squareName);
    expect(rook).toContain('a3');
    expect(rook).not.toContain('a4');
    expect(rook).toContain('h1');
    const bishop = reachable('B', squareIndex('c1'), new Set(sq(['e3']))).map(squareName);
    expect(bishop).toContain('e3');
    expect(bishop).not.toContain('f4');
  });

  it('Springer und König springen bzw. gehen ein Feld, ohne Blockade', () => {
    expect(reachable('N', squareIndex('a1'), new Set()).map(squareName).sort()).toEqual(['b3', 'c2']);
    expect(reachable('K', squareIndex('a1'), new Set()).map(squareName).sort()).toEqual(['a2', 'b1', 'b2']);
    expect(reachableStars('N', squareIndex('a1'), new Set(sq(['b3', 'd4'])))).toEqual(sq(['b3']));
  });

  it('zählt die Reihenfolgen: eine, keine, zwei', () => {
    // a1 → a3 → c3: c3 ist vom Start aus nicht erreichbar.
    expect(solveStars('R', squareIndex('a1'), sq(['a3', 'c3']))).toEqual([sq(['a3', 'c3'])]);
    // a3 und c1 liegen auf keiner gemeinsamen Linie.
    expect(solveStars('R', squareIndex('a1'), sq(['a3', 'c1']))).toEqual([]);
    // Rundherum geht es in beide Richtungen.
    expect(solveStars('R', squareIndex('a1'), sq(['a3', 'c3', 'c1'])).length).toBe(2);
  });

  it('jede Stufe bekommt eindeutige Aufgaben mit der richtigen Zahl Sterne', () => {
    const rng = seededRng(42);
    for (const stage of STAR_STAGES) {
      for (let i = 0; i < 5; i++) {
        const p = generateStarPuzzle(stage.piece, stage.stars, rng);
        expect(p).withContext(`Stufe ${stage.stage}`).not.toBeNull();
        expect(p!.stars.length).toBe(stage.stars);
        expect(p!.stars).not.toContain(p!.start);
        expect(solveStars(p!.piece, p!.start, p!.stars, 3)).toEqual([p!.solution]);
      }
    }
  });

  it('die Stellung trägt nur die Figur', () => {
    expect(starFen('R', 0)).toBe('8/8/8/8/8/8/8/R7 w - - 0 1');
    expect(starFen('N', squareIndex('e4'))).toBe('8/8/8/8/4N3/8/8/8 w - - 0 1');
  });

  it('der Stern ist ein Polygon im Feldraster', () => {
    expect(STAR_SVG).toMatch(/^<polygon points="[\d., ]+"/);
  });
});
