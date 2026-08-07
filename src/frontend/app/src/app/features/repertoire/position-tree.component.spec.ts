import { PositionTreeComponent } from './position-tree.component';
import { PositionTreeNode } from '../../core/repertoire.service';

describe('PositionTreeComponent', () => {
  function node(san: string, children: PositionTreeNode[] = []): PositionTreeNode {
    return { san, count: 1, isEnd: children.length === 0, children };
  }

  function make(): PositionTreeComponent {
    const c = new PositionTreeComponent();
    // d6 → d4 → cxd4 (drei Ebenen), daneben ein zweiter Zweig ohne Kinder.
    c.nodes = [node('d6', [node('d4', [node('cxd4')])]), node('g6')];
    return c;
  }

  it('opens nodes up to the default depth and collapses deeper ones', () => {
    const c = make();
    const d6 = c.nodes[0];
    const d4 = d6.children[0];
    const cxd4 = d4.children[0];

    expect(c.isOpen(d6, 0)).toBeTrue();          // Tiefe 0 + 1 offen …
    expect(c.isOpen(d4, 1)).toBeTrue();
    expect(c.isOpen(cxd4, 2)).toBeFalse();       // … ab Tiefe 2 zu
  });

  it('toggle() flips a node against its default state', () => {
    const c = make();
    const d6 = c.nodes[0];
    c.toggle(d6);
    expect(c.isOpen(d6, 0)).toBeFalse();         // war offen → zu
    c.toggle(d6);
    expect(c.isOpen(d6, 0)).toBeTrue();
  });

  it('toggle() ignores leaves', () => {
    const c = make();
    const g6 = c.nodes[1];
    c.toggle(g6);
    expect(c.isOpen(g6, 0)).toBeTrue();          // unverändert (Default für Tiefe 0)
  });

  it('numbers moves from the position (white to move)', () => {
    const c = make();
    c.startMoveNumber = 3;
    c.blackToMove = false;
    expect(c.movePrefix(0)).toBe('3.');          // 3. …
    expect(c.movePrefix(1)).toBe('');            // schwarze Antwort ohne Nummer
    expect(c.movePrefix(2)).toBe('4.');
  });

  it('numbers moves from the position (black to move)', () => {
    const c = make();
    c.startMoveNumber = 2;
    c.blackToMove = true;
    expect(c.movePrefix(0)).toBe('2…');          // 2… c5
    expect(c.movePrefix(1)).toBe('3.');
    expect(c.movePrefix(2)).toBe('');
  });

  it('a move click plays the path when a board listens, otherwise it toggles', () => {
    const c = make();
    const d6 = c.nodes[0];

    const paths: string[][] = [];
    c.playPath.subscribe(p => paths.push(p));
    c.canPlay = true;
    c.onMoveClick(d6, 0, ['d6']);
    expect(paths).toEqual([['d6']]);
    expect(c.isOpen(d6, 0)).toBeTrue();          // Klick hat NICHT zugeklappt

    c.canPlay = false;
    c.onMoveClick(d6, 0, ['d6']);
    expect(paths.length).toBe(1);                // kein weiteres Play
    expect(c.isOpen(d6, 0)).toBeFalse();         // stattdessen zugeklappt
  });
});
