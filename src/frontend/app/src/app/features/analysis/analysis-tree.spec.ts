import {
  AnalysisNode, MoveRow, VariationBlock, buildMoveTable, createRoot, fromDto, indexPath, isMainline, lineThrough, mainline,
  makeMainline, nodeAtPath, numberedSan, parsePgnTree, playSan, playUci, promote, removeNode, starredNodes, toDto,
  variationTokens,
} from './analysis-tree';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

function play(parent: AnalysisNode, sans: string): AnalysisNode {
  let n = parent;
  for (const san of sans.split(' ')) n = playSan(n, san)!;
  return n;
}

const sans = (nodes: AnalysisNode[]) => nodes.map(n => n.san);
const cell = (c: AnalysisNode | 'gap' | null) => c === 'gap' ? '…' : c ? c.san : null;
const labels = (tokens: ReturnType<typeof variationTokens>) =>
  tokens.map(t => t.kind === 'move' ? t.label : t.kind === 'open' ? '(' : ')').join(' ');

describe('analysis-tree', () => {
  it('ein anderer Zug an derselben Stelle wird eine Variante, derselbe Zug nimmt den vorhandenen', () => {
    const root = createRoot(START);
    const e4 = play(root, 'e4');
    const e5 = play(e4, 'e5');
    const c5 = play(e4, 'c5');
    expect(playSan(e4, 'e5')).toBe(e5);
    expect(sans(e4.children)).toEqual(['e5', 'c5']);
    expect(sans(lineThrough(c5))).toEqual(['e4', 'c5']);
    play(e5, 'Nf3 Nc6');
    expect(sans(lineThrough(e4))).toEqual(['e4', 'e5', 'Nf3', 'Nc6']);
    expect(isMainline(e5)).toBeTrue();
    expect(isMainline(c5)).toBeFalse();
    expect(playUci(root, 'e2e5')).toBeNull();
  });

  it('hochstufen, zur Hauptvariante machen und löschen', () => {
    const root = createRoot(START);
    const e4 = play(root, 'e4');
    play(e4, 'e5 Nf3');
    const c5 = play(e4, 'c5');
    const nf3 = play(c5, 'Nf3');
    const c3 = play(c5, 'c3');
    // c3 ist Untervariante der Variante 1...c5 — hochstufen macht sie zur Fortsetzung von c5, nicht zur Hauptlinie
    promote(c3);
    expect(sans(c5.children)).toEqual(['c3', 'Nf3']);
    expect(sans(mainline(root))).toEqual(['e4', 'e5', 'Nf3']);
    promote(c3);
    expect(sans(mainline(root))).toEqual(['e4', 'c5', 'c3']);
    makeMainline(nf3);
    expect(sans(mainline(root))).toEqual(['e4', 'c5', 'Nf3']);
    expect(removeNode(c5)).toBe(e4);
    expect(sans(mainline(root))).toEqual(['e4', 'e5', 'Nf3']);
  });

  it('Index-Weg, Sterne und Verlauf hin und zurück', () => {
    const root = createRoot(START);
    const e4 = play(root, 'e4');
    play(e4, 'e5');
    const c5 = play(e4, 'c5');
    const nf3 = play(c5, 'Nf3');
    nf3.starred = true;
    root.starred = true;
    expect(indexPath(nf3)).toEqual([0, 1, 0]);
    expect(nodeAtPath(root, [0, 1, 0])).toBe(nf3);
    expect(nodeAtPath(root, [0, 5, 0])).toBe(e4);   // bricht an der unbekannten Stelle ab
    expect(starredNodes(root)).toEqual([root, nf3]);

    c5.evalText = '+0.30';
    const { tree, current } = toDto(root, nf3);
    expect(tree).toEqual({ s: true, n: [{ p: -1, u: 'e2e4' }, { p: 0, u: 'e7e5' }, { p: 0, u: 'c7c5', e: '+0.30' }, { p: 2, u: 'g1f3', s: true }] });
    expect(current).toBe(3);
    expect(toDto(root, root).current).toBe(-1);
    const back = fromDto(START, tree);
    expect(sans(mainline(back.root))).toEqual(['e4', 'e5']);
    expect(back.root.starred).toBeTrue();
    expect(back.nodes[3]).toBe(nodeAtPath(back.root, [0, 1, 0]));
    expect(back.nodes[3]!.starred).toBeTrue();
    expect(back.nodes[2]!.evalText).toBe('+0.30');
    // Ein Zug, der nicht geht, fällt samt Teilbaum weg
    const broken = fromDto(START, { n: [{ p: -1, u: 'e2e4' }, { p: 0, u: 'e2e4' }, { p: 1, u: 'g1f3' }, { p: 0, u: 'd7d5' }] });
    expect(sans(broken.root.children[0].children)).toEqual(['d5']);
    expect(broken.nodes[1]).toBeNull();
    expect(broken.nodes[2]).toBeNull();
  });

  it('Zugnummern', () => {
    const root = createRoot(START);
    const e5 = play(root, 'e4 e5');
    expect(numberedSan(e5.parent!)).toBe('1.e4');
    expect(numberedSan(e5)).toBe('1...e5');
  });

  it('Start mit Schwarz am Zug (Puzzle-Analyse, FEN laden): Nummern und Paare aus der Start-FEN (F4-005)', () => {
    // Vor dem Zugbaum (0.604.0) nummerierte die Zugliste „Weiß zuerst, ab 1." — hier stand „1. a6 Ba4 2. Nf6".
    const root = createRoot('r1bqkbnr/pppp1ppp/2n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 27');
    const nf6 = play(root, 'a6 Ba4 Nf6');
    const rows = buildMoveTable(root).map(it => `${(it as MoveRow).number}: ${cell((it as MoveRow).white)} | ${cell((it as MoveRow).black)}`);
    expect(rows).toEqual(['27: … | a6', '28: Ba4 | Nf6']);
    expect(numberedSan(nf6.parent!.parent!)).toBe('27...a6');
    expect(numberedSan(nf6.parent!)).toBe('28.Ba4');
  });

  it('Zugtabelle wie Lichess: Varianten unterbrechen die Zeile', () => {
    const root = createRoot(START);
    const c5 = play(root, 'e4 c5');
    const f4 = play(c5, 'f4');
    play(f4, 'f6');
    const e6 = play(f4, 'e6');
    const g3 = play(e6, 'g3');
    play(g3, 'Nc6');
    play(g3, 'Na6 f5 Ke7');

    const table = buildMoveTable(root);
    const rows = table.map(it => it.kind === 'row'
      ? `${(it as MoveRow).number}: ${cell((it as MoveRow).white)} | ${cell((it as MoveRow).black)}`
      : 'V: ' + (it as VariationBlock).lines.map(l => labels(l)).join(' / '));
    expect(rows).toEqual([
      '1: e4 | c5',
      '2: f4 | f6',
      'V: 2...e6 3.g3 Nc6 ( 3...Na6 4.f5 Ke7 )',
    ]);
  });

  it('eine Variante zu einem weißen Zug: „1 | e4 | …", Block, „1 | … | c5"', () => {
    const root = createRoot(START);
    play(root, 'e4 c5');
    play(root, 'd4 d5');
    const rows = buildMoveTable(root).map(it => it.kind === 'row'
      ? `${(it as MoveRow).number}: ${cell((it as MoveRow).white)} | ${cell((it as MoveRow).black)}`
      : 'V: ' + (it as VariationBlock).lines.map(l => labels(l)).join(' / '));
    expect(rows).toEqual(['1: e4 | …', 'V: 1.d4 d5', '1: … | c5']);
  });

  it('PGN mit Varianten (auch verschachtelt), Kommentaren und NAGs', () => {
    const pgn = '[White "Carlsen"]\n[Black "Nakamura"]\n\n1. e4 {Kommentar} e5 (1... c5 2. Nf3 (2. c3 d5) 2... d6) 2. Nf3 $1 Nc6! (2... d6) 3. Bb5 *';
    const t = parsePgnTree(pgn)!;
    expect(t.headers['White']).toBe('Carlsen');
    expect(sans(mainline(t.root))).toEqual(['e4', 'e5', 'Nf3', 'Nc6', 'Bb5']);
    const e4 = t.root.children[0];
    expect(sans(e4.children)).toEqual(['e5', 'c5']);
    const c5 = e4.children[1];
    expect(sans(c5.children)).toEqual(['Nf3', 'c3']);
    expect(sans(c5.children[0].children)).toEqual(['d6']);
    expect(sans(c5.children[1].children)).toEqual(['d5']);
    const nf3 = e4.children[0].children[0];
    expect(sans(nf3.children)).toEqual(['Nc6', 'd6']);
    // Ein ungültiger Zug beendet nur SEINE Variante
    const bad = parsePgnTree('1. e4 e5 (1... Ke2 2. d4) 2. Nf3 *')!;
    expect(sans(mainline(bad.root))).toEqual(['e4', 'e5', 'Nf3']);
    expect(bad.root.children[0].children.length).toBe(1);
    expect(parsePgnTree('[Event "x"]\n\n*')).toBeNull();
  });

  it('PGN: nur die erste Partie, Wörter ohne Zug überlesen, Rochade mit Nullen, FEN-Kopfzeile', () => {
    const two = '[White "A"]\n\n1. e4 e5 2. Nf3 1-0\n\n[White "B"]\n\n1. d4 d5 0-1';
    const t = parsePgnTree(two)!;
    expect(t.headers['White']).toBe('A');
    expect(sans(mainline(t.root))).toEqual(['e4', 'e5', 'Nf3']);
    const ep = parsePgnTree('1. e4 d5 2. e5 f5 3. exf6 e.p. Nxf6 4. Nf3 e6 5. Be2 Be7 6. 0-0 *')!;
    expect(sans(mainline(ep.root))).toEqual(['e4', 'd5', 'e5', 'f5', 'exf6', 'Nxf6', 'Nf3', 'e6', 'Be2', 'Be7', 'O-O']);
    const fen = parsePgnTree('[FEN "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1"]\n\n1. e4 Kd7 *')!;
    expect(fen.root.fen).toBe('4k3/8/8/8/8/8/4P3/4K3 w - - 0 1');
    expect(sans(mainline(fen.root))).toEqual(['e4', 'Kd7']);
  });
});
