import { formatRevengeThemes } from './friend-revenge.component';

describe('formatRevengeThemes', () => {
  it('returns empty string for null/empty input', () => {
    expect(formatRevengeThemes(null)).toBe('');
    expect(formatRevengeThemes('')).toBe('');
  });

  it('joins space-separated themes with commas', () => {
    expect(formatRevengeThemes('fork pin')).toBe('fork, pin');
  });

  it('caps the number of themes at the limit', () => {
    expect(formatRevengeThemes('a b c d e f', 4)).toBe('a, b, c, d');
  });

  it('ignores extra whitespace', () => {
    expect(formatRevengeThemes('fork   pin')).toBe('fork, pin');
  });
});
