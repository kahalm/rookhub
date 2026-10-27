import { fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AnalysisNode, createRoot, playUci } from './analysis-tree';
import { LineStudySession } from './line-study-session';
import { LineStudy, LineStudyEvals, LineStudyService } from './line-study.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('LineStudySession (ohne Engine, 0.855.0)', () => {
  let root: AnalysisNode;
  let current: AnalysisNode;
  let revealedCalls = 0;
  let saved: { tree: { n?: { p: number; u: string }[] | null } }[];
  let study: LineStudy;
  let evals: LineStudyEvals;
  let api: LineStudyService;

  function host() {
    return {
      root: () => root, current: () => current,
      setTree: (r: AnalysisNode, c: AnalysisNode) => { root = r; current = c; },
      revealed: () => { revealedCalls++; }, changed: () => {},
    };
  }

  beforeEach(() => {
    root = createRoot(START);
    current = playUci(playUci(root, 'e2e4')!, 'e7e5')!;
    revealedCalls = 0;
    saved = [];
    study = { id: 7, gameKey: 'g:k', startFen: START, title: null, tree: {}, current: -1, revealed: false, analyzed: 0, total: 3 };
    evals = { revealed: false, analyzed: 1, total: 3, nodes: [] };
    api = {
      get: () => throwError(() => ({ status: 404 })),
      save: (req: { tree: { n?: { p: number; u: string }[] | null } }) => { saved.push(req); return of(study); },
      reveal: () => of({ ...study, revealed: true }),
      evals: () => of(evals),
    } as unknown as LineStudyService;
  });

  it('neu: legt den Partie-Baum an, Änderungen gehen gedrosselt hinterher', fakeAsync(() => {
    const s = new LineStudySession(api, host(), 'g:k', ['e2e4', 'e7e5'], null);
    s.open();
    expect(saved.length).toBe(1);
    expect(saved[0].tree.n!.map(d => d.u)).toEqual(['e2e4', 'e7e5']);
    expect(s.id).toBe(7);
    current = playUci(current, 'g1f3')!;
    s.schedule();
    tick(LineStudySession.SaveDelayMs - 1);
    expect(saved.length).toBe(1);
    tick(1);
    expect(saved.length).toBe(2);
    s.destroy();
  }));

  it('vorhanden: legt den gespeicherten Baum aufs Brett', () => {
    api.get = () => of({ ...study, tree: { n: [{ p: -1, u: 'd2d4' }] }, current: 0 });
    const s = new LineStudySession(api, host(), 'g:k', ['d2d4'], null);
    s.open();
    expect(root.children.map(c => c.uci)).toEqual(['d2d4']);
    expect(current.uci).toBe('d2d4');
    s.destroy();
  });

  it('aufdecken: Bewertungen an die Knoten der gespeicherten Reihenfolge, dazu der Vergleich', () => {
    const s = new LineStudySession(api, host(), 'g:k', ['e2e4'], null);
    s.open();                                   // speichert e4, e5 (Index 0, 1)
    evals = { revealed: true, analyzed: 3, total: 3, nodes: [
      { index: -1, cp: 30, bestUci: 'e2e4', depth: 20 }, { index: 0, cp: 25, bestUci: 'e7e5', depth: 20 }, { index: 1, cp: 35, depth: 20 }] };
    s.reveal();
    expect(revealedCalls).toBe(1);
    expect(s.revealed).toBeTrue();
    const e4 = root.children[0];
    expect(e4.evalText).toBe('+0.25');
    expect(e4.children[0].evalText).toBe('+0.35');
    expect(s.comparison?.rated).toBe(1);        // 1…e5 ist eigener Zug (die Partie war nur 1.e4)
    expect(s.comparison?.best).toBe(1);
    s.destroy();
  });
});
