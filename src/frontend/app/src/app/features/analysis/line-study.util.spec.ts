import { addMove, createRoot, playUci, toDto } from './analysis-tree';
import { blindResumeNode, compareLines, flatNodes, gameNodes, isUntouchedGame, NodeEval, sanOf } from './line-study.util';
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

  it('compareLines: ab −4,00 vor dem Zug ist ein eigener Zug kein Fehler mehr (wie im Rückblick, 2026-11-15)', () => {
    const run = (cp: number) => {
      const { root, e4, e5, nf3, qh5 } = tree();
      const evals = new Map<AnalysisNode, NodeEval>([
        [root, { cp, bestUci: 'e2e4' }], [e4, { cp }], [e5, { cp, bestUci: 'g1f3' }], [nf3, { cp }], [qh5, { cp: -1100 }],
      ]);
      return compareLines(root, evals, ['e2e4', 'e7e5', 'g1f3']);
    };
    expect(run(-399).verdicts.map(v => v.cls)).toEqual(['mistake']);
    expect(run(-399).mistakes).toBe(1);
    expect(run(-400).verdicts).toEqual([]);
    expect(run(-400).rated).toBe(1);
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

// Wunsch 2026-11-18: „wenn ich eine Vereinspartie ohne Engine analysieren will, lande ich am Ende der Partie"
describe('line-study.util: wo „ohne Engine" beim Wiederöffnen steht', () => {
  /** 1.e4 e5 2.Nf3 als nackte Partie. */
  function game() {
    const root = createRoot(START);
    const e4 = playUci(root, 'e2e4')!;
    const e5 = playUci(e4, 'e7e5')!;
    const nf3 = playUci(e5, 'g1f3')!;
    return { root, e4, e5, nf3 };
  }

  it('unberührte Partie am letzten Zug (so wurden die Bäume bis 0.991.0 angelegt) → Ausgangsstellung', () => {
    const saved = game();
    expect(isUntouchedGame(saved.root, game().root)).toBeTrue();
    expect(blindResumeNode(saved.root, saved.nf3, game().root)).toBe(saved.root);
  });

  it('unberührte Partie mittendrin bzw. an der Wurzel → bleibt, wo man war', () => {
    const saved = game();
    expect(blindResumeNode(saved.root, saved.e5, game().root)).toBe(saved.e5);
    expect(blindResumeNode(saved.root, saved.root, game().root)).toBe(saved.root);
  });

  it('eigene Variante im Baum → die gespeicherte Stellung bleibt, auch am Partieende', () => {
    const saved = game();
    const c5 = playUci(saved.e4, 'c7c5')!;
    expect(isUntouchedGame(saved.root, game().root)).toBeFalse();
    expect(blindResumeNode(saved.root, saved.nf3, game().root)).toBe(saved.nf3);
    expect(blindResumeNode(saved.root, c5, game().root)).toBe(c5);
  });

  it('eigener Zug hinter dem Partieende → kein unberührter Baum', () => {
    const saved = game();
    const nc6 = playUci(saved.nf3, 'b8c6')!;
    expect(isUntouchedGame(saved.root, game().root)).toBeFalse();
    expect(blindResumeNode(saved.root, nc6, game().root)).toBe(nc6);
  });

  it('Symbol, Bewertung, Kommentar oder Stern zählen als eigene Arbeit — die Engine-Bewertung nicht', () => {
    for (const mark of [
      (g: ReturnType<typeof game>) => { g.e5.glyph = '?!'; },
      (g: ReturnType<typeof game>) => { g.e4.evaluation = '='; },
      (g: ReturnType<typeof game>) => { g.nf3.comment = 'Plan: d4'; },
      (g: ReturnType<typeof game>) => { g.e4.starred = true; },
      (g: ReturnType<typeof game>) => { g.root.starred = true; },
    ]) {
      const saved = game();
      mark(saved);
      expect(isUntouchedGame(saved.root, game().root)).toBeFalse();
      expect(blindResumeNode(saved.root, saved.nf3, game().root)).toBe(saved.nf3);
    }
    const evaluated = game();
    evaluated.nf3.evalText = '+0.30';
    expect(blindResumeNode(evaluated.root, evaluated.nf3, game().root)).toBe(evaluated.root);
  });

  it('eine Partie, deren PGN selbst Varianten hat, gilt samt ihnen als unberührt; ein kürzerer Baum nicht', () => {
    const pgnTree = game();
    playUci(pgnTree.e4, 'c7c5');
    const saved = game();
    playUci(saved.e4, 'c7c5');
    expect(blindResumeNode(saved.root, saved.nf3, pgnTree.root)).toBe(saved.root);
    const shorter = createRoot(START);
    const e4 = playUci(shorter, 'e2e4')!;
    expect(isUntouchedGame(shorter, game().root)).toBeFalse();
    expect(blindResumeNode(shorter, e4, game().root)).toBe(e4);
  });
});
