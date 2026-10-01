import { SOLVER_ACTION_KEYS, SOLVER_EVAL_KEYS, canMouseslip, canReset, isSolvingState } from './solver-actions.util';

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

  // Codereview F2-012: Endless zeigte auf Deutsch „Reset"/„Mouseslip"/„Give Up"/„Show Eval", das Buch
  // „Mausverrutscher" — dieselben Knöpfe wie auf /puzzles, nur mit eigenen, auseinandergelaufenen Keys.
  it('all three solvers label the same action with the same key', () => {
    for (const mode of ['standard', 'endless', 'book'] as const) {
      expect(SOLVER_ACTION_KEYS[mode]).withContext(mode).toEqual({
        reset: 'puzzles.actions.reset', mouseslip: 'puzzles.actions.mouseslip', giveUp: 'puzzles.actions.giveUp',
      });
      expect(SOLVER_EVAL_KEYS[mode]).withContext(mode).toEqual({
        show: 'puzzles.eval.show', hide: 'puzzles.eval.hide', start: 'puzzles.eval.start', now: 'puzzles.eval.now',
      });
    }
  });
});
