import { clampGoal } from './goal.util';

describe('clampGoal', () => {
  it('clamps into [0, max]', () => {
    expect(clampGoal(50, 100)).toBe(50);
    expect(clampGoal(150, 100)).toBe(100);
    expect(clampGoal(-5, 100)).toBe(0);
  });

  it('rounds to whole numbers', () => {
    expect(clampGoal(3.4, 100)).toBe(3);
    expect(clampGoal(3.6, 100)).toBe(4);
  });

  it('treats NaN / falsy as 0', () => {
    expect(clampGoal(NaN, 100)).toBe(0);
    expect(clampGoal(undefined as unknown as number, 100)).toBe(0);
  });

  it('never exceeds max even when max is 0', () => {
    expect(clampGoal(10, 0)).toBe(0);
  });
});
