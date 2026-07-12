import { MANUAL_KINDS, isMinutesKind } from './manual-activity.util';

describe('manual-activity.util', () => {
  it('isMinutesKind: only OtbGame is counted in games (not minutes)', () => {
    expect(isMinutesKind('OtbGame')).toBeFalse();
    expect(isMinutesKind('OfflinePuzzle')).toBeTrue();
    expect(isMinutesKind('OfflineStudy')).toBeTrue();
    expect(isMinutesKind('Coaching')).toBeTrue();
  });

  it('MANUAL_KINDS covers all four kinds and its minutes flag matches isMinutesKind', () => {
    expect(MANUAL_KINDS.map(k => k.kind)).toEqual(['OtbGame', 'OfflinePuzzle', 'OfflineStudy', 'Coaching']);
    for (const { kind, minutes } of MANUAL_KINDS) {
      expect(minutes).toBe(isMinutesKind(kind));
    }
  });
});
