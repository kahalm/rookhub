import { of } from 'rxjs';
import { BookSolveModeController, BookSolveModeHost } from './book-solve-mode';

describe('BookSolveModeController (Zerlegung Z18)', () => {
  function host(over: Partial<Record<string, unknown>> = {}): BookSolveModeHost & { setups: number; held: number } {
    const store: Record<string, 'training' | 'easy'> = {};
    const h: any = {
      inCourse: false, inWeekly: false, isDaily: false, course: { bookId: null }, displayBookName: 'Buch',
      translate: { instant: (k: string) => k },
      solveMode: {
        ensure: jasmine.createSpy('ensure').and.callFake((scope: string) => of(store[scope] ?? 'easy')),
        set: (scope: string, m: 'training' | 'easy') => { store[scope] = m; },
        levelFor: (m: string) => (m === 'easy' ? 0 : 1), modeForLevel: (l: number) => (l > 0 ? 'training' : 'easy'),
      },
      snackbar: { info: jasmine.createSpy('info') },
      visualizationMode: 3, awaitingSolveMode: false, setups: 0, held: 0,
      holdForSolveMode() { h.held++; h.awaitingSolveMode = true; },
      setupPuzzle() { h.setups++; },
      ...over,
    };
    return h;
  }
  const P: any = { id: 1, fen: 'x', moves: '' };

  it('Bereich: Kurs je Buch, sonst daily/book; Wochenpost ohne Bereich', () => {
    expect(new BookSolveModeController(host({ inWeekly: true })).scope).toBeNull();
    expect(new BookSolveModeController(host({ inCourse: true, course: { bookId: null } })).scope).toBeNull();
    expect(new BookSolveModeController(host({ isDaily: true })).scope).toBe('daily');
    expect(new BookSolveModeController(host()).scope).toBe('book');
    expect(new BookSolveModeController(host({ inCourse: true, course: { bookId: 7 } })).scope).toContain('7');
  });

  it('fragt einmal je Bereich, setzt danach mit der Wahl auf und wendet sie als Stufe an', () => {
    const h = host();
    const s = new BookSolveModeController(h);
    expect(s.askIfNeeded(P)).toBeTrue();
    expect(h.held).toBe(1);
    expect(h.awaitingSolveMode).toBeFalse();
    expect(s.choice).toBe('easy');
    expect(h.setups).toBe(1);
    expect(s.askIfNeeded(P)).toBeFalse();
    s.apply();
    expect(h.visualizationMode).toBe(0);
  });

  it('per Link festgelegt: kein Dialog, keine Stufe, kein Umschalter', () => {
    const h = host();
    const s = new BookSolveModeController(h);
    s.suppressed = true;
    expect(s.askIfNeeded(P)).toBeFalse();
    s.apply();
    expect(h.visualizationMode).toBe(3);
    expect(s.showSwitch).toBeFalse();
    s.toggle();
    expect(h.snackbar.info).not.toHaveBeenCalled();
  });

  it('Umschalten und Stufe von Hand ziehen die gemerkte Wahl mit; gemeldet wird die wirksame Stufe', () => {
    const h = host();
    const s = new BookSolveModeController(h);
    s.toggle();
    expect(s.choice).toBe('easy');
    s.followLevel(2);
    expect(s.choice).toBe('training');
    h.visualizationMode = 0;
    expect(s.attemptMode).toBe('easy');
  });
});
