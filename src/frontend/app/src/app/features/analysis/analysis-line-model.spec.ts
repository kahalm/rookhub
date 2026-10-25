import { Key } from 'chessground/types';
import { AnalysisLineModel, boardMove, playLine } from './analysis-line-model';
import { addMove, playSan, playUci } from './analysis-tree';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('AnalysisLineModel', () => {
  it('springt auf Knoten und klemmt die Stelle', () => {
    const m = new AnalysisLineModel(START);
    const end = playLine(m.root, ['e2e4', 'e7e5', 'g1f3'], playUci);
    m.goToNode(end);
    expect([m.ply, m.line.length]).toEqual([3, 3]);
    m.goTo(1);
    expect(m.currentNode.san).toBe('e4');
    m.goTo(99);
    expect(m.ply).toBe(3);
    m.goTo(-2);
    expect(m.currentNode).toBe(m.root);
  });

  it('die Linie läuft durch eine Variante samt Fortsetzung', () => {
    const m = new AnalysisLineModel(START);
    const e4 = playUci(m.root, 'e2e4')!;
    playSan(e4, 'e5');
    const c5 = playSan(e4, 'c5')!;
    m.goToNode(c5);
    expect(m.line.map(n => n.san)).toEqual(['e4', 'c5']);
    m.relineThrough(e4);
    expect(m.line.map(n => n.san)).toEqual(['e4', 'e5']);
  });

  it('Sterne: Liste je Baumstand zwischengespeichert, Umschalten zählt als Änderung', () => {
    const m = new AnalysisLineModel(START);
    const e4 = playUci(m.root, 'e2e4')!;
    const first = m.starred;
    expect(m.starred).toBe(first);
    m.toggleStar(e4);
    expect(m.starred).toEqual([e4]);
    m.toggleStar(e4);
    expect(m.starred).toEqual([]);
    expect(m.treeVersion).toBe(2);
    m.setRoot(addMove(m.root, { san: 'x', uci: 'x', fen: START }));
    expect(m.treeVersion).toBe(3);
  });

  it('playLine bricht am ersten unmöglichen Zug ab', () => {
    const m = new AnalysisLineModel(START);
    expect(playLine(m.root, ['e4', 'Ke7', 'Ke2'], playSan).san).toBe('e4');
  });
});

describe('boardMove', () => {
  it('kaputte Stellung, illegaler Zug, Umwandlung mit Dame als Rückfall', () => {
    expect(boardMove('kaputt', { orig: 'e2' as Key, dest: 'e4' as Key }).kind).toBe('bad-fen');
    expect(boardMove(START, { orig: 'e2' as Key, dest: 'e5' as Key }).kind).toBe('illegal');
    const r = boardMove('8/4P3/8/8/8/8/8/k6K w - - 0 1', { orig: 'e7' as Key, dest: 'e8' as Key });
    expect(r.kind === 'ok' && r.move.uci).toBe('e7e8q');
    expect(r.kind === 'ok' && r.mover).toBe('white');
    const n = boardMove('8/4P3/8/8/8/8/8/k6K w - - 0 1', { orig: 'e7' as Key, dest: 'e8' as Key, promotion: 'n' });
    expect(n.kind === 'ok' && n.move.san).toBe('e8=N');
  });
});
