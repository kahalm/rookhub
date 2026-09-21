import { groupByChapter } from './chapter-groups.util';

interface Line { id: number; chapter?: string | null; }

describe('groupByChapter', () => {
  it('returns an empty list for no items', () => {
    expect(groupByChapter<Line, string>([], l => l.chapter || '')).toEqual([]);
  });

  it('keeps the order of FIRST occurrence, not alphabetical order', () => {
    const lines: Line[] = [
      { id: 1, chapter: 'Sizilianisch' },
      { id: 2, chapter: 'Aljechin' },
      { id: 3, chapter: 'Sizilianisch' },
    ];
    const groups = groupByChapter(lines, l => l.chapter || '');
    expect(groups.map(g => g.key)).toEqual(['Sizilianisch', 'Aljechin']);
    expect(groups[0].lines.map(l => l.id)).toEqual([1, 3]);
    expect(groups[1].lines.map(l => l.id)).toEqual([2]);
  });

  it('keeps the order of the lines inside a group', () => {
    const lines: Line[] = [{ id: 3 }, { id: 1 }, { id: 2 }];
    expect(groupByChapter(lines, () => '').at(0)!.lines.map(l => l.id)).toEqual([3, 1, 2]);
  });

  it('groups by the key the caller supplies — null and empty string stay apart', () => {
    // Die Kurs-Ansicht nennt „ohne Kapitel" null, die Repertoire-Liste ''. Beides ist erlaubt,
    // und der Helfer entscheidet nicht darüber.
    const lines: Line[] = [{ id: 1, chapter: null }, { id: 2, chapter: '' }, { id: 3 }];
    expect(groupByChapter(lines, l => l.chapter || null).map(g => g.key)).toEqual([null]);
    expect(groupByChapter(lines, l => l.chapter || '').map(g => g.key)).toEqual(['']);
  });

  it('separates chapters that only differ in case (no normalising)', () => {
    const lines: Line[] = [{ id: 1, chapter: 'Damengambit' }, { id: 2, chapter: 'damengambit' }];
    expect(groupByChapter(lines, l => l.chapter || '').length).toBe(2);
  });
});
