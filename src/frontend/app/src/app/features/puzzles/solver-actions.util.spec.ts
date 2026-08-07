import { SOLVER_ACTION_KEYS, canMouseslip, canReset, isSolvingState } from './solver-actions.util';

describe('solver-actions.util', () => {
  it('isSolvingState covers exactly the three solving states', () => {
    expect(isSolvingState('AWAITING_USER_MOVE')).toBeTrue();
    expect(isSolvingState('THINKING')).toBeTrue();
    expect(isSolvingState('PLAYING')).toBeTrue();
    expect(isSolvingState('SOLVED')).toBeFalse();
    expect(isSolvingState('FAILED')).toBeFalse();
    expect(isSolvingState('LOADING')).toBeFalse();
  });

  it('canReset: nothing to reset before the first own move', () => {
    expect(canReset('AWAITING_USER_MOVE', false)).toBeFalse();
    expect(canReset('AWAITING_USER_MOVE', true)).toBeTrue();
    expect(canReset('PLAYING', false)).toBeTrue();
    expect(canReset('THINKING', false)).toBeTrue();
  });

  it('canMouseslip needs the parent flag plus a state where a move can be taken back', () => {
    expect(canMouseslip('PLAYING', false, true, false)).toBeFalse();      // Eltern sagt nein
    expect(canMouseslip('AWAITING_USER_MOVE', true, false, false)).toBeFalse();
    expect(canMouseslip('AWAITING_USER_MOVE', true, true, false)).toBeTrue();
    expect(canMouseslip('PLAYING', true, false, false)).toBeTrue();
    expect(canMouseslip('THINKING', true, false, false)).toBeFalse();     // nur Endless …
    expect(canMouseslip('THINKING', true, false, true)).toBeTrue();       // … mit showMouseslipInThinking
  });

  it('every mode has its own translation keys', () => {
    for (const mode of ['standard', 'endless', 'book'] as const) {
      const k = SOLVER_ACTION_KEYS[mode];
      expect(k.reset.length).toBeGreaterThan(0);
      expect(k.mouseslip.length).toBeGreaterThan(0);
      expect(k.giveUp.length).toBeGreaterThan(0);
    }
    expect(SOLVER_ACTION_KEYS.endless.reset).toBe('endless.game.reset');
    expect(SOLVER_ACTION_KEYS.book.giveUp).toBe('book.actions.giveUp');
  });
});
