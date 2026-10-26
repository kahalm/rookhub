import { of } from 'rxjs';
import { StandardViewHost, StandardViewSettings } from './standard-view-settings';

function makeHost(result: unknown = null): StandardViewHost & Record<string, any> {
  const prefs: any = {
    boardTheme: 'green', pieceSet: 'cburnett', themeMode: 'fixed', stockfishDepth: 12, visualization: 0, vizArrow: true,
    puzzleDifficulty: 'schwer', puzzleWorstTags: false, puzzleExcludeSolved: false, offPathWarnMoves: 3, enPassantForced: false,
    setBoardTheme: jasmine.createSpy(), setPieceSet: jasmine.createSpy(), setThemeMode: jasmine.createSpy(),
    setVisualization: jasmine.createSpy(), setVizArrow: jasmine.createSpy(), setStockfishDepth: jasmine.createSpy(),
    setPuzzleDifficulty: jasmine.createSpy(), setPuzzleExcludeSolved: jasmine.createSpy(), setPuzzleWorstTags: jasmine.createSpy(),
    setOffPathWarnMoves: jasmine.createSpy(), setEnPassantForced: jasmine.createSpy(),
  };
  return {
    prefs,
    dialog: { open: () => ({ afterClosed: () => of(result) }) } as any,
    solveMode: {
      ensure: () => of('easy'), set: jasmine.createSpy('set'),
      levelFor: (m: string) => (m === 'easy' ? 0 : 2), modeForLevel: (l: number) => (l > 0 ? 'training' : 'easy'),
    } as any,
    snackbar: { info: jasmine.createSpy('info') } as any,
    translate: { instant: (k: string) => k } as any,
    puzzleService: { getStats: () => of({ puzzleElo: 1600 }) } as any,
    puzzle: null, isSolving: false, isLoggedIn: false, anarchyForcedByUrl: false,
    boardTheme: '', pieceSet: '', themeMode: 'fixed', visualizationMode: 2, vizArrowEnabled: true, enPassantForced: false,
    stockfishDepth: 16, difficulty: 'normal', excludeSolved: false, worstTagsEnabled: false, stats: null, awaitingSolveMode: false,
    setupPuzzle: jasmine.createSpy('setupPuzzle'), clearVizOpponentArrow: jasmine.createSpy('clear'),
    updateBoard: jasmine.createSpy('updateBoard'), onPuzzleFilterChange: jasmine.createSpy('onPuzzleFilterChange'),
  };
}

describe('StandardViewSettings', () => {
  it('übernimmt die gemerkten Einstellungen', () => {
    const h = makeHost();
    new StandardViewSettings(h).loadConfig();
    expect(h.boardTheme).toBe('green');
    expect(h.stockfishDepth).toBe(12);
    expect(h.difficulty).toBe('schwer');
  });

  it('Dialog: nur geänderte Filter, EIN Neuaufbau; Schwierigkeit lädt sofort neu', () => {
    const h = makeHost({ boardTheme: 'blue', pieceSet: 'alpha', themeMode: 'fixed', visualizationMode: 2, vizArrowEnabled: true,
      difficulty: 'leicht', excludeSolved: true, worstTags: false });
    new StandardViewSettings(h).openDialog();
    expect(h.difficulty).toBe('leicht');
    expect(h.excludeSolved).toBeTrue();
    expect(h['onPuzzleFilterChange']).toHaveBeenCalledOnceWith(true);
    expect(h['setupPuzzle']).not.toHaveBeenCalled();     // Stufe unverändert → kein Neuaufsetzen
  });

  it('nur „Gelöste ausschließen" lässt das laufende Puzzle stehen', () => {
    const h = makeHost({ boardTheme: 'b', pieceSet: 'p', themeMode: 'fixed', visualizationMode: 2, vizArrowEnabled: true,
      excludeSolved: true });
    new StandardViewSettings(h).openDialog();
    expect(h['onPuzzleFilterChange']).toHaveBeenCalledOnceWith(false);
  });

  it('Spielweise: unterdrückt fragt nicht; sonst setzt die Wahl die Stufe und das Puzzle neu auf', () => {
    const h = makeHost();
    const v = new StandardViewSettings(h);
    v.askSuppressed = true;
    v.askSolveMode();
    expect(v.solveModeChoice).toBeNull();
    v.askSuppressed = false;
    (h as any).puzzle = { id: 1 };
    v.askSolveMode();
    expect(v.solveModeChoice).toBe('easy');
    expect(h.visualizationMode).toBe(0);
    expect(h.awaitingSolveMode).toBeFalse();
    expect(h['setupPuzzle']).toHaveBeenCalled();
    v.toggleSolveMode();
    expect(v.solveModeChoice).toBe('training');
  });
});
