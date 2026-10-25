import { CalcChapterModel } from './calc-chapter-model';
import { CalcBook, CalcPositionListItem } from './calculation.service';

function item(id: number, chapter: string | null, over: Partial<CalcPositionListItem> = {}): CalcPositionListItem {
  return { id, round: String(id * 10), title: null, chapter, hasTree: false,
    chosenSan: null, chosenUci: null, secondsSpent: 0, grade: null, ...over };
}

function book(positions: CalcPositionListItem[], over: Partial<CalcBook> = {}): CalcBook {
  return { bookId: 1, displayName: 'B', isCalculation: true, positions, ...over };
}

describe('CalcChapterModel', () => {
  it('bildet Kapitel, nummeriert je Kapitel ab 1 und füllt fehlende Felder auf', () => {
    const m = new CalcChapterModel();
    const raw = { ...item(1, 'A'), chosenSan: undefined, secondsSpent: undefined } as unknown as CalcPositionListItem;
    m.setBook(book([raw, item(2, 'A'), item(3, 'B')]));
    expect(m.groups.map(g => g.items.map(p => p.id))).toEqual([[1, 2], [3]]);
    expect(m.chapterNumberOf(m.positions[1])).toBe('2');
    expect(m.chapterNumberOf(m.positions[2])).toBe('1');
    expect(m.positions[0].chosenSan).toBeNull();
    expect(m.positions[0].secondsSpent).toBe(0);
  });

  it('steigt bei der ersten offenen Stellung ein, Deep-Link geht vor; „durch" erst nach dem Ankommen', () => {
    const m = new CalcChapterModel();
    m.setBook(book([item(1, 'A', { hasTree: true }), item(2, 'A'), item(3, 'A')]));
    expect(m.enter(0, null)).toBe(2);
    expect(m.enter(0, 3)).toBe(3);
    expect(m.atLastPosition).toBeTrue();
    expect(m.atChapterEnd).withContext('eingestiegen ist nicht durchgearbeitet').toBeFalse();
    m.moveTo(1);
    expect(m.atChapterEnd).toBeFalse();
    expect(m.moveTo(2)).toBe(3);
    expect(m.atChapterEnd).toBeTrue();
    expect(m.hasNext()).toBeFalse();
    expect(m.hasPrev()).toBeTrue();
    expect(m.doneCount).toBe(1);
  });

  it('leeres Buch: kein Einstieg; chapterOf findet das Kapitel einer Stellung', () => {
    const m = new CalcChapterModel();
    m.setBook(book([]));
    expect(m.enter(0, null)).toBeNull();
    m.setBook(book([item(1, 'A'), item(2, 'B')]));
    expect(m.chapterOf(2)).toBe(1);
    expect(m.chapterOf(9)).toBe(-1);
    m.enter(0, null);
    expect(m.hasNextChapter).toBeTrue();
    expect(m.nextChapter?.chapter).toBe('B');
  });

  it('nimmt die Server-Summen, bis sie verworfen werden — dann rechnet es selbst', () => {
    const m = new CalcChapterModel();
    m.setBook(book([item(1, 'A', { grade: 2 }), item(2, 'A')], {
      points: 7, maxPoints: 40,
      chapters: [{ chapter: 'A', points: 5, maxPoints: 8, ratedCount: 1, positionCount: 2 }] as never,
    }));
    m.enter(0, null);
    expect([m.totalPoints, m.totalMaxPoints, m.bookPoints, m.bookMaxPoints]).toEqual([5, 8, 7, 40]);
    m.dropServerSums();
    expect([m.totalPoints, m.totalMaxPoints, m.bookPoints, m.bookMaxPoints]).toEqual([2, 8, 2, 8]);
  });
});
