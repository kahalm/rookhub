import { createRoot, playSan } from './analysis-tree';
import { nextMoveKey, openNextMoveChoice, siblingLine } from './next-move-choice';

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

  // 0.858.0, Wunsch 2026-10-27: „Pfeiltaste rauf/runter soll zwischen nebeneinanderliegenden Lines wechseln (oder zurück zur Mainline)"
  it('↑/↓ wechseln zur Nachbarlinie in gleicher Tiefe, ↑ führt über äußere Verzweigungen zurück zur Hauptlinie', () => {
    const root = createRoot(START);
    const e4 = playSan(root, 'e4')!;
    const e5 = playSan(e4, 'e5')!;
    const nf3 = playSan(e5, 'Nf3')!;
    const c5 = playSan(e4, 'c5')!;
    const nf3b = playSan(c5, 'Nf3')!;
    const d6 = playSan(nf3b, 'd6')!;
    const nc6 = playSan(nf3b, 'Nc6')!;
    expect(siblingLine(nf3, 1)).toBe(nf3b);               // gleiche Tiefe in der Variante
    expect(siblingLine(nf3b, -1)).toBe(nf3);              // zurück zur Hauptlinie
    expect(siblingLine(nf3, -1)).toBeNull();              // über der Hauptlinie nichts
    expect(siblingLine(d6, 1)).toBe(nc6);                 // innere Verzweigung zuerst
    expect(siblingLine(d6, -1)).toBe(nf3);                // innen schon oben → äußere: zur Hauptlinie, Tiefe nur so weit da
    expect(siblingLine(e5, 1)).toBe(c5);
    expect(siblingLine(root, 1)).toBeNull();
  });
});
