import { createRoot, playSan } from './analysis-tree';
import { nextMoveKey, openNextMoveChoice } from './next-move-choice';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

// 0.857.0, Wunsch 2026-10-27: „Popup mit next move, Zug der Partie vorausgewählt, rauf/runter, Leertaste/Enter/→ bestätigen"
describe('next-move-choice', () => {
  it('öffnet nur bei mehreren Fortsetzungen, Hauptlinie vorausgewählt', () => {
    const root = createRoot(START);
    expect(openNextMoveChoice(root)).toBeNull();
    const e4 = playSan(root, 'e4')!;
    expect(openNextMoveChoice(root)).toBeNull();
    const d4 = playSan(root, 'd4')!;
    const c = openNextMoveChoice(root)!;
    expect(c.options).toEqual([e4, d4]);
    expect(c.index).toBe(0);
  });

  it('↑/↓ wählen ringsum, →/Enter/Leertaste nehmen, ←/Esc schließen, sonst nichts', () => {
    const root = createRoot(START);
    const e4 = playSan(root, 'e4')!;
    const d4 = playSan(root, 'd4')!;
    const c4 = playSan(root, 'c4')!;
    let c = openNextMoveChoice(root)!;
    const down = nextMoveKey(c, 'ArrowDown');
    expect(down.kind === 'select' && down.choice.index).toBe(1);
    const up = nextMoveKey(c, 'ArrowUp');
    expect(up.kind === 'select' && up.choice.index).toBe(2);
    c = { ...c, index: 1 };
    for (const k of ['ArrowRight', 'Enter', ' ']) expect(nextMoveKey(c, k)).toEqual({ kind: 'pick', node: d4 });
    for (const k of ['ArrowLeft', 'Escape']) expect(nextMoveKey(c, k)).toEqual({ kind: 'close' });
    expect(nextMoveKey(c, 's')).toEqual({ kind: 'ignore' });
    expect([e4, c4]).not.toContain(d4);
  });
});
