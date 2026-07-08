import { isStateDue, isStateLearnable, earliestDueIso, relDueLabel, shuffle } from './repertoire-sr.util';
import { LineStateDto } from './repertoire-training.service';

function st(overrides: Partial<LineStateDto> = {}): LineStateDto {
  return {
    lineKey: 'k', level: 1, reps: 1, lapses: 0,
    dueAt: new Date().toISOString(), lastReviewedAt: null,
    inPool: true, paused: false, ...overrides,
  };
}

describe('repertoire-sr.util', () => {
  const now = 1_000_000_000_000;

  describe('isStateDue', () => {
    it('true when in pool, not paused and dueAt <= now', () => {
      expect(isStateDue(st({ dueAt: new Date(now - 1).toISOString() }), now)).toBeTrue();
    });
    it('false for undefined state (never learned)', () => {
      expect(isStateDue(undefined, now)).toBeFalse();
    });
    it('false when not in pool, paused, or scheduled ahead', () => {
      expect(isStateDue(st({ inPool: false, dueAt: new Date(now - 1).toISOString() }), now)).toBeFalse();
      expect(isStateDue(st({ paused: true, dueAt: new Date(now - 1).toISOString() }), now)).toBeFalse();
      expect(isStateDue(st({ dueAt: new Date(now + 1).toISOString() }), now)).toBeFalse();
    });
  });

  describe('isStateLearnable', () => {
    it('true when no state (never learned)', () => {
      expect(isStateLearnable(undefined)).toBeTrue();
    });
    it('true when not in pool and not paused', () => {
      expect(isStateLearnable(st({ inPool: false }))).toBeTrue();
    });
    it('false when in pool', () => {
      expect(isStateLearnable(st({ inPool: true }))).toBeFalse();
    });
    it('false when paused (even if not in pool)', () => {
      expect(isStateLearnable(st({ inPool: false, paused: true }))).toBeFalse();
    });
  });

  describe('earliestDueIso', () => {
    it('returns the earliest dueAt among eligible pool states', () => {
      const a = st({ dueAt: new Date(now + 5000).toISOString() });
      const b = st({ dueAt: new Date(now + 1000).toISOString() });
      expect(earliestDueIso([a, b])).toBe(new Date(now + 1000).toISOString());
    });
    it('ignores undefined, not-in-pool and paused states', () => {
      const paused = st({ paused: true, dueAt: new Date(now).toISOString() });
      const notPool = st({ inPool: false, dueAt: new Date(now).toISOString() });
      const good = st({ dueAt: new Date(now + 9000).toISOString() });
      expect(earliestDueIso([undefined, paused, notPool, good])).toBe(new Date(now + 9000).toISOString());
    });
    it('null when nothing is eligible', () => {
      expect(earliestDueIso([undefined, st({ inPool: false })])).toBeNull();
    });
  });

  describe('relDueLabel', () => {
    it('empty for null', () => { expect(relDueLabel(null, now)).toBe(''); });
    it('< 1 h for under an hour', () => {
      expect(relDueLabel(new Date(now + 30 * 60_000).toISOString(), now)).toBe('< 1 h');
    });
    it('hours under 48h', () => {
      expect(relDueLabel(new Date(now + 4 * 3_600_000).toISOString(), now)).toBe('4 h');
    });
    it('days under 14d', () => {
      expect(relDueLabel(new Date(now + 3 * 24 * 3_600_000).toISOString(), now)).toBe('3 d');
    });
    it('weeks under 9w', () => {
      expect(relDueLabel(new Date(now + 14 * 24 * 3_600_000).toISOString(), now)).toBe('2 w');
    });
    it('months beyond 9w', () => {
      expect(relDueLabel(new Date(now + 90 * 24 * 3_600_000).toISOString(), now)).toBe('3 mo');
    });
  });

  describe('shuffle', () => {
    it('returns a new array with the same elements (permutation)', () => {
      const input = [1, 2, 3, 4, 5];
      const out = shuffle(input);
      expect(out).not.toBe(input);            // Kopie, kein in-place
      expect(out.slice().sort((a, b) => a - b)).toEqual([1, 2, 3, 4, 5]);
      expect(input).toEqual([1, 2, 3, 4, 5]);  // Original unverändert
    });
    it('handles empty and single-element arrays', () => {
      expect(shuffle([])).toEqual([]);
      expect(shuffle([7])).toEqual([7]);
    });
  });
});
