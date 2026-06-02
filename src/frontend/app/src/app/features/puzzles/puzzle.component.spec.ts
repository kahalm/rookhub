import { fakeAsync, tick } from '@angular/core/testing';
import { PuzzleComponent } from './puzzle.component';

/**
 * Fokussierter Test des Aufgeben-Verhaltens (giveUp) ohne TestBed/Template:
 * Aufgeben soll die Lösung ab der Anfangsstellung automatisch durchspielen
 * (NICHT bloß zurücksetzen wie resetPuzzle).
 */
function makeComponent(): any {
  const prefs: any = { boardTheme: 'green', pieceSet: 'cburnett', themeMode: 'fixed', stockfishDepth: 12, visualization: 0 };
  const stockfish: any = { init: () => Promise.resolve(), getEval: () => Promise.resolve('') };
  const auth: any = { isLoggedIn: false };
  const puzzleService: any = {};
  const router: any = { navigate: jasmine.createSpy('navigate') };
  const route: any = { snapshot: { paramMap: { get: () => null } } };
  const dialog: any = {};
  const offline: any = { puzzleCount: 0, endlessRuns: 0 };
  return new PuzzleComponent(puzzleService, stockfish, auth, prefs, router, route, dialog, offline);
}

const PUZZLE = { id: 1, fen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', moves: 'e2e4 e7e5 g1f3', rating: 1500 };

describe('PuzzleComponent give-up', () => {
  it('plays the solution from the start position move by move', fakeAsync(() => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.attemptRecorded = true;   // HTTP-Aufzeichnung überspringen

    c.giveUp();

    expect(c.gaveUp).toBeTrue();
    expect(c.state).toBe('SOLVED');
    expect(c.reviewMode).toBeTrue();
    expect(c.reviewIndex).toBe(0);          // startet an der Anfangsstellung

    tick(900); expect(c.reviewIndex).toBe(1);
    tick(900); expect(c.reviewIndex).toBe(2);
    tick(900); expect(c.reviewIndex).toBe(3); // = reviewTotal, fertig
    tick(900); expect(c.reviewIndex).toBe(3); // bleibt stehen (Timer beendet)

    c.ngOnDestroy();
  }));

  it('is NOT a plain reset (review-playthrough, not back to solving)', fakeAsync(() => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.attemptRecorded = true;

    c.giveUp();
    // resetPuzzle würde wieder in einen Lös-Zustand gehen; Aufgeben bleibt im Review.
    expect(c.state).toBe('SOLVED');
    expect(c.reviewMode).toBeTrue();

    c.ngOnDestroy();
  }));

  it('reviewLastPuzzle navigates straight to the analysis board with the last solved puzzle', () => {
    const c = makeComponent();
    // Zustand wie nach einem gelösten Puzzle (handleSolved merkt sich id/fen/moves/orientation):
    c.lastSolvedPuzzleId = 123;
    c.lastSolvedFen = PUZZLE.fen;
    c.lastSolvedMoves = PUZZLE.moves;       // 'e2e4 e7e5 g1f3'
    c.lastSolvedOrientation = 'black';

    c.reviewLastPuzzle();

    expect((c as any).router.navigate).toHaveBeenCalledWith(['/analysis'], {
      queryParams: { fen: PUZZLE.fen, moves: 'e2e4,e7e5,g1f3', orientation: 'black', from: '/puzzles/123' },
    });
  });

  it('manual review navigation stops the auto-playback', fakeAsync(() => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.attemptRecorded = true;

    c.giveUp();
    tick(900); expect(c.reviewIndex).toBe(1);

    c.reviewNext();              // manuell → Auto-Play stoppt
    expect(c.reviewIndex).toBe(2);
    tick(2000);
    expect(c.reviewIndex).toBe(2); // kein weiterer Auto-Schritt

    c.ngOnDestroy();
  }));
});
