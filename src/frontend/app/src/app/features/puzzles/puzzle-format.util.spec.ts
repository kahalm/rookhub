import { formatPuzzleTime } from './puzzle-format.util';

describe('formatPuzzleTime', () => {
  it('shows seconds with an s suffix below a minute', () => {
    expect(formatPuzzleTime(0)).toBe('0s');
    expect(formatPuzzleTime(45)).toBe('45s');
    expect(formatPuzzleTime(59)).toBe('59s');
  });

  it('shows m:ss from a minute upwards, zero-padding seconds', () => {
    expect(formatPuzzleTime(60)).toBe('1:00');
    expect(formatPuzzleTime(65)).toBe('1:05');
    expect(formatPuzzleTime(125)).toBe('2:05');
    expect(formatPuzzleTime(600)).toBe('10:00');
  });
});
