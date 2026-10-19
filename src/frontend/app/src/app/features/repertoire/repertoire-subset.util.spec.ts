import { chaptersOf, groupByChapter, subsetPgn } from './repertoire-subset.util';
import { RepertoireLine } from './repertoire-viewer.service';

const line = (gameIndex: number, chapter: string): RepertoireLine => ({
  gameIndex, summary: '', opening: '', white: 'W', black: chapter, result: '*', moveCount: 1, chapter,
  lineKey: `k${gameIndex}`, isInfo: false, startFen: '', lastMoveSide: 'w',
} as RepertoireLine);

describe('repertoire-subset.util', () => {
  it('gruppiert nach Kapitel in Reihenfolge des ersten Auftretens', () => {
    const g = groupByChapter([line(0, 'B'), line(1, 'A'), line(2, 'B'), line(3, '')]);
    expect(g.map(c => c.chapter)).toEqual(['B', 'A', '']);
    expect(g[0].lines.map(l => l.gameIndex)).toEqual([0, 2]);
  });

  it('baut das PGN aus den Originalabschnitten in Repertoire-Reihenfolge, jeden einmal', () => {
    const raw = ['[Event "a"]\n\n1. e4 *\n', '[Event "b"]\n\n1. d4 *\n\n', '[Event "c"]\n\n1. c4 *'];
    const r = subsetPgn(raw, [2, 0, 2]);
    expect(r.count).toBe(2);
    expect(r.pgn).toBe('[Event "a"]\n\n1. e4 *\n\n[Event "c"]\n\n1. c4 *\n');
  });

  it('lässt Linien ohne Originaltext weg', () => {
    const r = subsetPgn(['[Event "a"]\n\n1. e4 *', undefined, '  '], [0, 1, 2]);
    expect(r.count).toBe(1);
    expect(subsetPgn([], [0]).pgn).toBe('');
  });

  it('liefert die Kapitel der gewählten Linien', () => {
    const lines = [line(0, 'A'), line(1, 'B'), line(2, 'A')];
    expect([...chaptersOf(lines, new Set([0, 2]))]).toEqual(['A']);
  });
});
