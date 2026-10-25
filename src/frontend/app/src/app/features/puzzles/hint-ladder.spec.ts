import { HintLadder } from './hint-ladder';

describe('HintLadder', () => {
  it('deckt Stufe für Stufe auf, nie über die Liste hinaus', () => {
    const h = new HintLadder();
    h.reveal(2, true);
    h.reveal(2, true);
    h.reveal(2, true);
    expect(h.level).toBe(2);
    expect(h.shown(['a', 'b', 'c'])).toEqual(['a', 'b']);
    expect(h.canReveal(2)).toBeFalse();
  });

  it('maxLevel überlebt den Zug-Wechsel und zählt nur auf dem Lösungsweg', () => {
    const h = new HintLadder();
    h.reveal(3, true);
    h.reveal(3, true);
    h.level = 0;                 // neuer erwarteter Zug
    h.reveal(3, true);
    expect(h.maxLevel).toBe(2);
    h.level = 0;
    h.reveal(3, false);
    h.reveal(3, false);
    h.reveal(3, false);
    expect(h.maxLevel).toBe(2);
    expect(h.failsSolve).toBeFalse();
    h.level = 0;
    h.reveal(3, true); h.reveal(3, true); h.reveal(3, true);
    expect(h.failsSolve).toBeTrue();
  });

  it('effective: off-path vor Anarchy vor normal; leere Sonderlisten fallen durch', () => {
    const off = ['o'], ep = ['e'], n = ['n'];
    expect(HintLadder.effective(false, off, true, ep, n)).toBe(off);
    expect(HintLadder.effective(false, [], true, ep, n)).toBe(ep);
    expect(HintLadder.effective(true, off, true, ep, n)).toBe(ep);
    expect(HintLadder.effective(true, off, true, [], n)).toBe(n);
    expect(HintLadder.effective(true, off, false, ep, n)).toBe(n);
  });
});
