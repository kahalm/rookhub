import { defaultLockDay, isIndefiniteLock, lockEndFromDate, todayIso } from './lock-user.util';

/** Codereview P-4: Sperrende aus dem Datumsfeld, unbefristet erkennen. */
describe('lock-user.util', () => {
  it('„bis einschließlich 3. 11.“ endet zu Beginn des 4. 11. in Ortszeit', () => {
    expect(lockEndFromDate('2026-11-03')).toBe(new Date(2026, 10, 4, 0, 0, 0, 0).toISOString());
    expect(lockEndFromDate('2026-12-31')).toBe(new Date(2027, 0, 1).toISOString());
    expect(lockEndFromDate('')).toBeNull();
    expect(lockEndFromDate('3.11.2026')).toBeNull();
  });

  it('unbefristet ist das Jahr 9999 des Servers', () => {
    expect(isIndefiniteLock('9999-12-31T00:00:00Z')).toBeTrue();
    expect(isIndefiniteLock('2026-11-04T00:00:00Z')).toBeFalse();
    expect(isIndefiniteLock(null)).toBeFalse();
  });

  it('heute und der Vorschlag in sieben Tagen im Format des Datumsfelds', () => {
    const now = new Date(2026, 9, 26, 15, 0);
    expect(todayIso(now)).toBe('2026-10-26');
    expect(defaultLockDay(now)).toBe('2026-11-02');
  });
});
