import { BoardFsActionsComponent } from './board-fs-actions.component';

describe('BoardFsActionsComponent', () => {
  function make(): BoardFsActionsComponent {
    const c = new BoardFsActionsComponent();
    c.state = 'AWAITING_USER_MOVE';
    return c;
  }

  it('is only visible while solving and not in review mode', () => {
    const c = make();
    expect(c.visible).toBeTrue();

    c.state = 'SOLVED';
    expect(c.visible).toBeFalse();

    c.state = 'PLAYING';
    expect(c.visible).toBeTrue();
    c.reviewMode = true;
    expect(c.visible).toBeFalse();
  });

  it('shows the hint button only while hints are left', () => {
    const c = make();
    expect(c.showHint).toBeFalse();                 // ohne Tipps

    c.hasHints = true;
    c.canShowMoreHints = true;
    expect(c.showHint).toBeTrue();

    c.canShowMoreHints = false;                     // alle Stufen aufgedeckt
    expect(c.showHint).toBeFalse();
  });

  it('hint title switches from show to next once a hint was taken', () => {
    const c = make();
    expect(c.hintTitle).toBe('puzzles.hints.show');
    c.hintLevel = 1;
    expect(c.hintTitle).toBe('puzzles.hints.next');
  });

  it('reset appears once there is something to reset', () => {
    const c = make();
    expect(c.showReset).toBeFalse();
    c.hasMadeFirstMove = true;
    expect(c.showReset).toBeTrue();
  });

  it('mouseslip follows the same rule as the normal action row', () => {
    const c = make();
    expect(c.showMouseslipAction).toBeFalse();

    c.showMouseslip = true;
    expect(c.showMouseslipAction).toBeFalse();      // noch kein eigener Zug
    c.hasMadeFirstMove = true;
    expect(c.showMouseslipAction).toBeTrue();

    c.state = 'THINKING';
    c.hasMadeFirstMove = false;
    expect(c.showMouseslipAction).toBeFalse();      // Standard/Buch
    c.showMouseslipInThinking = true;
    expect(c.showMouseslipAction).toBeTrue();       // Endless
  });

  it('uses the translation keys of its mode', () => {
    const c = make();
    expect(c.keys.reset).toBe('puzzles.actions.reset');
    c.mode = 'endless';
    expect(c.keys.giveUp).toBe('endless.game.giveUp');
    c.mode = 'book';
    expect(c.keys.mouseslip).toBe('book.actions.mouseslip');
  });

  it('emits the four solver actions', () => {
    const c = make();
    const seen: string[] = [];
    c.hintClicked.subscribe(() => seen.push('hint'));
    c.resetClicked.subscribe(() => seen.push('reset'));
    c.mouseslipClicked.subscribe(() => seen.push('mouseslip'));
    c.giveUpClicked.subscribe(() => seen.push('giveUp'));

    c.hintClicked.emit();
    c.resetClicked.emit();
    c.mouseslipClicked.emit();
    c.giveUpClicked.emit();

    expect(seen).toEqual(['hint', 'reset', 'mouseslip', 'giveUp']);
  });
});
