import { ChessBoardComponent } from './chess-board.component';

/**
 * Testet die Aufräum-Garantie der Breite-0-Retry-Schleife (requestAnimationFrame):
 * Wird die Komponente während der Retries zerstört, darf kein weiterer initBoard-Lauf
 * Chessground/ResizeObserver auf dem abgekoppelten Element aufbauen.
 */
describe('ChessBoardComponent RAF cleanup', () => {
  let rafCb: FrameRequestCallback | null;
  let rafSpy: jasmine.Spy;
  let cancelSpy: jasmine.Spy;

  beforeEach(() => {
    rafCb = null;
    rafSpy = spyOn(window, 'requestAnimationFrame').and.callFake((cb: FrameRequestCallback) => { rafCb = cb; return 42; });
    cancelSpy = spyOn(window, 'cancelAnimationFrame');
  });

  function make(): any {
    const c = new ChessBoardComponent();
    // parentElement mit Breite 0 → erzwingt den Retry-Pfad (requestAnimationFrame).
    c.boardEl = { nativeElement: { parentElement: { clientWidth: 0 }, clientWidth: 0, style: {} } } as any;
    return c;
  }

  it('cancels the pending RAF on destroy and the late callback is a no-op', () => {
    const c = make();
    c.ngAfterViewInit();                 // hostWidth 0 → schedules a retry
    expect(rafSpy).toHaveBeenCalledTimes(1);

    c.ngOnDestroy();                     // destroyed → cancel the pending frame
    expect(cancelSpy).toHaveBeenCalledWith(42);

    // Die noch zugestellte RAF-Callback darf nichts mehr aufbauen.
    rafCb!(0);
    expect(rafSpy).toHaveBeenCalledTimes(1);   // kein erneutes Scheduling
    expect((c as any).ground).toBeUndefined();
    expect((c as any).resizeObserver).toBeUndefined();
  });
});
