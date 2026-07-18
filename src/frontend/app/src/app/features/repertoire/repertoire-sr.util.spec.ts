import { isStateDue, isStateLearnable, earliestDueIso, relDueLabel, shuffle, applySrReview, applyPromote, hoursOfLevel, DEFAULT_SR_LEVELS } from './repertoire-sr.util';
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

/** Lokale SR-Übergänge (Offline-Spiegel des Backends ScheduleLevel/PromoteAsync). */
describe('applySrReview / applyPromote / hoursOfLevel', () => {
  const NOW = Date.parse('2026-07-18T12:00:00Z');
  const base: LineStateDto = {
    lineKey: 'k', level: 3, reps: 5, lapses: 1,
    dueAt: '2026-07-18T00:00:00Z', lastReviewedAt: null, inPool: true, paused: false,
  };

  it('hoursOfLevel converts units (h/d/w/mo=30d)', () => {
    expect(hoursOfLevel({ value: 4, unit: 'h' })).toBe(4);
    expect(hoursOfLevel({ value: 2.5, unit: 'd' })).toBe(60);
    expect(hoursOfLevel({ value: 1, unit: 'w' })).toBe(168);
    expect(hoursOfLevel({ value: 3, unit: 'mo' })).toBe(3 * 720);
  });

  it('correct: level +1 (max 9), reps +1, dueAt = now + interval of the NEW level', () => {
    const st = applySrReview(base, 'k', true, DEFAULT_SR_LEVELS, NOW);
    expect(st.level).toBe(4);
    expect(st.reps).toBe(6);
    expect(st.lapses).toBe(1);
    // Stufe 4 = 2.5 Tage = 60 h
    expect(Date.parse(st.dueAt) - NOW).toBe(60 * 3_600_000);
    expect(st.inPool).toBeTrue();
    expect(st.paused).toBeFalse();
    expect(st.lastReviewedAt).toBe(new Date(NOW).toISOString());
  });

  it('correct caps at level 9', () => {
    const st = applySrReview({ ...base, level: 9 }, 'k', true, DEFAULT_SR_LEVELS, NOW);
    expect(st.level).toBe(9);
  });

  it('wrong: back to level 1, lapses +1, due after the first interval', () => {
    const st = applySrReview(base, 'k', false, DEFAULT_SR_LEVELS, NOW);
    expect(st.level).toBe(1);
    expect(st.lapses).toBe(2);
    expect(st.reps).toBe(5);
    expect(Date.parse(st.dueAt) - NOW).toBe(4 * 3_600_000);   // Stufe 1 = 4 h
  });

  it('missing previous state starts fresh (correct → level 1)', () => {
    const st = applySrReview(undefined, 'neu', true, DEFAULT_SR_LEVELS, NOW);
    expect(st.level).toBe(1);
    expect(st.reps).toBe(1);
    expect(st.lapses).toBe(0);
  });

  it('applyPromote puts the line in the pool, immediately due, counters preserved', () => {
    const st = applyPromote({ ...base, inPool: false, paused: true }, 'k', NOW);
    expect(st.inPool).toBeTrue();
    expect(st.paused).toBeFalse();
    expect(st.level).toBe(3);
    expect(st.dueAt).toBe(new Date(NOW).toISOString());
    const fresh = applyPromote(undefined, 'neu', NOW);
    expect(fresh.level).toBe(0);
    expect(fresh.reps).toBe(0);
  });
});
