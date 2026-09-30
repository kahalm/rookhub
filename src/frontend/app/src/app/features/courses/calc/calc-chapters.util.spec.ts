import {
  NO_CHAPTER_KEY, applyChapterSums, chapterGroupLabel, chapterKey, groupByChapter, normChapter,
  pickChapterIndex, serverChapterSums,
} from './calc-chapters.util';
import { CalcPositionListItem } from './calculation.service';

function item(id: number, chapter: string | null, over: Partial<CalcPositionListItem> = {}): CalcPositionListItem {
  return {
    id, round: String(id), title: null, chapter, hasTree: false,
    chosenSan: null, chosenUci: null, secondsSpent: 0, grade: null, ...over,
  };
}

describe('calc-chapters.util', () => {
  describe('chapterKey (streng, wie der Server)', () => {
    it('leer, null und reine Leerzeichen sind „ohne Kapitel"', () => {
      expect(chapterKey(null)).toBe(NO_CHAPTER_KEY);
      expect(chapterKey(undefined)).toBe(NO_CHAPTER_KEY);
      expect(chapterKey('')).toBe(NO_CHAPTER_KEY);
      expect(chapterKey('   ')).toBe(NO_CHAPTER_KEY);
      expect(NO_CHAPTER_KEY).toBe('\u0000');
    });

    it('ordinal über den ROHEN Namen: kein Trimmen, keine Groß-/Kleinschreibung', () => {
      expect(chapterKey('Taktik')).toBe('Taktik');
      expect(chapterKey(' Taktik ')).toBe(' Taktik ');
      expect(chapterKey('taktik')).not.toBe(chapterKey('Taktik'));
    });
  });

  it('normChapter vergleicht nachsichtig (nur für ?chapter=)', () => {
    expect(normChapter('  TakTik ')).toBe('taktik');
    expect(normChapter(null)).toBe('');
    expect(normChapter(undefined)).toBe('');
  });

  it('chapterGroupLabel: erstes Label einer Stellung, sonst der Originalname', () => {
    expect(chapterGroupLabel([item(1, 'A'), item(2, 'A', { chapterLabel: 'Alpha' })], 'A')).toBe('Alpha');
    expect(chapterGroupLabel([item(1, 'A', { chapterLabel: '  ' })], 'A')).toBe('A');
    expect(chapterGroupLabel([item(1, null)], null)).toBeNull();
  });

  describe('groupByChapter', () => {
    it('gruppiert eindeutig je Schlüssel, zählt die Nummer IM Kapitel und beschriftet', () => {
      const { groups, numbers } = groupByChapter([
        item(10, 'A'),
        item(11, null),
        item(12, 'A', { chapterLabel: 'Alpha' }),
        item(13, '  '),
        item(14, 'a'),
      ]);
      expect(groups.map(g => g.key)).toEqual(['A', '\u0000', 'a']);
      expect(groups.map(g => g.chapter)).toEqual(['A', null, 'a']);
      expect(groups.map(g => g.label)).toEqual(['Alpha', null, 'a']);
      expect(groups.map(g => g.items.map(i => i.id))).toEqual([[10, 12], [11, 13], [14]]);
      expect([...numbers.entries()]).toEqual([[10, 1], [11, 1], [12, 2], [13, 2], [14, 1]]);
      expect(groups.every(g => g.points === 0 && g.maxPoints === 0 && g.seconds === 0)).toBeTrue();
    });

    it('leere Liste ergibt keine Gruppen', () => {
      const { groups, numbers } = groupByChapter([]);
      expect(groups).toEqual([]);
      expect(numbers.size).toBe(0);
    });
  });

  describe('pickChapterIndex', () => {
    const groups = groupByChapter([
      item(1, 'Eröffnung', { hasTree: true }),
      item(2, 'Taktik', { hasTree: true }),
      item(3, 'taktik'),
      item(4, 'Endspiel'),
    ]).groups;

    it('?chapter= trifft nachsichtig, bei mehreren das erste Kapitel', () => {
      expect(pickChapterIndex(groups, ' TAKTIK ', null)).toBe(1);
      expect(pickChapterIndex(groups, 'endspiel', 1)).toBe(3);
    });

    it('unbekanntes Kapitel fällt auf ?pos=, dann auf das erste offene zurück', () => {
      expect(pickChapterIndex(groups, 'Gibt es nicht', 1)).toBe(0);
      expect(pickChapterIndex(groups, 'Gibt es nicht', null)).toBe(2);
      expect(pickChapterIndex(groups, null, 999)).toBe(2);
    });

    it('alles bearbeitet oder keine Gruppen: Kapitel 0', () => {
      const done = groupByChapter([item(1, 'A', { hasTree: true }), item(2, 'B', { hasTree: true })]).groups;
      expect(pickChapterIndex(done, null, null)).toBe(0);
      expect(pickChapterIndex([], null, null)).toBe(0);
    });
  });

  describe('Summen', () => {
    it('serverChapterSums: Schlüssel streng, secondsSum als Zeit, fehlende Werte als 0', () => {
      const sums = serverChapterSums([
        { chapter: 'A', points: 5, maxPoints: 8, secondsSum: 90 },
        { chapter: 'a', points: 1, maxPoints: 4, secondsSum: 10 },
        { chapter: null, points: undefined as never, maxPoints: undefined as never, secondsSum: undefined as never },
      ]);
      expect([...sums.entries()]).toEqual([
        ['A', { points: 5, maxPoints: 8, seconds: 90 }],
        ['a', { points: 1, maxPoints: 4, seconds: 10 }],
        ['\u0000', { points: 0, maxPoints: 0, seconds: 0 }],
      ]);
      expect(serverChapterSums(undefined).size).toBe(0);
    });

    it('applyChapterSums: Server-Summe, wo vorhanden, sonst aus den Zeilen; Maximum 0 wird selbst gerechnet', () => {
      const { groups } = groupByChapter([
        item(1, 'A', { grade: 4, secondsSpent: 30 }),
        item(2, 'A', { grade: 1, secondsSpent: 15 }),
        item(3, 'B', { grade: 2, secondsSpent: 7 }),
        item(4, 'C', { grade: 3, secondsSpent: 5 }),
      ]);
      applyChapterSums(groups, new Map([
        ['A', { points: 7, maxPoints: 12, seconds: 100 }],
        ['C', { points: 0, maxPoints: 0, seconds: 0 }],
      ]));
      expect(groups.map(g => [g.points, g.maxPoints, g.seconds])).toEqual([
        [7, 12, 100],   // Server
        [2, 4, 7],      // selbst gerechnet (kein Server-Eintrag)
        [0, 4, 0],      // Server-Punkte/-Zeit gelten, Maximum 0 → 4 je Stellung
      ]);
      applyChapterSums(groups, new Map());
      expect(groups.map(g => [g.points, g.maxPoints, g.seconds])).toEqual([[5, 8, 45], [2, 4, 7], [3, 4, 5]]);
    });
  });
});
