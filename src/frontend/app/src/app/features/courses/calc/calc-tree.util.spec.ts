import {
  addMove, createTree, deserializeTree, depthOf, findNode, isEmpty, leafIds, lines, plyPrefix,
  removeLine, removeSubtree, serializeTree, setComment, setEvaluation, setGlyph, whiteToMove,
  CALC_EVALS, CALC_GLYPHS, CALC_EVAL_KEYS, CALC_GLYPH_KEYS, evalNameKey, glyphNameKey,
} from './calc-tree.util';

const START = 'r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4';
const BLACK_START = '8/8/8/4k3/8/8/4K3/8 b - - 0 7';

/** Kurzhelfer: Zug anhängen (FEN ist für die Baumlogik nur Nutzlast). */
function mv(san: string, uci: string) {
  return { san, uci, fen: `fen-${uci}` };
}

describe('calc-tree.util', () => {
  it('creates a tree with just the root', () => {
    const tree = createTree(START);
    expect(isEmpty(tree)).toBeTrue();
    expect(tree.nodes.length).toBe(1);
    expect(lines(tree).length).toBe(0);
    expect(findNode(tree, tree.rootId)!.san).toBe('');
  });

  it('appends moves and builds a single line', () => {
    const tree = createTree(START);
    const a = addMove(tree, tree.rootId, mv('Nxe5', 'f3e5'));
    const b = addMove(tree, a.id, mv('Nxe5', 'c6e5'));
    expect(isEmpty(tree)).toBeFalse();
    const all = lines(tree);
    expect(all.length).toBe(1);
    expect(all[0].moves.map(n => n.san)).toEqual(['Nxe5', 'Nxe5']);
    expect(all[0].leafId).toBe(b.id);
    expect(depthOf(tree, b.id)).toBe(2);
  });

  it('does not duplicate an identical move at the same node', () => {
    const tree = createTree(START);
    const first = addMove(tree, tree.rootId, mv('Nxe5', 'f3e5'));
    const again = addMove(tree, tree.rootId, mv('Nxe5', 'f3e5'));
    expect(again.id).toBe(first.id);
    expect(tree.nodes.length).toBe(2);
  });

  it('branching mid-line creates a second line sharing the prefix', () => {
    const tree = createTree(START);
    const a = addMove(tree, tree.rootId, mv('Nxe5', 'f3e5'));
    addMove(tree, a.id, mv('Nxe5', 'c6e5'));
    // Abzweigung: an derselben Stelle ein anderer schwarzer Zug.
    const alt = addMove(tree, a.id, mv('Bd6', 'f8d6'));

    const all = lines(tree);
    expect(all.length).toBe(2);
    expect(all[0].sharedPrefix).toBe(0);          // erste Linie hat keinen Vorgänger
    expect(all[1].sharedPrefix).toBe(1);          // teilt 1. Nxe5
    expect(all[1].moves.map(n => n.san)).toEqual(['Nxe5', 'Bd6']);
    expect(leafIds(tree)).toContain(alt.id);
  });

  it('removeSubtree drops the node and everything below it', () => {
    const tree = createTree(START);
    const a = addMove(tree, tree.rootId, mv('Nxe5', 'f3e5'));
    const b = addMove(tree, a.id, mv('Nxe5', 'c6e5'));
    addMove(tree, b.id, mv('d4', 'd2d4'));

    const cursor = removeSubtree(tree, b.id);

    expect(cursor).toBe(a.id);
    expect(tree.nodes.length).toBe(2);            // Wurzel + Nxe5
    expect(findNode(tree, a.id)!.childIds).toEqual([]);
  });

  it('removeSubtree refuses to delete the root', () => {
    const tree = createTree(START);
    addMove(tree, tree.rootId, mv('Nxe5', 'f3e5'));
    expect(removeSubtree(tree, tree.rootId)).toBe(tree.rootId);
    expect(tree.nodes.length).toBe(2);
  });

  it('removeLine deletes only the exclusive tail, keeping shared moves', () => {
    const tree = createTree(START);
    const a = addMove(tree, tree.rootId, mv('Nxe5', 'f3e5'));       // geteilt
    const b = addMove(tree, a.id, mv('Nxe5', 'c6e5'));
    const c = addMove(tree, b.id, mv('d4', 'd2d4'));                // Linie 1 (exklusiv ab b)
    const alt = addMove(tree, a.id, mv('Bd6', 'f8d6'));             // Linie 2

    const cursor = removeLine(tree, c.id);

    // Der gemeinsame Zug Nxe5 bleibt, die zweite Linie bleibt vollständig.
    expect(cursor).toBe(a.id);
    expect(findNode(tree, a.id)).toBeDefined();
    expect(findNode(tree, alt.id)).toBeDefined();
    expect(findNode(tree, b.id)).toBeUndefined();
    expect(findNode(tree, c.id)).toBeUndefined();
    expect(lines(tree).length).toBe(1);
  });

  it('removeLine on the only line clears the tree back to the root', () => {
    const tree = createTree(START);
    const a = addMove(tree, tree.rootId, mv('Nxe5', 'f3e5'));
    const b = addMove(tree, a.id, mv('Nxe5', 'c6e5'));

    const cursor = removeLine(tree, b.id);

    expect(cursor).toBe(tree.rootId);
    expect(isEmpty(tree)).toBeTrue();
  });

  it('glyph/eval toggle off when set twice, comment trims and clears', () => {
    const tree = createTree(START);
    const a = addMove(tree, tree.rootId, mv('Nxe5', 'f3e5'));

    setGlyph(tree, a.id, '??');
    expect(findNode(tree, a.id)!.glyph).toBe('??');
    setGlyph(tree, a.id, '??');                        // gleiches Symbol → aus
    expect(findNode(tree, a.id)!.glyph).toBeUndefined();

    setEvaluation(tree, a.id, '⩲');
    expect(findNode(tree, a.id)!.evaluation).toBe('⩲');
    setEvaluation(tree, a.id, undefined);
    expect(findNode(tree, a.id)!.evaluation).toBeUndefined();

    setComment(tree, a.id, '  gewinnt einen Bauern  ');
    expect(findNode(tree, a.id)!.comment).toBe('gewinnt einen Bauern');
    setComment(tree, a.id, '   ');
    expect(findNode(tree, a.id)!.comment).toBeUndefined();
  });

  it('does not put a move glyph on the start position itself', () => {
    const tree = createTree(START);
    setGlyph(tree, tree.rootId, '??');
    expect(findNode(tree, tree.rootId)!.glyph).toBeUndefined();
  });

  it('numbers plies from the start FEN (white to move, move 4)', () => {
    expect(plyPrefix(START, 0, true)).toBe('4.');
    expect(plyPrefix(START, 1, false)).toBe('');       // Schwarz mitten in der Linie
    expect(plyPrefix(START, 1, true)).toBe('4…');      // Schwarz eröffnet die Anzeige
    expect(plyPrefix(START, 2, false)).toBe('5.');
  });

  it('numbers plies when black starts', () => {
    expect(plyPrefix(BLACK_START, 0, true)).toBe('7…');
    expect(plyPrefix(BLACK_START, 1, false)).toBe('8.');
    expect(whiteToMove(BLACK_START)).toBeFalse();
    expect(whiteToMove(START)).toBeTrue();
  });

  it('round-trips through serialize/deserialize', () => {
    const tree = createTree(START);
    const a = addMove(tree, tree.rootId, mv('Nxe5', 'f3e5'));
    setGlyph(tree, a.id, '!');
    setEvaluation(tree, a.id, '±');
    setComment(tree, a.id, 'Pointe');
    addMove(tree, a.id, mv('Bd6', 'f8d6'));

    const restored = deserializeTree(serializeTree(tree), START)!;

    expect(restored.nodes.length).toBe(tree.nodes.length);
    expect(restored.nextId).toBe(tree.nextId);
    const node = findNode(restored, a.id)!;
    expect(node.glyph).toBe('!');
    expect(node.evaluation).toBe('±');
    expect(node.comment).toBe('Pointe');
    expect(lines(restored).length).toBe(1);
  });

  it('rejects a stored tree that belongs to a different start position', () => {
    const tree = createTree(START);
    addMove(tree, tree.rootId, mv('Nxe5', 'f3e5'));
    expect(deserializeTree(serializeTree(tree), BLACK_START)).toBeNull();
  });

  it('rejects garbage, empty and wrong-version payloads', () => {
    expect(deserializeTree(null, START)).toBeNull();
    expect(deserializeTree('', START)).toBeNull();
    expect(deserializeTree('{nope', START)).toBeNull();
    expect(deserializeTree('[]', START)).toBeNull();
    expect(deserializeTree(JSON.stringify({ version: 99, startFen: START, nodes: [{ id: 0 }] }), START)).toBeNull();
    expect(deserializeTree(JSON.stringify({ version: 1, startFen: START, nodes: [] }), START)).toBeNull();
  });

  it('repairs nextId when a stored tree carries a stale counter', () => {
    const payload = JSON.stringify({
      version: 1, startFen: START, rootId: 0, nextId: 1,
      nodes: [
        { id: 0, parentId: null, san: '', uci: '', fen: START, childIds: [7] },
        { id: 7, parentId: 0, san: 'Nxe5', uci: 'f3e5', fen: 'x', childIds: [] },
      ],
    });
    const restored = deserializeTree(payload, START)!;
    expect(restored.nextId).toBe(8);
    // Und ein neuer Zug kollidiert nicht mit der bestehenden Id.
    const added = addMove(restored, 0, mv('d4', 'd2d4'));
    expect(added.id).toBe(8);
  });
});

