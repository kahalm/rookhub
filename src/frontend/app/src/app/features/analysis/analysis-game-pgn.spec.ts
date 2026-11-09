import { createRoot, parsePgnTree, playSan, mainline } from './analysis-tree';
import { detailsFromHeaders, gameFileName, GameDetails, isStandardStart, treeToPgn } from './analysis-game-pgn';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const D = (o: Partial<GameDetails> = {}): GameDetails => ({ white: 'Anna', black: 'Berta', result: '1-0', date: '2026-11-09', event: '', round: '', ...o });

function tree() {
  const root = createRoot(START);
  const e4 = playSan(root, 'e4')!;
  const c5 = playSan(e4, 'c5')!;
  playSan(e4, 'e5');                 // Variante zu 1...c5
  playSan(c5, 'Nf3');
  return { root, e4, c5 };
}

describe('treeToPgn', () => {
  it('schreibt Kopfdaten und Varianten, Rundreise über parsePgnTree', () => {
    const { root } = tree();
    const pgn = treeToPgn(root, D({ event: 'Liga', round: '3' }));
    expect(pgn).toContain('[White "Anna"]');
    expect(pgn).toContain('[Date "2026.11.09"]');
    expect(pgn).toContain('[Event "Liga"]');
    expect(pgn).toContain('[Round "3"]');
    expect(pgn).toContain('[Result "1-0"]');
    expect(pgn).toContain('1. e4 c5 (1... e5) 2. Nf3 1-0');
    expect(pgn).not.toContain('SetUp');
    const back = parsePgnTree(pgn)!;
    expect(mainline(back.root).map(n => n.san)).toEqual(['e4', 'c5', 'Nf3']);
    expect(back.root.children[0].children.map(n => n.san)).toEqual(['c5', 'e5']);
  });

  it('nur Hauptlinie, wenn gewünscht', () => {
    expect(treeToPgn(tree().root, D(), { mainlineOnly: true })).toContain('1. e4 c5 2. Nf3 1-0');
  });

  it('abweichende Startstellung: SetUp + FEN, Schwarz am Zug mit „…"', () => {
    const fen = '4k3/8/8/8/8/8/4P3/4K3 b - - 0 10';
    const root = createRoot(fen);
    playSan(root, 'Kd7');
    const pgn = treeToPgn(root, D({ result: '*' }));
    expect(isStandardStart(root)).toBeFalse();
    expect(pgn).toContain('[SetUp "1"]');
    expect(pgn).toContain(`[FEN "${fen}"]`);
    expect(pgn).toContain('10... Kd7 *');
    expect(parsePgnTree(pgn)!.root.fen).toBe(fen);
  });

  it('Symbole als NAG, Kommentar in Klammern, danach wieder mit Zugnummer', () => {
    const { root, e4, c5 } = tree();
    e4.glyph = '!';
    c5.evaluation = '±';
    c5.comment = 'gut {genug}';
    expect(treeToPgn(root, D())).toContain('1. e4 $1 c5 $16 {gut genug}');
  });

  it('maskiert Anführungszeichen, leere Felder werden „?"', () => {
    const pgn = treeToPgn(createRoot(START), D({ white: 'a"b\\c', black: '  ', date: '' }));
    expect(pgn).toContain('[White "a\\"b\\\\c"]');
    expect(pgn).toContain('[Black "?"]');
    expect(pgn).toContain('[Date "????.??.??"]');
  });
});

describe('detailsFromHeaders', () => {
  it('liest die Kopfdaten, Platzhalter bleiben leer', () => {
    expect(detailsFromHeaders({ White: 'A', Black: '?', Result: '0-1', Date: '2024.03.05', Event: '-', Round: '2' }))
      .toEqual(jasmine.objectContaining({ white: 'A', black: '', result: '0-1', date: '2024-03-05', event: '', round: '2' }));
  });

  it('ohne geladene Partie alles leer, Ergebnis offen', () => {
    expect(detailsFromHeaders(null)).toEqual(jasmine.objectContaining({ white: '', black: '', result: '*', date: '' }));
  });

  it('ein unvollständiges Datum bleibt erhalten, solange das Feld leer ist', () => {
    const d = detailsFromHeaders({ Date: '2024.??.??' });
    expect(d.date).toBe('');
    expect(treeToPgn(createRoot(START), { ...d, white: 'a', black: 'b' })).toContain('[Date "2024.??.??"]');
  });
});

describe('gameFileName', () => {
  it('nimmt Namen und Datum, ohne Namen „analyse"', () => {
    expect(gameFileName(D())).toBe('Anna_Berta_2026_11_09.pgn');
    expect(gameFileName(D({ white: '', black: '', date: '' }))).toBe('analyse.pgn');
  });
});
