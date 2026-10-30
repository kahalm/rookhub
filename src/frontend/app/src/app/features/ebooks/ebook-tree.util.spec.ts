import { Chess } from 'chess.js';
import { countNodes, mainline, parsePgnTree } from '../analysis/analysis-tree';
import { EbookOpeningTree, EbookTreeWire, placementOf, rotatedPlacement, treeToPgn } from './ebook-tree.util';

/**
 * Dänisches Gambit im Kleinen: 1.e4 e5 2.d4 exd4 3.c3 (3.Nf3) dxc3 4.Bc4 (4.Nxc3) cxb2 5.Bxb2 — plus ein kaputter Zug
 * („Ke5“ nach 1.e4) samt Kind, der wegfallen muss.
 */
const WIRE: EbookTreeWire = [
  [0, 'e4'],    // 1
  [1, 'e5'],    // 2
  [2, 'd4'],    // 3
  [3, 'exd4'],  // 4
  [4, 'c3'],    // 5
  [4, 'Nf3'],   // 6
  [5, 'dxc3'],  // 7
  [7, 'Bc4'],   // 8
  [7, 'Nxc3'],  // 9
  [8, 'cxb2'],  // 10
  [10, 'Bxb2'], // 11
  [1, 'Ke5'],   // 12 illegal
  [12, 'd4'],   // 13 hängt am illegalen
];

describe('EbookOpeningTree', () => {
  const tree = new EbookOpeningTree(WIRE);

  it('baut alle Stellungen in Server-Nummern, illegale Züge fallen samt Teilbaum weg', () => {
    expect(tree.size).toBe(11);
    expect(tree.parent[12]).toBe(-1);
    expect(tree.parent[13]).toBe(-1);
    expect(tree.depth[11]).toBe(9);
    const c = new Chess();
    for (const s of ['e4', 'e5', 'd4', 'exd4', 'c3', 'dxc3', 'Bc4', 'cxb2', 'Bxb2']) c.move(s);
    expect(tree.fen[11]).toBe(c.fen());
    expect(tree.path(11)).toEqual([1, 2, 3, 4, 5, 7, 8, 10, 11]);
  });

  it('Linien = Blätter darunter; Kinder meistverzweigt zuerst', () => {
    expect(tree.lines[0]).toBe(3);   // Bxb2, Nxc3, Nf3
    expect(tree.lines[4]).toBe(3);
    expect(tree.lines[5]).toBe(2);
    expect(tree.lines[6]).toBe(1);
    expect(tree.children[4]).toEqual([5, 6]);
    expect(tree.children[7]).toEqual([8, 9]);   // Gleichstand → Buchreihenfolge
    expect(tree.move(6)).toEqual({ node: 6, san: 'Nf3', label: '3.', lines: 1 });
    expect(tree.moveNumber(4)).toBe('2…');
    expect(tree.subtreeSize(5)).toBe(5);
  });

  it('findet Diagrammstellungen nur über die Figurenstellung, auch um 180° gedreht', () => {
    const fen = tree.fen[7];
    // Diagramm-FEN mit falschem Zugrecht und eigenen Zählern passt trotzdem.
    expect(tree.find(placementOf(fen) + ' w - - 0 1')).toEqual({ node: 7, rotated: false });
    expect(tree.find(rotatedPlacement(placementOf(fen)) + ' b - - 0 1')).toEqual({ node: 7, rotated: true });
    expect(tree.find('8/8/8/8/8/8/8/K6k w - - 0 1')).toEqual({ node: null, rotated: false });
    expect(tree.find(new Chess().fen()).node).toBe(0);
  });

  it('bei Zugumstellung gewinnt die geringste Tiefe', () => {
    const t = new EbookOpeningTree([[0, 'Nf3'], [1, 'Nf6'], [2, 'Ng1'], [3, 'Ng8'], [4, 'e4'], [0, 'e4']]);
    expect(t.find(t.fen[5]).node).toBe(6);
  });

  it('Farbvorschlag: Seite, die in der Mehrheit der Linien zuletzt zieht', () => {
    expect(tree.majorityLastMover(0)).toBe('w');   // Bxb2 (w), Nxc3 (w), Nf3 (w)
    const black = new EbookOpeningTree([[0, 'e4'], [1, 'c5'], [1, 'e6']]);
    expect(black.majorityLastMover(0)).toBe('b');
    expect(new EbookOpeningTree([]).majorityLastMover(0)).toBe('w');
  });

  it('PGN mit Varianten: Hauptlinie = meiste Linien; liest sich mit dem Baumparser zurück', () => {
    const pgn = treeToPgn(tree, 0, { bookTitle: 'Danish "Dynamite"', name: 'Dänisch', color: 'w' });
    expect(pgn).toContain('[Event "Danish \\"Dynamite\\""]');
    expect(pgn).toContain('[Annotator "RookHub book tree"]');
    expect(pgn).toContain('[ChessableColor "white"]');
    expect(pgn).toContain('[Black "Dänisch"]');
    expect(pgn).toContain('1. e4 e5 2. d4 exd4 3. c3 (3. Nf3) 3... dxc3 4. Bc4 (4. Nxc3) 4... cxb2 5. Bxb2 *');
    const parsed = parsePgnTree(pgn)!;
    expect(parsed.headers['Event']).toBe('Danish "Dynamite"');
    expect(countNodes(parsed.root)).toBe(tree.size);
    expect(mainline(parsed.root).map(n => n.san).filter(Boolean)).toEqual(['e4', 'e5', 'd4', 'exd4', 'c3', 'dxc3', 'Bc4', 'cxb2', 'Bxb2']);
  });

  it('PGN ab einer Stellung: Weg dorthin als Hauptlinie, danach nur der Teilbaum', () => {
    const pgn = treeToPgn(tree, 7, { bookTitle: 'B', name: 'N', color: 'b' });
    expect(pgn).toContain('[ChessableColor "black"]');
    expect(pgn).toContain('1. e4 e5 2. d4 exd4 3. c3 dxc3 4. Bc4 (4. Nxc3) 4... cxb2 5. Bxb2 *');
    expect(pgn).not.toContain('Nf3');
    expect(countNodes(parsePgnTree(pgn)!.root)).toBe(6 + tree.subtreeSize(7));
  });

  it('Teilbaum ab einem Zug von Schwarz beginnt mit Zugnummer', () => {
    expect(tree.movetext(2)).toBe('1. e4 e5 2. d4 exd4 3. c3 (3. Nf3) 3... dxc3 4. Bc4 (4. Nxc3) 4... cxb2 5. Bxb2');
    const t = new EbookOpeningTree([[0, 'e4'], [1, 'e5'], [1, 'c5'], [3, 'Nf3']]);
    expect(t.movetext(1)).toBe('1. e4 e5 (1... c5 2. Nf3)');
  });
});
