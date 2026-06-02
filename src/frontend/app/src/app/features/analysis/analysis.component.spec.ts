import { Subject } from 'rxjs';
import { AnalysisComponent } from './analysis.component';

/**
 * Fokussierter Test des Vorladens aus Query-Params (genutzt vom „Analysieren"-Button
 * der Puzzles): ?fen=…&moves=…&orientation=… → Linie ab fen aufbauen, ans Ende springen.
 */
function makeComponent(params: Record<string, string | null>): any {
  const engine: any = {
    analysis$: new Subject(),
    setMultiPv: () => {},
    analyze: () => {},
    stop: () => {},
  };
  const route: any = { snapshot: { queryParamMap: { get: (k: string) => params[k] ?? null } } };
  const snackBar: any = { open: () => {} };
  return new AnalysisComponent(engine, route, snackBar);
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
