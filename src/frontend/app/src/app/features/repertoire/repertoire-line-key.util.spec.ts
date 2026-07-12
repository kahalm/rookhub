import { lineKeyFromSans } from './repertoire-line-key.util';

describe('lineKeyFromSans', () => {
  it('is deterministic for the same move sequence', () => {
    expect(lineKeyFromSans(['e4', 'e5', 'Nf3'])).toBe(lineKeyFromSans(['e4', 'e5', 'Nf3']));
  });

  it('differs for different move sequences', () => {
    expect(lineKeyFromSans(['e4', 'e5'])).not.toBe(lineKeyFromSans(['e4', 'c5']));
  });

  it('normalizes SAN (strips check/mate/annotation) so equivalent lines share a key', () => {
    expect(lineKeyFromSans(['Qh5+', 'Ke7', 'Qxf7#'])).toBe(lineKeyFromSans(['Qh5', 'Ke7', 'Qxf7']));
    expect(lineKeyFromSans(['e4!', 'e5?'])).toBe(lineKeyFromSans(['e4', 'e5']));
  });

  it('is order-sensitive', () => {
    expect(lineKeyFromSans(['e4', 'e5'])).not.toBe(lineKeyFromSans(['e5', 'e4']));
  });

  it('produces a stable "l"-prefixed base36 key (incl. empty line)', () => {
    expect(lineKeyFromSans([])).toMatch(/^l[0-9a-z]+$/);
    expect(lineKeyFromSans(['e4'])).toMatch(/^l[0-9a-z]+$/);
  });
});
