import { addMove, createRoot, playUci, toDto } from './analysis-tree';
import { compareLines, flatNodes, gameNodes, NodeEval, sanOf } from './line-study.util';
import { lineStudyKey } from './line-study.service';
import { AnalysisNode } from './analysis-tree';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

/** 1.e4 e5 2.Nf3 (Partie) — eigene Linien: 1…c5, 2.Qh5 (Variante zu 2.Nf3). */
function tree() {
  const root = createRoot(START);
  const e4 = playUci(root, 'e2e4')!;
  const c5 = playUci(e4, 'c7c5')!;
  const e5 = e4.children.length ? playUci(e4, 'e7e5')! : null!;
  const nf3 = playUci(e5, 'g1f3')!;
  const qh5 = playUci(e5, 'd1h5')!;
  return { root, e4, e5, c5, nf3, qh5 };
}

describe('line-study.util (ohne Engine, 0.855.0)', () => {
  it('flatNodes hat die Reihenfolge von toDto — die Indizes des Servers zeigen darauf', () => {
    const { root } = tree();
    const flat = flatNodes(root);
    expect(flat.map(n => n.uci)).toEqual(toDto(root).tree.n!.map(d => d.u));
  });

  it('gameNodes folgt den Partiezügen', () => {
    const { root, e4, e5, nf3 } = tree();
    expect([...gameNodes(root, ['e2e4', 'e7e5', 'g1f3'])]).toEqual([e4, e5, nf3]);
  });

  it('compareLines: nur eigene Züge, Klasse aus dem Verlust, besserer Zug in SAN', () => {
    const { root, e4, e5, c5, nf3, qh5 } = tree();
    const evals = new Map<AnalysisNode, NodeEval>([
      [root, { cp: 30, bestUci: 'e2e4' }], [e4, { cp: 30, bestUci: 'c7c5' }], [c5, { cp: 30 }],
      [e5, { cp: 40, bestUci: 'g1f3' }], [nf3, { cp: 35 }], [qh5, { cp: -300 }],
    ]);
    const c = compareLines(root, evals, ['e2e4', 'e7e5', 'g1f3']);
    expect(c.rated).toBe(2);                 // 1…c5 und 2.Dh5 — die Partiezüge zählen nicht
    expect(c.best).toBe(1);                  // 1…c5 war der Bestzug
    expect(c.verdicts.length).toBe(1);
    expect(c.verdicts[0].node).toBe(qh5);
    expect(c.verdicts[0].cls).toBe('blunder');
    expect(c.verdicts[0].bestSan).toBe('Nf3');
    expect(c.verdicts[0].evalText).toBe('-3.00');
  });

  it('sanOf: illegaler Zug → null', () => {
    expect(sanOf(START, 'e2e4')).toBe('e4');
    expect(sanOf(START, 'e2e5')).toBeNull();
  });

  it('lineStudyKey: gleiche Partie gleicher Schlüssel, andere Züge anderer, Zugzähler egal', () => {
    const a = lineStudyKey(START, ['e2e4', 'e7e5']);
    expect(a).toMatch(/^g:[0-9a-f]{16}:2$/);
    expect(lineStudyKey(START.replace(' 0 1', ' 3 9'), ['e2e4', 'e7e5'])).toBe(a);
    expect(lineStudyKey(START, ['e2e4', 'c7c5'])).not.toBe(a);
  });
});

// addMove wird hier nur für die Typprüfung gebraucht
void addMove;