describe('calc-tree.util Symbol-Namen', () => {
  it('gibt JEDEM Symbol einen Übersetzungs-Schlüssel (kein leerer Tooltip)', () => {
    for (const g of CALC_GLYPHS) {
      expect(CALC_GLYPH_KEYS[g]).toBeTruthy();
      expect(glyphNameKey(g)).toBe(`calc.glyph.${CALC_GLYPH_KEYS[g]}`);
    }
    for (const e of CALC_EVALS) {
      expect(CALC_EVAL_KEYS[e]).toBeTruthy();
      expect(evalNameKey(e)).toBe(`calc.eval.${CALC_EVAL_KEYS[e]}`);
    }
  });

  it('vergibt die Slugs eindeutig (kein Symbol erbt die Erklärung eines anderen)', () => {
    const slugs = [...Object.values(CALC_GLYPH_KEYS), ...Object.values(CALC_EVAL_KEYS)];
    expect(new Set(slugs).size).toBe(slugs.length);
  });

  it('benennt die Gewinn-Symbole erkennbar (+− = Weiß, −+ = Schwarz)', () => {
    expect(CALC_EVAL_KEYS['+−']).toBe('whiteWinning');
    expect(CALC_EVAL_KEYS['−+']).toBe('blackWinning');
  });
});
