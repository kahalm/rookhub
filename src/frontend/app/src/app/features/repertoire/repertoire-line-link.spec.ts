import { of } from 'rxjs';
import { LineKeyResolver, chapterGroups, findSection, treeAsMatch, treeNodeAsLine } from './repertoire-line-link';
import { lineKeyFromSans } from './repertoire-line-key.util';

const PGN =
  '[Event "R"]\n[White "Main line"]\n[Black "Najdorf"]\n\n1. e4 c5 2. Nf3 d6 *\n\n' +
  '[Event "R"]\n[White "Yugoslav"]\n[Black "Dragon"]\n\n1. e4 c5 2. Nf3 g6 *\n';

describe('repertoire-line-link', () => {
  it('chapterGroups gruppiert in Reihenfolge des ersten Auftretens', () => {
    const groups = chapterGroups({ repertoireId: 1, repertoireName: 'R', kind: '', shared: false, lines: [
      { chapter: 'B', lineName: '1', gameIndex: 0, ply: 1 }, { chapter: '', lineName: '2', gameIndex: 1, ply: 1 },
      { chapter: 'B', lineName: '3', gameIndex: 2, ply: 1 },
    ] });
    expect(groups.map(g => [g.name, g.lines.length])).toEqual([['B', 2], ['', 1]]);
  });

  it('Baum-Knoten werden zu Treffer + Linie (ply -1 = Linienanfang)', () => {
    expect(treeAsMatch({ repertoireId: 3, repertoireName: 'X', kind: 'Opening', shared: true, occurrences: 2, truncated: false, moves: [] }))
      .toEqual({ repertoireId: 3, repertoireName: 'X', kind: 'Opening', shared: true, lines: [] });
    expect(treeNodeAsLine({ san: 'd6', count: 1, isEnd: true, chapter: null, lineName: null, gameIndex: null, children: [] }))
      .toEqual({ chapter: '', lineName: '', gameIndex: 0, ply: -1 });
  });

  it('findSection fällt bei falschem Index auf Kapitel + Name zurück', () => {
    const sections = PGN.split('\n\n[').map((s, i) => (i ? '[' + s : s));
    expect(findSection(sections, { chapter: 'Dragon', lineName: 'Yugoslav', gameIndex: 0 })).toBe(sections[1]);
    expect(findSection(sections, { chapter: 'X', lineName: 'Y', gameIndex: 7 })).toBeNull();
  });

  it('LineKeyResolver rechnet den Schlüssel aus dem Client-Parse und holt das PGN nur einmal', () => {
    const getPgnText = jasmine.createSpy('getPgnText').and.returnValue(of(PGN));
    const r = new LineKeyResolver(() => ({ getPgnText }));
    let key = '';
    r.resolve(5, { chapter: 'Dragon', lineName: 'Yugoslav', gameIndex: 1, ply: 4 }).subscribe(k => key = k);
    expect(key).toBe(lineKeyFromSans(['e4', 'c5', 'Nf3', 'g6']));
    r.resolve(5, { chapter: 'Najdorf', lineName: 'Main line', gameIndex: 0, ply: 4 }).subscribe(k => key = k);
    expect(key).toBe(lineKeyFromSans(['e4', 'c5', 'Nf3', 'd6']));
    expect(getPgnText).toHaveBeenCalledTimes(1);
    r.resolve(5, { chapter: 'Z', lineName: 'Z', gameIndex: 9, ply: 0 }).subscribe(k => key = k);
    expect(key).toBe('');
  });
});
