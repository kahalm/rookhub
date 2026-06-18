import { BookPuzzleComponent } from './book-puzzle.component';

/**
 * Fokussierter Test der Lade-Epoche (loadEpoch) ohne TestBed/Template: eine veraltete,
 * langsamer auflösende Puzzle-Antwort darf das inzwischen geladene Puzzle nicht überschreiben.
 */
function makeComponent(): any {
  const prefs: any = { boardTheme: 'green', pieceSet: 'cburnett', themeMode: 'fixed', stockfishDepth: 12, visualization: 0 };
  const stockfish: any = { init: () => Promise.resolve(), getEval: () => Promise.resolve('') };
  const puzzleService: any = {};
  const route: any = { snapshot: { paramMap: { get: () => null }, queryParamMap: { get: () => null } }, paramMap: { subscribe: () => ({ unsubscribe() {} }) } };
  const dialog: any = {};
  const courseService: any = {};
  const weeklyService: any = {};
  const router: any = { navigate: jasmine.createSpy('navigate') };
  const translate: any = { instant: (k: string) => k };
  const auth: any = { isLoggedIn: false };
  const snackbar: any = { info: () => {} };
  const offlineQueue: any = { enqueue: () => {} };
  const challengeService: any = { resolve: () => ({ subscribe: () => {} }) };
  return new BookPuzzleComponent(
    puzzleService, stockfish, prefs, route, dialog, courseService, weeklyService,
    router, translate, auth, snackbar, offlineQueue, challengeService
  );
}

const FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('BookPuzzleComponent load race (loadEpoch)', () => {
  it('a stale getBookPuzzleById response does not overwrite a newer one', () => {
    const c = makeComponent();
    spyOn(c as any, 'setupPuzzle');

    const emits: Array<(v: any) => void> = [];
    c.puzzleService.getBookPuzzleById = () => ({
      subscribe: (h: any) => { emits.push((v: any) => (typeof h === 'function' ? h : h.next)(v)); return { unsubscribe() {} }; }
    });

    (c as any).loadPuzzle(1);   // Epoch 1 → emits[0]
    (c as any).loadPuzzle(2);   // Epoch 2 → emits[1]

    emits[1]({ id: 222, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' });   // neuere zuerst
    expect(c.puzzle.id).toBe(222);
    emits[0]({ id: 111, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' });   // ältere danach
    expect(c.puzzle.id).toBe(222);   // bleibt das neuere
  });
});
