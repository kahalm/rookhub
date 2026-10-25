import { Chess } from 'chess.js';
import { CalcTreeCursor } from './calc-tree-cursor';
import { createTree, findNode } from './calc-tree.util';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

/** Züge (SAN) von der Cursor-Stellung aus anhängen. */
function play(cursor: CalcTreeCursor, ...sans: string[]): void {
  for (const san of sans) {
    const chess = new Chess(cursor.cursorFen);
    const m = chess.move(san);
    cursor.addMove({ san: m.san, uci: m.from + m.to + (m.promotion ?? ''), fen: chess.fen() });
  }
}

describe('CalcTreeCursor', () => {
  function make(): CalcTreeCursor {
    const c = new CalcTreeCursor();
    c.reset(createTree(START), START);
    return c;
  }

  it('reset steht auf der Wurzel mit leerem Entwurf', () => {
    const c = make();
    expect(c.atStart).toBeTrue();
    expect(c.cursorFen).toBe(START);
    expect(c.lineCount).toBe(0);
    expect(c.whiteToMove).toBeTrue();
  });

  it('hängt Züge an, navigiert in der Linie und wechselt Linien', () => {
    const c = make();
    play(c, 'e4', 'e5', 'Nf3');
    c.startNewLine();
    play(c, 'd4', 'd5');
    expect(c.lineCount).toBe(2);
    expect(c.node!.san).toBe('d5');
    c.goBack();
    expect(c.node!.san).toBe('d4');
    c.goForward();
    expect(c.node!.san).toBe('d5');
    c.switchLine(-1);
    expect(c.node!.san).toBe('Nf3');
    c.switchLine(1);
    expect(c.node!.san).toBe('d5');
    expect(c.movesTo(c.cursorId)).toEqual(['d2d4', 'd7d5']);
  });

  it('vorwärts folgt der aktiven Linie, nicht der ersten Fortsetzung', () => {
    const c = make();
    play(c, 'e4', 'e5');
    c.goBack(); c.goBack();
    play(c, 'e4', 'c5');                         // Abzweigung nach 1.e4
    c.goBack(); c.goBack();
    c.goForward(); c.goForward();
    expect(c.node!.san).toBe('c5');
  });

  it('löscht ab dem Cursor (nicht an der Wurzel) und ganze Linien', () => {
    const c = make();
    expect(c.deleteFromCursor()).toBeFalse();
    play(c, 'e4', 'e5');
    expect(c.deleteFromCursor()).toBeTrue();
    expect(c.node!.san).toBe('e4');
    c.startNewLine();
    play(c, 'd4');
    const leaf = c.cursorId;
    c.deleteLine(leaf);
    expect(c.lineCount).toBe(1);
  });

  it('Kommentar: Entwurf zieht beim eigenen Zug mit, sonst nicht', () => {
    const c = make();
    play(c, 'e4');
    const e4 = c.cursorId;
    play(c, 'e5');
    c.setComment(e4, '  Zentrum  ');
    expect(c.comment).toBe('');
    c.setCursor(e4);
    expect(c.comment).toBe('Zentrum');
    c.comment = 'neu';
    c.saveComment();
    expect(findNode(c.tree, e4)!.comment).toBe('neu');
  });
});
