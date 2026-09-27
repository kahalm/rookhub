import { nextUnsolved } from './course-play.component';

describe('nextUnsolved (Kinderkurs)', () => {
  const lines = [{ id: 10 }, { id: 11 }, { id: 12 }];

  it('nimmt die erste ungeloeste ab der Stelle', () => {
    expect(nextUnsolved(lines, new Set(), 0)).toBe(0);
    expect(nextUnsolved(lines, new Set([10]), 0)).toBe(1);
    expect(nextUnsolved(lines, new Set([11]), 1)).toBe(2);
  });

  it('faengt am Ende von vorn an', () => {
    expect(nextUnsolved(lines, new Set([12]), 2)).toBe(0);
    expect(nextUnsolved(lines, new Set([10, 12]), 3)).toBe(1);
  });

  it('alles geloest → -1', () => {
    expect(nextUnsolved(lines, new Set([10, 11, 12]), 0)).toBe(-1);
    expect(nextUnsolved([], new Set(), 0)).toBe(-1);
  });
});
