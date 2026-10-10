import {
  maxStars, turnsEveryMove, STAR_STAGES, STAR_SVG, generateStarPuzzle, reachable, reachableStars, seededRng, solveStars, squareIndex, squareName,
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

  it('der Springer springt, ohne Blockade', () => {
    expect(reachable('N', squareIndex('a1'), new Set()).map(squareName).sort()).toEqual(['b3', 'c2']);
    expect(reachableStars('N', squareIndex('a1'), new Set(sq(['b3', 'd4'])))).toEqual(sq(['b3']));
  });

  it('zählt die Reihenfolgen: eine, keine, zwei', () => {
    // a1 → a3 → c3: c3 ist vom Start aus nicht erreichbar.
    expect(solveStars('R', squareIndex('a1'), sq(['a3', 'c3']))).toEqual([sq(['a3', 'c3'])]);
    // a3 und c1 liegen auf keiner gemeinsamen Linie.
    expect(solveStars('R', squareIndex('a1'), sq(['a3', 'c1']))).toEqual([]);
    // Rundherum geht es in beide Richtungen.
    expect(solveStars('R', squareIndex('a1'), sq(['a3', 'c3', 'c1']))?.length).toBe(2);
  });

  it('jede Stufe bekommt eindeutige Aufgaben mit der richtigen Zahl Sterne, die nie geradeaus weitergehen', () => {
    const rng = seededRng(42);
    for (const stage of STAR_STAGES) {
      for (const count of stage.counts) {
        const p = generateStarPuzzle(stage.piece, count, rng);
        expect(p).withContext(`Stufe ${stage.stage}`).not.toBeNull();
        expect(p!.stars.length).toBe(count);
        expect(turnsEveryMove(p!.piece, p!.start, p!.solution)).toBeTrue();
        expect(p!.stars).not.toContain(p!.start);
        expect(solveStars(p!.piece, p!.start, p!.stars, 3)).toEqual([p!.solution]);
      }
    }
  });

  it('die Kurve: Stufe 1 = 2, 2, 3, 3, 4, 4 Sterne, alle vier Stufen eins mehr', () => {
    expect(STAR_STAGES[0].counts).toEqual([2, 2, 3, 3, 4, 4]);
    expect(STAR_STAGES[4].counts).toEqual([3, 3, 4, 4, 5, 5]);
    expect(STAR_STAGES[19].counts).toEqual([6, 6, 7, 7, 8, 8]);
  });

  it('geradeaus weiter zählt nicht, zurück und abbiegen schon', () => {
    const a = squareIndex;
    expect(turnsEveryMove('R', a('a1'), [a('a3'), a('a5')])).toBeFalse();
    expect(turnsEveryMove('R', a('a1'), [a('a3'), a('a2')])).toBeTrue();
    expect(turnsEveryMove('R', a('a1'), [a('a3'), a('c3')])).toBeTrue();
    expect(turnsEveryMove('N', a('a1'), [a('b3'), a('c5')])).toBeFalse();
    expect(turnsEveryMove('N', a('a1'), [a('b3'), a('d4')])).toBeTrue();
  });

  it('viele Sterne: bis 63 (Läufer 31) mit einer legalen Lösung, die nach jedem Stern abbiegt', () => {
    const rng = seededRng(7);
    for (const [piece, count] of [['Q', 63], ['N', 63], ['N', 62], ['R', 40], ['B', 30], ['N', 15], ['Q', 13]] as const) {
      const p = generateStarPuzzle(piece, count, rng, 2000);
      expect(p).withContext(`${piece} ${count}`).not.toBeNull();
      expect(new Set(p!.stars).size).toBe(count);
      expect(p!.stars).not.toContain(p!.start);
      expect(turnsEveryMove(piece, p!.start, p!.solution)).toBeTrue();
      let pos = p!.start;
      const left = new Set(p!.stars);
      for (const sq of p!.solution) {
        expect(reachableStars(piece, pos, left)).toContain(sq);
        left.delete(sq);
        pos = sq;
      }
      if (p!.unique) expect(solveStars(piece, p!.start, p!.stars, 2, 1e6)).toEqual([p!.solution]);
    }
    expect(generateStarPuzzle('B', 32, rng)).toBeNull();
    expect(maxStars('B')).toBe(31);
  });

  it('nur Turm, Läufer, Springer und Dame', () => {
    expect([...new Set(STAR_STAGES.map(s => s.piece))].sort()).toEqual(['B', 'N', 'Q', 'R']);
  });

  it('die Stellung trägt nur die Figur', () => {
    expect(starFen('R', 0)).toBe('8/8/8/8/8/8/8/R7 w - - 0 1');
    expect(starFen('N', squareIndex('e4'))).toBe('8/8/8/8/4N3/8/8/8 w - - 0 1');
  });

  it('der Stern ist ein Polygon im Feldraster', () => {
    expect(STAR_SVG).toMatch(/^<polygon points="[\d., ]+"/);
  });
});
