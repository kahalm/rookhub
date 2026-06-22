import { Subject } from 'rxjs';
import { AnalysisComponent } from './analysis.component';

/**
 * Fokussierter Test des Vorladens aus Query-Params (genutzt vom „Analysieren"-Button
 * der Puzzles): ?fen=…&moves=…&orientation=… → Linie ab fen aufbauen, ans Ende springen.
 */
function makeComponent(params: Record<string, string | null>): any {
  const engine: any = {
    analysis$: new Subject(),
    engineFatalError$: new Subject(),   // Crash-Detection-Stream (seit 0.97.10), in ngOnInit subscribed
    setMultiPv: jasmine.createSpy('setMultiPv'),
    setDepth: jasmine.createSpy('setDepth'),
    analyze: jasmine.createSpy('analyze'),
    stop: () => {},
    destroy: () => {},                  // in ngOnDestroy aufgerufen
  };
  const route: any = { snapshot: { queryParamMap: { get: (k: string) => params[k] ?? null } } };
  const snackBar: any = { open: () => {} };
  const router: any = { navigateByUrl: jasmine.createSpy('navigateByUrl') };
  return new AnalysisComponent(engine, route, snackBar, router);
}

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('AnalysisComponent query-param preload', () => {
  it('builds the line from fen + UCI moves and lands at the last ply', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4,e7e5,g1f3', orientation: 'black' });
    c.ngOnInit();

    expect(c.startFen).toBe(START);
    expect(c.orientation).toBe('black');
    expect(c.line.length).toBe(3);
    expect(c.line.map((n: any) => n.san)).toEqual(['e4', 'e5', 'Nf3']);
    expect(c.ply).toBe(3);                      // aktuelle (= letzte) Stellung
    expect(c.currentFen).toBe(c.line[2].fen);
    c.ngOnDestroy();
  });

  it('accepts space-separated moves too', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4 e7e5' });
    c.ngOnInit();
    expect(c.line.length).toBe(2);
    expect(c.ply).toBe(2);
    c.ngOnDestroy();
  });

  it('stops at the first illegal move (robust gegen kaputte Param)', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4,e2e4' });   // 2. Zug illegal
    c.ngOnInit();
    expect(c.line.length).toBe(1);
    c.ngOnDestroy();
  });

  it('without moves it just starts at the given fen (ply 0)', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    expect(c.line.length).toBe(0);
    expect(c.ply).toBe(0);
    c.ngOnDestroy();
  });
});

// Verhalten hinter den mobilen Tap-Zonen (links = prev, rechts = next): goTo clampt
// an beiden Grenzen, daher ist Tippen am Anfang/Ende ein No-op statt eines Fehlers.
describe('AnalysisComponent prev/next navigation (Tap-Zonen)', () => {
  it('prev/next bewegen sich durch die Linie und clampen an den Grenzen', () => {
    const c = makeComponent({ fen: START, moves: 'e2e4,e7e5,g1f3' });
    c.ngOnInit();
    expect(c.ply).toBe(3);

    c.next();                 // bereits am Ende → bleibt
    expect(c.ply).toBe(3);

    c.prev();
    expect(c.ply).toBe(2);
    c.goTo(0);                // an den Anfang
    expect(c.ply).toBe(0);

    c.prev();                 // am Anfang → bleibt
    expect(c.ply).toBe(0);
    c.next();
    expect(c.ply).toBe(1);
    c.ngOnDestroy();
  });
});

describe('AnalysisComponent back-to-puzzle + depth', () => {
  it('reads the from param and navigates back to it', () => {
    const c = makeComponent({ fen: START, from: '/puzzles/123' });
    c.ngOnInit();
    expect(c.returnTo).toBe('/puzzles/123');
    c.backToPuzzle();
    expect((c as any).router.navigateByUrl).toHaveBeenCalledWith('/puzzles/123');
    c.ngOnDestroy();
  });

  it('ignores an unsafe from param (no back button)', () => {
    const c = makeComponent({ fen: START, from: 'https://evil.example/x' });
    c.ngOnInit();
    expect(c.returnTo).toBeNull();
    c.backToPuzzle();
    expect((c as any).router.navigateByUrl).not.toHaveBeenCalled();
    c.ngOnDestroy();
  });

  it('applies the configured max depth to the engine on init', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    expect((c as any).engine.setDepth).toHaveBeenCalledWith(c.depthSetting);
    c.ngOnDestroy();
  });

  it('onDepthChange re-applies depth to the engine', () => {
    const c = makeComponent({ fen: START });
    c.ngOnInit();
    c.engineOn = true;
    c.depthSetting = 30;
    c.onDepthChange();
    expect((c as any).engine.setDepth).toHaveBeenCalledWith(30);
    c.ngOnDestroy();
  });
});
